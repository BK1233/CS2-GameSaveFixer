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

    [Fact]
    public void Test_LoadArchive_CorruptedZip_FallbackToRawRecovery()
    {
        string filePath = Path.Combine(_tempDirectory, "CorruptedSave.coc");
        byte[] corruptedData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };
        File.WriteAllBytes(filePath, corruptedData);

        var archive = SaveRepairEngine.LoadArchive(filePath);

        Assert.Equal(corruptedData, archive.EntitiesData);
        Assert.NotNull(archive.Metadata);
        Assert.Equal("CorruptedSave", archive.Metadata.CityName);
        Assert.Equal("1.0.0 (Recovered)", archive.Metadata.GameVersion);
    }

    [Fact]
    public void Test_LoadArchive_EmptyFile_ThrowsInvalidDataException()
    {
        string filePath = Path.Combine(_tempDirectory, "EmptySave.coc");
        File.WriteAllBytes(filePath, Array.Empty<byte>());

        var exception = Assert.Throws<InvalidDataException>(() => SaveRepairEngine.LoadArchive(filePath));

        Assert.Equal("Save file is completely empty (0 bytes).", exception.Message);
    }

    [Fact]
    public void Test_SaveRepairEngine_SynthesizesCosvHeaderWhenEntitiesEmpty()
    {
        var archive = new SaveArchive
        {
            EntitiesData = Array.Empty<byte>()
        };

        var repairResult = SaveRepairEngine.Repair(archive, cleanNan: false, remapPrefabs: false);

        Assert.Contains(repairResult.FixesApplied, f => f.Contains("Initialized missing Entities.bin with valid COSV binary header"));
        Assert.Equal(8, repairResult.RepairedArchive.EntitiesData.Length);
        Assert.Equal((byte)'C', repairResult.RepairedArchive.EntitiesData[0]);
        Assert.Equal((byte)'O', repairResult.RepairedArchive.EntitiesData[1]);
        Assert.Equal((byte)'S', repairResult.RepairedArchive.EntitiesData[2]);
        Assert.Equal((byte)'V', repairResult.RepairedArchive.EntitiesData[3]);
    }

    [Fact]
    public void Test_LoadArchive_NestedZipEntryPaths_MatchedCorrectly()
    {
        string filePath = Path.Combine(_tempDirectory, "NestedSave.cok");
        using (var fileStream = new FileStream(filePath, FileMode.Create))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var metaEntry = zip.CreateEntry("savegame/world/metadata.json");
            using (var writer = new StreamWriter(metaEntry.Open()))
            {
                writer.Write("{\"CityName\":\"Nested City\",\"GameVersion\":\"1.0.0\",\"Population\":1234}");
            }

            var entitiesEntry = zip.CreateEntry("savegame/world/Entities.bin");
            using (var stream = entitiesEntry.Open())
            {
                byte[] entitiesBytes = new byte[] { (byte)'C', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 };
                stream.Write(entitiesBytes, 0, entitiesBytes.Length);
            }
        }

        var archive = SaveRepairEngine.LoadArchive(filePath);

        Assert.Equal("Nested City", archive.Metadata.CityName);
        Assert.Equal(1234, archive.Metadata.Population);
        Assert.Equal(8, archive.EntitiesData.Length);
        Assert.Equal((byte)'C', archive.EntitiesData[0]);
    }

    [Fact]
    public void Test_SaveArchiveFile_UsesNoCompressionForCS2FileSystem()
    {
        string outputPath = Path.Combine(_tempDirectory, "UncompressedSave.coc");
        var archive = new SaveArchive
        {
            Metadata = new MetadataHeader { CityName = "Uncompressed Test" },
            EntitiesData = new byte[] { (byte)'C', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 }
        };

        SaveRepairEngine.SaveArchiveFile(archive, outputPath);

        using var zip = ZipFile.OpenRead(outputPath);
        Assert.NotEmpty(zip.Entries);
        // ZipArchiveEntry does not expose compression method directly, but opening and reading verifies valid stored entries
        var metadataEntry = zip.GetEntry("metadata.json");
        Assert.NotNull(metadataEntry);
        var entitiesEntry = zip.GetEntry("Entities.bin");
        Assert.NotNull(entitiesEntry);
        Assert.Equal(metadataEntry.Length, metadataEntry.CompressedLength);
        Assert.Equal(entitiesEntry.Length, entitiesEntry.CompressedLength);
    }

    [Fact]
    public void Test_BuildCidIndexData_MetadataSizeMatchesSavedFileBytes()
    {
        string outputPath = Path.Combine(_tempDirectory, "CidSizeMatchTest.coc");
        var archive = new SaveArchive
        {
            Metadata = new MetadataHeader { CityName = "CID Match Test City", Population = 999 },
            EntitiesData = new byte[] { (byte)'C', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 }
        };

        var repairResult = SaveRepairEngine.Repair(archive, cleanNan: false, remapPrefabs: false);
        SaveRepairEngine.SaveArchiveFile(repairResult.RepairedArchive, outputPath);

        using var zip = ZipFile.OpenRead(outputPath);
        var metadataEntry = zip.GetEntry("metadata.json");
        Assert.NotNull(metadataEntry);

        var cidEntry = zip.GetEntry("savegame.cid");
        Assert.NotNull(cidEntry);

        using var ms = new MemoryStream();
        using (var cidStream = cidEntry.Open())
        {
            cidStream.CopyTo(ms);
        }

        byte[] cidBytes = ms.ToArray();
        using var reader = new BinaryReader(new MemoryStream(cidBytes), Encoding.UTF8);

        char[] magic = reader.ReadChars(4);
        Assert.Equal("COCD", new string(magic));
        int version = reader.ReadInt32();
        Assert.Equal(100, version);

        int entryCount = reader.ReadInt32();
        var entries = new Dictionary<string, int>();
        for (int i = 0; i < entryCount; i++)
        {
            string name = reader.ReadString();
            int size = reader.ReadInt32();
            entries[name] = size;
        }

        Assert.True(entries.ContainsKey("metadata.json"));
        Assert.Equal((int)metadataEntry.Length, entries["metadata.json"]);
    }

    [Fact]
    public void Test_SaveRepairEngine_RebuildsCidManifestWithUpdatedSizes()
    {
        var archive = new SaveArchive
        {
            Metadata = new MetadataHeader { CityName = "Updated CID City" },
            EntitiesData = new byte[] { (byte)'C', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 }
        };

        var repairResult = SaveRepairEngine.Repair(archive, cleanNan: false, remapPrefabs: false);

        Assert.True(repairResult.RepairedArchive.AdditionalFiles.ContainsKey("savegame.cid"));
        byte[] cidBytes = repairResult.RepairedArchive.AdditionalFiles["savegame.cid"];
        using var reader = new BinaryReader(new MemoryStream(cidBytes), Encoding.UTF8);
        char[] magic = reader.ReadChars(4);
        Assert.Equal("COCD", new string(magic));
    }

    [Fact]
    public void Test_SaveArchiveFile_PreventsDuplicatePrimaryEntries()
    {
        string outputPath = Path.Combine(_tempDirectory, "DeduplicationTest.coc");
        var archive = new SaveArchive
        {
            Metadata = new MetadataHeader { CityName = "Deduplication Test City" },
            EntitiesData = new byte[] { (byte)'C', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 },
            AdditionalFiles = new Dictionary<string, byte[]>
            {
                { "metadata.json", Encoding.UTF8.GetBytes("{\"duplicate\":true}") },
                { "Entities.bin", new byte[] { 1, 2, 3, 4 } }
            }
        };

        SaveRepairEngine.SaveArchiveFile(archive, outputPath);

        using var zip = ZipFile.OpenRead(outputPath);
        int metadataCount = zip.Entries.Count(e => e.Name.Equals("metadata.json", StringComparison.OrdinalIgnoreCase));
        int entitiesCount = zip.Entries.Count(e => e.Name.Equals("Entities.bin", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1, metadataCount);
        Assert.Equal(1, entitiesCount);
    }

    [Fact]
    public void Test_CreateBackupFile_GeneratesBakAndTimestampedBak()
    {
        string samplePath = Path.Combine(_tempDirectory, "SampleSave.coc");
        File.WriteAllBytes(samplePath, new byte[] { 1, 2, 3, 4 });

        string backup1 = SaveRepairEngine.CreateBackupFile(samplePath);
        Assert.True(File.Exists(backup1));
        Assert.EndsWith(".bak", backup1);

        string backup2 = SaveRepairEngine.CreateBackupFile(samplePath);
        Assert.True(File.Exists(backup2));
        Assert.EndsWith(".bak", backup2);
        Assert.NotEqual(backup1, backup2);
    }

    [Fact]
    public void Test_SanitizeFloatingPointNumbers_SanitizesSimulationBin()
    {
        var archive = new SaveArchive
        {
            EntitiesData = new byte[] { (byte)'C', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 },
            SimulationData = new byte[16]
        };

        // Inject NaN in SimulationData
        byte[] nanBytes = BitConverter.GetBytes(float.NaN);
        Buffer.BlockCopy(nanBytes, 0, archive.SimulationData, 8, 4);

        var repairResult = SaveRepairEngine.Repair(archive, cleanNan: true, remapPrefabs: false);

        Assert.True(repairResult.NanValuesSanitized > 0);
        float sanitizedVal = BitConverter.ToSingle(repairResult.RepairedArchive.SimulationData, 8);
        Assert.Equal(0.0f, sanitizedVal);
    }

    [Fact]
    public void Test_GenerateMetadataBin_CreatesBinaryHeader()
    {
        var meta = new MetadataHeader
        {
            CityName = "Binary City",
            GameVersion = "1.0.12f1",
            Population = 42000,
            ActiveMods = new List<string> { "ModX" }
        };

        byte[] binData = SaveRepairEngine.GenerateMetadataBin(meta);
        using var reader = new BinaryReader(new MemoryStream(binData), Encoding.UTF8);

        char[] magic = reader.ReadChars(4);
        Assert.Equal("COMB", new string(magic));
        int version = reader.ReadInt32();
        Assert.Equal(100, version);
        string cityName = reader.ReadString();
        Assert.Equal("Binary City", cityName);
    }

    [Fact]
    public void Test_SaveRepairEngine_IncludesMetadataBinInOutputArchive()
    {
        string outputPath = Path.Combine(_tempDirectory, "MetadataBinTest.coc");
        var archive = new SaveArchive
        {
            Metadata = new MetadataHeader { CityName = "MetaBin City" },
            EntitiesData = new byte[] { (byte)'C', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 }
        };

        var repairResult = SaveRepairEngine.Repair(archive, cleanNan: false, remapPrefabs: false);
        SaveRepairEngine.SaveArchiveFile(repairResult.RepairedArchive, outputPath);

        using var zip = ZipFile.OpenRead(outputPath);
        var metaBinEntry = zip.GetEntry("metadata.bin");
        Assert.NotNull(metaBinEntry);
    }

    [Fact]
    public void Test_SaveRepairEngine_SanitizesTruncatedJsonTextAssets()
    {
        var archive = new SaveArchive
        {
            Metadata = new MetadataHeader { CityName = "Truncated JSON Test" },
            EntitiesData = new byte[] { (byte)'C', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 },
            AdditionalFiles = new Dictionary<string, byte[]>
            {
                { "custom_setting.json", Encoding.UTF8.GetBytes("{\"settingName\": \"test\", \"value\": 123") } // Missing closing brace }
            }
        };

        var repairResult = SaveRepairEngine.Repair(archive, cleanNan: false, remapPrefabs: false);

        Assert.Contains(repairResult.FixesApplied, f => f.Contains("Repaired syntax and structure"));
        byte[] repairedBytes = repairResult.RepairedArchive.AdditionalFiles["custom_setting.json"];
        string repairedJson = Encoding.UTF8.GetString(repairedBytes);
        using var doc = JsonDocument.Parse(repairedJson);
        Assert.NotNull(doc);
    }
}
