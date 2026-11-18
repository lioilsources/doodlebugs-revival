using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One hangar offer card (Prompts/30 §4): category band with the verb,
/// icon badge, name, one-line description, an animated sketch of the
/// projectile's path, and up to three stat bars - or a footer line for
/// upgrades. The selected card lifts, tilts and bobs. The active UI theme
/// (Prompts/31) decides the panel, band, badge, bars and the motion.
/// </summary>
public class DraftCard : MonoBehaviour
{
    public struct Data
    {
        public Color Category;
        public string Verb;
        public string Corner;          // "KEEP", "1 PT", ...
        public Sprite Icon;
        public Sprite Glyph;           // icon without its disc: drawn on a theme badge
        public Color IconTint;
        public string Title;
        public string Description;
        public PreviewKind? Preview;
        public (string label, float value)[] Stats;
        public string Footer;          // replaces the stats (upgrades)
        public bool Disabled;
        public Action OnClick;
    }

    private RectTransform _body;
    private Ui.Panel _panel;
    private GameObject _check;
    private Text _corner;
    private bool _selected;
    private float _phase;
    private Button _button;
    private UiThemeDef _theme;

    public static DraftCard Build(Transform parent, Data d, int index)
    {
        var root = Ui.Node(parent, "Card_" + d.Title);
        var le = root.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minWidth = 120f;
        var card = root.gameObject.AddComponent<DraftCard>();
        card._theme = UiTheme.Active;
        card.BuildInside(root, d);
        UiTween.EnterFromRight(card._body, index);
        card._phase = index * 0.7f;
        return card;
    }

