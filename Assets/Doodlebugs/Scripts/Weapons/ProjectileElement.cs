using UnityEngine;

/// <summary>
/// What a plane's projectiles are MADE of. The weapon decides physics
/// (damage, cooldown, pellets, gravity); the element decides how that
/// projectile looks, trails, splashes and sounds.
///
/// Ids go over the network (Bullet's synced element id) - keep values
/// stable once shipped, same rule as WeaponType and the skin/model ids.
/// Keys must match tools/weapons/elements.py.
///
/// Design notes: Prompts/24-CLAUDE-PLAN-projectile-elements.md.
/// </summary>
public enum ProjectileElement
{
    Metal = 0,
    Fire = 1,
    Lightning = 2,
    Venom = 3,
    Plasma = 4,
    Air = 5
}

/// <summary>Which runtime particle texture a preset draws with.</summary>
public enum ParticleTexture
{
    SoftCircle = 0,
    Spark = 1,
    Droplet = 2,
    Square = 3,
    Feather = 4
}

/// <summary>
/// Trail hung off a projectile in flight. Emission is continuous; the
/// system is detached and left to finish when the bullet despawns, or the
/// last puff would vanish with it.
/// </summary>
public class TrailPreset
{
    public float Rate = 18f;              // particles per second
    public float LifetimeMin = 0.25f;
    public float LifetimeMax = 0.5f;
    public float SizeMin = 0.10f;
    public float SizeMax = 0.20f;
    public float Speed = 0.15f;           // outward drift
    public float GravityModifier = 0f;    // negative = rises (fire, air)
    public float Jitter = 0.03f;          // emitter sphere radius
    public float StartSizeCurveEnd = 1.6f;  // grow (>1) or shrink (<1) over life
    public Color ColorStart = Color.white;
    public Color ColorEnd = Color.white;
    public float AlphaPeak = 0.75f;
    public ParticleTexture Texture = ParticleTexture.SoftCircle;
}

/// <summary>One-shot puff layered under an impact/explosion flipbook.</summary>
public class BurstPreset
{
    public int Count = 12;
    public float SpeedMin = 1.5f;
    public float SpeedMax = 4f;
    public float LifetimeMin = 0.2f;
    public float LifetimeMax = 0.45f;
    public float SizeMin = 0.10f;
    public float SizeMax = 0.22f;
    public float GravityModifier = 0f;
    public Color ColorStart = Color.white;
    public Color ColorEnd = Color.white;
    public ParticleTexture Texture = ParticleTexture.SoftCircle;
}

/// <summary>
/// Static element definitions. Everything here is presentation only
/// (plan decision D5) - no element changes damage, range or cooldown, so
/// the weapon draft stays the only place balance lives.
/// </summary>
public class ElementProfile
{
    public ProjectileElement Element;
    public string Key;                    // Resources subfolder + tools/weapons key
    public string DisplayName;            // picker badge - keep it short
    public Color Tint;                    // fallback projectile tint + badge colour
    public string ShootSfxGunKey;         // Resources/Sfx/Elements/<key>/...
    public TrailPreset Trail;
    public BurstPreset Burst;

    /// <summary>Sound group for a weapon form: light guns vs. heavy ordnance.
    /// Two groups rather than eight - under the shot pitch jitter nobody
    /// hears the difference between an MG and a sniper (plan D6).</summary>
    public static string SfxGroup(WeaponType weapon)
    {
        switch (weapon)
        {
            case WeaponType.Bomb:
            case WeaponType.Rocket:
            case WeaponType.Mine:
                return "heavy";
            default:
                return "gun";
        }
    }

    /// <summary>Art form a weapon draws as. Eight weapons, six forms -
    /// the pellet/tracer pairs share their sprite. Must match
    /// tools/weapons/forms.py.</summary>
    public static string SpriteForm(WeaponType weapon)
    {
        switch (weapon)
        {
            case WeaponType.MG:
            case WeaponType.TwinMG:
                return "tracer";
            case WeaponType.Flak:
            case WeaponType.HeavyFlak:
                return "pellet";
            case WeaponType.Bomb:
                return "bomb";
            case WeaponType.Sniper:
                return "bolt";
            case WeaponType.Rocket:
                return "rocket";
            case WeaponType.Mine:
                return "mine";
            default:
                return "tracer";
        }
    }

