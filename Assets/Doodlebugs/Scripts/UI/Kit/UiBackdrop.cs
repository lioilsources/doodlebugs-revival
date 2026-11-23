using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The theme's treatment of the arena behind the hangar (Prompts/31 §3.4).
///
/// Filters (mono phosphor, posterize, grayscale, blur) run once per arena
/// and theme on the CPU at a small size: the arena texture is blitted into a
/// tiny RenderTexture, read back and processed. No custom shader, so nothing
/// can fail to compile on a device; a failure just returns the arena as is.
/// The arenas' own textures are not readable - the blit is what makes the
/// pixels reachable.
///
/// Overlays (scanlines, grid, vignette, colour blobs, vines) are plain
/// Images above the veil. Theme art (backdrop, wordmark, ornaments) comes
/// from Resources/Sprites/UiThemes/&lt;key&gt;/ and every piece is optional.
/// </summary>
public static class UiBackdrop
{
    private static readonly Dictionary<string, Sprite> _cache = new();

    /// <summary>The arena as the active theme shows it.</summary>
    public static Sprite Filtered(Sprite arena)
    {
        var t = UiTheme.Active;
        if (arena == null || t.Filter == UiBackdropFilter.None) return arena;
        string key = arena.GetInstanceID() + "/" + t.Key;
        if (_cache.TryGetValue(key, out var s) && s != null) return s;
        try
        {
            s = Process(arena, t);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[UiBackdrop] filter failed, showing the arena as is: " + e.Message);
            s = arena;
        }
        _cache[key] = s;
        return s;
    }

    private static Sprite Process(Sprite arena, UiThemeDef t)
    {
        int w = t.Filter switch
        {
            UiBackdropFilter.Posterize => 288,   // chunky pixels
            UiBackdropFilter.Blur => 96,
            _ => 512,
        };
        var r = arena.textureRect;
        int h = Mathf.Max(1, Mathf.RoundToInt(w * r.height / r.width));
        var tex = arena.texture;
        var scale = new Vector2(r.width / tex.width, r.height / tex.height);
        var offset = new Vector2(r.x / tex.width, r.y / tex.height);

        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt, scale, offset);
        RenderTexture.active = rt;
        var read = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        var px = read.GetPixels32();
        switch (t.Filter)
        {
            case UiBackdropFilter.Mono: Mono(px, t.FilterTint); break;
            case UiBackdropFilter.Grayscale: Mono(px, new Color(0.93f, 0.95f, 0.95f)); break;
            case UiBackdropFilter.Posterize: Posterize(px, t.FilterLevels); break;
            case UiBackdropFilter.Blur: Blur(px, w, h, 3); Blur(px, w, h, 3); break;
        }
        read.SetPixels32(px);
        read.filterMode = t.Filter == UiBackdropFilter.Posterize ? FilterMode.Point : FilterMode.Bilinear;
        read.wrapMode = TextureWrapMode.Clamp;
        read.Apply(false, true);
        return Sprite.Create(read, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    private static void Mono(Color32[] px, Color tint)
    {
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            float l = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
            l = Mathf.Clamp01((l - 0.08f) * 1.25f);
            px[i] = new Color32((byte)(l * tint.r * 255f), (byte)(l * tint.g * 255f), (byte)(l * tint.b * 255f), 255);
        }
    }

    private static void Posterize(Color32[] px, int levels)
    {
        float n = Mathf.Max(2, levels) - 1;
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            px[i] = new Color32(Q(c.r, n), Q(c.g, n), Q(c.b, n), 255);
        }

