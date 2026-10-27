using System.Text.Json;

namespace CS2SaveFixer.Tool;

public class MetadataHeader
{
    public string CityName { get; set; } = "Unknown City";
    public string GameVersion { get; set; } = "1.0.0";
    public int Population { get; set; } = 0;
    public string SaveDate { get; set; } = DateTime.UtcNow.ToString("o");
    public List<string> ActiveMods { get; set; } = new();
}

public class SaveArchive
{
    public MetadataHeader Metadata { get; set; } = new();
    public byte[] EntitiesData { get; set; } = Array.Empty<byte>();
    public byte[] SimulationData { get; set; } = Array.Empty<byte>();
    public Dictionary<string, byte[]> AdditionalFiles { get; set; } = new();
}

public class ValidationIssue
{
    public string Severity { get; set; } = "INFO"; // INFO, WARNING, ERROR
    public string Message { get; set; } = string.Empty;
}

public class SaveAnalyzer
{
    public static List<ValidationIssue> Analyze(SaveArchive archive)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(archive.Metadata.CityName))
        {
            issues.Add(new ValidationIssue { Severity = "WARNING", Message = "Save metadata contains empty CityName." });
        }

        if (archive.EntitiesData.Length == 0)
        {
            issues.Add(new ValidationIssue { Severity = "ERROR", Message = "EntitiesData stream is empty or corrupt." });
            return issues;
        }

        // Check COSV magic bytes
        if (!SaveFormatUtils.HasValidCosvHeader(archive.EntitiesData))
        {
            issues.Add(new ValidationIssue { Severity = "ERROR", Message = "Invalid magic bytes in Entities.bin (expected COSV)." });
        }

        // Scan EntitiesData binary stream for float NaN / Infinity
        int nanCount = CountFloatingPointAnomalies(archive.EntitiesData);
        if (nanCount > 0)
        {
            issues.Add(new ValidationIssue { Severity = "ERROR", Message = $"Found {nanCount} NaN or Infinity floating-point corruptions in Entities stream." });
        }

        // Check missing prefab GUID indicators (all zeros or null markers)
        int missingGuidCount = CountMissingGuidReferences(archive.EntitiesData);
        if (missingGuidCount > 0)
        {
            issues.Add(new ValidationIssue { Severity = "WARNING", Message = $"Found {missingGuidCount} unmapped or missing asset GUID references." });
        }

        if (issues.Count == 0)
        {
            issues.Add(new ValidationIssue { Severity = "INFO", Message = "No save corruption detected. Save file is healthy." });
        }

        return issues;
    }

    private static int CountFloatingPointAnomalies(byte[] buffer)
    {
        int count = 0;
        for (int i = 0; i <= buffer.Length - 4; i += 4)
        {
            float val = BitConverter.ToSingle(buffer, i);
            if (float.IsNaN(val) || float.IsInfinity(val))
            {
                count++;
            }
        }
        return count;
    }

    private static int CountMissingGuidReferences(byte[] buffer)
    {
        int count = 0;
        byte[] nullGuid = new byte[16];
        for (int i = 0; i <= buffer.Length - 16; i += 16)
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
            if (isNull) count++;
        }
        return count;
    }
}
