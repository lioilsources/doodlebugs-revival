using UnityEngine;

/// <summary>
/// The eleven UI themes (Prompts/31 §1). Numbers come from
/// tools/ui_themes/tokens.py, where they passed the WCAG contrast gate;
/// the look is drawn in Prompts/31-ui-themes-mockup.html.
/// Ids are append-only and equal the array index.
/// </summary>
public static class UiThemeCatalog
{
    // own helper: the catalog must not touch UiTheme while its statics initialise
    private static Color H(int rgb, float a = 1f) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
    private static readonly Color None = new Color(0f, 0f, 0f, 0f);

    public static readonly UiThemeDef[] All =
    {
        // 0 ------------------------------------------------------------------ default
        new UiThemeDef
        {
            Id = 0, Key = "skyposter", Name = "SKY POSTER",
            Line = H(0x0D1730), Paper = H(0xF2F6FB), PaperB = None, PaperDim = H(0xD5DEE9),
            Text = H(0x0D1730), TextDim = H(0x33425E),
            Frame = H(0xF2F6FB), FrameB = None, FrameText = H(0x0D1730),
            OnBackdrop = Color.white, BackdropShadow = H(0x0D1730),
            Weapon = H(0xFF8A1F), Gadget = H(0x22C7DB), Special = H(0xE0489A), Highlight = H(0xFFD23F), Danger = H(0xFF5A4A),
            Band = None, BandB = None, BandText = H(0x0D1730),
            Confirm = H(0x3DDC68), ConfirmB = None, OnConfirm = H(0x0D1730),
            TabOn = H(0xF2F6FB), TabOnB = None, TabOnText = H(0x0D1730),
            Glass = H(0x0D1730, .74f), GlassLight = H(0xFFFFFF, .16f),
            VeilTop = H(0x0D1730, .30f), VeilBottom = H(0x0D1730, .92f),
            Pick = H(0xFFD23F), OnPick = H(0x0D1730), BarFill = H(0x0D1730),
            FontDisplay = "BarlowCondensed-ExtraBoldItalic", FontLabel = "BarlowCondensed-Bold",
            FontBody = "Barlow-Medium", FontStrong = "Barlow-SemiBold",
            ShadowColor = H(0x0D1730),
        },
        // 1 ------------------------------------------------------------------ 1970s
        new UiThemeDef
        {
            Id = 1, Key = "phosphor", Name = "PHOSPHOR",
            Line = H(0x63FF9A), Paper = H(0x000A04, .78f), PaperB = None, PaperDim = H(0x0F2A19),
            Text = H(0x63FF9A), TextDim = H(0x4BC074),
            Frame = H(0x000A04, .78f), FrameB = None, FrameText = H(0x63FF9A),
            OnBackdrop = H(0x63FF9A), BackdropShadow = H(0x63FF9A, .35f),
            Weapon = H(0x63FF9A), Gadget = H(0x63FF9A), Special = H(0xFFC857), Highlight = H(0xFFC857), Danger = H(0xFF6B5A),
            Band = H(0x000A04, .9f), BandB = None, BandText = H(0x63FF9A),
            Confirm = H(0xFFC857), ConfirmB = None, OnConfirm = H(0x031006),
            TabOn = H(0x63FF9A), TabOnB = None, TabOnText = H(0x031006),
            Glass = H(0x000A04, .85f), GlassLight = H(0x000A04, .6f),
            VeilTop = H(0x000803, .50f), VeilBottom = H(0x000803, .84f),
            Pick = H(0xFFC857), OnPick = H(0x031006), BarFill = H(0x63FF9A), ArtTint = new Color(.8f, 1f, .85f),
            FontDisplay = "MajorMonoDisplay-Regular", FontLabel = "IBMPlexMono-Regular",
            FontBody = "IBMPlexMono-Regular", FontStrong = "IBMPlexMono-Medium",
            ScaleDisplay = .8f, ScaleLabel = .82f, TrackLabel = .03f,
            RadiusScale = 0f, Outline = 2f, Shadow = UiShadowKind.Glow, ShadowColor = H(0x63FF9A, .45f),
            SelectLift = 4f, SelectTilt = 0f, SelectBob = 0f,
            Bar = UiBarStyle.Line, Badge = UiBadgeStyle.Ring,
            Filter = UiBackdropFilter.Mono, FilterTint = H(0x63FF9A), Overlay = UiOverlay.Scanlines,
        },
        // 2 ------------------------------------------------------------------ 1980s
        new UiThemeDef
        {
            Id = 2, Key = "coinop", Name = "COIN-OP",
            Line = H(0x2DE2E6), Paper = H(0x07061A), PaperB = None, PaperDim = H(0x1D1B45),
            Text = Color.white, TextDim = H(0xB9B6E8),
            Frame = H(0x07061A), FrameB = None, FrameText = Color.white,
            OnBackdrop = Color.white, BackdropShadow = Color.black,
            Weapon = H(0xFF9A2E), Gadget = H(0x2DE2E6), Special = H(0xFF3EA5), Highlight = H(0xFFE14D), Danger = H(0xFF3EA5),
            Band = H(0x2DE2E6), BandB = None, BandText = H(0x07061A),
            Confirm = H(0xFFE14D), ConfirmB = None, OnConfirm = H(0x07061A),
            TabOn = H(0xFFE14D), TabOnB = None, TabOnText = H(0x07061A),
            Glass = H(0x07061A), GlassLight = H(0x07061A, .55f),
            VeilTop = H(0x07061A, .58f), VeilBottom = H(0x07061A, .90f),
            Pick = H(0xFFE14D), OnPick = H(0x07061A), BarFill = H(0xFFE14D),
            FontDisplay = "PressStart2P", FontLabel = "PixelifySans-Bold",
            FontBody = "PixelifySans-Medium", FontStrong = "PixelifySans-Bold",
            ScaleDisplay = .58f, ScaleLabel = .94f, ScaleBody = 1.08f,
            RadiusScale = 0f, Outline = 3f, Shadow = UiShadowKind.Hard, ShadowSkew = 1f, ShadowColor = Color.black,
            SelectLift = 7f, SelectTilt = 0f, SelectBob = 0f,
            Bar = UiBarStyle.Blocks, Badge = UiBadgeStyle.Square,
            Filter = UiBackdropFilter.Posterize, FilterLevels = 5, Overlay = UiOverlay.Scanlines,
        },
        // 3 ------------------------------------------------------------------ 1990s
        new UiThemeDef
        {
            Id = 3, Key = "chrome", Name = "CHROME",
            Line = H(0x0B0E13), Paper = H(0x121820), PaperB = None, PaperDim = H(0x26303B),
            Text = H(0xE9F0F8), TextDim = H(0x9FB0C4),
            Frame = H(0xE2E7EE), FrameB = H(0x8B95A5), FrameText = H(0x11161D),
            OnBackdrop = Color.white, BackdropShadow = Color.black,
            Weapon = H(0xFF7A1A), Gadget = H(0x19C3D6), Special = H(0xE0489A), Highlight = H(0xFFD23F), Danger = H(0xFF4B3A),
            Band = H(0xE2E7EE, 0f), BandB = None, BandText = H(0x11161D),
            Confirm = H(0x8DFF7C), ConfirmB = H(0x178A2A), OnConfirm = H(0x06210B),
            TabOn = H(0xE2E7EE), TabOnB = H(0x8B95A5), TabOnText = H(0x11161D),
            Glass = H(0x0C1410), GlassLight = H(0x333A45, .9f),
            VeilTop = H(0x08080C, .30f), VeilBottom = H(0x08080C, .88f),
            Pick = H(0xFFD23F), OnPick = H(0x11161D), BarFill = H(0x39D353),
            FontDisplay = "RussoOne-Regular", FontLabel = "SairaCondensed-Bold",
            FontBody = "SairaCondensed-SemiBold", FontStrong = "SairaCondensed-Bold",
            ScaleDisplay = .76f, ScaleBody = 1.08f, TrackDisplay = .04f, TrackLabel = .06f,
            RadiusScale = .43f, Outline = 2f, Shadow = UiShadowKind.Soft, ShadowColor = H(0x000000, .6f),
            FaceFx = UiFaceFx.Bevel, CardInset = 5f, SelectLift = 8f, SelectTilt = 0f, SelectBob = 2f,
            Bar = UiBarStyle.Led, Badge = UiBadgeStyle.Plate, Overlay = UiOverlay.Vignette,
        },
        // 4 ------------------------------------------------------------------ 2000s
        new UiThemeDef
        {
            Id = 4, Key = "aqua", Name = "AQUA",
            Line = Color.white, Paper = Color.white, PaperB = H(0xE0EFFF), PaperDim = H(0xD8E3F0),
            Text = H(0x12325C), TextDim = H(0x44628C),
            Frame = Color.white, FrameB = H(0xE0EFFF), FrameText = H(0x12325C),
            OnBackdrop = Color.white, BackdropShadow = H(0x0A2C6E),
            Weapon = H(0xFF8A1F), Gadget = H(0x18B6E8), Special = H(0xE0489A), Highlight = H(0xFFE45C), Danger = H(0xD61F1F),
            Band = H(0xFFCB7A), BandB = H(0xF27800), BandText = H(0x12325C),
            Confirm = H(0xBDF793), ConfirmB = H(0x35B80E), OnConfirm = H(0x0B3D02),
            TabOn = Color.white, TabOnB = H(0xC3DBFA), TabOnText = H(0x12325C),
            Glass = H(0x0E2A52, .80f), GlassLight = H(0xFFFFFF, .30f),
            VeilTop = H(0x4096EB, .30f), VeilBottom = H(0x0A2C6E, .86f),
            Pick = H(0xFFE45C), OnPick = H(0x12325C), BarFill = H(0x2F9BF0),
            FontDisplay = "PaytoneOne-Regular", FontLabel = "Rubik-SemiBold",
            FontBody = "Rubik-Medium", FontStrong = "Rubik-SemiBold",
            ScaleDisplay = .86f, ScaleLabel = .94f, TrackLabel = .02f,
            RadiusScale = 1.14f, Outline = 2f, Shadow = UiShadowKind.Soft, ShadowColor = H(0x082864, .45f),
            FaceFx = UiFaceFx.Gloss, SelectLift = 8f, SelectTilt = 0f, SelectBob = 3f,
            Bar = UiBarStyle.Tube, Badge = UiBadgeStyle.Orb, Overlay = UiOverlay.Bokeh,
            OverlayA = H(0xFFFFFF, .5f),
        },
        // 5 ------------------------------------------------------------------ 2010s
        new UiThemeDef
        {
            Id = 5, Key = "flat", Name = "FLAT",
            Line = H(0x00796B), Paper = Color.white, PaperB = None, PaperDim = H(0xE6EEED),
            Text = H(0x212121), TextDim = H(0x6B6B6B),
            Frame = Color.white, FrameB = None, FrameText = H(0x212121),
            OnBackdrop = Color.white, BackdropShadow = new Color(0f, 0f, 0f, 0f),
            Weapon = H(0xFF7043), Gadget = H(0x26C6DA), Special = H(0xEC407A), Highlight = H(0xFFC107), Danger = H(0xD84315),
            Band = None, BandB = None, BandText = H(0x212121),
            Confirm = H(0xFF5722), ConfirmB = None, OnConfirm = Color.white,
            TabOn = H(0x00796B), TabOnB = None, TabOnText = Color.white,
            Glass = H(0x00332C, .70f), GlassLight = H(0x000000, 0f),
            VeilTop = H(0x00796B, .74f), VeilBottom = H(0x004D40, .90f),
            Pick = H(0xFFC107), OnPick = H(0x212121), BarFill = H(0x00796B),
            FontDisplay = "Montserrat-ExtraBold", FontLabel = "Roboto-Medium",
            FontBody = "Roboto-Regular", FontStrong = "Roboto-Medium",
            ScaleDisplay = .74f, ScaleLabel = .84f, TrackDisplay = .05f, TrackLabel = .06f,
            RadiusScale = .21f, Outline = 0f, Shadow = UiShadowKind.Soft, ShadowColor = H(0x000000, .28f),
            SelectLift = 7f, SelectTilt = 0f, SelectBob = 0f,
            Bar = UiBarStyle.Hairline, Badge = UiBadgeStyle.Disc,
            Filter = UiBackdropFilter.Grayscale, Overlay = UiOverlay.Facets,
        },
        // 6 ------------------------------------------------------------------ 2020s
        new UiThemeDef
        {
            Id = 6, Key = "clay", Name = "CLAY",
            Line = H(0xA58BFF), Paper = H(0xFFF8F1), PaperB = None, PaperDim = H(0xEADFF5),
            Text = H(0x3B2A5C), TextDim = H(0x77679A),
            Frame = H(0xFFF8F1), FrameB = None, FrameText = H(0x3B2A5C),
            OnBackdrop = H(0x3B2A5C), BackdropShadow = new Color(0f, 0f, 0f, 0f),
            Weapon = H(0xFFB38A), Gadget = H(0x8FE3C0), Special = H(0xF7A6D0), Highlight = H(0xB5380F), Danger = H(0xB5380F),
            Band = None, BandB = None, BandText = H(0x3B2A5C),
            Confirm = H(0xA6F0CB), ConfirmB = H(0x6FDCA6), OnConfirm = H(0x1F4A38),
            TabOn = H(0x7C5CE0), TabOnB = None, TabOnText = Color.white,
            Glass = H(0xFFFFFF, .86f), GlassLight = H(0xFFFFFF, .46f),
            VeilTop = H(0xFFF0E6, .56f), VeilBottom = H(0xE8D7FF, .80f),
            Pick = H(0xFFE08A), OnPick = H(0x3B2A5C), BarFill = H(0x7C5CE0),
            FontDisplay = "Baloo2-ExtraBold", FontLabel = "Baloo2-Bold",
            FontBody = "Baloo2-Medium", FontStrong = "Baloo2-Bold",
            ScaleDisplay = .96f, ScaleBody = 1.08f, TrackLabel = .02f,
            RadiusScale = 1.86f, Outline = 0f, Shadow = UiShadowKind.Soft, ShadowColor = H(0x6E46A0, .30f),
            FaceFx = UiFaceFx.Clay, SelectLift = 8f, SelectTilt = 0f, SelectBob = 3f, SelectScale = 1.02f,
            Bar = UiBarStyle.Pill, Badge = UiBadgeStyle.Squircle,
            Filter = UiBackdropFilter.Blur, Overlay = UiOverlay.Blobs,
            OverlayA = H(0xA58BFF, .55f), OverlayB = H(0xFFB38A, .50f), OverlayC = H(0x8FE3C0, .55f),
        },
        // 7 ------------------------------------------------------------------ 2030s
        new UiThemeDef
        {
            Id = 7, Key = "lightfield", Name = "LIGHTFIELD",
            Line = H(0xFFFFFF, .26f), Paper = H(0x1C1E3C, .62f), PaperB = None, PaperDim = H(0xFFFFFF, .08f),
            Text = H(0xF4F6FF), TextDim = H(0xB9BCD6),
            Frame = H(0x1C1E3C, .62f), FrameB = None, FrameText = H(0xF4F6FF),
            OnBackdrop = H(0xF4F6FF), BackdropShadow = new Color(0f, 0f, 0f, 0f),
            Weapon = H(0xFFB57A), Gadget = H(0x7CF7FF), Special = H(0xFF9DE1), Highlight = H(0xFFE29C), Danger = H(0xFF8A9A),
            Band = H(0xFFFFFF, .05f), BandB = None, BandText = H(0xB9BCD6),
            Confirm = H(0x7CF7FF), ConfirmB = H(0xFF9DE1), OnConfirm = H(0x12142C),
            TabOn = H(0xEDEEFB), TabOnB = None, TabOnText = H(0x12142C),
            Glass = H(0x14182E, .55f), GlassLight = H(0xFFFFFF, .07f),
            VeilTop = H(0x0A0822, .50f), VeilBottom = H(0x0A0822, .80f),
            Pick = H(0xFFE29C), OnPick = H(0x12142C), BarFill = H(0xB69CFF),
            FontDisplay = "Unbounded-Medium", FontLabel = "Manrope-Bold",
            FontBody = "Manrope-Medium", FontStrong = "Manrope-SemiBold",
            ScaleDisplay = .62f, ScaleLabel = .84f, TrackDisplay = .12f, TrackLabel = .08f,
            RadiusScale = 1.57f, Outline = 1f, Shadow = UiShadowKind.Soft, ShadowColor = H(0x000000, .45f),
            SelectLift = 6f, SelectTilt = 0f, SelectBob = 2f, SelectScale = 1.03f,
            Bar = UiBarStyle.Beam, Badge = UiBadgeStyle.Halo,
            Filter = UiBackdropFilter.Blur, Overlay = UiOverlay.Aurora,
            OverlayA = H(0x7CF7FF, .22f), OverlayB = H(0xFF9DE1, .20f), OverlayC = H(0xB69CFF, .26f),
        },
        // 8 ------------------------------------------------------------------ space
        new UiThemeDef
        {
            Id = 8, Key = "cockpit", Name = "COCKPIT",
            Line = H(0x3FE0FF), Paper = H(0x07182B), PaperB = None, PaperDim = H(0x0D2A40),
            Text = H(0xE6F7FF), TextDim = H(0x8FB7CC),
            Frame = H(0x07182B), FrameB = None, FrameText = H(0xE6F7FF),
            OnBackdrop = H(0xE6F7FF), BackdropShadow = H(0x04101E),
            Weapon = H(0xFF9B2F), Gadget = H(0x7CFFCB), Special = H(0xFF6FB1), Highlight = H(0xFFD23F), Danger = H(0xFF5A4A),
            Band = None, BandB = None, BandText = H(0x0A0F18),
            Confirm = H(0xFF9B2F), ConfirmB = None, OnConfirm = H(0x0A0F18),
            TabOn = H(0x3FE0FF), TabOnB = None, TabOnText = H(0x06121F),
            Glass = H(0x061224, .78f), GlassLight = H(0x3FE0FF, .14f),
            VeilTop = H(0x040A18, .25f), VeilBottom = H(0x040A18, .92f),
            Pick = H(0xFFD23F), OnPick = H(0x0A0F18), BarFill = H(0x3FE0FF),
            FontDisplay = "Oxanium-Bold", FontLabel = "Rajdhani-Bold",
            FontBody = "Rajdhani-SemiBold", FontStrong = "Rajdhani-Bold",
            ScaleDisplay = .84f, ScaleBody = 1.08f, TrackDisplay = .1f, TrackLabel = .06f,
            Shape = UiPanelShape.Chamfer, RadiusScale = .86f, Outline = 2f,
            Shadow = UiShadowKind.Glow, ShadowColor = H(0x3FE0FF, .40f),
            SelectLift = 8f, SelectTilt = 0f, SelectBob = 2f,
            Bar = UiBarStyle.Slant, Badge = UiBadgeStyle.Hex, Overlay = UiOverlay.Grid,
            OverlayA = H(0x3FE0FF, .07f),
        },
        // 9 ------------------------------------------------------------------ jungle
        new UiThemeDef
        {
            Id = 9, Key = "canopy", Name = "CANOPY",
            Line = H(0x3A2010), Paper = H(0xF4E4BC), PaperB = None, PaperDim = H(0xD9C392),
            Text = H(0x3A2010), TextDim = H(0x6A4827),
            Frame = H(0xCF9150), FrameB = H(0xAD6C31), FrameText = H(0x3A2010),
            OnBackdrop = H(0xFFE9A8), BackdropShadow = H(0x3A2010),
            Weapon = H(0xF08A2B), Gadget = H(0x3CC6B0), Special = H(0xE5482E), Highlight = H(0xFFE27A), Danger = H(0xE5482E),
            Band = H(0x73492A), BandB = H(0x5B3A1E), BandText = H(0xFFF3D0),
            Confirm = H(0xFFE98F), ConfirmB = H(0xD98E12), OnConfirm = H(0x3A2010),
            TabOn = H(0xFFE98F), TabOnB = H(0xF2A81D), TabOnText = H(0x3A2010),
            Glass = H(0x1E1408, .68f), GlassLight = H(0x465446, .9f),
            VeilTop = H(0x0A2818, .10f), VeilBottom = H(0x081A10, .86f),
            Pick = H(0xFFE27A), OnPick = H(0x3A2010), BarFill = H(0x4FB34A),
            FontDisplay = "TitanOne-Regular", FontLabel = "Nunito-ExtraBold",
            FontBody = "Nunito-Bold", FontStrong = "Nunito-ExtraBold",
            ScaleDisplay = .82f, ScaleLabel = .9f, TrackLabel = .02f,
            RadiusScale = .86f, Outline = 3f, Shadow = UiShadowKind.Hard, ShadowColor = H(0x24130A),
            FaceFx = UiFaceFx.Planks, CardInset = 6f,
            Bar = UiBarStyle.Bamboo, Badge = UiBadgeStyle.Disc, Overlay = UiOverlay.Vines,
            OverlayA = H(0x3F9A45),
        },
        // 10 ----------------------------------------------------------------- utopia
        new UiThemeDef
        {
            Id = 10, Key = "solaria", Name = "SOLARIA",
            Line = H(0xC39A3B), Paper = Color.white, PaperB = H(0xF1F7F4), PaperDim = H(0xE3EFEB),
            Text = H(0x0F3B3A), TextDim = H(0x4A6B66),
            Frame = Color.white, FrameB = H(0xF1F7F4), FrameText = H(0x0F3B3A),
            OnBackdrop = H(0x0F3B3A), BackdropShadow = new Color(0f, 0f, 0f, 0f),
            Weapon = H(0xF6A04D), Gadget = H(0x3CC6C0), Special = H(0xE98BB0), Highlight = H(0x8A5C0C), Danger = H(0xB5361F),
            Band = H(0xFFFFFF, 0f), BandB = None, BandText = H(0x0E7C73),
            Confirm = H(0x149488), ConfirmB = H(0x0E7C73), OnConfirm = Color.white,
            TabOn = Color.white, TabOnB = None, TabOnText = H(0x0F3B3A),
            Glass = H(0xFFFFFF, .74f), GlassLight = H(0xFFFFFF, .40f),
            VeilTop = H(0xFFFFFF, .55f), VeilBottom = H(0xE8F6F0, .90f),
            Pick = H(0xF7E7B5), OnPick = H(0x0F3B3A), BarFill = H(0x0E7C73),
            FontDisplay = "PoiretOne-Regular", FontLabel = "Jost-SemiBold",
            FontBody = "Jost-Regular", FontStrong = "Jost-Medium",
            ScaleDisplay = .78f, ScaleLabel = .88f, ScaleBody = 1.04f, TrackDisplay = .12f, TrackLabel = .1f,
            RadiusScale = 1.29f, Outline = 1f, Shadow = UiShadowKind.Soft, ShadowColor = H(0x145A50, .22f),
            ArchCards = true, SelectLift = 8f, SelectTilt = 0f, SelectBob = 2f,
            Bar = UiBarStyle.Leaves, Badge = UiBadgeStyle.Ringed,
        },
    };

    public static UiThemeDef Default => All[0];

    public static UiThemeDef Get(int id) => id >= 0 && id < All.Length ? All[id] : Default;

    public static UiThemeDef Get(string key)
    {
        foreach (var t in All) if (t.Key == key) return t;
        return Default;
    }

    /// <summary>Boot check: index == id, every font loads.</summary>
    public static void Validate()
    {
        for (int i = 0; i < All.Length; i++)
        {
            var t = All[i];
            if (t.Id != i) Debug.LogError($"[UiTheme] {t.Key}: id {t.Id} at index {i}");
            foreach (var f in new[] { t.FontDisplay, t.FontLabel, t.FontBody, t.FontStrong })
                if (Resources.Load<Font>("Fonts/" + f) == null)
                    Debug.LogWarning($"[UiTheme] {t.Key}: font {f} missing, falls back");
        }
    }
}
