using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Weapon ids. Serialized over the network as int - keep values stable,
/// only ever append. Tier chains (crate = +1 tier, Prompts/29 D2): every
/// draftable weapon has a Mk II.
/// </summary>
public enum WeaponType
{
    MG = 0,
    TwinMG = 1,        // MG Mk II
    Flak = 2,
    HeavyFlak = 3,     // Flak Mk II
    Bomb = 4,
    Sniper = 5,        // "LANCE" since Prompts/29: hold to charge, release to fire
    Rocket = 6,
    Mine = 7,
    Stinger = 8,       // fires backwards
    Airburst = 9,      // hold = flies, release = bursts
    Seeker = 10,       // homing
    Bomblet = 11,      // cluster child (never drafted)
    BigOne = 12,       // special (supply drop)
    MiniSeeker = 13,   // special SWARM child
    Emp = 14,          // special: instant pulse, no projectile
    ClusterBomb = 15,  // Bomb Mk II
    HeavyRocket = 16,  // Rocket Mk II
    TripleMine = 17,   // Mine Mk II
    LanceMk2 = 18,     // Lance Mk II: pierces
    TwinStinger = 19,  // Stinger Mk II
    AirburstMk2 = 20,
    TwinSeeker = 21,   // Seeker Mk II
    Swarm = 22,        // special: 5 mini seekers
}

/// <summary>What a weapon is for the UI and the loadout (Prompts/29 §3).</summary>
public enum WeaponCategory { Weapon, Upgrade, Special, Child }

/// <summary>Shape of the animated flight-path sketch on a draft card (Prompts/30 §4).</summary>
public enum PreviewKind { Straight, Spread, Drop, Lurk, Charge, Backward, Burst, Homing }

/// <summary>
/// Static weapon definitions. Every weapon is a parametric variant of the
/// same bullet prefab: damage, cooldown, force, gravity, pellet count,
/// spread, lifetime - plus explosion radius (AoE + terrain destruction),
/// knockback, acceleration (rocket thrust), drag and arm delay (mine),
/// homing, charge and remote detonation (Prompts/29).
/// </summary>
public class WeaponProfile
{
    public WeaponType Type;
    public string DisplayName;
    public string Description;      // one line for the hangar card
    public string Verb = "SHOOT";   // card band label (Prompts/29 §7)
    public PreviewKind Preview = PreviewKind.Straight;
    public WeaponCategory Category = WeaponCategory.Weapon;
    public float StatDamage = 0.3f, StatRate = 0.5f, StatRange = 0.5f;   // card bars, 0..1

    public int Damage = 1;          // per pellet, before the power-up multiplier
    public float Cooldown = 0.4f;   // seconds between shots
    public float ForceMultiplier = 1f;   // scales base bullet force (20); 0 = released with plane speed only
    public float GravityScale = 0f;      // bullet drop
    public int PelletCount = 1;
    public float SpreadDegrees = 0f;     // total cone angle for multi-pellet
    public float BulletLifetime = 0f;    // 0 = unlimited; >0 despawns (short-range weapons)
    public bool ExplodeOnExpire;         // lifetime end = detonation, not a fizzle

    /// <summary>AoE radius in world units. >0 = damages every plane in range on
    /// impact and blows away foreground tiles in the same radius.</summary>
    public float ExplosionRadius = 0f;

    /// <summary>Impulse (u/s) given to planes near the blast (Prompts/29 D3).
    /// Reaches 1.6 x ExplosionRadius, falling off linearly.</summary>
    public float Knockback = 0f;

    /// <summary>Forward thrust in force units per second (rocket).</summary>
    public float Acceleration = 0f;

    /// <summary>Speed cap in u/s (0 = none) - seekers must stay slower than nitro.</summary>
    public float MaxSpeed = 0f;

    /// <summary>Rigidbody linear damping - mines brake to a standstill.</summary>
    public float LinearDrag = 0f;

    /// <summary>Seconds after launch during which the projectile hits nothing.</summary>
    public float ArmDelay = 0f;

    /// <summary>Fire direction relative to the nose, degrees (180 = out of the tail).</summary>
    public float FireAngleOffset = 0f;

