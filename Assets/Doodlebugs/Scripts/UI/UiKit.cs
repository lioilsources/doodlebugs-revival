using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Small runtime-UI helpers for the screens that live outside GameHUD
/// (ROADMAP §3.9: new screens get their own class over shared utilities).
/// Same pixel font and flat look as the HUD.
/// </summary>
public static class UiKit
{
    private static Font _font;

    public static Font PixelFont
    {
        get
        {
            if (_font == null)
            {
                _font = Resources.Load<Font>("Fonts/PressStart2P");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return _font;
        }
    }

    /// <summary>A RectTransform with a point anchor.</summary>
    public static RectTransform Rect(GameObject go, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var rect = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        return rect;
    }

    /// <summary>A RectTransform stretched over its parent.</summary>
    public static RectTransform Stretch(GameObject go)
    {
        var rect = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    public static Text Label(Transform parent, string name, string content, int fontSize,
        Vector2 anchor, Vector2 pos, Color color, Vector2? size = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Rect(go, anchor, new Vector2(0.5f, 0.5f), pos, size ?? new Vector2(600, 30));

        var text = go.AddComponent<Text>();
        text.text = content;
        text.font = PixelFont;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>Flat rectangle button with a centred label.</summary>
    public static (Button button, Image bg, Text label) FlatButton(Transform parent, string name,
        string label, int fontSize, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
        Color color, UnityAction onClick)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Rect(go, anchor, pivot, pos, size);

        var bg = go.AddComponent<Image>();
        bg.color = color;

        var button = go.AddComponent<Button>();
        button.targetGraphic = bg;
        if (onClick != null) button.onClick.AddListener(onClick);

        var text = Label(go.transform, "Label", label, fontSize,
            new Vector2(0.5f, 0.5f), Vector2.zero, Color.white, size);
        return (button, bg, text);
    }

    /// <summary>Full-stretch image; with raycastTarget it swallows touches
    /// (that is how an overlay blocks whatever sits under it).</summary>
    public static Image Panel(Transform parent, string name, Color color, bool raycastTarget)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Stretch(go);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        return image;
    }
}