    private void BuildInside(RectTransform root, Data d)
    {
        var t = UiTheme.Active;
        bool inset = t.CardInset > 0f;
        bool arch = t.ArchCards;
        _body = Ui.Node(root, "Body");
        Ui.Inset(_body, 0, 0, 0, 0);

        Color faceColor = d.Disabled ? UiTheme.PaperDim : inset ? t.Frame : UiTheme.Paper;
        _panel = Ui.Card(_body, "Panel", faceColor);
        Ui.Inset(_panel.Root, 0, 0, 0, 0);
        var face = _panel.Face.rectTransform;
        if (arch) MakeArch();
        Color band = UiTheme.BandFor(d.Category);
        float bandH = arch ? 50f : 28f;

        // Band: a coloured strip on top - or, inside the arch, a centred label
        if (arch)
        {
            var verb = Ui.Text(face, "Verb", d.Verb ?? "", UiTheme.Label, UiTheme.SizeLabel, UiTheme.BandText, TextAnchor.MiddleCenter);
            Ui.TopBand(verb.rectTransform, 13f, 16f, 10f, 10f);
            _corner = Ui.Text(face, "Corner", d.Corner ?? "", UiTheme.Label, UiTheme.SizeSmall, UiTheme.Highlight, TextAnchor.MiddleCenter);
            Ui.TopBand(_corner.rectTransform, 30f, 15f, 10f, 10f);
        }
        else
        {
            var bandImg = Ui.Round(face, "Band", band, UiTheme.RadiusCard - 3f);
            Ui.Paint(bandImg, band);
            Ui.TopBand(bandImg.rectTransform, 0f, bandH);
            var square = Ui.Node(bandImg.rectTransform, "Square");
            Ui.BottomBand(square, 0f, 12f);
            var sq = square.gameObject.AddComponent<Image>();
            sq.color = band;
            sq.raycastTarget = false;
            if (band.a < 0.02f) bandImg.enabled = sq.enabled = false;
            var verb = Ui.Text(bandImg.rectTransform, "Verb", d.Verb ?? "", UiTheme.Label, UiTheme.SizeLabel, UiTheme.BandText);
            Ui.Inset(verb.rectTransform, 10f, 0f, 44f, 0f);
            _corner = Ui.Text(bandImg.rectTransform, "Corner", d.Corner ?? "", UiTheme.Label, UiTheme.SizeLabel, UiTheme.BandText,
                TextAnchor.MiddleRight);
            Ui.Inset(_corner.rectTransform, 10f, 0f, 10f, 0f);
            if (t.Line.a > 0f && t.Outline > 0f && t.Band.a <= 0f && t.Key == "phosphor")
            {
                var rule = Ui.Node(face, "Rule");
                Ui.TopBand(rule, bandH, 2f);
                rule.gameObject.AddComponent<Image>().color = t.Line;
            }
        }

        // Inner display (Chrome screen, Canopy parchment) or the face itself
        Transform host = face;
        float y0 = bandH + 6f;
        if (inset)
        {
            float k = t.CardInset;
            var display = Ui.Round(face, "Display", d.Disabled ? UiTheme.PaperDim : t.Paper, 8f);
            Ui.Paint(display, d.Disabled ? UiTheme.PaperDim : t.Paper);
            Ui.Inset(display.rectTransform, k, k, k, bandH);
            host = display.transform;
            y0 = 6f;
            if ((t.FaceFx & UiFaceFx.Planks) != 0) Planks(face, bandH);
            if ((t.FaceFx & UiFaceFx.Bevel) != 0) Rivets(face);
        }

        // Icon + name + description
        float iconSize = t.ScaleDisplay < 0.7f || t.Badge == UiBadgeStyle.Hex ? 46f : 52f;
        Graphic iconG;
        if (d.Glyph != null)
        {
            var (br, discImg, glyphImg) = Ui.Badge(host, "Icon", d.Glyph, d.Category, iconSize, d.IconTint == default ? (Color?)null : d.IconTint);
            Ui.Place(br, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -y0), new Vector2(iconSize, iconSize));
            iconG = glyphImg;
            if (d.Disabled) discImg.color = UiTheme.WithAlpha(discImg.color, 0.45f);
        }
        else
        {
            var icon = Ui.Sprite(host, "Icon", d.Icon);
            icon.color = (d.IconTint == default ? Color.white : d.IconTint) * t.ArtTint;
            Ui.Place(icon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -y0), new Vector2(iconSize, iconSize));
            iconG = icon;
        }
        if (d.Disabled) iconG.color = new Color(1f, 1f, 1f, 0.45f);

        float textLeft = iconSize + 14f;
        var title = Ui.Text(host, "Title", d.Title, UiTheme.Display, UiTheme.SizeCardTitle, UiTheme.Ink);
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = UiTheme.MinText;
        title.resizeTextMaxSize = title.fontSize;
        title.horizontalOverflow = HorizontalWrapMode.Wrap;
        Ui.TopBand(title.rectTransform, y0, 28f, textLeft, 6f);
        var desc = Ui.Text(host, "Desc", d.Description ?? "", UiTheme.Body, UiTheme.SizeSmall, UiTheme.TextDim, TextAnchor.UpperLeft);
        desc.lineSpacing = 0.9f;
        desc.resizeTextForBestFit = true;
        desc.resizeTextMinSize = UiTheme.MinText;
        desc.resizeTextMaxSize = desc.fontSize;
        desc.verticalOverflow = VerticalWrapMode.Truncate;
        Ui.TopBand(desc.rectTransform, y0 + 28f, 32f, textLeft, 6f);

        float y = y0 + iconSize + 10f;
        if (d.Preview.HasValue)
        {
            var strip = Ui.Round(host, "Path", UiTheme.PaperDim, 8f);
            Ui.TopBand(strip.rectTransform, y, 26f, 10f, 10f);
            strip.gameObject.AddComponent<PathPreview>().Init(d.Preview.Value, d.Category);
            y += 32f;
        }

        if (!string.IsNullOrEmpty(d.Footer))
        {
            var foot = Ui.Text(host, "Footer", d.Footer, UiTheme.Label, 15, UiTheme.Ink, TextAnchor.UpperLeft);
            Ui.TopBand(foot.rectTransform, y + 2f, 40f, 10f, 10f);
        }
        else if (d.Stats != null)
        {
            foreach (var (label, value) in d.Stats)
            {
                var l = Ui.Text(host, "L_" + label, label, UiTheme.Label, UiTheme.SizeSmall, UiTheme.Ink);
                Ui.TopBand(l.rectTransform, y - 2f, 16f, 10f, 0f);
                var bar = Ui.Bar(host, "B_" + label, value, UiTheme.BarFill, UiTheme.PaperDim);
                Ui.TopBand(bar.transform.parent as RectTransform, y + 2f, 8f, 58f, 10f);
                y += 18f;
            }
        }

        // Selected check
        var check = Ui.Node(face, "Check");
        if (arch) Ui.Place(check, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(24f, 24f));
        else Ui.Place(check, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -2f), new Vector2(24f, 24f));
        var disc = check.gameObject.AddComponent<Image>();
        disc.sprite = UiSprites.Disc;
        disc.color = UiTheme.Confirm;
        disc.raycastTarget = false;
        var tick = Ui.Sprite(check, "Tick", UiSprites.Check);
        tick.color = UiTheme.OnConfirm;
        Ui.Inset(tick.rectTransform, 3f, 3f, 3f, 3f);
        _check = check.gameObject;
        _check.SetActive(false);

        if (!d.Disabled && d.OnClick != null)
        {
            _button = Ui.Press(_panel.Face, d.OnClick);
        }
    }

    /// <summary>Solaria: the panel's sprites become the arch, plus a gold hairline inside.</summary>
    private void MakeArch()
    {
        foreach (var img in new[] { _panel.Shadow, _panel.Outline, _panel.Face })
        {
            if (img == null || img.sprite == UiSprites.SoftRect) continue;
            img.sprite = UiSprites.Arch;
            img.pixelsPerUnitMultiplier = 1f;
        }
        var line = Ui.Sprite(_panel.Face.transform, "ArchLine", UiSprites.ArchLine);
        line.type = Image.Type.Sliced;
        line.preserveAspect = false;
        line.pixelsPerUnitMultiplier = 1.07f;
        line.color = UiTheme.Line;
        Ui.Inset(line.rectTransform, 5f, 5f, 5f, 5f);
        line.transform.SetAsFirstSibling();
    }

    private static void Planks(Transform face, float bandH)
    {
        for (float y = bandH + 30f; y < 260f; y += 32f)
        {
            var r = Ui.Node(face, "Plank");
            Ui.TopBand(r, y, 2f, 3f, 3f);
            var img = r.gameObject.AddComponent<Image>();
            img.color = new Color(0.23f, 0.13f, 0.06f, 0.45f);
            img.raycastTarget = false;
            r.SetAsFirstSibling();
        }
        var wood = UiBackdrop.Art("tile_wood");
        if (wood != null)
        {
            var tex = Ui.Sprite(face, "Wood", wood);
            tex.type = Image.Type.Tiled;
            tex.preserveAspect = false;
            tex.color = new Color(1f, 1f, 1f, 0.55f);
            Ui.Inset(tex.rectTransform, 0, 0, 0, 0);
            tex.transform.SetAsFirstSibling();
        }
    }

    private static void Rivets(Transform face)
    {
        foreach (var at in new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) })
        {
            var r = Ui.Node(face, "Rivet");
            r.anchorMin = r.anchorMax = at;
            r.sizeDelta = new Vector2(6f, 6f);
            r.anchoredPosition = new Vector2(at.x < 0.5f ? 7f : -7f, at.y < 0.5f ? 4f : -6f);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = UiSprites.Disc;
            img.color = new Color(0.22f, 0.25f, 0.3f);
            img.raycastTarget = false;
            var hi = Ui.Node(r, "Hi");
            Ui.Inset(hi, 1.5f, 2.5f, 2.5f, 1.5f);
            var hii = hi.gameObject.AddComponent<Image>();
            hii.sprite = UiSprites.Disc;
            hii.color = new Color(0.95f, 0.97f, 1f);
            hii.raycastTarget = false;
        }
    }

    public void SetSelected(bool selected)
    {
        if (_selected == selected) return;
        _selected = selected;
        _check.SetActive(selected);
        if (_corner != null) _corner.enabled = !selected;
        _panel.SetShadow(selected ? 12f : 6f);
        if (selected) UiTween.Punch(_body, 0.06f);
        else
        {
            _body.localRotation = Quaternion.identity;
            _body.anchoredPosition = Vector2.zero;
            _body.localScale = Vector3.one;
        }
    }

    public void SetInteractable(bool on)
    {
        if (_button != null) _button.interactable = on;
    }

    private void Update()
    {
        if (!_selected || _body == null) return;
        var t = _theme ?? UiTheme.Active;
        float s = Mathf.Sin((Time.unscaledTime + _phase) * Mathf.PI * 2f / 2.6f);
        _body.anchoredPosition = new Vector2(_body.anchoredPosition.x, t.SelectLift + t.SelectBob * s);
        _body.localRotation = Quaternion.Euler(0f, 0f, t.SelectTilt);
        if (t.SelectScale != 1f) _body.localScale = Vector3.one * t.SelectScale;
    }
}

