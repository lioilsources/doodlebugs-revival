using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Impact and explosion visuals, resolved per projectile element.
///
/// Everything here is LOCAL-VISUAL on every client, driven by ids the
/// server synced - the same pattern the terrain craters already use. No
/// network object is spawned for an effect.
///
/// Resolution order, so the game is playable before any art exists:
///   Resources/Sprites/Effects/&lt;element&gt;/&lt;kind&gt;_00..  (generated flipbook)
///   -> Resources/Sprites/Effects/metal/&lt;kind&gt;_00..     (the default element)
///   -> the legacy explosion.prefab passed in by the caller
/// A burst of particles is layered under whichever wins.
/// </summary>
public static class EffectLibrary
{
    public const int SortingOrder = 12;   // above bullets (10), below the HUD

    private const float ImpactFps = 24f;
    private const float ExplosionFps = 20f;   // matches tools/weapons/gate.py KINDS

    // Folder path -> frames. Empty array = checked, missing (never re-probed:
    // Resources.LoadAll on a missing folder is not free).
    private static readonly Dictionary<string, Sprite[]> _frames = new();

    /// <summary>Frames of one flipbook, element with a Metal fallback.</summary>
    public static Sprite[] Frames(ProjectileElement element, string kind)
    {
        var own = Load(ElementProfile.Get(element).Key, kind);
        if (own.Length > 0) return own;

        return Load(ElementProfile.Get(ProjectileElement.Metal).Key, kind);
    }

    /// <summary>
    /// Both kinds share one folder per element (impact_00.png,
    /// explosion_00.png ...), so the load path is the FOLDER and the kind is
    /// a name prefix - Resources.LoadAll on ".../impact" would look for an
    /// asset by that exact name and quietly find nothing.
    /// </summary>
    private static Sprite[] Load(string elementKey, string kind)
    {
        string cacheKey = elementKey + "/" + kind;
        if (_frames.TryGetValue(cacheKey, out var cached)) return cached;

        var all = Resources.LoadAll<Sprite>($"Sprites/Effects/{elementKey}");
        var matched = new List<Sprite>();
        if (all != null)
        {
            string prefix = kind + "_";
            foreach (var sprite in all)
            {
                if (sprite != null && sprite.name.StartsWith(prefix)) matched.Add(sprite);
            }
        }

        // LoadAll does not promise order; the files are <kind>_00, _01, ...
        matched.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        var frames = matched.ToArray();
        _frames[cacheKey] = frames;   // an empty result is cached too
        return frames;
    }

    /// <summary>
    /// Non-AoE hit: a bullet striking a plane, a wall or a terrain tile.
    /// </summary>
    /// <param name="legacyPrefab">explosion.prefab, used when no flipbook art exists.</param>
    public static void SpawnImpact(ProjectileElement element, Vector3 position, GameObject legacyPrefab)
    {
        var profile = ElementProfile.Get(element);
        EffectAssets.CreateBurst(position, profile.Burst, 1f, SortingOrder);

        var frames = Frames(element, "impact");
        if (frames.Length > 0)
        {
            FlipbookEffect.Play(frames, position, ImpactFps, 1f, SortingOrder);
            return;
        }
        SpawnLegacy(legacyPrefab, position, 1f);
    }

    /// <summary>
    /// AoE boom. Scale follows the blast radius so a bomb reads bigger than
    /// a mine, exactly as the legacy path did.
    /// </summary>
    public static void SpawnExplosion(ProjectileElement element, Vector3 position, float radius,
        GameObject legacyPrefab)
    {
        var profile = ElementProfile.Get(element);
        float scale = Mathf.Max(1f, radius);

        EffectAssets.CreateBurst(position, profile.Burst, scale, SortingOrder);

        var frames = Frames(element, "explosion");
        if (frames.Length > 0)
        {
            FlipbookEffect.Play(frames, position, ExplosionFps, scale, SortingOrder);
            return;
        }
        SpawnLegacy(legacyPrefab, position, scale);
    }

    private static void SpawnLegacy(GameObject prefab, Vector3 position, float scale)
    {
        if (prefab == null) return;
        var effect = Object.Instantiate(prefab, position, Quaternion.identity);
        effect.transform.localScale *= scale;
        Object.Destroy(effect, 0.8f);
    }
}
