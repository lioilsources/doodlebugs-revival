using UnityEngine;

/// <summary>
/// Direction A "Sky Poster" (Prompts/30 D1): the one place colours, type
/// sizes and fonts live. Pixel art stays with what flies and shoots; the
/// frame around it is bold, flat and light. Units are MenuCanvas units
/// (~0.94 pt on an iPhone mini) - minimum text 12, minimum touch target 48.
/// </summary>
public static class UiTheme
{
    // --- colours ---------------------------------------------------------
    public static readonly Color Ink = Hex(0x0D1730);
    public static readonly Color Paper = Hex(0xF2F6FB);
    public static readonly Color PaperDim = Hex(0xD5DEE9);
    public static readonly Color TextDim = Hex(0x33425E);
    public static readonly Color Weapon = Hex(0xFF8A1F);
    public static readonly Color Gadget = Hex(0x22C7DB);
    public static readonly Color Special = Hex(0xE0489A);
    public static readonly Color Highlight = Hex(0xFFD23F);
    public static readonly Color Confirm = Hex(0x3DDC68);
    public static readonly Color Danger = Hex(0xFF5A4A);
    public static readonly Color Glass = new Color(13f / 255f, 23f / 255f, 48f / 255f, 0.74f);
    public static readonly Color GlassLight = new Color(1f, 1f, 1f, 0.16f);
    public static readonly Color SkyTop = new Color(13f / 255f, 23f / 255f, 48f / 255f, 0.30f);
    public static readonly Color SkyBottom = new Color(13f / 255f, 23f / 255f, 48f / 255f, 0.92f);

    // --- sizes -----------------------------------------------------------
    public const int SizeTitle = 30;
    public const int SizeCardTitle = 25;
    public const int SizeTab = 17;
    public const int SizeLabel = 13;
    public const int SizeBody = 13;
    public const int SizeSmall = 12;
    public const int MinText = 12;
    public const float MinTouch = 48f;

    public const float Outline = 3f;
    public const float RadiusCard = 14f;
    public const float RadiusButton = 12f;
    public const float RadiusCta = 16f;

    // --- fonts -----------------------------------------------------------
    private static Font _display, _label, _body, _bodyStrong;

    /// <summary>Barlow Condensed ExtraBold Italic - titles, card names, CTA.</summary>
    public static Font Display => _display != null ? _display : _display = Load("BarlowCondensed-ExtraBoldItalic");

    /// <summary>Barlow Condensed Bold - tabs, labels, caps.</summary>
    public static Font Label => _label != null ? _label : _label = Load("BarlowCondensed-Bold");

    /// <summary>Barlow Medium - running text.</summary>
    public static Font Body => _body != null ? _body : _body = Load("Barlow-Medium");

    /// <summary>Barlow SemiBold - emphasised text.</summary>
    public static Font BodyStrong => _bodyStrong != null ? _bodyStrong : _bodyStrong = Load("Barlow-SemiBold");

    private static Font Load(string name)
    {
        var f = Resources.Load<Font>("Fonts/" + name);
        return f != null ? f : UiKit.PixelFont;
    }

    public static Color Hex(int rgb, float a = 1f) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
}