/// <summary>Dotted sketch of how a projectile flies; dots light up in sequence.</summary>
public class PathPreview : MonoBehaviour
{
    private readonly List<Image> _dots = new();
    private Image _ring;
    private PreviewKind _kind;

    public void Init(PreviewKind kind, Color accent)
    {
        _kind = kind;
        var rect = (RectTransform)transform;
        // Layout may not have run yet - draw in a fixed 150 x 26 box, left-anchored
        var pts = Points(kind, 150f, 26f);
        foreach (var p in pts)
        {
            var d = Ui.Node(transform, "Dot");
            d.anchorMin = d.anchorMax = new Vector2(0f, 0.5f);
            d.sizeDelta = new Vector2(5f, 5f);
            d.anchoredPosition = p;
            var img = d.gameObject.AddComponent<Image>();
            img.sprite = UiSprites.Disc;
            img.color = UiTheme.Ink;
            img.raycastTarget = false;
            _dots.Add(img);
        }
        if (kind == PreviewKind.Straight || kind == PreviewKind.Burst || kind == PreviewKind.Lurk ||
            kind == PreviewKind.Drop || kind == PreviewKind.Homing)
        {
            var r = Ui.Node(transform, "Ring");
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            float size = kind == PreviewKind.Burst || kind == PreviewKind.Lurk ? 20f : 14f;
            r.sizeDelta = new Vector2(size, size);
            r.anchoredPosition = pts.Count > 0 ? pts[pts.Count - 1] : Vector2.zero;
            _ring = r.gameObject.AddComponent<Image>();
            _ring.sprite = UiSprites.Ring(0.14f);
            _ring.color = accent;
            _ring.raycastTarget = false;
        }
    }