    private static readonly ElementProfile[] Profiles =
    {
        new ElementProfile
        {
            Element = ProjectileElement.Metal,
            Key = "metal",
            DisplayName = "STEEL",
            Tint = new Color(0.776f, 0.698f, 0.471f),   // brass, the current tracer
            Trail = new TrailPreset
            {
                Rate = 10f, LifetimeMin = 0.15f, LifetimeMax = 0.3f,
                SizeMin = 0.06f, SizeMax = 0.12f, Speed = 0.1f,
                StartSizeCurveEnd = 1.2f,
                ColorStart = new Color(0.85f, 0.85f, 0.85f),
                ColorEnd = new Color(0.5f, 0.5f, 0.5f),
                AlphaPeak = 0.45f, Texture = ParticleTexture.SoftCircle
            },
            Burst = new BurstPreset
            {
                Count = 14, SpeedMin = 2f, SpeedMax = 5f,
                SizeMin = 0.05f, SizeMax = 0.11f, GravityModifier = 0.6f,
                ColorStart = new Color(1f, 0.92f, 0.7f),
                ColorEnd = new Color(0.6f, 0.55f, 0.45f),
                Texture = ParticleTexture.Spark
            }
        },
        new ElementProfile
        {
            Element = ProjectileElement.Fire,
            Key = "fire",
            DisplayName = "FIRE",
            Tint = new Color(1f, 0.541f, 0.180f),
            Trail = new TrailPreset
            {
                Rate = 30f, LifetimeMin = 0.3f, LifetimeMax = 0.6f,
                SizeMin = 0.12f, SizeMax = 0.24f, Speed = 0.2f,
                GravityModifier = -0.25f,           // embers rise
                Jitter = 0.05f, StartSizeCurveEnd = 1.9f,
                ColorStart = new Color(1f, 0.85f, 0.35f),
                ColorEnd = new Color(0.35f, 0.12f, 0.05f),
                AlphaPeak = 0.85f, Texture = ParticleTexture.SoftCircle
            },
            Burst = new BurstPreset
            {
                Count = 20, SpeedMin = 1.5f, SpeedMax = 4.5f,
                LifetimeMin = 0.3f, LifetimeMax = 0.6f,
                SizeMin = 0.08f, SizeMax = 0.2f, GravityModifier = -0.3f,
                ColorStart = new Color(1f, 0.8f, 0.3f),
                ColorEnd = new Color(0.25f, 0.08f, 0.03f),
                Texture = ParticleTexture.Square    // crisp embers
            }
        },
        new ElementProfile
        {
            Element = ProjectileElement.Lightning,
            Key = "lightning",
            DisplayName = "SPARK",
            Tint = new Color(0.588f, 0.824f, 1f),
            Trail = new TrailPreset
            {
                Rate = 34f, LifetimeMin = 0.1f, LifetimeMax = 0.22f,
                SizeMin = 0.07f, SizeMax = 0.16f, Speed = 0.5f,
                Jitter = 0.09f,                      // crackle scatter
                StartSizeCurveEnd = 0.5f,
                ColorStart = new Color(0.9f, 0.97f, 1f),
                ColorEnd = new Color(0.35f, 0.5f, 1f),
                AlphaPeak = 0.9f, Texture = ParticleTexture.Spark
            },
            Burst = new BurstPreset
            {
                Count = 18, SpeedMin = 3f, SpeedMax = 7f,
                LifetimeMin = 0.12f, LifetimeMax = 0.28f,
                SizeMin = 0.06f, SizeMax = 0.14f,
                ColorStart = Color.white,
                ColorEnd = new Color(0.4f, 0.55f, 1f),
                Texture = ParticleTexture.Spark
            }
        },
        new ElementProfile
        {
            Element = ProjectileElement.Venom,
            Key = "venom",
            DisplayName = "VENOM",
            Tint = new Color(0.471f, 0.902f, 0.353f),
            Trail = new TrailPreset
            {
                Rate = 16f, LifetimeMin = 0.4f, LifetimeMax = 0.8f,
                SizeMin = 0.08f, SizeMax = 0.16f, Speed = 0.08f,
                GravityModifier = 0.5f,             // droplets drip
                StartSizeCurveEnd = 0.8f,
                ColorStart = new Color(0.7f, 1f, 0.45f),
                ColorEnd = new Color(0.15f, 0.4f, 0.1f),
                AlphaPeak = 0.7f, Texture = ParticleTexture.Droplet
            },
            Burst = new BurstPreset
            {
                Count = 16, SpeedMin = 1.2f, SpeedMax = 3.5f,
                LifetimeMin = 0.35f, LifetimeMax = 0.7f,
                SizeMin = 0.09f, SizeMax = 0.2f, GravityModifier = 0.9f,
                ColorStart = new Color(0.75f, 1f, 0.5f),
                ColorEnd = new Color(0.1f, 0.3f, 0.08f),
                Texture = ParticleTexture.Droplet
            }
        },
        new ElementProfile
        {
            Element = ProjectileElement.Plasma,
            Key = "plasma",
            DisplayName = "PLASMA",
            Tint = new Color(0.922f, 0.431f, 1f),
            Trail = new TrailPreset
            {
                Rate = 26f, LifetimeMin = 0.2f, LifetimeMax = 0.4f,
                SizeMin = 0.1f, SizeMax = 0.2f, Speed = 0.1f,
                StartSizeCurveEnd = 0.4f,           // tight glow tail
                ColorStart = new Color(1f, 0.85f, 1f),
                ColorEnd = new Color(0.5f, 0.15f, 0.8f),
                AlphaPeak = 0.8f, Texture = ParticleTexture.SoftCircle
            },
            Burst = new BurstPreset
            {
                Count = 16, SpeedMin = 2.5f, SpeedMax = 5.5f,
                LifetimeMin = 0.15f, LifetimeMax = 0.35f,
                SizeMin = 0.08f, SizeMax = 0.18f,
                ColorStart = Color.white,
                ColorEnd = new Color(0.6f, 0.2f, 0.9f),
                Texture = ParticleTexture.SoftCircle
            }
        },
        new ElementProfile
        {
            Element = ProjectileElement.Air,
            Key = "air",
            DisplayName = "GUST",
            Tint = new Color(0.882f, 0.941f, 1f),
            Trail = new TrailPreset
            {
                Rate = 14f, LifetimeMin = 0.35f, LifetimeMax = 0.7f,
                SizeMin = 0.1f, SizeMax = 0.22f, Speed = 0.25f,
                GravityModifier = -0.08f, Jitter = 0.07f,
                StartSizeCurveEnd = 1.7f,
                ColorStart = Color.white,
                ColorEnd = new Color(0.8f, 0.87f, 0.95f),
                AlphaPeak = 0.4f, Texture = ParticleTexture.Feather
            },
            Burst = new BurstPreset
            {
                Count = 14, SpeedMin = 1.8f, SpeedMax = 4f,
                LifetimeMin = 0.3f, LifetimeMax = 0.6f,
                SizeMin = 0.1f, SizeMax = 0.24f, GravityModifier = 0.15f,
                ColorStart = Color.white,
                ColorEnd = new Color(0.78f, 0.85f, 0.95f),
                Texture = ParticleTexture.Feather
            }
        }
    };

    public static int Count => Profiles.Length;

    public static ElementProfile Get(ProjectileElement element)
    {
        int idx = (int)element;
        if (idx < 0 || idx >= Profiles.Length) return Profiles[0];
        return Profiles[idx];
    }

    public static ElementProfile Get(int elementId) => Get((ProjectileElement)elementId);

    /// <summary>Resources path of a projectile sprite, or null for the
    /// Metal fallback chain in Bullet.ApplyVisual.</summary>
    public string ProjectilePath(WeaponType weapon) =>
        $"Sprites/Projectiles/{Key}/{SpriteForm(weapon)}";
}
