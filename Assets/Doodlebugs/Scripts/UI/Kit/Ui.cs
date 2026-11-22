using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Widgets of the UI kit (Prompts/30 §3), styled by the active theme
/// (Prompts/31): panels with an outline and a hard, soft or glowing shadow,
/// pills, icon badges, stat bars, press buttons. Everything is built at
/// runtime from <see cref="UiSprites"/>; no prefab.
/// </summary>
public static class Ui
{
    // --- rect helpers ------------------------------------------------------

    public static RectTransform Node(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    /// <summary>Anchor a rect to a corner/edge of its parent with a pixel offset and size.</summary>
    public static RectTransform Place(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        r.anchorMin = anchor;
        r.anchorMax = anchor;
        r.pivot = pivot;
        r.anchoredPosition = pos;
        r.sizeDelta = size;
        return r;
    }

    /// <summary>Stretch with insets: left, bottom, right, top (positive = inward).</summary>
    public static RectTransform Inset(RectTransform r, float left, float bottom, float right, float top)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = new Vector2(left, bottom);
        r.offsetMax = new Vector2(-right, -top);
        return r;
    }

    /// <summary>Stretched horizontally across the top, fixed height.</summary>
    public static RectTransform TopBand(RectTransform r, float top, float height, float left = 0f, float right = 0f)
    {
        r.anchorMin = new Vector2(0f, 1f);
        r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.offsetMin = new Vector2(left, -top - height);
        r.offsetMax = new Vector2(-right, -top);
        return r;
    }

    public static RectTransform BottomBand(RectTransform r, float bottom, float height, float left = 0f, float right = 0f)
    {
        r.anchorMin = new Vector2(0f, 0f);
        r.anchorMax = new Vector2(1f, 0f);
        r.pivot = new Vector2(0.5f, 0f);
        r.offsetMin = new Vector2(left, bottom);
        r.offsetMax = new Vector2(-right, bottom + height);
        return r;
    }

    // --- text --------------------------------------------------------------

    /// <summary>Text in a theme font. The size is in Sky Poster units: the
    /// active theme scales it per font role and adds its letter spacing.</summary>
    public static Text Text(Transform parent, string name, string content, Font font, int size, Color color,
        TextAnchor align = TextAnchor.MiddleLeft)
    {
        var r = Node(parent, name);
        Inset(r, 0, 0, 0, 0);
        var t = r.gameObject.AddComponent<Text>();
        t.text = content;
        t.font = font;
        t.fontSize = Mathf.Max(UiTheme.MinText, Mathf.RoundToInt(size * UiTheme.ScaleFor(font)));
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.supportRichText = false;
        float em = UiTheme.TrackFor(font);
        if (Mathf.Abs(em) > 0.001f) r.gameObject.AddComponent<UiTracking>().Em = em;
        return t;
    }

    /// <summary>Text that sizes itself to its content (inside layout groups).</summary>
    public static Text AutoText(Transform parent, string name, string content, Font font, int size, Color color)
    {
        var t = Text(parent, name, content, font, size, color, TextAnchor.MiddleLeft);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return t;
    }

    /// <summary>Drop shadow under text that sits on the arena (theme colour;
    /// none when the theme has no backdrop shadow).</summary>
    public static void BackdropShadow(Text t, float depth)
    {
        var c = UiTheme.BackdropShadow;
        if (c.a <= 0f) return;
        var sh = t.gameObject.AddComponent<Shadow>();
        sh.effectColor = c;
        sh.effectDistance = UiTheme.Active.Shadow == UiShadowKind.Glow ? Vector2.zero : new Vector2(0f, -depth);
    }

    // --- shapes ------------------------------------------------------------

