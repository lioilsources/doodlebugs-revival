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
    private const float ExplosionFps = 22f;

    // Folder path -> frames. Empty array = checked, missing (never re-probed:
    // Resources.LoadAll on a missing folder is not free).
    private static readonly Dictionary<string, Sprite[]> _frames = new();

    /// <summary>Frames of one flipbook, element with a Metal fallback.</summary>
    public static Sprite[] Frames(ProjectileElement element, string kind)
    {
        var own = Load($"Sprites/Effects/{ElementProfile.Get(element).Key}/{kind}");
        if (own.Length > 0) return own;

        var fallback = ElementProfile.Get(ProjectileElement.Metal).Key;
        return Load($"Sprites/Effects/{fallback}/{kind}");
    }

    private static Sprite[] Load(string path)
    {
        if (_frames.TryGetValue(path, out var cached)) return cached;

        var loaded = Resources.LoadAll<Sprite>(path);
        if (loaded == null) loaded = new Sprite[0];

        // LoadAll does not promise order; the files are <kind>_00, _01, ...
        System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
        _frames[path] = loaded;
        return loaded;
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
