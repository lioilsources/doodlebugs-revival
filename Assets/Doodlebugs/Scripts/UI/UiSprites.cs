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

    // --- Sky Poster kit (Prompts/30) -----------------------------------------

    private const int RoundRadiusPx = 32;
    private static Sprite _round, _gradient, _check, _lock, _gear;

    /// <summary>9-sliced rounded rectangle, radius 32 px. Set
    /// <see cref="UnityEngine.UI.Image.pixelsPerUnitMultiplier"/> to
    /// <see cref="RoundMultiplier"/>(radius) for any other radius.</summary>
    public static Sprite RoundedRect
    {
        get
        {
            if (_round != null) return _round;
            int size = RoundRadiusPx * 2 + 4;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float r = RoundRadiusPx;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, size - r);
                float cy = Mathf.Clamp(y + 0.5f, r, size - r);
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                float a = Mathf.Clamp01(r - d + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = RoundRadiusPx + 1;
            _round = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            return _round;
        }
    }

    /// <summary>pixelsPerUnitMultiplier giving a corner radius in canvas units
    /// (Sky Poster units: the active theme scales them, 0 = square).</summary>
    public static float RoundMultiplier(float radius) =>
        RoundRadiusPx / Mathf.Max(1f, radius * UiTheme.Active.RadiusScale);

    /// <summary>Panel sprite of the active theme: rounded or chamfered.</summary>
    public static Sprite Panel => UiTheme.Active.Shape == UiPanelShape.Chamfer ? Chamfer : RoundedRect;

    // --- theme shapes (Prompts/31) -------------------------------------------

    private static Sprite _chamfer, _soft, _hex, _arch, _archLine, _scan, _grid, _vignette, _leaf, _softDisc;

    /// <summary>9-sliced rectangle with the top-left and bottom-right corners
    /// cut at 45 degrees (cut = 32 px, same multiplier as RoundedRect).</summary>
    public static Sprite Chamfer => _chamfer != null ? _chamfer : _chamfer = Sliced(RoundRadiusPx * 2 + 4, RoundRadiusPx + 1,
        (x, y, n) =>
        {
            float c = RoundRadiusPx;
            float tl = (n - y) + x - c;          // distance past the top-left cut line
            float br = y + (n - x) - c;          // bottom-right
            return Mathf.Clamp01(Mathf.Min(tl, br) / 1.4f + 0.5f);
        });

    /// <summary>Blurred rounded rectangle for soft shadows and glows. Its
    /// visible edge sits <see cref="SoftPad"/> units inside the sprite rect.</summary>
    public static Sprite SoftRect => _soft != null ? _soft : _soft = Sliced(96, 47, (x, y, n) =>
    {
        const float pad = 22f, r = 18f;
        float cx = Mathf.Clamp(x, pad + r, n - pad - r), cy = Mathf.Clamp(y, pad + r, n - pad - r);
        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;
        return Mathf.Clamp01(0.5f - d / (pad * 1.6f));
    });

    public const float SoftPad = 22f;

    /// <summary>Soft round blob (glows, bokeh, colour blobs).</summary>
    public static Sprite SoftDisc => _softDisc != null ? _softDisc : _softDisc = Plain(64, 64, (x, y, n, m) =>
    {
        float dx = x / n - 0.5f, dy = y / m - 0.5f;
        float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
        return Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d);
    });

    /// <summary>Flat-topped hexagon (Cockpit badges).</summary>
    public static Sprite Hex => _hex != null ? _hex : _hex = Glyph((x, y) =>
    {
        float dx = Mathf.Abs(x - 0.5f), dy = Mathf.Abs(y - 0.5f);
        return dy < 0.43f && dx < 0.48f - dy * 0.577f;
    });

    /// <summary>Leaf for the Solaria bar.</summary>
    public static Sprite Leaf => _leaf != null ? _leaf : _leaf = Glyph((x, y) =>
    {
        float u = (x + y) * 0.7071f - 0.7071f, v = (y - x) * 0.7071f;
        return v * v * 9f + u * u * 2.2f < 0.2f;
    });

    /// <summary>Card with a tall arch on top (Solaria): top corners 64 px,
    /// bottom 18 px; 1 px = 1 unit at multiplier 1.</summary>
    public static Sprite Arch => _arch != null ? _arch : _arch = ArchSprite(false);

    /// <summary>The arch as a 1 px hairline (the gold inner line).</summary>
    public static Sprite ArchLine => _archLine != null ? _archLine : _archLine = ArchSprite(true);

    private static Sprite ArchSprite(bool line)
    {
        const int top = 64, bottom = 18, w = top * 2 + 4, h = top + bottom + 4;
        var tex = NewTex(w, h);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            float r = fy > h / 2f ? top : bottom;
            // corner centre: the top arcs span the top 64 px, the bottom ones 18 px
            float cx = Mathf.Clamp(fx, r, w - r);
            float cy = fy > h / 2f ? Mathf.Min(fy, h - top) : Mathf.Max(fy, bottom);
            float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
            float a = Mathf.Clamp01(r - d + 0.5f);              // inside = r - d
            if (line) a *= Mathf.Clamp01(d - r + 2f);           // only the outer 1.5 px
            px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(top + 1, bottom + 1, top + 1, top + 1));
    }

    /// <summary>Scanlines: 3 px period, 1 dark row. Use Image.Type.Tiled.</summary>
    public static Sprite Scanlines => _scan != null ? _scan : _scan = Tile(2, 3, (x, y) => y == 0 ? 0.32f : 0f, Color.black);

    /// <summary>40 px grid of 1 px lines. Use Image.Type.Tiled.</summary>
    public static Sprite Grid => _grid != null ? _grid : _grid = Tile(40, 40, (x, y) => x == 0 || y == 0 ? 1f : 0f, Color.white);

    /// <summary>Dark edges, clear centre.</summary>
    public static Sprite Vignette => _vignette != null ? _vignette : _vignette = Plain(64, 64, (x, y, n, m) =>
    {
        float dx = x / n - 0.5f, dy = y / m - 0.5f;
        float d = Mathf.Sqrt(dx * dx + dy * dy) * 1.6f;
        return Mathf.Clamp01((d - 0.45f) / 0.55f);
    }, Color.black);

    private static Sprite Sliced(int n, int border, System.Func<float, float, int, float> alpha)
    {
        var tex = NewTex(n, n);
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
            px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f, n)) * 255f));
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    private static Sprite Plain(int w, int h, System.Func<float, float, float, float, float> alpha, Color? tint = null)
    {
        var tex = NewTex(w, h);
        var c = tint ?? Color.white;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float a = Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f, w, h));
            px[y * w + x] = new Color(c.r, c.g, c.b, a);
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite Tile(int w, int h, System.Func<int, int, float> alpha, Color tint)
    {
        var tex = NewTex(w, h);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Point;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            px[y * w + x] = new Color(tint.r, tint.g, tint.b, alpha(x, y));
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    /// <summary>White vertical gradient, alpha 1 at the top, 0 at the bottom
    /// (tint the Image; for a two-colour gradient stack two).</summary>
    public static Sprite VerticalGradient
    {
        get
        {
            if (_gradient != null) return _gradient;
            var tex = NewTex(2, 64);
            var px = new Color32[2 * 64];
            for (int y = 0; y < 64; y++)
            {
                byte a = (byte)(255f * y / 63f);
                px[y * 2] = px[y * 2 + 1] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _gradient = Sprite.Create(tex, new Rect(0, 0, 2, 64), new Vector2(0.5f, 0.5f), 100f);
            return _gradient;
        }
    }

    /// <summary>Tick mark glyph (the UI font has none).</summary>
    public static Sprite Check => _check != null ? _check : _check = Glyph((x, y) =>
        SegDist(x, y, 0.22f, 0.52f, 0.42f, 0.30f) < 0.075f || SegDist(x, y, 0.42f, 0.30f, 0.80f, 0.72f) < 0.075f);

    /// <summary>Padlock glyph.</summary>
    public static Sprite Lock => _lock != null ? _lock : _lock = Glyph((x, y) =>
        (x > 0.25f && x < 0.75f && y > 0.12f && y < 0.55f) ||
        (y >= 0.55f && Mathf.Abs(Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.55f) * (y - 0.55f)) - 0.17f) < 0.055f));

    /// <summary>Gear glyph (settings).</summary>
    public static Sprite Gear => _gear != null ? _gear : _gear = Glyph((x, y) =>
    {
        float dx = x - 0.5f, dy = y - 0.5f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        float ang = Mathf.Atan2(dy, dx);
        float rim = 0.30f + (Mathf.Cos(ang * 8f) > 0.3f ? 0.09f : 0f);
        return d < rim && d > 0.12f;
    });

    private static float SegDist(float x, float y, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay;
        float t = Mathf.Clamp01(((x - ax) * vx + (y - ay) * vy) / (vx * vx + vy * vy));
        float px = ax + vx * t - x, py = ay + vy * t - y;
        return Mathf.Sqrt(px * px + py * py);
    }

    private static Sprite Glyph(System.Func<float, float, bool> inside)
    {
        const int n = 64, ss = 3;
        var tex = NewTex(n, n);
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int hits = 0;
            for (int sy = 0; sy < ss; sy++)
            for (int sx = 0; sx < ss; sx++)
                if (inside((x + (sx + 0.5f) / ss) / n, (y + (sy + 0.5f) / ss) / n)) hits++;
            px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * hits / (ss * ss)));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Texture2D NewTex(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
    {
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.HideAndDontSave,
    };
}