    /// <summary>Rounded (or chamfered, per theme) rectangle; radius in Sky Poster units.</summary>
    public static Image Round(Transform parent, string name, Color color, float radius, bool raycast = false)
    {
        var r = Node(parent, name);
        Inset(r, 0, 0, 0, 0);
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = UiSprites.Panel;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(radius);
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    /// <summary>Paint a face colour, as a gradient when the theme paints it so.</summary>
    public static void Paint(Image img, Color c)
    {
        img.color = c;
        var b = UiTheme.GradientFor(c);
        if (b.a > 0f) UiGradient.Set(img, Color.white, new Color(b.r / Mathf.Max(c.r, 0.01f), b.g / Mathf.Max(c.g, 0.01f),
            b.b / Mathf.Max(c.b, 0.01f), 1f));
        else UiGradient.Clear(img);
    }

    /// <summary>
    /// Card: shadow (hard, soft or glow, per theme), outline, face colour
    /// inset by the outline, plus the theme's face treatment (gloss, clay,
    /// bevel). Children added to the returned root draw above the face. The
    /// face is the raycast target.
    /// </summary>
    public class Panel
    {
        public RectTransform Root;
        public Image Shadow, Outline, Face;
        public float Radius;
        public UiShadowKind Kind;
        public Vector2 Skew;
        private RectTransform _fx;

        public void SetFace(Color c)
        {
            if (Face == null) return;
            Paint(Face, c);
            if (_fx != null) _fx.gameObject.SetActive(c.a > 0.5f);
        }

        public void SetShadow(float depth)
        {
            if (Shadow == null) return;
            var r = Shadow.rectTransform;
            Shadow.enabled = depth > 0f;
            switch (Kind)
            {
                case UiShadowKind.Soft:
                    float pad = UiSprites.SoftPad * 0.5f + depth * 0.6f;
                    r.offsetMin = new Vector2(-pad, -pad - depth * 1.2f);
                    r.offsetMax = new Vector2(pad, pad - depth * 1.2f);
                    break;
                case UiShadowKind.Glow:
                    float g = UiSprites.SoftPad * 0.5f + depth * 0.5f;
                    r.offsetMin = new Vector2(-g, -g);
                    r.offsetMax = new Vector2(g, g);
                    break;
                default:
                    r.offsetMin = new Vector2(depth * Skew.x, -depth);
                    r.offsetMax = new Vector2(depth * Skew.x, -depth);
                    break;
            }
        }

        internal void AttachFx(RectTransform fx) => _fx = fx;
    }

    public static Panel Card(Transform parent, string name, Color face, float radius = UiTheme.RadiusCard,
        float shadow = 6f, bool outline = true)
    {
        var t = UiTheme.Active;
        var p = new Panel { Root = Node(parent, name), Radius = radius, Kind = t.Shadow, Skew = new Vector2(t.ShadowSkew, 0f) };
        if (t.Shadow == UiShadowKind.Soft || t.Shadow == UiShadowKind.Glow)
        {
            var sr = Node(p.Root, "Shadow");
            p.Shadow = sr.gameObject.AddComponent<Image>();
            p.Shadow.sprite = UiSprites.SoftRect;
            p.Shadow.type = Image.Type.Sliced;
            p.Shadow.pixelsPerUnitMultiplier = 1.4f;
            p.Shadow.color = t.ShadowColor;
            p.Shadow.raycastTarget = false;
            sr.anchorMin = Vector2.zero;
            sr.anchorMax = Vector2.one;
        }
        else if (t.Shadow == UiShadowKind.Hard)
        {
            p.Shadow = Round(p.Root, "Shadow", t.ShadowColor, radius);
        }
        p.SetShadow(shadow);

        float ol = outline ? t.Outline : 0f;
        if (ol > 0f)
        {
            p.Outline = Round(p.Root, "Outline", t.Line, radius);
            p.Face = Round(p.Root, "Face", face, Mathf.Max(2f, radius - ol / Mathf.Max(0.01f, t.RadiusScale)), true);
            Inset(p.Face.rectTransform, ol, ol, ol, ol);
        }
        else
        {
            p.Face = Round(p.Root, "Face", face, radius, true);
        }
        Paint(p.Face, face);
        AddFaceFx(p, radius);
        return p;
    }

    private static void AddFaceFx(Panel p, float radius)
    {
        var fx = UiTheme.Active.FaceFx;
        if (fx == UiFaceFx.None || fx == UiFaceFx.Planks) return;
        var host = Node(p.Face.transform, "Fx");
        Inset(host, 0, 0, 0, 0);
        host.SetAsFirstSibling();
        p.AttachFx(host);
        if ((fx & UiFaceFx.Gloss) != 0)
        {
            // Aqua: a lozenge highlight over the top half
            var g = Round(host, "Gloss", new Color(1f, 1f, 1f, 0.42f), radius);
            var r = g.rectTransform;
            r.anchorMin = new Vector2(0f, 0.52f);
            r.anchorMax = new Vector2(1f, 1f);
            r.offsetMin = new Vector2(3f, 0f);
            r.offsetMax = new Vector2(-3f, -2f);
        }
        if ((fx & UiFaceFx.Clay) != 0)
        {
            // Clay: soft light on the upper rim, soft shade at the bottom
            var hi = Node(host, "Light");
            var hiImg = hi.gameObject.AddComponent<Image>();
            hiImg.sprite = UiSprites.SoftRect;
            hiImg.type = Image.Type.Sliced;
            hiImg.color = new Color(1f, 1f, 1f, 0.75f);
            hiImg.raycastTarget = false;
            hi.anchorMin = new Vector2(0f, 0.55f);
            hi.anchorMax = new Vector2(1f, 1f);
            hi.offsetMin = new Vector2(-6f, -4f);
            hi.offsetMax = new Vector2(6f, 10f);
            var lo = Node(host, "Shade");
            var loImg = lo.gameObject.AddComponent<Image>();
            loImg.sprite = UiSprites.SoftRect;
            loImg.type = Image.Type.Sliced;
            loImg.color = new Color(0.43f, 0.27f, 0.63f, 0.16f);
            loImg.raycastTarget = false;
            lo.anchorMin = new Vector2(0f, 0f);
            lo.anchorMax = new Vector2(1f, 0.35f);
            lo.offsetMin = new Vector2(-6f, -14f);
            lo.offsetMax = new Vector2(6f, 2f);
        }
        if ((fx & UiFaceFx.Bevel) != 0)
        {
            // Chrome: a bright top edge and a dark bottom edge
            var top = Node(host, "BevelTop");
            TopBand(top, 1f, 2f, 3f, 3f);
            var ti = top.gameObject.AddComponent<Image>();
            ti.color = new Color(1f, 1f, 1f, 0.8f);
            ti.raycastTarget = false;
            var bot = Node(host, "BevelBottom");
            BottomBand(bot, 1f, 3f, 3f, 3f);
            var bi = bot.gameObject.AddComponent<Image>();
            bi.color = new Color(0f, 0f, 0f, 0.4f);
            bi.raycastTarget = false;
        }
    }

    /// <summary>Pill-shaped label (glass by default) that sizes to its text.</summary>
    public static (RectTransform root, Image bg, Text label) Pill(Transform parent, string name, string text,
        Color bg, Color fg, float height = 26f, int size = UiTheme.SizeSmall, Font font = null)
    {
        var r = Node(parent, name);
        r.sizeDelta = new Vector2(80f, height);
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = UiSprites.Panel;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(height / 2f);
        img.color = bg;
        img.raycastTarget = false;

        var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(11, 11, 0, 0);
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        var fit = r.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        var le = r.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;

        var t = AutoText(r, "Label", text, font ?? UiTheme.BodyStrong, size, fg);
        t.alignment = TextAnchor.MiddleCenter;
        return (r, img, t);
    }

    /// <summary>Icon badge in the theme's style (disc, ring, square, hex...):
    /// category colour, line rim, sprite on top. Returns the disc so callers
    /// can recolour it.</summary>
    public static (RectTransform root, Image disc, Image icon) Badge(Transform parent, string name, Sprite icon,
        Color disc, float size, Color? tint = null)
    {
        var t = UiTheme.Active;
        var r = Node(parent, name);
        r.sizeDelta = new Vector2(size, size);
        float ol = Mathf.Max(t.Outline, t.Badge == UiBadgeStyle.Ring || t.Badge == UiBadgeStyle.Halo ? 2f : 0f);

        Image Shape(string n, Color c, float inset)
        {
            var node = Node(r, n);
            Inset(node, inset, inset, inset, inset);
            var img = node.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = c;
            switch (t.Badge)
            {
                case UiBadgeStyle.Square:
                case UiBadgeStyle.Plate:
                case UiBadgeStyle.Squircle:
                    img.sprite = UiSprites.Panel;
                    img.type = Image.Type.Sliced;
                    float rad = t.Badge == UiBadgeStyle.Squircle ? size * 0.36f : t.Badge == UiBadgeStyle.Plate ? 6f : 0f;
                    img.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(rad / Mathf.Max(0.01f, t.RadiusScale > 0f ? t.RadiusScale : 1f));
                    if (t.Badge == UiBadgeStyle.Square) img.pixelsPerUnitMultiplier = 32f;
                    break;
                case UiBadgeStyle.Hex:
                    img.sprite = UiSprites.Hex;
                    break;
                default:
                    img.sprite = UiSprites.Disc;
                    break;
            }
            return img;
        }

        Image discImg;
        switch (t.Badge)
        {
            case UiBadgeStyle.Ring:
                // outline only, in the category colour
                Shape("Ring", disc, 0f).sprite = UiSprites.Ring(0.04f);
                discImg = Shape("Disc", UiTheme.WithAlpha(t.Paper, 0.6f), 3f);
                break;
            case UiBadgeStyle.Halo:
                var glow = Node(r, "Glow");
                Inset(glow, -size * 0.25f, -size * 0.25f, -size * 0.25f, -size * 0.25f);
                var gi = glow.gameObject.AddComponent<Image>();
                gi.sprite = UiSprites.SoftDisc;
                gi.color = UiTheme.WithAlpha(disc, 0.45f);
                gi.raycastTarget = false;
                discImg = Shape("Disc", new Color(1f, 1f, 1f, 0.06f), 0f);
                Shape("Ring", disc, 0f).sprite = UiSprites.Ring(0.025f);
                break;
            case UiBadgeStyle.Ringed:
                Shape("Gold", t.Line, 0f);
                Shape("Gap", Color.white, 1.5f);
                discImg = Shape("Disc", disc, 4f);
                break;
            default:
                if (ol > 0f) Shape("Ring", t.Line, 0f);
                discImg = Shape("Disc", disc, ol);
                break;
        }

        if (t.Badge == UiBadgeStyle.Orb)
        {
            var gl = Node(r, "Gloss");
            gl.anchorMin = new Vector2(0.2f, 0.52f);
            gl.anchorMax = new Vector2(0.8f, 0.92f);
            gl.offsetMin = gl.offsetMax = Vector2.zero;
            var gli = gl.gameObject.AddComponent<Image>();
            gli.sprite = UiSprites.Disc;
            gli.color = new Color(1f, 1f, 1f, 0.45f);
            gli.raycastTarget = false;
        }

        var ic = Node(r, "Icon");
        float pad = size * (t.Badge == UiBadgeStyle.Hex ? 0.24f : 0.16f);
        Inset(ic, pad, pad, pad, pad);
        var iconImg = ic.gameObject.AddComponent<Image>();
        iconImg.sprite = icon;
        iconImg.preserveAspect = true;
        iconImg.color = (tint ?? Color.white) * t.ArtTint;
        iconImg.enabled = icon != null;
        iconImg.raycastTarget = false;
        return (r, discImg, iconImg);
    }

    /// <summary>Progress bar, 0..1, in the theme's style (solid, segments,
    /// LED, beam...). Returns the fill (for a segmented style: the first
    /// segment); the bar's root is its transform.parent.</summary>
    public static Image Bar(Transform parent, string name, float value, Color fill, Color track)
    {
        var t = UiTheme.Active;
        value = Mathf.Clamp01(value);
        var r = Node(parent, name);

        switch (t.Bar)
        {
            case UiBarStyle.Blocks:
            case UiBarStyle.Led:
            case UiBarStyle.Slant:
            case UiBarStyle.Bamboo:
            case UiBarStyle.Leaves:
                return Segments(r, value, fill, track, t);
        }

        // continuous styles: track + fill
        float trackH = t.Bar switch { UiBarStyle.Line => 1f, UiBarStyle.Beam => 1f, _ => 0f };
        var trackImg = r.gameObject.AddComponent<Image>();
        trackImg.raycastTarget = false;
        trackImg.color = t.Bar == UiBarStyle.Beam || t.Bar == UiBarStyle.Line ? UiTheme.WithAlpha(t.TextDim, 0.6f) : track;
        if (trackH > 0f)
        {
            // a hairline track through the middle: draw it as a child, keep the root clear
            trackImg.color = new Color(0f, 0f, 0f, 0f);
            var line = Node(r, "TrackLine");
            line.anchorMin = new Vector2(0f, 0.5f);
            line.anchorMax = new Vector2(1f, 0.5f);
            line.offsetMin = new Vector2(0f, -trackH * 0.5f);
            line.offsetMax = new Vector2(0f, trackH * 0.5f);
            var li = line.gameObject.AddComponent<Image>();
            li.color = UiTheme.WithAlpha(t.TextDim, 0.6f);
            li.raycastTarget = false;
        }
        else
        {
            trackImg.sprite = UiSprites.RoundedRect;
            trackImg.type = Image.Type.Sliced;
            trackImg.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(t.Bar == UiBarStyle.Hairline ? 0f : 4f);
        }

        var f = Node(r, "Fill");
        float h = t.Bar switch { UiBarStyle.Line => 0.7f, UiBarStyle.Beam => 0.3f, _ => 1f };
        f.anchorMin = new Vector2(0f, 0.5f - h / 2f);
        f.anchorMax = new Vector2(value, 0.5f + h / 2f);
        f.offsetMin = Vector2.zero;
        f.offsetMax = Vector2.zero;
        var fillImg = f.gameObject.AddComponent<Image>();
        fillImg.sprite = UiSprites.RoundedRect;
        fillImg.type = Image.Type.Sliced;
        fillImg.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(t.Bar == UiBarStyle.Hairline ? 0f : 4f);
        fillImg.color = fill;
        fillImg.raycastTarget = false;
        fillImg.enabled = value > 0.01f;

        if (t.Bar == UiBarStyle.Tube)
        {
            var g = Node(f, "Gloss");
            g.anchorMin = new Vector2(0f, 0.5f);
            g.anchorMax = new Vector2(1f, 1f);
            g.offsetMin = new Vector2(2f, 0f);
            g.offsetMax = new Vector2(-2f, -1f);
            var gi = g.gameObject.AddComponent<Image>();
            gi.color = new Color(1f, 1f, 1f, 0.45f);
            gi.raycastTarget = false;
        }
        else if (t.Bar == UiBarStyle.Beam && value > 0.01f)
        {
            UiGradient.Set(fillImg, Color.white, Color.white);
            var dot = Node(r, "Dot");
            dot.anchorMin = dot.anchorMax = new Vector2(value, 0.5f);
            dot.sizeDelta = new Vector2(8f, 8f);
            var di = dot.gameObject.AddComponent<Image>();
            di.sprite = UiSprites.Disc;
            di.color = Color.white;
            di.raycastTarget = false;
        }
        else if (t.Bar == UiBarStyle.Line && value > 0.01f)
        {
            var glow = Node(f, "Glow");
            Inset(glow, -4f, -4f, -4f, -4f);
            var gi = glow.gameObject.AddComponent<Image>();
            gi.sprite = UiSprites.SoftRect;
            gi.type = Image.Type.Sliced;
            gi.color = UiTheme.WithAlpha(fill, 0.5f);
            gi.raycastTarget = false;
            glow.SetAsFirstSibling();
        }
        return fillImg;
    }

    private static readonly Color[] LedColours =
    {
        UiTheme.Hex(0x39D353), UiTheme.Hex(0x39D353), UiTheme.Hex(0x39D353), UiTheme.Hex(0x39D353),
        UiTheme.Hex(0x39D353), UiTheme.Hex(0x39D353), UiTheme.Hex(0xFFD23F), UiTheme.Hex(0xFFD23F),
        UiTheme.Hex(0xFFD23F), UiTheme.Hex(0xFF4B3A),
    };

    private static Image Segments(RectTransform r, float value, Color fill, Color track, UiThemeDef t)
    {
        int n = t.Bar switch { UiBarStyle.Led => 10, UiBarStyle.Bamboo => 5, UiBarStyle.Leaves => 8, _ => 8 };
        float gap = t.Bar switch { UiBarStyle.Bamboo => 2f, UiBarStyle.Leaves => 1f, _ => 2f };
        var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = gap;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;
        Image first = null;
        float lit = value * n;
        for (int i = 0; i < n; i++)
        {
            var s = Node(r, "Seg" + i);
            var img = s.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            bool on = i < Mathf.Round(lit) || (i == 0 && value > 0.01f);
            Color c = t.Bar == UiBarStyle.Led ? LedColours[i] : fill;
            switch (t.Bar)
            {
                case UiBarStyle.Leaves:
                    img.sprite = UiSprites.Leaf;
                    img.preserveAspect = true;
                    img.color = on ? c : UiTheme.WithAlpha(c, 0.22f);
                    break;
                case UiBarStyle.Bamboo:
                    img.sprite = UiSprites.RoundedRect;
                    img.type = Image.Type.Sliced;
                    img.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(5f);
                    img.color = on ? c : track;
                    break;
                default:
                    img.color = on ? c : (t.Bar == UiBarStyle.Led ? UiTheme.WithAlpha(t.PaperDim, 1f) : UiTheme.WithAlpha(fill, 0.22f));
                    if (t.Bar == UiBarStyle.Slant) s.localRotation = Quaternion.identity;
                    break;
            }
            if (first == null) first = img;
        }
        return first;
    }

    public static Image Sprite(Transform parent, string name, Sprite sprite, bool pixel = true)
    {
        var r = Node(parent, name);
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Make a graphic a button with a squash on press and a tick sound.</summary>
    public static Button Press(Graphic target, Action onClick, bool sound = true)
    {
        var go = target.gameObject;
        target.raycastTarget = true;
        var b = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        b.targetGraphic = target;
        b.transition = Selectable.Transition.None;
        var nav = b.navigation;
        nav.mode = Navigation.Mode.None;
        b.navigation = nav;
        b.onClick.AddListener(() =>
        {
            if (sound) SfxManager.PlayNamed("tab", 0.7f, 0.05f);
            onClick?.Invoke();
        });
        var squash = go.GetComponent<PressSquash>() ?? go.AddComponent<PressSquash>();
        squash.Target = FindSquashTarget(go.transform);
        return b;
    }

    // A card's face is inset inside its root: squash the whole card.
    private static Transform FindSquashTarget(Transform t) =>
        t.name == "Face" && t.parent != null ? t.parent : t;

    /// <summary>Scales its target down while pressed, back with a little overshoot.</summary>
    public class PressSquash : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Transform Target;
        private Coroutine _c;
        private bool _down;

        public void OnPointerDown(PointerEventData e)
        {
            var sel = GetComponent<Selectable>();
            if (sel != null && !sel.interactable) return;
            _down = true;
            Go(0.94f, 0.06f, UiTween.Ease.OutCubic);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_down) return;
            _down = false;
            Go(1f, 0.18f, UiTween.Ease.OutBack);
        }

        public void OnPointerExit(PointerEventData e) => OnPointerUp(e);

        private void Go(float to, float seconds, UiTween.Ease ease)
        {
            var t = Target != null ? Target : transform;
            UiTween.Stop(_c);
            float from = t.localScale.x;
            _c = UiTween.Run(seconds, k => { if (t != null) t.localScale = Vector3.one * Mathf.LerpUnclamped(from, to, k); }, ease);
        }

        private void OnDisable()
        {
            UiTween.Stop(_c);
            var t = Target != null ? Target : transform;
            if (t != null) t.localScale = Vector3.one;
        }
    }

