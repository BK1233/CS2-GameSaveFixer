using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CS2SaveFixer.Mod;
using CS2SaveFixer.Tool;
using Xunit;

namespace CS2SaveFixer.Tests;

public class SaveFixerTests : IDisposable
{
    private readonly string _tempDirectory;

    public SaveFixerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CS2SaveFixer_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private string CreateMockSaveFile(bool addCorruptedNan, bool addMissingGuids)
    {
        string filePath = Path.Combine(_tempDirectory, "MockCity.coc");
        using var fileStream = new FileStream(filePath, FileMode.Create);
        using var zip = new ZipArchive(fileStream, ZipArchiveMode.Create);

        // Add metadata.json
        var metaEntry = zip.CreateEntry("metadata.json");
        using (var writer = new StreamWriter(metaEntry.Open()))
        {
            var meta = new MetadataHeader
            {
                CityName = "Test Metropolis",
                GameVersion = "1.0.12f1",
                Population = 50000,
                ActiveMods = new List<string> { "ModA", "ModB", "ModA" }
            };
            writer.Write(JsonSerializer.Serialize(meta));
        }

        // Add Entities.bin
        var entitiesEntry = zip.CreateEntry("Entities.bin");
        using (var stream = entitiesEntry.Open())
        {
            List<byte> bytes = new List<byte> { (byte)'C', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 };

            if (addCorruptedNan)
            {
                bytes.AddRange(BitConverter.GetBytes(float.NaN));
                bytes.AddRange(BitConverter.GetBytes(float.PositiveInfinity));
            }
            else
            {
                bytes.AddRange(BitConverter.GetBytes(100.5f));
                bytes.AddRange(BitConverter.GetBytes(200.0f));
            }

            if (addMissingGuids)
            {
                bytes.AddRange(new byte[16]); // All zeros Guid
            }
            else
            {
                bytes.AddRange(Guid.NewGuid().ToByteArray());
            }

            stream.Write(bytes.ToArray(), 0, bytes.Count);
        }

        return filePath;
    }

    [Fact]
    public void Test_SaveAnalyzer_DetectsCorruptions()
    {
        string savePath = CreateMockSaveFile(addCorruptedNan: true, addMissingGuids: true);
        var archive = SaveRepairEngine.LoadArchive(savePath);

        var issues = SaveAnalyzer.Analyze(archive);

        Assert.Contains(issues, i => i.Message.Contains("NaN or Infinity"));
        Assert.Contains(issues, i => i.Message.Contains("unmapped or missing asset GUID"));
    }

    [Fact]
    public void Test_SaveRepairEngine_FixesCorruptions()
    {
        string savePath = CreateMockSaveFile(addCorruptedNan: true, addMissingGuids: true);
        string outputPath = Path.Combine(_tempDirectory, "MockCity_Fixed.coc");

        var archive = SaveRepairEngine.LoadArchive(savePath);
        var repairResult = SaveRepairEngine.Repair(archive, cleanNan: true, remapPrefabs: true);

        Assert.NotEmpty(repairResult.FixesApplied);
        Assert.True(repairResult.NanValuesSanitized > 0);
        Assert.True(repairResult.NullGuidsRemapped > 0);

        SaveRepairEngine.SaveArchiveFile(repairResult.RepairedArchive, outputPath);

        Assert.True(File.Exists(outputPath));

        var reloadedArchive = SaveRepairEngine.LoadArchive(outputPath);
        var issues = SaveAnalyzer.Analyze(reloadedArchive);

        Assert.DoesNotContain(issues, i => i.Message.Contains("NaN or Infinity"));
        Assert.DoesNotContain(issues, i => i.Message.Contains("unmapped or missing asset GUID"));
    }

    [Fact]
    public void Test_SaveFormatUtils_HasValidCosvHeader()
    {
        byte[] validHeader = new byte[] { (byte)'C', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 };
        byte[] invalidMagic = new byte[] { (byte)'X', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 };
        byte[] tooShort = new byte[] { (byte)'C', (byte)'O', (byte)'S', (byte)'V' };

        Assert.True(SaveFormatUtils.HasValidCosvHeader(validHeader));
        Assert.False(SaveFormatUtils.HasValidCosvHeader(invalidMagic));
        Assert.False(SaveFormatUtils.HasValidCosvHeader(tooShort));
    }

    [Fact]
    public void Test_SaveFormatUtils_ApplyCosvHeader()
    {
        byte[] buffer = new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 };
        bool applied = SaveFormatUtils.ApplyCosvHeader(buffer);

        Assert.True(applied);
        Assert.Equal((byte)'C', buffer[0]);
        Assert.Equal((byte)'O', buffer[1]);
        Assert.Equal((byte)'S', buffer[2]);
        Assert.Equal((byte)'V', buffer[3]);

        // Calling again on an already valid buffer returns false (no mutation needed)
        bool reApplied = SaveFormatUtils.ApplyCosvHeader(buffer);
        Assert.False(reApplied);
    }

    [Fact]
    public void Test_ModDeserializationInterceptor_ResolvesMissingPrefabs()
    {
        string missingGuid = Guid.NewGuid().ToString();
        string resolvedFallback = DeserializationInterceptor.ResolveMissingPrefabGuid(missingGuid, "BuildingPrefab");

        Assert.Equal("BaseGame.DefaultBuilding", resolvedFallback);
    }

    [Fact]
    public void Test_ModSaveFixerCleanupSystem_SanitizesEntities()
    {
        var entities = new List<SimulatedEntity>
        {
            new SimulatedEntity { Id = 1, PositionX = float.NaN, PositionY = 10f, PositionZ = 20f },
            new SimulatedEntity { Id = 2, PositionX = 5f, PositionY = 5f, PositionZ = 5f, IsOrphaned = true },
            new SimulatedEntity { Id = 3, PositionX = 1f, PositionY = 2f, PositionZ = 3f, HasValidPrefab = true }
        };

        int fixedCount = SaveFixerCleanupSystem.RunSanitizePass(entities);

        Assert.Equal(2, fixedCount);
        Assert.Equal(2, entities.Count); // Entity #1 (sanitized position) and Entity #3 remain
        Assert.Equal(0f, entities[0].PositionX); // Entity #1 position sanitized from NaN to 0f
        Assert.Equal(1f, entities[1].PositionX); // Entity #3 position intact
    }
}
