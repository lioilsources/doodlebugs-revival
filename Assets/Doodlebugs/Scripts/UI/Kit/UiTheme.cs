using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one place colours, type sizes and fonts are read from (Prompts/30,
/// themed by Prompts/31). Every value comes from the active
/// <see cref="UiThemeDef"/>; the default is Sky Poster, whose values are
/// exactly what this class held as constants before themes existed.
///
/// <see cref="Ink"/> is the colour of TEXT on a paper face - most callers use
/// it that way. Outlines and hard shadows read <see cref="Line"/>. Text that
/// sits straight on the arena reads <see cref="OnBackdrop"/>.
///
/// Switching a theme (<see cref="Apply"/>) raises <see cref="Changed"/>; the
/// screens tear down and rebuild, so nothing has to know how to recolour.
/// Units are MenuCanvas units - minimum text 12, minimum touch 48.
/// </summary>
public static class UiTheme
{
    private static UiThemeDef _active;

    public static UiThemeDef Active => _active ??= UiThemeCatalog.Default;

    /// <summary>Raised after the active theme changed.</summary>
    public static event Action<UiThemeDef> Changed;

    public static void Apply(UiThemeDef def)
    {
        if (def == null || def == Active) return;
        _active = def;
        // one failing screen must not leave the others half rebuilt
        if (Changed == null) return;
        foreach (var h in Changed.GetInvocationList())
        {
            try { ((Action<UiThemeDef>)h)(def); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>Build something under another theme (theme picker previews).</summary>
    public static void With(UiThemeDef def, Action build)
    {
        var prev = Active;
        _active = def ?? prev;
        try { build(); }
        finally { _active = prev; }
    }

    // --- colours ---------------------------------------------------------
    public static Color Ink => Active.Text;
    public static Color Line => Active.Line;
    public static Color Paper => Active.Paper;
    public static Color PaperDim => Active.PaperDim;
    public static Color TextDim => Active.TextDim;
    public static Color Frame => Active.Frame;
    public static Color FrameText => Active.FrameText;
    public static Color OnBackdrop => Active.OnBackdrop;
    public static Color BackdropShadow => Active.BackdropShadow;
    public static Color Weapon => Active.Weapon;
    public static Color Gadget => Active.Gadget;
    public static Color Special => Active.Special;
    public static Color Highlight => Active.Highlight;
    public static Color Confirm => Active.Confirm;
    public static Color OnConfirm => Active.OnConfirm;
    public static Color Danger => Active.Danger;
    public static Color Pick => Active.Pick;
    public static Color OnPick => Active.OnPick;
    public static Color TabOn => Active.TabOn;
    public static Color TabOnText => Active.TabOnText;
    public static Color BandText => Active.BandText;
    public static Color BarFill => Active.BarFill;
    public static Color Glass => Active.Glass;
    public static Color GlassLight => Active.GlassLight;
    public static Color SkyTop => Active.VeilTop;
    public static Color SkyBottom => Active.VeilBottom;

    /// <summary>Band colour of a card: the theme's band, or the category colour.</summary>
    public static Color BandFor(Color category) => Active.Band.a > 0f ? Active.Band : category;

    /// <summary>Second stop of a vertical gradient for a face colour, if the
    /// theme paints that colour as a gradient (alpha 0 = flat).</summary>
    public static Color GradientFor(Color face)
    {
        var t = Active;
        if (Same(face, t.Paper)) return t.PaperB;
        if (Same(face, t.Frame)) return t.FrameB;
        if (Same(face, t.Confirm)) return t.ConfirmB;
        if (Same(face, t.TabOn)) return t.TabOnB;
        if (t.Band.a > 0f && Same(face, t.Band)) return t.BandB;
        return new Color(0f, 0f, 0f, 0f);
    }

    private static bool Same(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f &&
        Mathf.Abs(a.b - b.b) < 0.002f && Mathf.Abs(a.a - b.a) < 0.002f;

    // --- sizes (Sky Poster units; Ui.Text scales by the font role) --------
    public const int SizeTitle = 30;
    public const int SizeCardTitle = 25;
    public const int SizeTab = 17;
    public const int SizeLabel = 13;
    public const int SizeBody = 13;
    public const int SizeSmall = 12;
    public const int MinText = 12;
    public const float MinTouch = 48f;

    /// <summary>Outline width of the active theme (0 = none).</summary>
    public static float Outline => Active.Outline;

    // Sky Poster radii; Ui scales them by Active.RadiusScale.
    public const float RadiusCard = 14f;
    public const float RadiusButton = 12f;
    public const float RadiusCta = 16f;

    // --- fonts -----------------------------------------------------------
    private static readonly Dictionary<string, Font> _fonts = new();

    /// <summary>Titles, card names, CTA.</summary>
    public static Font Display => Load(Active.FontDisplay);

    /// <summary>Tabs, labels, caps.</summary>
    public static Font Label => Load(Active.FontLabel);

    /// <summary>Running text.</summary>
    public static Font Body => Load(Active.FontBody);

    /// <summary>Emphasised text.</summary>
    public static Font BodyStrong => Load(Active.FontStrong);

    /// <summary>Size multiplier of a font in the active theme (fonts run at
    /// very different widths: Press Start 2P vs Barlow Condensed).</summary>
    public static float ScaleFor(Font f)
    {
        if (f == null) return 1f;
        var t = Active;
        // A theme may reuse one face for several roles: body wins over label,
        // so running text never inherits label tracking.
        if (f == Display) return t.ScaleDisplay;
        if (f == Body || f == BodyStrong) return t.ScaleBody;
        if (f == Label) return t.ScaleLabel;
        return 1f;
    }

    /// <summary>Letter spacing in em for a font in the active theme.</summary>
    public static float TrackFor(Font f)
    {
        if (f == null) return 0f;
        if (f == Display) return Active.TrackDisplay;
        if (f == Body || f == BodyStrong) return 0f;
        if (f == Label) return Active.TrackLabel;
        return 0f;
    }

    private static Font Load(string name)
    {
        if (string.IsNullOrEmpty(name)) return UiKit.PixelFont;
        if (_fonts.TryGetValue(name, out var f) && f != null) return Loc.Cover(f);
        f = Resources.Load<Font>("Fonts/" + name);
        if (f == null) f = Resources.Load<Font>("Fonts/BarlowCondensed-Bold");
        if (f == null) f = UiKit.PixelFont;
        _fonts[name] = f;
        return Loc.Cover(f);
    }

    public static Color Hex(int rgb, float a = 1f) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
}