    // Homing (Seeker): lock at launch on the nearest enemy inside the cone.
    public float HomingTurnRate = 0f;    // deg/s, 0 = no homing
    public float HomingConeDeg = 50f;
    public float HomingRange = 25f;
    public float HomingSeconds = 5f;

    /// <summary>Hold the trigger to keep it flying, release to burst (Airburst).</summary>
    public bool RemoteDetonate;

    // Charge (Lance): hold 0..ChargeSeconds, damage/force lerp to the max.
    public float ChargeSeconds = 0f;
    public int ChargeDamageMax = 0;
    public float ChargeForceMax = 0f;
    public bool Pierce;                  // passes its first plane / cloud

    // Split (cluster): on detonation spawn SplitCount of SplitInto.
    public WeaponType? SplitInto;
    public int SplitCount = 0;

    /// <summary>Shots a special carries before the drafted weapon returns.</summary>
    public int SpecialAmmo = 0;

    /// <summary>Visual scale multiplier on the bullet prefab's base scale.</summary>
    public float ProjectileScale = 1f;

    /// <summary>Visual tint (bomb dark, rocket orange, mine near-black).</summary>
    public Color ProjectileTint = Color.white;

    /// <summary>Optional sprite override, loaded from Resources/Sprites/Projectiles.</summary>
    public string ProjectileSpriteName;

    /// <summary>Art form (tracer, pellet, bomb, bolt, rocket, mine); null = by type.</summary>
    public string Form;

    /// <summary>Trigger icon in Resources/Sprites/Weapons (Prompts/26 D13).</summary>
    public string IconSpriteName;

    /// <summary>Next tier when a weapon crate is collected (null = maxed).</summary>
    public WeaponType? UpgradesTo;

    public bool IsDraftable => Category == WeaponCategory.Weapon;