    /// <summary>Big CTA (READY / FLY / JOIN): green panel, display label, optional count chip.</summary>
    public class Cta
    {
        public Panel Panel;
        public Button Button;
        public Text Label;
        public Text Count;
        public RectTransform CountChip;
    }

    public static Cta BigButton(Transform parent, string name, string label, Color face, Action onClick)
    {
        var c = new Cta { Panel = Card(parent, name, face, UiTheme.RadiusCta, 5f) };
        var row = Node(c.Panel.Root, "Row");
        Inset(row, 0, 0, 0, 0);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleCenter;
        h.spacing = 12f;
        h.childControlWidth = true;
        h.childControlHeight = false;
        h.childForceExpandWidth = false;
        c.Label = AutoText(row, "Label", label, UiTheme.Display, UiTheme.SizeTitle, UiTheme.OnConfirm);

        var chip = Node(row, "Count");
        chip.sizeDelta = new Vector2(38f, 24f);
        var chipLe = chip.gameObject.AddComponent<LayoutElement>();
        chipLe.preferredWidth = 38f;
        chipLe.preferredHeight = 24f;
        var chipImg = chip.gameObject.AddComponent<Image>();
        chipImg.sprite = UiSprites.Panel;
        chipImg.type = Image.Type.Sliced;
        chipImg.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(12f);
        chipImg.color = UiTheme.OnConfirm;
        chipImg.raycastTarget = false;
        c.Count = Text(chip, "Text", "", UiTheme.BodyStrong, 14, face, TextAnchor.MiddleCenter);
        c.CountChip = chip;
        chip.gameObject.SetActive(false);

        c.Button = Press(c.Panel.Face, onClick, false);
        return c;
    }

    public static void SetCount(Cta c, int seconds)
    {
        if (c == null) return;
        bool show = seconds > 0;
        c.CountChip.gameObject.SetActive(show);
        if (show) c.Count.text = seconds.ToString();
    }
}
