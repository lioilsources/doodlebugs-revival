using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI theme picker (Prompts/31 §3.5), opened from SETTINGS. Every tile is a
/// live preview built with its own theme's tokens (<see cref="UiTheme.With"/>):
/// panel, band, type and two bars - no screenshots to keep in sync. A tap
/// applies the theme at once; the screens behind rebuild, the sheet too.
/// Four channels: the rebuild (visual), a confirm sound, a light haptic and
/// a "THEME · NAME" banner.
/// </summary>
public class ThemeSheet : MonoBehaviour
{
    private static ThemeSheet _open;

    public static void Open(Transform parent)
    {
        if (_open != null) return;
        var go = new GameObject("ThemeSheet");
        go.transform.SetParent(parent, false);
        UiKit.Stretch(go);
        _open = go.AddComponent<ThemeSheet>();
        _open.Build();
        UiTween.Scale(_open._card, Vector3.one * 0.94f, Vector3.one, 0.2f);
        SfxManager.PlayTick();
        Telemetry.Log("theme_sheet_open");
    }

    private RectTransform _card;

    private void Awake() => UiTheme.Changed += OnChanged;

    private void OnDestroy()
    {
        UiTheme.Changed -= OnChanged;
        if (_open == this) _open = null;
    }

    private void OnChanged(UiThemeDef _)
    {
        foreach (Transform c in transform) Destroy(c.gameObject);
        Build();
        transform.SetAsLastSibling();
        UiTween.Punch(_card, 0.03f, 0.2f);
    }

    private void Close()
    {
        SfxManager.PlayTick();
        Destroy(gameObject);
    }

    private void Build()
    {
        var dim = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        dim.color = UiTheme.WithAlpha(UiTheme.Active.VeilBottom, 0.9f);   // swallows touches

        var card = Ui.Card(transform, "Card", UiTheme.Paper);
        _card = card.Root;
        Ui.Place(card.Root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780f, 340f));
        var face = card.Face.transform;

        var title = Ui.Text(face, "Title", Loc.T("theme.title"), UiTheme.Display, UiTheme.SizeTitle, UiTheme.Ink);
        Ui.TopBand(title.rectTransform, 10f, 36f, 20f, 200f);
        var sub = Ui.Text(face, "Sub", Loc.T("theme.sub"),
            UiTheme.Body, UiTheme.SizeSmall, UiTheme.TextDim);
        Ui.TopBand(sub.rectTransform, 46f, 18f, 20f, 200f);

        var grid = Ui.Node(face, "Grid");
        Ui.Inset(grid, 16f, 70f, 16f, 72f);
        var g = grid.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(115f, 92f);
        g.spacing = new Vector2(9f, 10f);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 6;
        g.childAlignment = TextAnchor.UpperCenter;

        _activeOutside = UiTheme.Active;
        foreach (var def in UiThemeCatalog.All)
        {
            var captured = def;
            UiTheme.With(def, () => Tile(grid, captured));
        }
        _activeOutside = null;

        var done = Ui.BigButton(face, "Done", Loc.T("common.done"), UiTheme.Confirm, Close);
        done.Label.fontSize = Mathf.Max(UiTheme.MinText, Mathf.RoundToInt(22 * UiTheme.ScaleFor(UiTheme.Display)));
        Ui.Place(done.Panel.Root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 14f), new Vector2(150f, 48f));
        UiAudit.Check(transform);
    }

    /// <summary>One preview, built while <paramref name="def"/> is the active theme.</summary>
    private void Tile(RectTransform grid, UiThemeDef def)
    {
        bool current = def == ActiveOutside;
        var cell = Ui.Node(grid, "Theme_" + def.Key);

        // the theme's "sky": its veil colours stand in for the arena
        var sky = Ui.Round(cell, "Sky", Color.white, 12f);
        UiGradient.Set(sky, Opaque(def.VeilTop.a > 0.4f ? def.VeilTop : Color.Lerp(def.VeilBottom, Color.white, 0.25f)),
            Opaque(def.VeilBottom));
        sky.raycastTarget = true;

        var panel = Ui.Card(cell, "Panel", def.CardInset > 0f ? def.Frame : def.Paper, UiTheme.RadiusCard, 4f);
        Ui.Inset(panel.Root, 8f, 8f, 8f, 26f);
        var face = panel.Face.transform;
        var band = Ui.Round(face, "Band", UiTheme.BandFor(def.Weapon), 4f);
        Ui.Paint(band, UiTheme.BandFor(def.Weapon));
        Ui.TopBand(band.rectTransform, 0f, 14f);
        var aa = Ui.Text(face, "Aa", "Aa", UiTheme.Display, 22, def.CardInset > 0f ? def.FrameText : def.Text, TextAnchor.MiddleLeft);
        Ui.TopBand(aa.rectTransform, 15f, 24f, 6f, 4f);
        var b1 = Ui.Bar(face, "B1", 0.75f, def.BarFill, def.PaperDim);
        Ui.TopBand(b1.transform.parent as RectTransform, 42f, 6f, 6f, 6f);
        var b2 = Ui.Bar(face, "B2", 0.4f, def.BarFill, def.PaperDim);
        Ui.TopBand(b2.transform.parent as RectTransform, 51f, 6f, 6f, 6f);

        var name = Ui.Text(cell, "Name", def.LocalizedName, UiTheme.Label, UiTheme.SizeSmall, def.OnBackdrop, TextAnchor.MiddleCenter);
        Ui.TopBand(name.rectTransform, 3f, 20f, 2f, 2f);
        name.resizeTextForBestFit = true;
        name.resizeTextMinSize = UiTheme.MinText;
        name.resizeTextMaxSize = name.fontSize;

        if (current)
        {
            var ring = Ui.Sprite(cell, "Current", UiSprites.RoundedRect);
            ring.type = Image.Type.Sliced;
            ring.pixelsPerUnitMultiplier = 32f / 12f;
            ring.color = Color.white;
            ring.preserveAspect = false;
            Ui.Inset(ring.rectTransform, -3f, -3f, -3f, -3f);
            ring.transform.SetAsFirstSibling();
            var check = Ui.Node(cell, "Check");
            Ui.Place(check, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-2f, 2f), new Vector2(22f, 22f));
            var disc = check.gameObject.AddComponent<Image>();
            disc.sprite = UiSprites.Disc;
            disc.color = def.Confirm;
            var tick = Ui.Sprite(check, "Tick", UiSprites.Check);
            tick.color = def.OnConfirm;
            Ui.Inset(tick.rectTransform, 3f, 3f, 3f, 3f);
        }

        // the preview panel sits on top of the sky and catches touches too
        foreach (var g in new Graphic[] { panel.Face, panel.Outline, panel.Shadow })
            if (g != null) g.raycastTarget = false;
        Ui.Press(sky, () => Pick(def));
    }

    // The theme active outside UiTheme.With - read once per build.
    private static UiThemeDef ActiveOutside => _activeOutside ?? UiTheme.Active;
    private static UiThemeDef _activeOutside;

    private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

    private void Pick(UiThemeDef def)
    {
        if (def == UiTheme.Active) return;
        SfxManager.PlayConfirm();
        SfxManager.Haptic(HapticKind.Light);
        ThemeSettings.Set(def);
        BannerOverlay.Show(Loc.F("settings.theme", def.LocalizedName), UiTheme.Highlight);
    }
}