    private static readonly WeaponProfile[] Profiles =
    {
        new WeaponProfile
        {
            Type = WeaponType.MG, DisplayName = "MG", IconSpriteName = "weapon_mg",
            Description = "One bullet. Never lets you down.", Verb = "SHOOT",
            StatDamage = 0.33f, StatRate = 0.8f, StatRange = 0.7f,
            Damage = 1, Cooldown = 0.4f, UpgradesTo = WeaponType.TwinMG
        },
        new WeaponProfile
        {
            Type = WeaponType.TwinMG, DisplayName = "TWIN MG", IconSpriteName = "weapon_twinmg",
            Description = "Two bullets per shot.", Verb = "SHOOT", Category = WeaponCategory.Upgrade,
            StatDamage = 0.5f, StatRate = 0.75f, StatRange = 0.7f,
            Damage = 1, Cooldown = 0.45f, PelletCount = 2, SpreadDegrees = 4f
        },
        new WeaponProfile
        {
            Type = WeaponType.Flak, DisplayName = "FLAK", IconSpriteName = "weapon_flak",
            Description = "Five pellets. Deadly up close.", Verb = "SHOTGUN", Preview = PreviewKind.Spread,
            StatDamage = 0.6f, StatRate = 0.48f, StatRange = 0.26f,
            Damage = 1, Cooldown = 0.8f, ForceMultiplier = 0.85f, PelletCount = 5, SpreadDegrees = 24f,
            BulletLifetime = 0.45f, Knockback = 3f, UpgradesTo = WeaponType.HeavyFlak
        },
        new WeaponProfile
        {
            Type = WeaponType.HeavyFlak, DisplayName = "HEAVY FLAK", IconSpriteName = "weapon_heavyflak",
            Description = "Seven pellets, wider cone.", Verb = "SHOTGUN", Preview = PreviewKind.Spread,
            Category = WeaponCategory.Upgrade,
            StatDamage = 0.75f, StatRate = 0.45f, StatRange = 0.28f,
            Damage = 1, Cooldown = 0.85f, ForceMultiplier = 0.85f, PelletCount = 7, SpreadDegrees = 32f,
            BulletLifetime = 0.5f, Knockback = 3f
        },
        new WeaponProfile
        {
            Type = WeaponType.Bomb, DisplayName = "AERO BOMB", IconSpriteName = "weapon_bomb",
            Description = "Drop it. Digs craters, throws planes.", Verb = "DROP", Preview = PreviewKind.Drop,
            StatDamage = 1f, StatRate = 0.2f, StatRange = 0.35f,
            Damage = 3, Cooldown = 1.6f, ForceMultiplier = 0f, GravityScale = 1.2f, BulletLifetime = 8f,
            ExplosionRadius = 3.5f, Knockback = 12f,
            ProjectileScale = 1f, ProjectileTint = new Color(0.25f, 0.25f, 0.28f),
            ProjectileSpriteName = "bomb_littleboy", UpgradesTo = WeaponType.ClusterBomb
        },
        new WeaponProfile
        {
            Type = WeaponType.Sniper, DisplayName = "LANCE", IconSpriteName = "weapon_sniper",
            Description = "Hold to charge, release. Up to 3 damage.", Verb = "CHARGE", Preview = PreviewKind.Charge,
            StatDamage = 0.9f, StatRate = 0.25f, StatRange = 1f,
            Damage = 1, Cooldown = 1.3f, ForceMultiplier = 1.6f, ChargeSeconds = 0.9f, ChargeDamageMax = 3,
            ChargeForceMax = 3.2f, ProjectileScale = 1.3f, ProjectileTint = new Color(0.7f, 0.95f, 1f),
            Form = "bolt", UpgradesTo = WeaponType.LanceMk2
        },
        new WeaponProfile
        {
            Type = WeaponType.Rocket, DisplayName = "ROCKET", IconSpriteName = "weapon_rocket",
            Description = "Accelerates. Small blast.", Verb = "FAST & STRAIGHT",
            StatDamage = 0.66f, StatRate = 0.36f, StatRange = 0.82f,
            Damage = 2, Cooldown = 1.1f, ForceMultiplier = 0.4f, Acceleration = 40f, BulletLifetime = 3.5f,
            ExplosionRadius = 1.6f, Knockback = 7f, ProjectileScale = 1.7f,
            ProjectileTint = new Color(1f, 0.6f, 0.25f), UpgradesTo = WeaponType.HeavyRocket
        },
        new WeaponProfile
        {
            Type = WeaponType.Mine, DisplayName = "MINE", IconSpriteName = "weapon_mine",
            Description = "Hides in clouds. Arms in 1.2 s.", Verb = "LURK", Preview = PreviewKind.Lurk,
            StatDamage = 1f, StatRate = 0.16f, StatRange = 0.22f,
            Damage = 3, Cooldown = 2.5f, ForceMultiplier = 0.35f, LinearDrag = 1.5f, ArmDelay = 1.2f,
            BulletLifetime = 25f, ExplosionRadius = 1.5f, Knockback = 9f, ProjectileScale = 2f,
            ProjectileTint = new Color(0.16f, 0.16f, 0.16f), UpgradesTo = WeaponType.TripleMine
        },
        new WeaponProfile
        {
            Type = WeaponType.Stinger, DisplayName = "STINGER", IconSpriteName = "weapon_stinger",
            Description = "Shoots out of the tail. Shake them off.", Verb = "BACKWARDS", Preview = PreviewKind.Backward,
            StatDamage = 0.33f, StatRate = 0.9f, StatRange = 0.55f,
            Damage = 1, Cooldown = 0.3f, ForceMultiplier = 0.9f, FireAngleOffset = 180f, Form = "tracer",
            UpgradesTo = WeaponType.TwinStinger
        },
        new WeaponProfile
        {
            Type = WeaponType.Airburst, DisplayName = "AIRBURST", IconSpriteName = "weapon_airburst",
            Description = "Hold to fly it, release to burst.", Verb = "DETONATE", Preview = PreviewKind.Burst,
            StatDamage = 0.66f, StatRate = 0.3f, StatRange = 0.6f,
            Damage = 2, Cooldown = 1.4f, ForceMultiplier = 0.55f, BulletLifetime = 2.2f, ExplodeOnExpire = true,
            RemoteDetonate = true, ExplosionRadius = 2.6f, Knockback = 10f, ProjectileScale = 1.4f,
            ProjectileTint = new Color(1f, 0.85f, 0.3f), Form = "pellet", UpgradesTo = WeaponType.AirburstMk2
        },
        new WeaponProfile
        {
            Type = WeaponType.Seeker, DisplayName = "SEEKER", IconSpriteName = "weapon_seeker",
            Description = "Homes in. Lost in clouds, outrun by nitro.", Verb = "HOMING", Preview = PreviewKind.Homing,
            StatDamage = 0.66f, StatRate = 0.25f, StatRange = 0.9f,
            Damage = 2, Cooldown = 1.8f, ForceMultiplier = 0.45f, MaxSpeed = 16f, Acceleration = 20f,
            HomingTurnRate = 120f, BulletLifetime = 5f, ExplosionRadius = 1.2f, Knockback = 5f,
            ProjectileScale = 1.4f, ProjectileTint = new Color(0.6f, 1f, 0.6f), Form = "rocket",
            UpgradesTo = WeaponType.TwinSeeker
        },
        new WeaponProfile
        {
            Type = WeaponType.Bomblet, DisplayName = "BOMBLET", IconSpriteName = "weapon_bomb",
            Description = "", Category = WeaponCategory.Child,
            Damage = 1, Cooldown = 1f, ForceMultiplier = 0f, GravityScale = 1.2f, BulletLifetime = 5f, ArmDelay = 0.25f,
            ExplosionRadius = 1.4f, Knockback = 6f, ProjectileScale = 0.6f, Form = "bomb"
        },
        new WeaponProfile
        {
            Type = WeaponType.BigOne, DisplayName = "BIG ONE", IconSpriteName = "weapon_bigone",
            Description = "One huge bomb. Siren included.", Verb = "SPECIAL", Preview = PreviewKind.Drop,
            Category = WeaponCategory.Special, SpecialAmmo = 1,
            StatDamage = 1f, StatRate = 0.05f, StatRange = 0.4f,
            Damage = 4, Cooldown = 1f, ForceMultiplier = 0f, GravityScale = 0.7f, BulletLifetime = 10f,
            ExplosionRadius = 7f, Knockback = 18f, ProjectileScale = 2.2f,
            ProjectileSpriteName = "bomb_littleboy", Form = "bomb"
        },
        new WeaponProfile
        {
            Type = WeaponType.MiniSeeker, DisplayName = "MINI SEEKER", IconSpriteName = "weapon_seeker",
            Description = "", Category = WeaponCategory.Child,
            Damage = 1, Cooldown = 1f, ForceMultiplier = 0.5f, MaxSpeed = 15f, Acceleration = 18f,
            HomingTurnRate = 160f, HomingConeDeg = 70f, BulletLifetime = 4f, ExplosionRadius = 0.8f,
            Knockback = 3f, ProjectileScale = 0.9f, ProjectileTint = new Color(0.6f, 1f, 0.6f), Form = "rocket"
        },
        new WeaponProfile
        {
            Type = WeaponType.Emp, DisplayName = "EMP", IconSpriteName = "weapon_emp",
            Description = "Kills every engine around you.", Verb = "SPECIAL",
            Category = WeaponCategory.Special, SpecialAmmo = 1,
            StatDamage = 0f, StatRate = 0.05f, StatRange = 0.5f, Cooldown = 1f, ExplosionRadius = 12f
        },
        new WeaponProfile
        {
            Type = WeaponType.ClusterBomb, DisplayName = "CLUSTER", IconSpriteName = "weapon_bomb",
            Description = "Splits into four bomblets.", Verb = "DROP", Preview = PreviewKind.Drop,
            Category = WeaponCategory.Upgrade,
            StatDamage = 1f, StatRate = 0.2f, StatRange = 0.4f,
            Damage = 3, Cooldown = 1.6f, ForceMultiplier = 0f, GravityScale = 1.2f, BulletLifetime = 8f,
            ExplosionRadius = 3.5f, Knockback = 12f, ProjectileTint = new Color(0.25f, 0.25f, 0.28f),
            ProjectileSpriteName = "bomb_littleboy", SplitInto = WeaponType.Bomblet, SplitCount = 4
        },
        new WeaponProfile
        {
            Type = WeaponType.HeavyRocket, DisplayName = "HEAVY ROCKET", IconSpriteName = "weapon_rocket",
            Description = "Bigger blast, harder push.", Verb = "FAST & STRAIGHT", Category = WeaponCategory.Upgrade,
            StatDamage = 0.7f, StatRate = 0.36f, StatRange = 0.82f,
            Damage = 2, Cooldown = 1.1f, ForceMultiplier = 0.4f, Acceleration = 40f, BulletLifetime = 3.5f,
            ExplosionRadius = 2.4f, Knockback = 10f, ProjectileScale = 2f, ProjectileTint = new Color(1f, 0.45f, 0.2f)
        },
        new WeaponProfile
        {
            Type = WeaponType.TripleMine, DisplayName = "TRIPLE MINE", IconSpriteName = "weapon_mine",
            Description = "Three mines in a fan.", Verb = "LURK", Preview = PreviewKind.Lurk,
            Category = WeaponCategory.Upgrade,
            StatDamage = 1f, StatRate = 0.16f, StatRange = 0.3f,
            Damage = 3, Cooldown = 2.5f, ForceMultiplier = 0.35f, LinearDrag = 1.5f, ArmDelay = 1.2f,
            BulletLifetime = 25f, ExplosionRadius = 1.5f, Knockback = 9f, PelletCount = 3, SpreadDegrees = 50f,
            ProjectileScale = 2f, ProjectileTint = new Color(0.16f, 0.16f, 0.16f)
        },
        new WeaponProfile
        {
            Type = WeaponType.LanceMk2, DisplayName = "LANCE MK II", IconSpriteName = "weapon_sniper",
            Description = "Charged shot pierces planes and clouds.", Verb = "CHARGE", Preview = PreviewKind.Charge,
            Category = WeaponCategory.Upgrade,
            StatDamage = 0.9f, StatRate = 0.25f, StatRange = 1f,
            Damage = 1, Cooldown = 1.3f, ForceMultiplier = 1.6f, ChargeSeconds = 0.9f, ChargeDamageMax = 3,
            ChargeForceMax = 3.2f, Pierce = true, ProjectileScale = 1.5f, ProjectileTint = new Color(0.7f, 0.95f, 1f),
            Form = "bolt"
        },
        new WeaponProfile
        {
            Type = WeaponType.TwinStinger, DisplayName = "TWIN STINGER", IconSpriteName = "weapon_stinger",
            Description = "Two bullets out of the tail.", Verb = "BACKWARDS", Preview = PreviewKind.Backward,
            Category = WeaponCategory.Upgrade,
            StatDamage = 0.5f, StatRate = 0.9f, StatRange = 0.55f,
            Damage = 1, Cooldown = 0.32f, ForceMultiplier = 0.9f, FireAngleOffset = 180f, PelletCount = 2,
            SpreadDegrees = 5f, Form = "tracer"
        },
        new WeaponProfile
        {
            Type = WeaponType.AirburstMk2, DisplayName = "AIRBURST MK II", IconSpriteName = "weapon_airburst",
            Description = "Wider burst.", Verb = "DETONATE", Preview = PreviewKind.Burst, Category = WeaponCategory.Upgrade,
            StatDamage = 0.7f, StatRate = 0.3f, StatRange = 0.6f,
            Damage = 2, Cooldown = 1.4f, ForceMultiplier = 0.55f, BulletLifetime = 2.2f, ExplodeOnExpire = true,
            RemoteDetonate = true, ExplosionRadius = 3.4f, Knockback = 11f, ProjectileScale = 1.6f,
            ProjectileTint = new Color(1f, 0.85f, 0.3f), Form = "pellet"
        },
        new WeaponProfile
        {
            Type = WeaponType.TwinSeeker, DisplayName = "TWIN SEEKER", IconSpriteName = "weapon_seeker",
            Description = "Two seekers at once.", Verb = "HOMING", Preview = PreviewKind.Homing,
            Category = WeaponCategory.Upgrade,
            StatDamage = 0.8f, StatRate = 0.25f, StatRange = 0.9f,
            Damage = 2, Cooldown = 1.8f, ForceMultiplier = 0.45f, MaxSpeed = 16f, Acceleration = 20f,
            HomingTurnRate = 120f, BulletLifetime = 5f, ExplosionRadius = 1.2f, Knockback = 5f, PelletCount = 2,
            SpreadDegrees = 30f, ProjectileScale = 1.4f, ProjectileTint = new Color(0.6f, 1f, 0.6f), Form = "rocket"
        },
        new WeaponProfile
        {
            Type = WeaponType.Swarm, DisplayName = "SWARM", IconSpriteName = "weapon_swarm",
            Description = "Five mini seekers in a fan.", Verb = "SPECIAL", Preview = PreviewKind.Homing,
            Category = WeaponCategory.Special, SpecialAmmo = 1,
            StatDamage = 0.8f, StatRate = 0.05f, StatRange = 0.8f,
            Damage = 1, Cooldown = 1f, ForceMultiplier = 0.5f, MaxSpeed = 15f, Acceleration = 18f,
            HomingTurnRate = 160f, HomingConeDeg = 70f, BulletLifetime = 4f, ExplosionRadius = 0.8f,
            Knockback = 3f, PelletCount = 5, SpreadDegrees = 40f, ProjectileScale = 0.9f,
            ProjectileTint = new Color(0.6f, 1f, 0.6f), Form = "rocket"
        },
    };

