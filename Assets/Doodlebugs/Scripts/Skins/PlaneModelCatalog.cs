using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static catalogue of plane MODELS (silhouettes) - the shape axis next to
/// PlaneSkinCatalog's livery axis. Every model is a 128x128 sprite that
/// passed tools/planes/gate.py's envelope contract (see
/// Prompts/23-CLAUDE-PLAN-plane-shapes.md, section 1): same bbox width,
/// centred, nose right, core box solid. That is what keeps the shared
/// BoxCollider2D on PlaneHolder an honest hitbox for all of them - nothing in
/// gameplay reads sprite bounds and no model gets its own collider.
///
/// A model ships as two PNGs in Resources/Sprites/PlaneModels/
/// (written by tools/planes/generate_planes.py apply):
///   model_{key}.png       base: red livery stored as (value,0,0) exactly like
///                         BiPlane1, grey fixed parts (engine, pilot, wheels)
///   model_{key}_mask.png  R = paint region, G = tail accent, A = silhouette
///
/// Skins are composited onto a model at runtime (LoadSprite) from the skin's
/// 128x128 material swatch and the model's masks - the same luminance
/// multiply tools/skins does offline for the original biplane. Baking every
/// (model x skin) pair was the alternative; at 64 KB of RGBA32 per pair that
/// is ~32 MB of build for 10 models x 50 skins, so runtime it is (one
/// composite is 16k pixels, cached).
///
/// Model 0 is the original BiPlane1 and keeps using the baked
/// Resources/Sprites/PlaneSkins/skin_*.png - pixel-identical to what shipped.
///
/// Ids go over the network (PlaneAppearance.NetModelId) - stable once
/// shipped, same rule as skins and WeaponType. Keys match
/// tools/planes/planes.py; keep both in sync by hand.
/// </summary>
public static class PlaneModelCatalog
{
    public const int BaseModelId = 0;   // BiPlane1 - the original, always available
    public const float PixelsPerUnit = 100f;

    // Luminance multiply, mirrored from tools/skins/generate_skins.py
    // composite_skin(): blend(pattern, pattern * lum, 0.85).
    private const float LumBase = 0.15f;
    private const float LumWeight = 0.85f;

    // Composites are created on demand: each plane once per look change, the
    // picker once per visible card (29 shapes + 50 skins on open, +50 per
    // shape switch). They are NEVER evicted on the load path. The first cut
    // capped the cache at 160 and destroyed everything past it - which
    // destroyed sprites still bound to a SpriteRenderer (plane goes
    // invisible, and a respawn does not re-run SetPlaneColor) and to picker
    // Images (white rectangles). Trimming happens only when the picker
    // closes, and keeps whatever spawned planes are wearing - see
    // TrimComposites.

    public readonly struct PlaneModelDef
    {
        public readonly int Id;
        public readonly string Key;          // tools/planes/planes.py key + Resources file stem
        public readonly string DisplayName;  // short - the picker card is 96 px wide
        public readonly bool IsPremium;      // all free for now (plan decision D4); IAP hook kept
        public readonly string BundleId;

        /// <summary>What this shape's projectiles are made of - a dragon
        /// breathes fire, a biplane fires brass (plan 24, decision D1).
        /// Presentation only; the weapon still decides every number.</summary>
        public readonly ProjectileElement Element;

        public PlaneModelDef(int id, string key, string displayName,
            ProjectileElement element = ProjectileElement.Metal,
            bool isPremium = false, string bundleId = "")
        {
            Id = id;
            Key = key;
            DisplayName = displayName;
            Element = element;
            IsPremium = isPremium;
            BundleId = bundleId;
        }

        public string ResourcePath => $"Sprites/PlaneModels/model_{Key}";
        public string MaskResourcePath => $"Sprites/PlaneModels/model_{Key}_mask";
    }

