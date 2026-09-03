using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static catalogue of plane skins - the visual counterpart to WeaponProfile.
/// A skin is a texture swap only: every skin shares the exact same 128x128
/// silhouette and hitbox as the original BiPlane1 sprite (see
/// tools/skins/README.md), so no gameplay code needs to know skins exist
/// beyond which sprite to draw. The tail-fin accent region stays pure red in
/// every skin and keeps going through the existing ColorReplace shader, so
/// per-player colour identity (PlayerColorManager) survives skin choice.
///
/// Ids are serialized over the network (PlaneAppearance.NetSkinId) - keep
/// them stable once shipped, the same rule as WeaponType.
/// </summary>
public static class PlaneSkinCatalog
{
    public const int StarterSkinId = 0; // Doodle Red - the original livery, always unlocked

    public readonly struct PlaneSkinDef
    {
        public readonly int Id;
        public readonly string Key;          // matches tools/skins/skins.py + Resources file stem
        public readonly string DisplayName;
        public readonly string Category;
        public readonly bool IsPremium;
        public readonly string BundleId;     // IAPManager.SkinBundles key; "" for free skins

        public PlaneSkinDef(int id, string key, string displayName, string category,
            bool isPremium, string bundleId)
        {
            Id = id;
            Key = key;
            DisplayName = displayName;
            Category = category;
            IsPremium = isPremium;
            BundleId = bundleId;
        }

        public string ResourcePath => $"Sprites/PlaneSkins/skin_{Key}";

        // The flat 128x128 material swatch the skin was painted from - what
        // PlaneModelCatalog composites onto non-base plane models at runtime.
        public string SwatchResourcePath => $"Sprites/PlaneSkins/Swatches/swatch_{Key}";
    }

    // Tiering assumption (flag for the user to revisit): 12 free starter
    // skins give every player real choice with no purchase, the remaining
    // 38 premium skins sell in 4 thematic bundles (see IAPManager.SkinBundles)
    // rather than 38 individual store products - fewer, saner store listings.
    public static readonly PlaneSkinDef[] All =
    {
        new(0,  "doodle_red",     "Doodle Red",         "Classic",     false, ""),
        new(1,  "raf_khaki",      "RAF Khaki",           "Historic",    false, ""),
        new(2,  "luftstreit_grey","Luftstreitkräfte Grey","Historic",   false, ""),
        new(3,  "aeronautique_blue","Aéronautique Blue", "Historic",    false, ""),
        new(4,  "racing_stripe",  "Racing Stripe",       "Pattern",     false, ""),
        new(5,  "checkerboard",   "Checkerboard",        "Pattern",     false, ""),
        new(6,  "candy_stripe",   "Candy Stripe",        "Pattern",     false, ""),
        new(7,  "polka_dot",      "Polka Dot",           "Pattern",     false, ""),
        new(8,  "woodgrain",      "Woodgrain",           "Pattern",     false, ""),
        new(9,  "barnstormer_yellow","Barnstormer Yellow","Pattern",    false, ""),
        new(10, "sunset_fade",    "Sunset Fade",         "Pattern",     false, ""),
        new(11, "silver_dart",    "Silver Dart",         "Pattern",     false, ""),

        new(12, "jungle_camo",    "Jungle Camo",         "Camo",        true, "camo_pack"),
        new(13, "desert_camo",    "Desert Camo",         "Camo",        true, "camo_pack"),
        new(14, "arctic_camo",    "Arctic Camo",         "Camo",        true, "camo_pack"),
        new(15, "naval_dazzle",   "Naval Dazzle",        "Camo",        true, "camo_pack"),
        new(16, "volcanic_camo",  "Volcanic Camo",       "Camo",        true, "camo_pack"),
        new(17, "forest_camo",    "Forest Camo",         "Camo",        true, "camo_pack"),
        new(18, "storm_grey_camo","Storm Grey Camo",     "Camo",        true, "camo_pack"),
        new(19, "autumn_camo",    "Autumn Camo",         "Camo",        true, "camo_pack"),
        new(20, "night_camo",     "Night Camo",          "Camo",        true, "camo_pack"),

        new(21, "chrome_shine",   "Chrome Shine",        "Metallic",    true, "metallic_pack"),
        new(22, "gold_leaf",      "Gold Leaf",           "Metallic",    true, "metallic_pack"),
        new(23, "copper_patina",  "Copper Patina",       "Metallic",    true, "metallic_pack"),
        new(24, "gunmetal",       "Gunmetal",             "Metallic",    true, "metallic_pack"),
        new(25, "holo_shift",     "Holo Shift",          "Metallic",    true, "metallic_pack"),
        new(26, "rose_gold",      "Rose Gold",           "Metallic",    true, "metallic_pack"),
        new(27, "obsidian_gloss", "Obsidian Gloss",      "Metallic",    true, "metallic_pack"),
        new(28, "circuit_board",  "Circuit Board",       "Metallic",    true, "metallic_pack"),
        new(29, "carbon_weave",   "Carbon Weave",        "Metallic",    true, "metallic_pack"),

        new(30, "galaxy_nebula",  "Galaxy Nebula",       "Cosmic",      true, "cosmic_pack"),
        new(31, "aurora_borealis","Aurora Borealis",     "Cosmic",      true, "cosmic_pack"),
        new(32, "lava_flow",      "Lava Flow",           "Cosmic",      true, "cosmic_pack"),
        new(33, "deep_ocean",     "Deep Ocean",          "Cosmic",      true, "cosmic_pack"),
        new(34, "lightning_bolt", "Lightning Bolt",      "Cosmic",      true, "cosmic_pack"),
        new(35, "toxic_glow",     "Toxic Glow",          "Cosmic",      true, "cosmic_pack"),
        new(36, "crystal_ice",    "Crystal Ice",         "Cosmic",      true, "cosmic_pack"),
        new(37, "dragon_scale",   "Dragon Scale",        "Cosmic",      true, "cosmic_pack"),
        new(38, "phoenix_flame",  "Phoenix Flame",       "Cosmic",      true, "cosmic_pack"),

        // Franchise-flavoured: mood/palette homage only - no borrowed names,
        // characters, logos or signature marks. Same rule tools/ads/PROMPTS.md
        // and tools/backgrounds/themes.py already follow.
        new(39, "gotham_night",   "Gotham Night",        "Homage",      true, "homage_pack"),
        new(40, "dream_pink",     "Dream Pink",          "Homage",      true, "homage_pack"),
        new(41, "hero_comic",     "Hero Comic",          "Homage",      true, "homage_pack"),
        new(42, "galaxy_saber",   "Galaxy Saber",        "Homage",      true, "homage_pack"),
        new(43, "koala_pastel",   "Koala Pastel",        "Homage",      true, "homage_pack"),
        new(44, "sponge_yellow",  "Sponge Yellow",       "Homage",      true, "homage_pack"),
        new(45, "villain_purple", "Villain Purple",      "Homage",      true, "homage_pack"),
        new(46, "brick_hero",     "Brick Hero",          "Homage",      true, "homage_pack"),
        new(47, "arcade_pixel",   "Arcade Pixel",        "Homage",      true, "homage_pack"),
        new(48, "retro_wave",     "Retro Wave",          "Homage",      true, "homage_pack"),
        new(49, "tiger_stripe",   "Tiger Stripe",        "Homage",      true, "homage_pack"),
    };