    private static readonly Dictionary<WeaponType, (Sprite sprite, Color tint)> IconCache = new();

    /// <summary>Icon for the trigger button: own art first, then the metal
    /// projectile sprite of the weapon's form (tinted like the projectile),
    /// then null - the caller shows the name. Cached per weapon.</summary>
    public Sprite LoadIcon(out Color tint)
    {
        if (IconCache.TryGetValue(Type, out var cached))
        {
            tint = cached.tint;
            return cached.sprite;
        }

        Sprite sprite = null;
        tint = Color.white;
        if (!string.IsNullOrEmpty(IconSpriteName))
        {
            sprite = Resources.Load<Sprite>("Sprites/Weapons/" + IconSpriteName);
        }
        if (sprite == null)
        {
            sprite = Resources.Load<Sprite>("Sprites/Projectiles/metal/" + ElementProfile.SpriteForm(Type));
            if (sprite != null) tint = ProjectileTint;
        }
        IconCache[Type] = (sprite, tint);
        return sprite;
    }

    public static WeaponProfile Get(WeaponType type)
    {
        int idx = (int)type;
        if (idx < 0 || idx >= Profiles.Length) return Profiles[0];
        return Profiles[idx];
    }

    public static WeaponProfile Get(int weaponId) => Get((WeaponType)weaponId);