    // A concept listed here but not (yet) generated simply is not Available
    // - the picker hides it and the server rejects it - so the list can hold
    // the whole concept batch while the art pipeline is still gating seeds.
    public static readonly PlaneModelDef[] All =
    {
        new(0,  "biplane",      "Doodlebug"),
        new(1,  "triplane",     "Triplane"),
        new(2,  "racer",        "Racer"),
        new(3,  "flying_boat",  "Flying Boat"),
        new(4,  "canard",       "Canard"),
        new(5,  "twin_boom",    "Twin Boom"),
        new(6,  "gull_wing",    "Gull Wing"),
        new(7,  "barnstormer",  "Barnstormer"),
        new(8,  "rocket",       "Rocket", ProjectileElement.Plasma),
        new(9,  "gyrocopter",   "Gyrocopter"),
        new(10, "ornithopter",  "Ornithopter", ProjectileElement.Air),
        new(11, "paper_plane",  "Paper Plane", ProjectileElement.Air),
        new(12, "bathtub",      "Bathtub"),
        new(13, "crop_duster",  "Crop Duster"),
        new(14, "delta_glider", "Delta Glider", ProjectileElement.Air),
        new(15, "zeppelin",     "Zeppelin"),

        // Themed batch, 2026-09-04. Ids must match tools/planes/planes.py.
        // Several are listed but never shipped - the gate rejected every seed
        // (a balloon or a da Vinci screw simply is not a 110x26..72 shape) -
        // so IsAvailable reports false and the picker hides them.
        new(16, "starfighter",  "Starfighter", ProjectileElement.Plasma),
        new(17, "shuttle",      "Shuttle", ProjectileElement.Plasma),
        new(18, "interceptor",  "Interceptor", ProjectileElement.Plasma),
        new(19, "saucer",       "Saucer", ProjectileElement.Plasma),
        new(20, "lander",       "Lander"),
        new(21, "gunship",      "Gunship"),
        new(22, "galleon",      "Galleon"),
        new(23, "manta",        "Manta"),
        new(24, "seaplane",     "Seaplane"),
        new(25, "wright_flyer", "Wright Flyer"),
        new(26, "aerial_screw", "Aerial Screw"),
        new(27, "balloon",      "Balloon"),
        new(28, "stealth",      "Stealth", ProjectileElement.Plasma),
        new(29, "hover_pod",    "Hover Pod", ProjectileElement.Plasma),
        new(30, "tiltrotor",    "Tiltrotor"),
        new(31, "gotha_bomber", "Gotha"),
        new(32, "elliptical_fighter", "Spitfire"),
        new(33, "heavy_bomber", "Fortress"),
        new(34, "dragonfly",    "Dragonfly"),
        new(35, "flying_car",   "Flying Car"),

        // Creatures. Same envelope, same red paint region, same shared hitbox -
        // to the game a dragon is a silhouette like any other.
        new(36, "dragon",       "Dragon", ProjectileElement.Fire),
        new(37, "unicorn",      "Unicorn", ProjectileElement.Lightning),
        new(38, "wasp",         "Wasp", ProjectileElement.Venom),
        new(39, "fly",          "Fly", ProjectileElement.Venom),
        new(40, "eagle",        "Eagle", ProjectileElement.Air),
        new(41, "goose",        "Goose", ProjectileElement.Air),
    };

    private static Dictionary<int, PlaneModelDef> _byId;
    private static readonly Dictionary<int, Sprite> _baseSprites = new();      // null = checked, missing
    private static readonly Dictionary<int, Texture2D> _maskTextures = new();
    private static readonly Dictionary<long, Sprite> _composites = new();
    private static readonly HashSet<int> _warnedSwatch = new();
    private static List<int> _available;

    public static int Count => All.Length;

    public static PlaneModelDef Get(int id)
    {
        _byId ??= BuildIndex();
        return _byId.TryGetValue(id, out var def) ? def : All[BaseModelId];
    }

    public static bool IsValidId(int id) => id >= 0 && id < All.Length;

    /// <summary>Valid AND its sprites are actually in the build - the only
    /// ids the picker offers and the server accepts.</summary>
    /// <summary>What this shape shoots. Unknown/unlisted shapes are Metal,
    /// so a model added without an element still works (plan 24, D2).</summary>
    public static ProjectileElement ElementOf(int modelId) => Get(modelId).Element;

    public static bool IsAvailable(int id)
    {
        if (!IsValidId(id)) return false;
        if (id == BaseModelId) return true;
        return LoadBaseSprite(id) != null;
    }

    /// <summary>Ids the picker shows, catalogue order. Cached - Resources
    /// lookups for missing models are the expensive part.</summary>
    public static IReadOnlyList<int> Available
    {
        get
        {
            if (_available != null) return _available;
            _available = new List<int>(All.Length);
            foreach (var def in All)
            {
                if (IsAvailable(def.Id)) _available.Add(def.Id);
            }
            return _available;
        }
    }

    /// <summary>The model wearing the starter livery (its red base) - what
    /// the picker's shape cards and the validator look at.</summary>
    public static Sprite LoadBaseSprite(int modelId)
    {
        if (modelId == BaseModelId) return PlaneSkinCatalog.LoadSprite(PlaneSkinCatalog.StarterSkinId);
        if (_baseSprites.TryGetValue(modelId, out var cached)) return cached;

        var def = Get(modelId);
        var sprite = Resources.Load<Sprite>(def.ResourcePath);
        _baseSprites[modelId] = sprite; // null cached too: "not shipped" is a stable answer
        return sprite;
    }

