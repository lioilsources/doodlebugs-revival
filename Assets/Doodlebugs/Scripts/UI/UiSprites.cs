using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime-generated UI sprites (disc, ring), anti-aliased and cached - the
/// same idea as EffectAssets' particle shapes: no art asset for a circle.
/// White with alpha, so Image.color does the tinting.
/// </summary>
public static class UiSprites
{
    private const int Size = 128;

    private static Sprite _disc;
    private static readonly Dictionary<int, Sprite> _rings = new();

    /// <summary>Filled circle.</summary>
    public static Sprite Disc => _disc != null ? _disc : _disc = Make(0f);

    /// <summary>Annulus; <paramref name="widthFraction"/> of the diameter.</summary>
    public static Sprite Ring(float widthFraction)
    {
        int key = Mathf.RoundToInt(Mathf.Clamp01(widthFraction) * 1000f);
        if (!_rings.TryGetValue(key, out var sprite) || sprite == null)
        {
            sprite = Make(widthFraction);
            _rings[key] = sprite;
        }
        return sprite;
    }

    private static Sprite Make(float widthFraction)
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };

        float centre = Size / 2f;
        float outer = centre - 1f;
        float inner = widthFraction > 0f ? outer - widthFraction * Size : -1f;

        var pixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float r = Mathf.Sqrt((x + 0.5f - centre) * (x + 0.5f - centre) +
                                     (y + 0.5f - centre) * (y + 0.5f - centre));
                // One-pixel linear edges: enough anti-aliasing at UI scale.
                float a = Mathf.Clamp01(outer - r + 0.5f);
                if (inner > 0f) a *= Mathf.Clamp01(r - inner + 0.5f);
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
    }
}