    public static int Count => Profiles.Length;

    /// <summary>The draftable weapon a crate tier belongs to (itself if none).</summary>
    public static WeaponType BaseOf(WeaponType type)
    {
        foreach (var p in Profiles)
        {
            if (p.UpgradesTo.HasValue && p.UpgradesTo.Value == type && p.IsDraftable) return p.Type;
        }
        return type;
    }

    /// <summary>Registry sanity: array index must equal the enum value.</summary>
    public static void Validate()
    {
        for (int i = 0; i < Profiles.Length; i++)
        {
            if ((int)Profiles[i].Type != i)
                Debug.LogError($"[WeaponProfile] index {i} holds {Profiles[i].Type}");
        }
    }

    /// <summary>Weapons offered in the hangar draft (Mk II tiers stay crate-only, D2).</summary>
    public static readonly WeaponType[] DraftPool =
    {
        WeaponType.MG, WeaponType.Flak, WeaponType.Bomb, WeaponType.Sniper, WeaponType.Rocket,
        WeaponType.Mine, WeaponType.Stinger, WeaponType.Airburst, WeaponType.Seeker
    };

    /// <summary>Specials a supply drop can carry, with weights (Prompts/29 §5).</summary>
    public static readonly (WeaponType type, float weight)[] SupplySpecials =
    {
        (WeaponType.BigOne, 25f), (WeaponType.Swarm, 20f), (WeaponType.Emp, 15f)
    };
}
