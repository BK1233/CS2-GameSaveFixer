namespace CS2SaveFixer.Mod;

public interface IMod
{
    void OnLoad();
    void OnUnload();
}

public class SaveFixerMod : IMod
{
    public static SaveFixerMod Instance { get; private set; } = null!;
    public bool IsFallbackPrefabResolverEnabled { get; set; } = true;
    public bool IsAutomaticCleanupSystemEnabled { get; set; } = true;

    public void OnLoad()
    {
        Instance = this;
        Console.WriteLine("[CS2SaveFixer] CS2 Save Fixer Runtime Mod loaded successfully.");
        InitializeSystems();
    }

    public void OnUnload()
    {
        Console.WriteLine("[CS2SaveFixer] CS2 Save Fixer Runtime Mod unloaded.");
    }

    private void InitializeSystems()
    {
        DeserializationInterceptor.InstallPatches();
        SaveFixerCleanupSystem.Initialize();
    }
}
