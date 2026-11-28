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
            case RunUpgradeType.Shield: return Loc.T("upgrade.Shield.name", "SHIELD");
            case RunUpgradeType.Hull: return Loc.T("upgrade.Hull.name", "HULL");
            case RunUpgradeType.FireRate: return Loc.T("upgrade.FireRate.name", "FIRE RATE");
            case RunUpgradeType.Engine: return Loc.T("upgrade.Engine.name", "ENGINE");
            default: return "?";
        }
    }

    public static string Description(RunUpgradeType type)
    {
        switch (type)
        {
            case RunUpgradeType.Shield: return Loc.T("upgrade.Shield.desc", "+1 max shield");
            case RunUpgradeType.Hull: return Loc.T("upgrade.Hull.desc", "+1 max hull");
            case RunUpgradeType.FireRate: return Loc.T("upgrade.FireRate.desc", "+15% ROF");
            case RunUpgradeType.Engine: return Loc.T("upgrade.Engine.desc", "+10% speed");
            default: return "";
        }
    }
}
