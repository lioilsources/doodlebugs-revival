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
    HeavyFlak = 3,
    Bomb = 4,
    Sniper = 5,
    Rocket = 6,
    Mine = 7
}

/// <summary>
/// Static weapon definitions. Every weapon is a parametric variant of the
/// same bullet prefab: damage, cooldown, force, gravity, pellet count,
/// spread, lifetime - plus explosion radius (AoE + terrain destruction),
/// acceleration (rocket thrust), drag and arm delay (mine). Maturity-profile
/// ROF/force still apply as global multipliers on top, so pilot progression
/// keeps meaning.
/// </summary>
public class WeaponProfile
{
    public WeaponType Type;
    public string DisplayName;
    public string Description;      // short line for the hangar card
    public int Damage = 1;          // per pellet, before the power-up multiplier
    public float Cooldown = 0.4f;   // seconds between shots at Novice ROF
    public float ForceMultiplier = 1f;   // scales base bullet force (20); 0 = released with plane speed only
    public float GravityScale = 0f;      // bullet drop
    public int PelletCount = 1;
    public float SpreadDegrees = 0f;     // total cone angle for multi-pellet
    public float BulletLifetime = 0f;    // 0 = unlimited; >0 despawns (short-range weapons)

    /// <summary>AoE radius in world units. >0 = damages every plane in range on
    /// impact and blows away foreground tiles in the same radius.</summary>
    public float ExplosionRadius = 0f;

    /// <summary>Forward thrust in force units per second (rocket). Applied by
    /// the server every physics step along the projectile's facing.</summary>
    public float Acceleration = 0f;

    /// <summary>Rigidbody linear damping - mines brake to a standstill.</summary>
    public float LinearDrag = 0f;

    /// <summary>Seconds after launch during which the projectile hits nothing
    /// (mine arming time). 0 = armed immediately.</summary>
    public float ArmDelay = 0f;

    /// <summary>Visual scale multiplier on the bullet prefab's base scale.</summary>
    public float ProjectileScale = 1f;

    /// <summary>Visual tint (bomb dark, rocket orange, mine near-black).</summary>
    public Color ProjectileTint = Color.white;

    /// <summary>Optional sprite override, loaded from Resources/Sprites/
    /// Projectiles. Null = the shared bullet sprite. When set, the tint is
    /// ignored — the art carries its own colours.</summary>
    public string ProjectileSpriteName;

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
        },
        new WeaponProfile
        {
            Type = WeaponType.Bomb,
            DisplayName = "AERO BOMB",
            Description = "digs craters, big boom",
            Damage = 3,
            Cooldown = 1.6f,
            ForceMultiplier = 0f,        // released - keeps only the plane's speed
            GravityScale = 1.2f,
            BulletLifetime = 8f,         // safety despawn if it never lands
            ExplosionRadius = 3.5f,
            // Scale 1: the Little Boy sprite is authored at its world size,
            // unlike the shared tracer sprite the 2.2 was compensating for.
            ProjectileScale = 1f,
            ProjectileTint = new Color(0.25f, 0.25f, 0.28f),
            ProjectileSpriteName = "bomb_littleboy",
            UpgradesTo = null
        },
        new WeaponProfile
        {
            Type = WeaponType.Sniper,
            DisplayName = "SNIPER",
            Description = "2 dmg, across the map",
            Damage = 2,
            Cooldown = 1.3f,
            ForceMultiplier = 2.6f,
            ProjectileScale = 1.3f,
            ProjectileTint = new Color(0.7f, 0.95f, 1f),
            UpgradesTo = null
        },
        new WeaponProfile
        {
            Type = WeaponType.Rocket,
            DisplayName = "ROCKET",
            Description = "accelerates, small AoE",
            Damage = 2,
            Cooldown = 1.1f,
            ForceMultiplier = 0.4f,
            Acceleration = 40f,
            BulletLifetime = 3.5f,
            ExplosionRadius = 1.6f,
            ProjectileScale = 1.7f,
            ProjectileTint = new Color(1f, 0.6f, 0.25f),
            UpgradesTo = null
        },
        new WeaponProfile
        {
            Type = WeaponType.Mine,
            DisplayName = "MINE",
            Description = "hides in clouds, 3 dmg",
            Damage = 3,
            Cooldown = 2.5f,
            ForceMultiplier = 0.35f,
            LinearDrag = 1.5f,           // brakes to a drift and lurks
            ArmDelay = 1.2f,
            BulletLifetime = 25f,
            ExplosionRadius = 1.5f,
            ProjectileScale = 2f,
            ProjectileTint = new Color(0.16f, 0.16f, 0.16f),
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
    {
        WeaponType.MG, WeaponType.TwinMG, WeaponType.Flak,
        WeaponType.Bomb, WeaponType.Sniper, WeaponType.Rocket, WeaponType.Mine
    };
}
