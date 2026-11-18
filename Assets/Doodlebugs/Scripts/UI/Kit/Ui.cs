using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Widgets of the Sky Poster kit (Prompts/30 §3): rounded panels with an ink
/// outline and a hard drop shadow, pills, icon badges, stat bars, press
/// buttons. Everything is built at runtime from <see cref="UiSprites"/>; no
/// prefab, no art asset.
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

    public static Text Text(Transform parent, string name, string content, Font font, int size, Color color,
        TextAnchor align = TextAnchor.MiddleLeft)
    {
        var r = Node(parent, name);
        Inset(r, 0, 0, 0, 0);
        var t = r.gameObject.AddComponent<Text>();
        t.text = content;
        t.font = font;
        t.fontSize = Mathf.Max(UiTheme.MinText, size);
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.supportRichText = false;
        return t;
    }

    /// <summary>Text that sizes itself to its content (inside layout groups).</summary>
    public static Text AutoText(Transform parent, string name, string content, Font font, int size, Color color)
    {
        var t = Text(parent, name, content, font, size, color, TextAnchor.MiddleLeft);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return t;
    }

    // --- shapes ------------------------------------------------------------

    public static Image Round(Transform parent, string name, Color color, float radius, bool raycast = false)
    {
        var r = Node(parent, name);
        Inset(r, 0, 0, 0, 0);
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = UiSprites.RoundedRect;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(radius);
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    /// <summary>
    /// Card: hard ink shadow `shadow` units below, ink outline, face colour
    /// inset by the outline. Children added to the returned root draw above
    /// the face. The face is the raycast target.
    /// </summary>
    public class Panel
    {
        public RectTransform Root;
        public Image Shadow, Outline, Face;
        public float Radius;

        public void SetFace(Color c) { if (Face != null) Face.color = c; }

        public void SetShadow(float depth)
        {
            if (Shadow == null) return;
            var r = Shadow.rectTransform;
            r.offsetMin = new Vector2(0f, -depth);
            r.offsetMax = new Vector2(0f, -depth);
            Shadow.enabled = depth > 0f;
        }
    }

    public static Panel Card(Transform parent, string name, Color face, float radius = UiTheme.RadiusCard,
        float shadow = 6f, bool outline = true)
    {
        var p = new Panel { Root = Node(parent, name), Radius = radius };
        p.Shadow = Round(p.Root, "Shadow", UiTheme.Ink, radius);
        p.SetShadow(shadow);
        if (outline)
        {
            p.Outline = Round(p.Root, "Outline", UiTheme.Ink, radius);
            p.Face = Round(p.Root, "Face", face, Mathf.Max(2f, radius - UiTheme.Outline), true);
            Inset(p.Face.rectTransform, UiTheme.Outline, UiTheme.Outline, UiTheme.Outline, UiTheme.Outline);
        }
        else
        {
            p.Face = Round(p.Root, "Face", face, radius, true);
        }
        return p;
    }

    /// <summary>Pill-shaped label (glass by default) that sizes to its text.</summary>
    public static (RectTransform root, Image bg, Text label) Pill(Transform parent, string name, string text,
        Color bg, Color fg, float height = 26f, int size = UiTheme.SizeSmall, Font font = null)
    {
        var r = Node(parent, name);
        r.sizeDelta = new Vector2(80f, height);
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = UiSprites.RoundedRect;
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

    /// <summary>Round icon badge: category-coloured disc, ink ring, sprite on top.</summary>
    public static (RectTransform root, Image disc, Image icon) Badge(Transform parent, string name, Sprite icon,
        Color disc, float size, Color? tint = null)
    {
        var r = Node(parent, name);
        r.sizeDelta = new Vector2(size, size);
        var ring = Node(r, "Ring");
        Inset(ring, 0, 0, 0, 0);
        var ringImg = ring.gameObject.AddComponent<Image>();
        ringImg.sprite = UiSprites.Disc;
        ringImg.color = UiTheme.Ink;
        ringImg.raycastTarget = false;

        var d = Node(r, "Disc");
        Inset(d, UiTheme.Outline, UiTheme.Outline, UiTheme.Outline, UiTheme.Outline);
        var discImg = d.gameObject.AddComponent<Image>();
        discImg.sprite = UiSprites.Disc;
        discImg.color = disc;
        discImg.raycastTarget = false;

        var ic = Node(r, "Icon");
        float pad = size * 0.16f;
        Inset(ic, pad, pad, pad, pad);
        var iconImg = ic.gameObject.AddComponent<Image>();
        iconImg.sprite = icon;
        iconImg.preserveAspect = true;
        iconImg.color = tint ?? Color.white;
        iconImg.enabled = icon != null;
        iconImg.raycastTarget = false;
        return (r, discImg, iconImg);
    }

    /// <summary>Thin rounded progress bar, 0..1.</summary>
    public static Image Bar(Transform parent, string name, float value, Color fill, Color track)
    {
        var r = Node(parent, name);
        var trackImg = r.gameObject.AddComponent<Image>();
        trackImg.sprite = UiSprites.RoundedRect;
        trackImg.type = Image.Type.Sliced;
        trackImg.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(4f);
        trackImg.color = track;
        trackImg.raycastTarget = false;

        var f = Node(r, "Fill");
        f.anchorMin = Vector2.zero;
        f.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        f.offsetMin = Vector2.zero;
        f.offsetMax = Vector2.zero;
        var fillImg = f.gameObject.AddComponent<Image>();
        fillImg.sprite = UiSprites.RoundedRect;
        fillImg.type = Image.Type.Sliced;
        fillImg.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(4f);
        fillImg.color = fill;
        fillImg.raycastTarget = false;
        fillImg.enabled = value > 0.01f;
        return fillImg;
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
        c.Label = AutoText(row, "Label", label, UiTheme.Display, UiTheme.SizeTitle, UiTheme.Ink);

        var chip = Node(row, "Count");
        chip.sizeDelta = new Vector2(38f, 24f);
        var chipLe = chip.gameObject.AddComponent<LayoutElement>();
        chipLe.preferredWidth = 38f;
        chipLe.preferredHeight = 24f;
        var chipImg = chip.gameObject.AddComponent<Image>();
        chipImg.sprite = UiSprites.RoundedRect;
        chipImg.type = Image.Type.Sliced;
        chipImg.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(12f);
        chipImg.color = UiTheme.Ink;
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
