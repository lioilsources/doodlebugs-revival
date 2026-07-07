using UnityEngine;

/// <summary>
/// Weapon ids. Serialized over the network as int - keep values stable.
/// Tier chains: MG -> TwinMG; Flak -> HeavyFlak (crate pickups climb the chain).
/// </summary>
public enum WeaponType
{
    MG = 0,
    TwinMG = 1,
    Flak = 2,
    HeavyFlak = 3
}

/// <summary>
/// Static weapon definitions. Every weapon is a parametric variant of the
/// same bullet prefab: damage, cooldown, force, gravity, pellet count,
/// spread and lifetime. Maturity-profile ROF/force still apply as global
/// multipliers on top, so pilot progression keeps meaning.
/// </summary>
public class WeaponProfile
{
    public WeaponType Type;
    public string DisplayName;
    public string Description;      // short line for the hangar card
    public int Damage = 1;          // per pellet, before the power-up multiplier
    public float Cooldown = 0.4f;   // seconds between shots at Novice ROF
    public float ForceMultiplier = 1f;   // scales base bullet force (20)
    public float GravityScale = 0f;      // bullet drop
    public int PelletCount = 1;
    public float SpreadDegrees = 0f;     // total cone angle for multi-pellet
    public float BulletLifetime = 0f;    // 0 = unlimited; >0 despawns (short-range weapons)

    /// <summary>Next tier when a weapon crate is collected (null = maxed).</summary>
    public WeaponType? UpgradesTo;

    private static readonly WeaponProfile[] Profiles =
    {
        new WeaponProfile
        {
            Type = WeaponType.MG,
            DisplayName = "MG",
            Description = "1 bullet, balanced",
            Damage = 1,
            Cooldown = 0.4f,
            ForceMultiplier = 1f,
            PelletCount = 1,
            UpgradesTo = WeaponType.TwinMG
        },
        new WeaponProfile
        {
            Type = WeaponType.TwinMG,
            DisplayName = "TWIN MG",
            Description = "2 bullets, fast",
            Damage = 1,
            Cooldown = 0.45f,
            ForceMultiplier = 1f,
            PelletCount = 2,
            SpreadDegrees = 4f,
            UpgradesTo = null
        },
        new WeaponProfile
        {
            Type = WeaponType.Flak,
            DisplayName = "FLAK",
            Description = "5 pellets, close range",
            Damage = 1,
            Cooldown = 0.8f,
            ForceMultiplier = 0.85f,
            PelletCount = 5,
            SpreadDegrees = 24f,
            BulletLifetime = 0.45f,
            UpgradesTo = WeaponType.HeavyFlak
        },
        new WeaponProfile
        {
            Type = WeaponType.HeavyFlak,
            DisplayName = "HEAVY FLAK",
            Description = "7 pellets, wider cone",
            Damage = 1,
            Cooldown = 0.85f,
            ForceMultiplier = 0.85f,
            PelletCount = 7,
            SpreadDegrees = 32f,
            BulletLifetime = 0.5f,
            UpgradesTo = null
        }
    };

    public static WeaponProfile Get(WeaponType type)
    {
        int idx = (int)type;
        if (idx < 0 || idx >= Profiles.Length) return Profiles[0];
        return Profiles[idx];
    }

    public static WeaponProfile Get(int weaponId) => Get((WeaponType)weaponId);

    public static int Count => Profiles.Length;

    /// <summary>Weapons offered in the hangar draft (top tiers stay crate-only).</summary>
    public static readonly WeaponType[] DraftPool =
        { WeaponType.MG, WeaponType.TwinMG, WeaponType.Flak };
}
