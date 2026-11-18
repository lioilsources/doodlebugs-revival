using System.Collections.Generic;
using UnityEngine;

/// <summary>Gadget ids (slot 2, Prompts/29 §4). Synced as int - append only.</summary>
public enum GadgetType
{
    Nitro = 0,
    BarrelRoll = 1,
    Thunderclap = 2,
}

/// <summary>
/// Non-shooting second slot: a cooldown on the same ring as a weapon, no
/// ammo. Movement effects are applied by the plane's owner (movement is
/// owner-authoritative), damage-free area effects by the server.
/// </summary>
public class GadgetProfile
{
    public GadgetType Type;
    public string DisplayName;
    public string Description;
    public string Verb;
    public float Cooldown;
    public float Duration;
    public float Radius;
    public float Knockback;
    public float StatPower, StatRate;
    public string IconSpriteName;
    public string Sound;

    private static readonly GadgetProfile[] Profiles =
    {
        new GadgetProfile
        {
            Type = GadgetType.Nitro, DisplayName = "NITRO", Verb = "BOOST",
            Description = "1.2 s of top speed. Relights a dead engine.",
            Cooldown = 7f, Duration = 1.2f, StatPower = 0.7f, StatRate = 0.55f,
            IconSpriteName = "gadget_nitro", Sound = "nitro"
        },
        new GadgetProfile
        {
            Type = GadgetType.BarrelRoll, DisplayName = "BARREL ROLL", Verb = "DODGE",
            Description = "Shots and blasts pass through you for 0.45 s.",
            Cooldown = 5f, Duration = 0.45f, StatPower = 0.5f, StatRate = 0.75f,
            IconSpriteName = "gadget_roll", Sound = "roll"
        },
        new GadgetProfile
        {
            Type = GadgetType.Thunderclap, DisplayName = "THUNDERCLAP", Verb = "SHOCKWAVE",
            Description = "Shoves planes away and wipes enemy shots nearby.",
            Cooldown = 9f, Duration = 0f, Radius = 3.5f, Knockback = 11f, StatPower = 0.85f, StatRate = 0.4f,
            IconSpriteName = "gadget_clap", Sound = "clap"
        },
    };

    public static int Count => Profiles.Length;

    public static GadgetProfile Get(int id) =>
        id >= 0 && id < Profiles.Length ? Profiles[id] : Profiles[0];

    public static GadgetProfile Get(GadgetType type) => Get((int)type);

    public static readonly GadgetType[] DraftPool =
    {
        GadgetType.Nitro, GadgetType.BarrelRoll, GadgetType.Thunderclap
    };

    /// <summary>Default for every plane until a gadget is drafted (Prompts/29 D4).</summary>
    public const GadgetType Default = GadgetType.Nitro;

    private static readonly Dictionary<GadgetType, Sprite> IconCache = new();

    public Sprite LoadIcon()
    {
        if (IconCache.TryGetValue(Type, out var s)) return s;
        s = Resources.Load<Sprite>("Sprites/Weapons/" + IconSpriteName);
        IconCache[Type] = s;
        return s;
    }
}
