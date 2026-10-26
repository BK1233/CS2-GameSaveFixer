using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CS2SaveFixer.Tool;

public class SaveRepairEngine
{
    private static readonly byte[] FallbackGuidBytes = Guid.Parse("11111111-2222-3333-4444-555555555555").ToByteArray();

    public static SaveArchive Repair(SaveArchive archive, bool cleanNan, bool remapPrefabs)
    {
        var repaired = new SaveArchive
        {
            Metadata = archive.Metadata,
            SimulationData = (byte[])archive.SimulationData.Clone(),
            AdditionalFiles = new Dictionary<string, byte[]>(archive.AdditionalFiles)
        };

        byte[] entitiesBuffer = (byte[])archive.EntitiesData.Clone();

        if (entitiesBuffer.Length >= 4)
        {
            // Enforce COSV Header
            entitiesBuffer[0] = (byte)'C';
            entitiesBuffer[1] = (byte)'O';
            entitiesBuffer[2] = (byte)'S';
            entitiesBuffer[3] = (byte)'V';
        }

        if (cleanNan)
        {
            SanitizeFloatingPointNumbers(entitiesBuffer);
        }

        if (remapPrefabs)
        {
            RemapNullGuids(entitiesBuffer);
        }

        repaired.EntitiesData = entitiesBuffer;

        // Clean metadata active mods list if needed
        repaired.Metadata.ActiveMods = repaired.Metadata.ActiveMods.Distinct().ToList();

        return repaired;
    }

    private static void SanitizeFloatingPointNumbers(byte[] buffer)
    {
        // Guard: skip COSV header (8 bytes)
        int startOffset = buffer.Length >= 8 ? 8 : 0;
        for (int i = startOffset; i <= buffer.Length - 4; i += 4)
        {
            float val = BitConverter.ToSingle(buffer, i);
            if (float.IsNaN(val) || float.IsInfinity(val))
            {
                byte[] zeroBytes = BitConverter.GetBytes(0.0f);
                Buffer.BlockCopy(zeroBytes, 0, buffer, i, 4);
            }
        }
    }

    private static void RemapNullGuids(byte[] buffer)
    {
        // Guard: skip COSV header (8 bytes)
        int startOffset = buffer.Length >= 8 ? 8 : 0;
        for (int i = startOffset; i <= buffer.Length - 16; i += 16)
        {
            bool isNull = true;
            for (int j = 0; j < 16; j++)
            {
                if (buffer[i + j] != 0)
                {
                    isNull = false;
                    break;
                }
            }
            if (isNull)
            {
                Buffer.BlockCopy(FallbackGuidBytes, 0, buffer, i, 16);
            }
        }
    }

    public static SaveArchive LoadArchive(string filePath)
    {
        var archive = new SaveArchive();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Save file not found: {filePath}");
        }

        using var zip = ZipFile.OpenRead(filePath);
        foreach (var entry in zip.Entries)
        {
            using var ms = new MemoryStream();
            using var stream = entry.Open();
            stream.CopyTo(ms);
            byte[] bytes = ms.ToArray();

            if (entry.FullName.Equals("metadata.json", StringComparison.OrdinalIgnoreCase))
            {
                string json = Encoding.UTF8.GetString(bytes);
                archive.Metadata = JsonSerializer.Deserialize<MetadataHeader>(json) ?? new MetadataHeader();
            }
            else if (entry.FullName.Equals("Entities.bin", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith("Entities.bin"))
            {
                archive.EntitiesData = bytes;
            }
            else if (entry.FullName.Equals("Simulation.bin", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith("Simulation.bin"))
            {
                archive.SimulationData = bytes;
            }
            else
            {
                archive.AdditionalFiles[entry.FullName] = bytes;
            }
        }

        return archive;
    }

    public static void SaveArchiveFile(SaveArchive archive, string outputPath)
    {
        // Ensure parent directory exists
        string? parentDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var fileStream = new FileStream(outputPath, FileMode.Create);
        using var zip = new ZipArchive(fileStream, ZipArchiveMode.Create);

        // Write metadata.json
        var metaEntry = zip.CreateEntry("metadata.json");
        using (var writer = new StreamWriter(metaEntry.Open()))
        {
            writer.Write(JsonSerializer.Serialize(archive.Metadata, new JsonSerializerOptions { WriteIndented = true }));
        }

        // Write Entities.bin
        var entitiesEntry = zip.CreateEntry("Entities.bin");
        using (var stream = entitiesEntry.Open())
        {
            stream.Write(archive.EntitiesData, 0, archive.EntitiesData.Length);
        }

        // Write Simulation.bin
        if (archive.SimulationData.Length > 0)
        {
            var simEntry = zip.CreateEntry("Simulation.bin");
            using (var stream = simEntry.Open())
            {
                stream.Write(archive.SimulationData, 0, archive.SimulationData.Length);
            }
        }

        // Write additional entries
        foreach (var file in archive.AdditionalFiles)
        {
            var entry = zip.CreateEntry(file.Key);
            using var stream = entry.Open();
            stream.Write(file.Value, 0, file.Value.Length);
        }
    }
}