    private static Dictionary<int, PlaneSkinDef> _byId;
    private static Dictionary<int, Sprite> _spriteCache;
    private static Dictionary<int, Texture2D> _swatchCache;

    public static int Count => All.Length;

    public static PlaneSkinDef Get(int id)
    {
        _byId ??= BuildIndex();
        return _byId.TryGetValue(id, out var def) ? def : All[StarterSkinId];
    }

    public static bool IsValidId(int id) => id >= 0 && id < All.Length;

    /// <summary>Cached Resources.Load - skins are read often (every plane
    /// spawn, every hangar card) and Resources.Load itself isn't free.</summary>
    public static Sprite LoadSprite(int id)
    {
        _spriteCache ??= new Dictionary<int, Sprite>();
        if (_spriteCache.TryGetValue(id, out var cached) && cached != null) return cached;

        var def = Get(id);
        var sprite = Resources.Load<Sprite>(def.ResourcePath);
        if (sprite == null)
        {
            Debug.LogWarning($"[PlaneSkinCatalog] Missing sprite at Resources/{def.ResourcePath}, falling back to starter skin");
            if (id != StarterSkinId) return LoadSprite(StarterSkinId);
        }
        _spriteCache[id] = sprite;
        return sprite;
    }

    /// <summary>The skin's material swatch (Read/Write enabled, 128x128), or
    /// null for the starter skin (it has no pattern - it IS the red base) and
    /// for skins whose swatch hasn't been applied yet. Callers fall back to
    /// the model's base sprite; no warning here, the caller decides.</summary>
    public static Texture2D LoadSwatch(int id)
    {
        if (id == StarterSkinId) return null;
        _swatchCache ??= new Dictionary<int, Texture2D>();
        if (_swatchCache.TryGetValue(id, out var cached)) return cached;

        var tex = Resources.Load<Texture2D>(Get(id).SwatchResourcePath);
        _swatchCache[id] = tex; // null cached as well - a missing swatch is a stable answer
        return tex;
    }

    private static Dictionary<int, PlaneSkinDef> BuildIndex()
    {
        var map = new Dictionary<int, PlaneSkinDef>(All.Length);
        foreach (var def in All) map[def.Id] = def;
        return map;
    }
}