    private static List<Vector2> Points(PreviewKind kind, float w, float h)
    {
        var pts = new List<Vector2>();
        switch (kind)
        {
            case PreviewKind.Spread:
                for (int ray = -2; ray <= 2; ray++)
                    for (int i = 1; i <= 4; i++)
                        pts.Add(new Vector2(14f + i * 16f, ray * 2.4f * i * 0.55f));
                break;
            case PreviewKind.Drop:
                for (int i = 0; i < 9; i++) { float t = i / 8f; pts.Add(new Vector2(12f + t * 90f, 8f - 18f * t * t)); }
                break;
            case PreviewKind.Lurk:
                for (int i = 0; i < 6; i++) pts.Add(new Vector2(12f + i * 10f * (1f - i * 0.08f), 0f));
                break;
            case PreviewKind.Charge:
                for (int i = 0; i < 14; i++) pts.Add(new Vector2(12f + i * 9.5f, 0f));
                break;
            case PreviewKind.Backward:
                for (int i = 0; i < 8; i++) pts.Add(new Vector2(110f - i * 13f, 0f));
                break;
            case PreviewKind.Burst:
                for (int i = 0; i < 7; i++) pts.Add(new Vector2(12f + i * 12f, 0f));
                break;
            case PreviewKind.Homing:
                for (int i = 0; i < 10; i++) { float t = i / 9f; pts.Add(new Vector2(12f + t * 110f, 8f * Mathf.Sin(t * Mathf.PI * 1.5f))); }
                break;
            default:
                for (int i = 0; i < 9; i++) pts.Add(new Vector2(12f + i * 14f, 0f));
                break;
        }
        return pts;
    }

    private void Update()
    {
        int n = _dots.Count;
        if (n == 0) return;
        float head = (Time.unscaledTime * (_kind == PreviewKind.Charge ? 8f : 12f)) % (n + 6);
        for (int i = 0; i < n; i++)
        {
            float d = head - i;
            float a = d >= 0f && d < 4f ? 1f - d / 4f * 0.7f : 0.25f;
            var c = _dots[i].color;
            _dots[i].color = new Color(c.r, c.g, c.b, a);
        }
        if (_ring != null)
        {
            float pulse = head > n ? 1f : 0.35f;
            var c = _ring.color;
            _ring.color = new Color(c.r, c.g, c.b, pulse);
            _ring.transform.localScale = Vector3.one * (head > n ? 1f + 0.1f * (head - n) : 0.8f);
        }
    }
}
