using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Big callout text in the middle-top of the screen (SUPPLY DROP, specials),
/// with an optional arrow on the top edge pointing at a world x. Lives on the
/// MenuCanvas, never blocks touches. Phase 2's FIRST BLOOD / DOUBLE KILL go
/// through here too.
/// </summary>
public class BannerOverlay : MonoBehaviour
{
    private static BannerOverlay _instance;

    private Text _text;
    private CanvasGroup _group;
    private RectTransform _arrow;
    private Image _arrowImage;
    private float _arrowWorldX;
    private float _arrowUntil;
    private Coroutine _fade;

    public static void Show(string text, Color color, float? worldX = null)
    {
        var mc = MenuCanvas.Instance;
        if (mc == null) return;
        if (_instance == null)
        {
            var go = new GameObject("Banner");
            go.transform.SetParent(mc.Root, false);
            UiKit.Stretch(go);
            _instance = go.AddComponent<BannerOverlay>();
            _instance.Build();
        }
        _instance.transform.SetAsLastSibling();
        _instance._text.font = UiTheme.Display;   // the theme may have changed since Build
        _instance.Play(text, color, worldX);
    }

    private void Build()
    {
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;
        _group.alpha = 0f;

        var r = Ui.Node(transform, "Text");
        Ui.Place(r, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(700f, 60f));
        _text = r.gameObject.AddComponent<Text>();
        _text.font = UiTheme.Display;
        _text.fontSize = 44;
        _text.alignment = TextAnchor.MiddleCenter;
        _text.horizontalOverflow = HorizontalWrapMode.Overflow;
        _text.raycastTarget = false;
        var shadow = r.gameObject.AddComponent<Shadow>();
        var bs = UiTheme.BackdropShadow;
        shadow.effectColor = bs.a > 0f ? bs : new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(0f, -4f);

        _arrow = Ui.Node(transform, "Arrow");
        Ui.Place(_arrow, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(26f, 26f));
        _arrow.localRotation = Quaternion.Euler(0f, 0f, 45f);
        _arrowImage = _arrow.gameObject.AddComponent<Image>();
        _arrowImage.sprite = UiSprites.RoundedRect;
        _arrowImage.type = Image.Type.Sliced;
        _arrowImage.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(4f);
        _arrowImage.raycastTarget = false;
        _arrow.gameObject.SetActive(false);
    }

    private void Play(string text, Color color, float? worldX)
    {
        _text.text = text;
        _text.color = color;
        _arrowImage.color = color;
        UiTween.Stop(_fade);
        _group.alpha = 1f;
        UiTween.Scale(_text.transform, Vector3.one * 0.5f, Vector3.one, 0.3f, UiTween.Ease.OutBack);
        _fade = UiTween.Run(0.5f, k => { if (_group != null) _group.alpha = 1f - k; }, UiTween.Ease.Linear, 1.8f);

        _arrowUntil = worldX.HasValue ? Time.time + 3f : 0f;
        _arrowWorldX = worldX ?? 0f;
        _arrow.gameObject.SetActive(worldX.HasValue);
    }

    private void Update()
    {
        if (_arrow == null || !_arrow.gameObject.activeSelf) return;
        if (Time.time > _arrowUntil) { _arrow.gameObject.SetActive(false); return; }
        var cam = Camera.main;
        var parent = (RectTransform)transform;
        if (cam == null) return;
        float vx = cam.WorldToViewportPoint(new Vector3(_arrowWorldX, 0f, 0f)).x;
        _arrow.anchoredPosition = new Vector2(Mathf.Clamp01(vx) * parent.rect.width,
            -20f - 6f * Mathf.Abs(Mathf.Sin(Time.time * 8f)));
    }
}
