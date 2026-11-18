using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One hangar offer card (Prompts/30 §4): category band with the verb,
/// icon badge, name, one-line description, an animated sketch of the
/// projectile's path, and up to three stat bars - or a footer line for
/// upgrades. The selected card lifts, tilts and bobs.
/// </summary>
public class DraftCard : MonoBehaviour
{
    public struct Data
    {
        public Color Category;
        public string Verb;
        public string Corner;          // "KEEP", "1 PT", ...
        public Sprite Icon;
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

    public static DraftCard Build(Transform parent, Data d, int index)
    {
        var root = Ui.Node(parent, "Card_" + d.Title);
        var le = root.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minWidth = 120f;
        var card = root.gameObject.AddComponent<DraftCard>();
        card.BuildInside(root, d);
        UiTween.EnterFromRight(card._body, index);
        card._phase = index * 0.7f;
        return card;
    }

    private void BuildInside(RectTransform root, Data d)
    {
        _body = Ui.Node(root, "Body");
        Ui.Inset(_body, 0, 0, 0, 0);

        _panel = Ui.Card(_body, "Panel", d.Disabled ? UiTheme.PaperDim : UiTheme.Paper);
        Ui.Inset(_panel.Root, 0, 0, 0, 0);
        var face = _panel.Face.rectTransform;

        // Band: rounded top, squared bottom
        var band = Ui.Round(face, "Band", d.Category, UiTheme.RadiusCard - UiTheme.Outline);
        Ui.TopBand(band.rectTransform, 0f, 28f);
        var square = Ui.Node(band.rectTransform, "Square");
        Ui.BottomBand(square, 0f, 12f);
        var sq = square.gameObject.AddComponent<Image>();
        sq.color = d.Category;
        sq.raycastTarget = false;
        var verb = Ui.Text(band.rectTransform, "Verb", d.Verb ?? "", UiTheme.Label, UiTheme.SizeLabel, UiTheme.Ink);
        Ui.Inset(verb.rectTransform, 10f, 0f, 44f, 0f);
        _corner = Ui.Text(band.rectTransform, "Corner", d.Corner ?? "", UiTheme.Label, UiTheme.SizeLabel, UiTheme.Ink,
            TextAnchor.MiddleRight);
        Ui.Inset(_corner.rectTransform, 10f, 0f, 10f, 0f);

        // Icon + name + description
        var icon = Ui.Sprite(face, "Icon", d.Icon);
        icon.color = d.IconTint == default ? Color.white : d.IconTint;
        Ui.Place(icon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -34f), new Vector2(52f, 52f));
        if (d.Disabled) icon.color = new Color(1f, 1f, 1f, 0.45f);

        var title = Ui.Text(face, "Title", d.Title, UiTheme.Display, UiTheme.SizeCardTitle, UiTheme.Ink);
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 16;
        title.resizeTextMaxSize = UiTheme.SizeCardTitle;
        title.horizontalOverflow = HorizontalWrapMode.Wrap;
        Ui.TopBand(title.rectTransform, 34f, 28f, 66f, 6f);
        var desc = Ui.Text(face, "Desc", d.Description ?? "", UiTheme.Body, UiTheme.SizeSmall, UiTheme.TextDim, TextAnchor.UpperLeft);
        desc.lineSpacing = 0.9f;
        Ui.TopBand(desc.rectTransform, 62f, 30f, 66f, 6f);

        float y = 96f;
        if (d.Preview.HasValue)
        {
            var strip = Ui.Round(face, "Path", UiTheme.PaperDim, 8f);
            Ui.TopBand(strip.rectTransform, y, 26f, 10f, 10f);
            strip.gameObject.AddComponent<PathPreview>().Init(d.Preview.Value, d.Category);
            y += 32f;
        }

        if (!string.IsNullOrEmpty(d.Footer))
        {
            var foot = Ui.Text(face, "Footer", d.Footer, UiTheme.Label, 15, UiTheme.Ink, TextAnchor.UpperLeft);
            Ui.TopBand(foot.rectTransform, y + 2f, 40f, 10f, 10f);
        }
        else if (d.Stats != null)
        {
            foreach (var (label, value) in d.Stats)
            {
                var l = Ui.Text(face, "L_" + label, label, UiTheme.Label, UiTheme.SizeSmall, UiTheme.Ink);
                Ui.TopBand(l.rectTransform, y - 2f, 16f, 10f, 0f);
                var bar = Ui.Bar(face, "B_" + label, value, UiTheme.Ink, UiTheme.PaperDim);
                Ui.TopBand(bar.transform.parent as RectTransform, y + 2f, 8f, 58f, 10f);
                y += 18f;
            }
        }

        // Selected check on the band
        var check = Ui.Node(face, "Check");
        Ui.Place(check, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -2f), new Vector2(24f, 24f));
        var disc = check.gameObject.AddComponent<Image>();
        disc.sprite = UiSprites.Disc;
        disc.color = UiTheme.Confirm;
        disc.raycastTarget = false;
        var tick = Ui.Sprite(check, "Tick", UiSprites.Check);
        tick.color = UiTheme.Ink;
        Ui.Inset(tick.rectTransform, 3f, 3f, 3f, 3f);
        _check = check.gameObject;
        _check.SetActive(false);

        if (!d.Disabled && d.OnClick != null)
        {
            _button = Ui.Press(_panel.Face, d.OnClick);
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
        }
    }

    public void SetInteractable(bool on)
    {
        if (_button != null) _button.interactable = on;
    }

    private void Update()
    {
        if (!_selected || _body == null) return;
        float s = Mathf.Sin((Time.unscaledTime + _phase) * Mathf.PI * 2f / 2.6f);
        _body.anchoredPosition = new Vector2(_body.anchoredPosition.x, 8f + 3f * s);
        _body.localRotation = Quaternion.Euler(0f, 0f, -1.2f);
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
