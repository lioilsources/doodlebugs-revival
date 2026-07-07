/// <summary>
/// RPG upgrades bought with run points in the hangar. They persist across
/// rounds within a run (best-of-5) and reset when the run ends.
/// Serialized as int over the network - keep values stable.
/// </summary>
public enum RunUpgradeType
{
    Shield = 0,   // +1 max shield segment per level
    Hull = 1,     // +1 max health segment per level
    FireRate = 2, // +15% rate of fire per level
    Engine = 3    // +10% max speed per level
}

/// <summary>Static upgrade definitions (name, description, caps, effects).</summary>
public static class RunUpgrades
{
    public const int TypeCount = 4;
    public const int MaxLevel = 2;
    public const int CostPoints = 1;

    public const float FireRatePerLevel = 0.15f;
    public const float EnginePerLevel = 0.10f;

    public static string DisplayName(RunUpgradeType type)
    {
        switch (type)
        {
            case RunUpgradeType.Shield: return "SHIELD";
            case RunUpgradeType.Hull: return "HULL";
            case RunUpgradeType.FireRate: return "FIRE RATE";
            case RunUpgradeType.Engine: return "ENGINE";
            default: return "?";
        }
    }

    public static string Description(RunUpgradeType type)
    {
        switch (type)
        {
            case RunUpgradeType.Shield: return "+1 max shield";
            case RunUpgradeType.Hull: return "+1 max hull";
            case RunUpgradeType.FireRate: return "+15% ROF";
            case RunUpgradeType.Engine: return "+10% speed";
            default: return "";
        }
    }
}
