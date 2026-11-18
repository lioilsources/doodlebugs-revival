using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The phone-first canvas for menus (Prompts/30 §3). The scene canvas is
/// scaled for a 1920x1080 monitor, which puts a unit at 0.37 pt on a phone;
/// this one is 800x400 with Expand, so a unit is ~0.94 pt on an iPhone mini
/// and ~1.08 on a Pro Max. Built at runtime - no scene change. The scene's
/// EventSystem serves both canvases.
///
/// <see cref="Root"/> is a safe-area container: everything a finger touches
/// lives under it; full-bleed backgrounds go on <see cref="Full"/>.
/// </summary>
public class MenuCanvas : MonoBehaviour
{
    public static MenuCanvas Instance { get; private set; }

    public static readonly Vector2 Reference = new Vector2(800f, 400f);

    public Canvas Canvas { get; private set; }

    /// <summary>Full-screen layer (backgrounds).</summary>
    public RectTransform Full { get; private set; }

    /// <summary>Safe-area layer (everything interactive).</summary>
    public RectTransform Root { get; private set; }

    private Rect _appliedSafe = new Rect(-1, -1, -1, -1);
    private Vector2Int _appliedScreen;
    private float _appliedScale;

    public static MenuCanvas Create()
    {
        if (Instance != null) return Instance;

        var go = new GameObject("MenuCanvas");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;   // above the scene canvas (100): GameHUD must not draw over the hangar

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = Reference;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referencePixelsPerUnit = 100f;

        go.AddComponent<GraphicRaycaster>();

        var mc = go.AddComponent<MenuCanvas>();
        mc.Canvas = canvas;

        var full = new GameObject("Full");
        full.transform.SetParent(go.transform, false);
        mc.Full = UiKit.Stretch(full);

        var root = new GameObject("SafeArea");
        root.transform.SetParent(go.transform, false);
        mc.Root = UiKit.Stretch(root);
        mc.ApplySafeArea();
        return mc;
    }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update() => ApplySafeArea();

    private void ApplySafeArea()
    {
        var safe = Screen.safeArea;
        var screen = new Vector2Int(Screen.width, Screen.height);
        // The CanvasScaler sets scaleFactor on its first update, after Create
        // ran: until then it is 1 and the safe-area insets would be taken in
        // pixels (2-3x too wide - the hangar sat squeezed in the middle).
        float s = Canvas != null ? Canvas.scaleFactor : 1f;
        if (safe == _appliedSafe && screen == _appliedScreen && Mathf.Approximately(s, _appliedScale)) return;
        _appliedSafe = safe;
        _appliedScreen = screen;
        _appliedScale = s;
        UiKit.FitSafeArea(Root, Canvas);
    }

    /// <summary>A full-screen layer for one screen: background part under
    /// Full, interactive part under Root. Both start inactive.</summary>
    public (RectTransform full, RectTransform safe) CreateLayer(string name)
    {
        var f = new GameObject(name + "_Bg");
        f.transform.SetParent(Full, false);
        var fr = UiKit.Stretch(f);
        var s = new GameObject(name);
        s.transform.SetParent(Root, false);
        var sr = UiKit.Stretch(s);
        return (fr, sr);
    }
}
