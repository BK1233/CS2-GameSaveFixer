using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CS2SaveFixer.Tool;

public class RepairResult
{
    public SaveArchive RepairedArchive { get; set; } = new();
    public List<string> FixesApplied { get; set; } = new();
    public int NanValuesSanitized { get; set; } = 0;
    public int NullGuidsRemapped { get; set; } = 0;
}

public class SaveRepairEngine
{
    private static readonly byte[] FallbackGuidBytes = Guid.Parse("11111111-2222-3333-4444-555555555555").ToByteArray();
    private static readonly byte[] ZeroFloatBytes = BitConverter.GetBytes(0.0f);

    public static RepairResult Repair(SaveArchive archive, bool cleanNan, bool remapPrefabs)
    {
        var result = new RepairResult();
        var repaired = new SaveArchive
        {
            Metadata = archive.Metadata,
            SimulationData = (byte[])archive.SimulationData.Clone(),
            AdditionalFiles = new Dictionary<string, byte[]>(archive.AdditionalFiles)
        };

        byte[] entitiesBuffer = (byte[])archive.EntitiesData.Clone();

        if (entitiesBuffer.Length >= 4)
        {
            if (entitiesBuffer[0] != (byte)'C' || entitiesBuffer[1] != (byte)'O' ||
                entitiesBuffer[2] != (byte)'S' || entitiesBuffer[3] != (byte)'V')
            {
                // Enforce COSV Header
                entitiesBuffer[0] = (byte)'C';
                entitiesBuffer[1] = (byte)'O';
                entitiesBuffer[2] = (byte)'S';
                entitiesBuffer[3] = (byte)'V';
                result.FixesApplied.Add("Restored missing or corrupted COSV magic header bytes in Entities.bin.");
            }
        }

        if (cleanNan)
        {
            int nanCount = SanitizeFloatingPointNumbers(entitiesBuffer);
            result.NanValuesSanitized = nanCount;
            if (nanCount > 0)
            {
                result.FixesApplied.Add($"Sanitized {nanCount} corrupted floating-point (NaN/Infinity) values in entity component data.");
            }
        }

        if (remapPrefabs)
        {
            int guidCount = RemapNullGuids(entitiesBuffer);
            result.NullGuidsRemapped = guidCount;
            if (guidCount > 0)
            {
                result.FixesApplied.Add($"Remapped {guidCount} unmapped or missing asset GUID references to safe fallback prefabs.");
            }
        }

        repaired.EntitiesData = entitiesBuffer;

        // Clean metadata active mods list
        int originalModCount = repaired.Metadata.ActiveMods.Count;
        repaired.Metadata.ActiveMods = repaired.Metadata.ActiveMods.Distinct().ToList();
        if (originalModCount != repaired.Metadata.ActiveMods.Count)
        {
            result.FixesApplied.Add($"Deduplicated active mods metadata list ({originalModCount} -> {repaired.Metadata.ActiveMods.Count}).");
        }

        // Rebuild / repair CID (Content Index Directory) index buffer
        repaired.AdditionalFiles["savegame.cid"] = BuildCidIndexData(repaired);
        result.FixesApplied.Add("Rebuilt and refreshed Content Index Directory (savegame.cid) manifest.");

        result.RepairedArchive = repaired;
        return result;
    }

    public static byte[] BuildCidIndexData(SaveArchive archive)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        // CID Header Magic 'COCD' (Colossal Order Content Directory)
        writer.Write((byte)'C');
        writer.Write((byte)'O');
        writer.Write((byte)'C');
        writer.Write((byte)'D');
        writer.Write((int)100); // Version

        var entries = new List<(string Name, int Size)>
        {
            ("metadata.json", Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(archive.Metadata))),
            ("Entities.bin", archive.EntitiesData.Length)
        };

        if (archive.SimulationData.Length > 0)
        {
            entries.Add(("Simulation.bin", archive.SimulationData.Length));
        }

        foreach (var file in archive.AdditionalFiles)
        {
            if (!file.Key.Equals("savegame.cid", StringComparison.OrdinalIgnoreCase) &&
                !file.Key.Equals("world.cid", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add((file.Key, file.Value.Length));
            }
        }

        writer.Write(entries.Count);
        foreach (var entry in entries)
        {
            writer.Write(entry.Name);
            writer.Write(entry.Size);
        }

        return ms.ToArray();
    }

    private static int SanitizeFloatingPointNumbers(byte[] buffer)
    {
        int count = 0;
        // Guard: skip COSV header (8 bytes)
        int startOffset = buffer.Length >= 8 ? 8 : 0;
        for (int i = startOffset; i <= buffer.Length - 4; i += 4)
        {
            float val = BitConverter.ToSingle(buffer, i);
            if (float.IsNaN(val) || float.IsInfinity(val))
            {
                Buffer.BlockCopy(ZeroFloatBytes, 0, buffer, i, 4);
                count++;
            }
        }
        return count;
    }

    private static int RemapNullGuids(byte[] buffer)
    {
        int count = 0;
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
                count++;
            }
        }
        return count;
    }

    public static SaveArchive LoadArchive(string filePath)
    {
        var archive = new SaveArchive();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Save file not found: {filePath}");
        }

        // Try standard ZipArchive loading
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            foreach (var entry in zip.Entries)
            {
                using var ms = new MemoryStream();
                using var stream = entry.Open();
                stream.CopyTo(ms);
                byte[] bytes = ms.ToArray();

                ProcessArchiveEntry(archive, entry.FullName, bytes);
            }
        }
        catch (InvalidDataException)
        {
            // Truncated/corrupted ZIP header recovery mode: read raw binary bytes directly
            byte[] rawBytes = File.ReadAllBytes(filePath);
            if (rawBytes.Length == 0)
            {
                throw new InvalidDataException("Save file is completely empty (0 bytes).");
            }

            Console.WriteLine("[!] Warning: ZIP central directory header missing or corrupted. Attempting raw binary stream recovery...");
            archive.EntitiesData = rawBytes;
            archive.Metadata = new MetadataHeader
            {
                CityName = Path.GetFileNameWithoutExtension(filePath),
                GameVersion = "1.0.0 (Recovered)"
            };
        }

        return archive;
    }

    private static void ProcessArchiveEntry(SaveArchive archive, string entryName, byte[] bytes)
    {
        if (entryName.Equals("metadata.json", StringComparison.OrdinalIgnoreCase))
        {
            string json = Encoding.UTF8.GetString(bytes);
            archive.Metadata = JsonSerializer.Deserialize<MetadataHeader>(json) ?? new MetadataHeader();
        }
        else if (entryName.Equals("Entities.bin", StringComparison.OrdinalIgnoreCase) || entryName.EndsWith("Entities.bin"))
        {
            archive.EntitiesData = bytes;
        }
        else if (entryName.Equals("Simulation.bin", StringComparison.OrdinalIgnoreCase) || entryName.EndsWith("Simulation.bin"))
        {
            archive.SimulationData = bytes;
        }
        else
        {
            archive.AdditionalFiles[entryName] = bytes;
        }
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

        // Ensure savegame.cid index is built before saving
        if (!archive.AdditionalFiles.ContainsKey("savegame.cid"))
        {
            archive.AdditionalFiles["savegame.cid"] = BuildCidIndexData(archive);
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

        // Write additional entries including savegame.cid
        foreach (var file in archive.AdditionalFiles)
        {
            var entry = zip.CreateEntry(file.Key);
            using var stream = entry.Open();
            stream.Write(file.Value, 0, file.Value.Length);
        }
    }
}
