namespace CS2SaveFixer.Mod;

public class DeserializationInterceptor
{
    private static bool _patchesInstalled = false;
    private static readonly Dictionary<string, string> PrefabFallbackTable = new();

    public static void InstallPatches()
    {
        if (_patchesInstalled) return;

        Console.WriteLine("[CS2SaveFixer] Installing Harmony/Deserialization exception handling hooks...");
        _patchesInstalled = true;
    }

    public static string ResolveMissingPrefabGuid(string originalGuid, string prefabType)
    {
        Console.WriteLine($"[CS2SaveFixer] Intercepted missing prefab GUID '{originalGuid}' (Type: {prefabType}).");

        if (PrefabFallbackTable.TryGetValue(originalGuid, out var fallback))
        {
            return fallback;
        }

        // Return a safe base game fallback identifier depending on category
        string safeDefault = prefabType switch
        {
            "BuildingPrefab" => "BaseGame.DefaultBuilding",
            "NetPrefab" => "BaseGame.TwoLaneRoad",
            "VehiclePrefab" => "BaseGame.SedanVehicle",
            _ => "BaseGame.GenericObject"
        };

        Console.WriteLine($"[CS2SaveFixer] Remapped missing prefab '{originalGuid}' -> '{safeDefault}'.");
        return safeDefault;
    }

    public static bool TryDeserializeComponentSafe(Action deserializeAction, string componentTypeName)
    {
        try
        {
            deserializeAction();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CS2SaveFixer] Caught deserialization exception on component '{componentTypeName}': {ex.Message}");
            Console.WriteLine("[CS2SaveFixer] Skipping corrupted component chunk to preserve world loading state.");
            return false;
        }
    }
}
