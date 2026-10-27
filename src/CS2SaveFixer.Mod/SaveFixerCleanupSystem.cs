namespace CS2SaveFixer.Mod;

public class SimulatedEntity
{
    public int Id { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public bool HasValidPrefab { get; set; } = true;
    public bool IsOrphaned { get; set; } = false;
}

public class SaveFixerCleanupSystem
{
    public static void Initialize()
    {
        Console.WriteLine("[CS2SaveFixer] SaveFixerCleanupSystem registered for ECS world updates.");
    }

    public static int RunSanitizePass(List<SimulatedEntity> entities)
    {
        int fixedCount = 0;

        foreach (var entity in entities.ToList())
        {
            // 1. Sanitize float coordinates (NaN / Inf)
            if (float.IsNaN(entity.PositionX) || float.IsInfinity(entity.PositionX) ||
                float.IsNaN(entity.PositionY) || float.IsInfinity(entity.PositionY) ||
                float.IsNaN(entity.PositionZ) || float.IsInfinity(entity.PositionZ))
            {
                entity.PositionX = 0f;
                entity.PositionY = 0f;
                entity.PositionZ = 0f;
                fixedCount++;
                Console.WriteLine($"[CS2SaveFixer] Sanitized invalid position vector on Entity ID {entity.Id}.");
            }

            // 2. Remove invalid/orphaned simulation entities
            if (entity.IsOrphaned || !entity.HasValidPrefab)
            {
                entities.Remove(entity);
                fixedCount++;
                Console.WriteLine($"[CS2SaveFixer] Purged orphaned entity ID {entity.Id}.");
            }
        }

        Console.WriteLine($"[CS2SaveFixer] Sanitize pass complete. Fixed/cleaned {fixedCount} entity issues.");
        return fixedCount;
    }
}