        static byte Q(byte v, float n) => (byte)(Mathf.Round(v / 255f * n) / n * 255f);
    }

    /// <summary>Box blur, separable, radius r. Two passes look like a Gaussian.</summary>
    private static void Blur(Color32[] px, int w, int h, int r)
    {
        var tmp = new Color32[px.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            var src = pass == 0 ? px : tmp;
            var dst = pass == 0 ? tmp : px;
            int lines = pass == 0 ? h : w, len = pass == 0 ? w : h;
            for (int l = 0; l < lines; l++)
            {
                for (int i = 0; i < len; i++)
                {
                    int sr = 0, sg = 0, sb = 0, n = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int j = Mathf.Clamp(i + k, 0, len - 1);
                        var c = pass == 0 ? src[l * w + j] : src[j * w + l];
                        sr += c.r; sg += c.g; sb += c.b; n++;
                    }
                    var o = new Color32((byte)(sr / n), (byte)(sg / n), (byte)(sb / n), 255);
                    if (pass == 0) dst[l * w + i] = o; else dst[i * w + l] = o;
                }
            }
        }
    }

    // --- theme art -------------------------------------------------------------

    public static Sprite Art(string name)
    {
        var t = UiTheme.Active;
        return Resources.Load<Sprite>(t.ArtPath + name);
    }

    // --- overlays ----------------------------------------------------------------

    /// <summary>Build the theme's overlay under <paramref name="parent"/> (full screen).</summary>
    public static void BuildOverlay(RectTransform parent)
    {
        var t = UiTheme.Active;
        switch (t.Overlay)
        {
            case UiOverlay.Scanlines:
                Tiled(parent, "Scanlines", UiSprites.Scanlines, Color.white);
                Full(parent, "Vignette", UiSprites.Vignette, new Color(1f, 1f, 1f, 0.7f));
                break;
            case UiOverlay.Grid:
                Tiled(parent, "Grid", UiSprites.Grid, t.OverlayA);
                break;
            case UiOverlay.Vignette:
                Full(parent, "Vignette", UiSprites.Vignette, Color.white);
                break;
            case UiOverlay.Bokeh:
                Blob(parent, new Vector2(0.80f, 0.85f), 220f, t.OverlayA);
                Blob(parent, new Vector2(0.31f, 0.56f), 46f, UiTheme.WithAlpha(t.OverlayA, 0.35f));
                Blob(parent, new Vector2(0.09f, 0.26f), 70f, UiTheme.WithAlpha(t.OverlayA, 0.25f));
                Blob(parent, new Vector2(0.95f, 0.38f), 56f, UiTheme.WithAlpha(t.OverlayA, 0.25f));
                var flare = Art("orn_flare");
                if (flare != null) Place(parent, "Flare", flare, new Vector2(0.8f, 0.85f), 200f, Color.white);
                break;
            case UiOverlay.Blobs:
            case UiOverlay.Aurora:
                Blob(parent, new Vector2(0.12f, 0.82f), 520f, t.OverlayA);
                Blob(parent, new Vector2(0.88f, 0.88f), 560f, t.OverlayB);
                Blob(parent, new Vector2(0.60f, 0.0f), 620f, t.OverlayC);
                break;
            case UiOverlay.Facets:
                Facets(parent);
                break;
            case UiOverlay.Vines:
                var vine = Art("orn_vine");
                if (vine != null)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var v = Ui.Sprite(parent, "Vine" + i, vine);
                        var r = v.rectTransform;
                        r.anchorMin = r.anchorMax = new Vector2(i / 2.5f + 0.05f, 1f);
                        r.pivot = new Vector2(0.5f, 1f);
                        r.sizeDelta = new Vector2(380f, 120f);
                        r.anchoredPosition = new Vector2(0f, 26f);
                        r.localRotation = Quaternion.Euler(0f, 0f, i == 1 ? 180f : 0f);
                        if (i == 1) r.localScale = new Vector3(1f, -1f, 1f);
                    }
                }
                else
                {
                    for (int i = 0; i < 18; i++)
                    {
                        var leaf = Ui.Sprite(parent, "Leaf" + i, UiSprites.Leaf);
                        var r = leaf.rectTransform;
                        r.anchorMin = r.anchorMax = new Vector2((i + 0.5f) / 18f, 1f);
                        r.sizeDelta = new Vector2(34f, 34f);
                        r.anchoredPosition = new Vector2(0f, -10f - 8f * Mathf.Sin(i * 1.7f));
                        r.localRotation = Quaternion.Euler(0f, 0f, 200f + 40f * Mathf.Sin(i * 2.3f));
                        leaf.color = Color.Lerp(t.OverlayA, Color.black, 0.15f * (i % 3));
                    }
                }
                break;
        }
    }

    private static Image Full(RectTransform parent, string name, Sprite s, Color c)
    {
        var img = Ui.Sprite(parent, name, s);
        img.preserveAspect = false;
        img.color = c;
        Ui.Inset(img.rectTransform, 0, 0, 0, 0);
        return img;
    }

    private static void Tiled(RectTransform parent, string name, Sprite s, Color c)
    {
        var img = Full(parent, name, s, c);
        img.type = Image.Type.Tiled;
    }

    private static void Blob(RectTransform parent, Vector2 at, float size, Color c)
    {
        Place(parent, "Blob", UiSprites.SoftDisc, at, size, c);
    }

    private static void Place(RectTransform parent, string name, Sprite s, Vector2 at, float size, Color c)
    {
        var img = Ui.Sprite(parent, name, s);
        var r = img.rectTransform;
        r.anchorMin = r.anchorMax = at;
        r.sizeDelta = new Vector2(size, size);
        img.color = c;
    }

    private static void Facets(RectTransform parent)
    {
        // big flat triangles: rotated squares half off screen
        var rnd = new System.Random(5);
        for (int i = 0; i < 7; i++)
        {
            var img = Ui.Sprite(parent, "Facet" + i, null);
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble() * 0.8f);
            float s = 220f + 180f * (float)rnd.NextDouble();
            r.sizeDelta = new Vector2(s, s);
            r.localRotation = Quaternion.Euler(0f, 0f, 45f + 30f * (float)rnd.NextDouble());
            img.color = i % 2 == 0 ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.07f);
        }
    }
}
