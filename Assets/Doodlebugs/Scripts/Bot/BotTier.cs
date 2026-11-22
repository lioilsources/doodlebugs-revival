using UnityEngine;

/// <summary>
/// How well a combat bot flies (Prompts/27 WP4). Skill is reaction time, aim
/// error, lead and discipline - never turn rate: every tier shares the
/// keyboard input ramp (BotBrain.InputRampPerSecond), so no bot can out-turn
/// a human on the same plane. Starting numbers from the plan, tuned by the
/// soak harness (Doodlebugs → Debug → Bot Soak) and playtests.
/// Index = PlayerController.NetBotTier = the "bot.tier.N" name key.
/// </summary>
public sealed class BotTier
{
    public string Key;

    /// <summary>Seconds between a threat appearing and the bot reacting to
    /// it, and between acquiring a target and the first shot at it.</summary>
    public float ReactionDelay;

    /// <summary>Uniform +- error added to the aim heading, re-rolled per burst.</summary>
    public float AimErrorDeg;

    /// <summary>Fires only when the aim point sits inside this half-angle ahead.</summary>
    public float FireConeDeg;

    /// <summary>0 = aims at the target, 1 = at the full intercept point.</summary>
    public float LeadFactor;

    /// <summary>Beyond this the bot closes in but holds fire.</summary>
    public float EngageRange;

    public float BurstMin, BurstMax;
    public float GapMin, GapMax;

    /// <summary>Chance to break when an enemy sits on its tail (rolled once per threat).</summary>
    public float EvadeProbability;

    /// <summary>At 1 HP heads for a health/shield/repair crate or flies evasively.</summary>
    public bool DisengageAtOneHp;

    /// <summary>Chance that a due loop / reversal / zoom climb is flown while
    /// the target is out of range (1 = always, 0 = never).</summary>
    public float AerobaticsChance;

    /// <summary>One is drawn per round (Prompts/27 S5). No Bomb or Mine:
    /// dropped and lurking weapons need tactics the brain does not have.</summary>
    public WeaponType[] WeaponPool;

    public static readonly BotTier Rookie = new BotTier
    {
        Key = "rookie", ReactionDelay = 0.8f, AimErrorDeg = 14f, FireConeDeg = 12f, LeadFactor = 0f,
        EngageRange = 14f, BurstMin = 0.3f, BurstMax = 0.6f, GapMin = 2.5f, GapMax = 5f,
        EvadeProbability = 0f, DisengageAtOneHp = false, AerobaticsChance = 1f,
        WeaponPool = new[] { WeaponType.MG },
    };

    public static readonly BotTier Pilot = new BotTier
    {
        Key = "pilot", ReactionDelay = 0.45f, AimErrorDeg = 8f, FireConeDeg = 8f, LeadFactor = 0.5f,
        EngageRange = 20f, BurstMin = 0.4f, BurstMax = 0.9f, GapMin = 1.5f, GapMax = 3f,
        EvadeProbability = 0.4f, DisengageAtOneHp = false, AerobaticsChance = 0.5f,
        WeaponPool = new[] { WeaponType.MG, WeaponType.Flak },
    };

    public static readonly BotTier Ace = new BotTier
    {
        Key = "ace", ReactionDelay = 0.25f, AimErrorDeg = 4f, FireConeDeg = 5f, LeadFactor = 0.9f,
        EngageRange = 26f, BurstMin = 0.5f, BurstMax = 1.2f, GapMin = 0.8f, GapMax = 1.8f,
        EvadeProbability = 0.75f, DisengageAtOneHp = true, AerobaticsChance = 0f,
        WeaponPool = new[] { WeaponType.MG, WeaponType.Flak, WeaponType.TwinMG, WeaponType.HeavyFlak, WeaponType.Sniper },
    };

    public static readonly BotTier Baron = new BotTier
    {
        Key = "baron", ReactionDelay = 0.15f, AimErrorDeg = 2f, FireConeDeg = 4f, LeadFactor = 1f,
        EngageRange = 30f, BurstMin = 0.6f, BurstMax = 1.4f, GapMin = 0.5f, GapMax = 1.2f,
        EvadeProbability = 0.9f, DisengageAtOneHp = true, AerobaticsChance = 0f,
        WeaponPool = new[] { WeaponType.MG, WeaponType.Flak, WeaponType.TwinMG, WeaponType.HeavyFlak, WeaponType.Sniper, WeaponType.Rocket },
    };

    private static readonly BotTier[] All = { Rookie, Pilot, Ace, Baron };

    public const int Count = 4;

    public static BotTier Get(int tier) => All[Mathf.Clamp(tier, 0, All.Length - 1)];

    /// <summary>A weapon for one round from this tier's pool.</summary>
    public WeaponType PickRoundWeapon() => WeaponPool[Random.Range(0, WeaponPool.Length)];
}