    /// <summary>The sprite to draw for a (model, skin) pair. Falls back
    /// towards the base model / starter livery on anything missing so a plane
    /// is never invisible.</summary>
    public static Sprite LoadSprite(int modelId, int skinId)
    {
        if (!IsAvailable(modelId)) modelId = BaseModelId;
        if (modelId == BaseModelId) return PlaneSkinCatalog.LoadSprite(skinId);
        if (skinId == PlaneSkinCatalog.StarterSkinId) return LoadBaseSprite(modelId);

        long key = CompositeKey(modelId, skinId);
        if (_composites.TryGetValue(key, out var cached) && cached != null) return cached;

        var swatch = PlaneSkinCatalog.LoadSwatch(skinId);
        if (swatch == null)
        {
            if (_warnedSwatch.Add(skinId))
            {
                Debug.LogWarning($"[PlaneModelCatalog] No swatch for skin {PlaneSkinCatalog.Get(skinId).Key} " +
                                 "(run tools/skins/generate_skins.py swatches + apply) - non-base models show the starter livery");
            }
            return LoadBaseSprite(modelId);
        }

        var sprite = Composite(modelId, skinId, swatch);
        if (sprite == null) return LoadBaseSprite(modelId);

        _composites[key] = sprite;
        return sprite;
    }

    /// <summary>
    /// Drop every cached composite except the looks in <paramref name="keep"/>.
    /// Call it when the picker closes, with what spawned planes are wearing:
    /// the picker is the only thing that composites in bulk, and once its
    /// Images are gone nothing else references those sprites. A look a plane
    /// wears must survive - destroying it blanks the plane for the rest of
    /// the round.
    /// </summary>
    public static void TrimComposites(IEnumerable<(int modelId, int skinId)> keep)
    {
        var keepKeys = new HashSet<long>();
        foreach (var (m, s) in keep) keepKeys.Add(CompositeKey(m, s));

        var drop = new List<long>();
        foreach (var kv in _composites)
        {
            if (!keepKeys.Contains(kv.Key)) drop.Add(kv.Key);
        }
        foreach (long key in drop)
        {
            var sprite = _composites[key];
            _composites.Remove(key);
            if (sprite == null) continue;
            var tex = sprite.texture;
            Object.Destroy(sprite);
            if (tex != null) Object.Destroy(tex);
        }
    }

    private static Sprite Composite(int modelId, int skinId, Texture2D swatch)
    {
        var baseSprite = LoadBaseSprite(modelId);
        var mask = LoadMaskTexture(modelId);
        if (baseSprite == null || mask == null) return null;

        var baseTex = baseSprite.texture;
        int w = baseTex.width, h = baseTex.height;
        if (mask.width != w || mask.height != h || swatch.width != w || swatch.height != h)
        {
            Debug.LogError($"[PlaneModelCatalog] Size mismatch for model {Get(modelId).Key}: base {w}x{h}, " +
                           $"mask {mask.width}x{mask.height}, swatch {swatch.width}x{swatch.height}");
            return null;
        }
        if (!baseTex.isReadable || !mask.isReadable || !swatch.isReadable)
        {
            Debug.LogError($"[PlaneModelCatalog] Model {Get(modelId).Key} textures must be Read/Write enabled " +
                           "(generate_planes.py apply writes the .meta that way)");
            return null;
        }

        var bp = baseTex.GetPixels32();
        var mp = mask.GetPixels32();
        var sp = swatch.GetPixels32();
        var outPx = new Color32[bp.Length];
        for (int i = 0; i < bp.Length; i++)
        {
            var b = bp[i];
            if (b.a < 128)
            {
                outPx[i] = new Color32(0, 0, 0, 0);
                continue;
            }
            var m = mp[i];
            if (m.r > 127)
            {
                // Paint region: the base stores the original shading as the
                // red channel value - multiply the swatch by it.
                float f = LumBase + LumWeight * (b.r / 255f);
                var s = sp[i];
                outPx[i] = new Color32(
                    (byte)Mathf.Clamp(s.r * f + 0.5f, 0, 255),
                    (byte)Mathf.Clamp(s.g * f + 0.5f, 0, 255),
                    (byte)Mathf.Clamp(s.b * f + 0.5f, 0, 255), 255);
            }
            else if (m.g > 127)
            {
                outPx[i] = new Color32(255, 0, 0, 255); // tail accent - ColorReplace tints it per player
            }
            else
            {
                outPx[i] = new Color32(b.r, b.g, b.b, 255); // fixed part, verbatim
            }
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = $"plane_{Get(modelId).Key}_{PlaneSkinCatalog.Get(skinId).Key}",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        tex.SetPixels32(outPx);
        tex.Apply(false, true); // GPU only from here - nothing reads a composite back

        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), PixelsPerUnit, 0,
            SpriteMeshType.FullRect);
    }

    private static Texture2D LoadMaskTexture(int modelId)
    {
        if (_maskTextures.TryGetValue(modelId, out var cached)) return cached;
        var tex = Resources.Load<Texture2D>(Get(modelId).MaskResourcePath);
        if (tex == null)
        {
            Debug.LogError($"[PlaneModelCatalog] Missing mask at Resources/{Get(modelId).MaskResourcePath}");
        }
        _maskTextures[modelId] = tex;
        return tex;
    }

    private static long CompositeKey(int modelId, int skinId) => ((long)modelId << 32) | (uint)skinId;

    private static Dictionary<int, PlaneModelDef> BuildIndex()
    {
        var map = new Dictionary<int, PlaneModelDef>(All.Length);
        foreach (var def in All) map[def.Id] = def;
        return map;
    }
}
