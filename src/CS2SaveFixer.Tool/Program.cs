namespace CS2SaveFixer.Tool;

class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("  Cities Skylines II - Save File Fixer & Analyzer ");
        Console.WriteLine("==================================================");

        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string inputPath = GetArgValue(args, "--input") ?? GetArgValue(args, "-i") ?? string.Empty;
        string outputPath = GetArgValue(args, "--output") ?? GetArgValue(args, "-o") ?? string.Empty;
        bool cleanNan = HasFlag(args, "--clean-nan");
        bool remapPrefabs = HasFlag(args, "--remap-missing-prefabs");

        if (string.IsNullOrEmpty(inputPath))
        {
            Console.WriteLine("Error: Please specify an input save archive using --input <path>");
            return 1;
        }

        if (Directory.Exists(inputPath))
        {
            Console.WriteLine($"Error: Input path '{inputPath}' is a directory, not a save file (.coc / .zip / .cok). Please specify the full path to your save file.");
            return 1;
        }

        try
        {
            Console.WriteLine($"[+] Loading save archive: {inputPath}");
            var archive = SaveRepairEngine.LoadArchive(inputPath);
            Console.WriteLine($"[+] Loaded City: {archive.Metadata.CityName} (Population: {archive.Metadata.Population})");

            if (command == "analyze")
            {
                Console.WriteLine("\n--- Save File Diagnostics ---");
                var issues = SaveAnalyzer.Analyze(archive);
                foreach (var issue in issues)
                {
                    Console.WriteLine($"[{issue.Severity}] {issue.Message}");
                }
                return 0;
            }
            else if (command == "fix" || command == "repair")
            {
                string defaultFileName = Path.GetFileNameWithoutExtension(inputPath) + "_repaired" + Path.GetExtension(inputPath);

                if (string.IsNullOrEmpty(outputPath))
                {
                    outputPath = Path.Combine(Path.GetDirectoryName(inputPath) ?? ".", defaultFileName);
                }
                else if (Directory.Exists(outputPath) || !Path.HasExtension(outputPath))
                {
                    outputPath = Path.Combine(outputPath, defaultFileName);
                }

                Console.WriteLine("\n--- Executing Save Repair Operations ---");
                var repairResult = SaveRepairEngine.Repair(archive, cleanNan, remapPrefabs);

                Console.WriteLine("\n--- Summary of Issues Found & Fixes Applied ---");
                if (repairResult.FixesApplied.Count > 0)
                {
                    foreach (var fix in repairResult.FixesApplied)
                    {
                        Console.WriteLine($" [✓] {fix}");
                    }
                }
                else
                {
                    Console.WriteLine(" [i] No corruption issues found requiring modification.");
                }

                Console.WriteLine($"\n[+] Saving repaired archive to: {outputPath}");
                SaveRepairEngine.SaveArchiveFile(repairResult.RepairedArchive, outputPath);

                Console.WriteLine("[✓] Repair complete! You may now test loading the save in CS2.");
                return 0;
            }
            else
            {
                Console.WriteLine($"Error: Unknown command '{command}'. Use 'analyze' or 'fix'.");
                PrintUsage();
                return 1;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"[!] File Permission Error: Access denied when reading/writing path. ({ex.Message})");
            Console.WriteLine("    Ensure your terminal has administrative/write permissions or specify a valid file path for --output.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Critical Error: {ex.Message}");
            return 1;
        }
    }

    static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    static bool HasFlag(string[] args, string flag)
    {
        return args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
    }

    static void PrintUsage()
    {
        Console.WriteLine("\nUsage:");
        Console.WriteLine("  CS2SaveFixer.Tool <command> --input <path> [options]");
        Console.WriteLine("\nCommands:");
        Console.WriteLine("  analyze     Inspect and log corruption issues in a save archive.");
        Console.WriteLine("  fix         Repair corrupt binary streams and missing references.");
        Console.WriteLine("\nOptions:");
        Console.WriteLine("  --input, -i <path>           Path to .coc / .zip / .cok save file.");
        Console.WriteLine("  --output, -o <path>          Path or directory for fixed output file.");
        Console.WriteLine("  --clean-nan                  Scan and sanitize NaN/Infinity float values.");
        Console.WriteLine("  --remap-missing-prefabs      Remap unmapped/null prefab GUIDs to defaults.");
    }
}
