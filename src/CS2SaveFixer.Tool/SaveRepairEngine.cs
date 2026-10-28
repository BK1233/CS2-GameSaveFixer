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

        if (entitiesBuffer.Length < 4)
        {
            // Synthesize minimal valid COSV header (8 bytes: 'COSV' + version 1)
            entitiesBuffer = new byte[] { (byte)'C', (byte)'O', (byte)'S', (byte)'V', 0, 0, 0, 1 };
            result.FixesApplied.Add("Initialized missing Entities.bin with valid COSV binary header.");
        }
        else
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

        byte[] simulationBuffer = (byte[])archive.SimulationData.Clone();

        if (cleanNan)
        {
            int nanEntities = SanitizeFloatingPointNumbers(entitiesBuffer);
            int nanSimulation = simulationBuffer.Length > 0 ? SanitizeFloatingPointNumbers(simulationBuffer) : 0;
            int nanCount = nanEntities + nanSimulation;
            result.NanValuesSanitized = nanCount;
            if (nanCount > 0)
            {
                result.FixesApplied.Add($"Sanitized {nanCount} corrupted floating-point (NaN/Infinity) values ({nanEntities} in Entities, {nanSimulation} in Simulation).");
            }
        }
        repaired.SimulationData = simulationBuffer;

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

        // Generate binary metadata.bin header
        byte[] metadataBin = GenerateMetadataBin(repaired.Metadata);
        repaired.AdditionalFiles["metadata.bin"] = metadataBin;

        // Always rebuild / refresh CID (Content Index Directory) index buffer to keep entry byte counts accurate
        repaired.AdditionalFiles["savegame.cid"] = BuildCidIndexData(repaired);
        result.FixesApplied.Add("Rebuilt and refreshed Content Index Directory (savegame.cid) and metadata.bin headers.");

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

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        byte[] metadataBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(archive.Metadata, jsonOptions));

        var entries = new List<(string Name, int Size)>
        {
            ("metadata.json", metadataBytes.Length),
            ("Entities.bin", archive.EntitiesData.Length)
        };

        if (archive.SimulationData.Length > 0)
        {
            entries.Add(("Simulation.bin", archive.SimulationData.Length));
        }

        foreach (var file in archive.AdditionalFiles)
        {
            string fileName = Path.GetFileName(file.Key);
            if (!fileName.Equals("savegame.cid", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Equals("world.cid", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Equals("metadata.json", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Equals("Entities.bin", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Equals("Simulation.bin", StringComparison.OrdinalIgnoreCase))
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
                byte[] zeroBytes = BitConverter.GetBytes(0.0f);
                Buffer.BlockCopy(zeroBytes, 0, buffer, i, 4);
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

    public static byte[] GenerateMetadataBin(MetadataHeader metadata)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        // Header Magic 'COMB' (Colossal Order Metadata Binary)
        writer.Write((byte)'C');
        writer.Write((byte)'O');
        writer.Write((byte)'M');
        writer.Write((byte)'B');
        writer.Write((int)100); // Version

        writer.Write(metadata.CityName ?? string.Empty);
        writer.Write(metadata.GameVersion ?? string.Empty);
        writer.Write(metadata.Population);
        writer.Write(metadata.SaveDate ?? string.Empty);

        var activeMods = metadata.ActiveMods ?? new List<string>();
        writer.Write(activeMods.Count);
        foreach (var mod in activeMods)
        {
            writer.Write(mod ?? string.Empty);
        }

        return ms.ToArray();
    }

    public static string CreateBackupFile(string inputPath)
    {
        string directory = Path.GetDirectoryName(inputPath) ?? ".";
        string fileName = Path.GetFileName(inputPath);
        string defaultBakPath = Path.Combine(directory, fileName + ".bak");

        string backupPath = defaultBakPath;
        if (File.Exists(defaultBakPath))
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            backupPath = Path.Combine(directory, $"{fileName}.{timestamp}.bak");
        }

        File.Copy(inputPath, backupPath, true);
        return backupPath;
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
        string normalizedName = Path.GetFileName(entryName);

        if (normalizedName.Equals("metadata.json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string json = Encoding.UTF8.GetString(bytes);
                archive.Metadata = JsonSerializer.Deserialize<MetadataHeader>(json) ?? new MetadataHeader();
            }
            catch
            {
                archive.Metadata = new MetadataHeader();
            }
        }
        else if (normalizedName.Equals("Entities.bin", StringComparison.OrdinalIgnoreCase) ||
                 normalizedName.EndsWith(".entities", StringComparison.OrdinalIgnoreCase))
        {
            archive.EntitiesData = bytes;
        }
        else if (normalizedName.Equals("Simulation.bin", StringComparison.OrdinalIgnoreCase) ||
                 normalizedName.EndsWith(".simulation", StringComparison.OrdinalIgnoreCase))
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

        // Write metadata.json with NoCompression (Store mode) as required by CS2 FileSystemDataSource
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        byte[] metadataBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(archive.Metadata, jsonOptions));

        var metaEntry = zip.CreateEntry("metadata.json", CompressionLevel.NoCompression);
        using (var stream = metaEntry.Open())
        {
            stream.Write(metadataBytes, 0, metadataBytes.Length);
        }

        // Write Entities.bin with NoCompression
        var entitiesEntry = zip.CreateEntry("Entities.bin", CompressionLevel.NoCompression);
        using (var stream = entitiesEntry.Open())
        {
            stream.Write(archive.EntitiesData, 0, archive.EntitiesData.Length);
        }

        // Write Simulation.bin with NoCompression
        if (archive.SimulationData.Length > 0)
        {
            var simEntry = zip.CreateEntry("Simulation.bin", CompressionLevel.NoCompression);
            using (var stream = simEntry.Open())
            {
                stream.Write(archive.SimulationData, 0, archive.SimulationData.Length);
            }
        }

        // Write additional entries including savegame.cid with NoCompression
        foreach (var file in archive.AdditionalFiles)
        {
            string fileName = Path.GetFileName(file.Key);
            if (fileName.Equals("metadata.json", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("Entities.bin", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("Simulation.bin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entry = zip.CreateEntry(file.Key, CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write(file.Value, 0, file.Value.Length);
        }
    }
}
