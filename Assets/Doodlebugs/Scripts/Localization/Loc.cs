using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// UI string table. One catalogue per language in
/// <c>Resources/Localization/&lt;code&gt;.txt</c> (BCP 47 codes, English is the
/// source), lines of <c>key=value</c>, <c>#</c> comments, <c>\n</c> for a line
/// break, <c>{0}</c> placeholders for <see cref="string.Format(string, object[])"/>.
/// A line <c>@font=system</c> marks a language the Press Start 2P pixel font
/// cannot draw (CJK, Thai, Vietnamese): its UI uses the built-in dynamic font,
/// which falls back to the OS fonts, so no word is drawn in two typefaces.
///
/// The active language follows the device (iOS preferred localisation,
/// Android default locale, desktop <see cref="Application.systemLanguage"/>)
/// and can be forced with the <c>Language</c> PlayerPrefs key. A key missing
/// from a catalogue falls back to English, then to the caller's default.
///
/// ROADMAP §3 "one language per locale": a language whose script needs
/// shaping or right-to-left layout (Arabic, Hebrew, Urdu, the Indic scripts)
/// cannot be drawn correctly by legacy uGUI Text. Its catalogue ships, but
/// the game stays in English there until text moves to a shaping renderer -
/// see <see cref="NeedsShaping"/>.
/// </summary>
public static class Loc
{
    public const string SourceLanguage = "en";
    private const string PrefsKey = "Language";

    /// <summary>Every catalogue in Resources/Localization.</summary>
    public static readonly string[] Catalogues =
    {
        "cs", "en", "ar", "bn", "ca", "da", "de", "el", "es", "fi", "fr", "gu", "he", "hi", "hr",
        "hu", "id", "it", "ja", "kn", "ko", "ml", "mr", "ms", "nb", "nl", "or", "pa", "pl",
        "pt-BR", "pt-PT", "ro", "ru", "sk", "sl", "sv", "ta", "te", "th", "tr", "uk", "ur", "vi",
        "zh-Hans", "zh-Hant",
    };

    /// <summary>Translated, but not drawable by legacy uGUI Text (no bidi, no
    /// complex shaping). Remove a code here once text rendering handles it.</summary>
    public static readonly HashSet<string> NeedsShaping = new HashSet<string>
    {
        "ar", "he", "ur",                                            // right-to-left
        "bn", "gu", "hi", "kn", "ml", "mr", "or", "pa", "ta", "te",  // Indic shaping
    };

    /// <summary>Languages the game actually switches to (iOS
    /// CFBundleLocalizations is generated from this).</summary>
    public static IEnumerable<string> RuntimeLanguages
    {
        get
        {
            foreach (var code in Catalogues)
                if (!NeedsShaping.Contains(code)) yield return code;
        }
    }

    private static Dictionary<string, string> _active;
    private static Dictionary<string, string> _source;
    private static string _language;
    private static bool _pixelFont = true;
    private static CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>The language the UI is shown in.</summary>
    public static string Language { get { EnsureLoaded(); return _language; } }

    /// <summary>False when the active language needs glyphs the pixel font lacks.</summary>
    public static bool UsePixelFont { get { EnsureLoaded(); return _pixelFont; } }

    public static CultureInfo Culture { get { EnsureLoaded(); return _culture; } }

    /// <summary>The localised string for <paramref name="key"/>.</summary>
    public static string T(string key, string fallback = null)
    {
        EnsureLoaded();
        if (_active != null && _active.TryGetValue(key, out var value)) return value;
        if (_source != null && _source.TryGetValue(key, out value)) return value;
        return fallback ?? key;
    }

    /// <summary><see cref="T"/> with <c>{0}</c>-style arguments.</summary>
    public static string F(string key, params object[] args)
    {
        try
        {
            return string.Format(Culture, T(key), args);
        }
        catch (FormatException)
        {
            Debug.LogWarning($"[Loc] Bad placeholders in '{key}' ({Language})");
            return string.Format(CultureInfo.InvariantCulture, _source != null && _source.TryGetValue(key, out var en) ? en : key, args);
        }
    }

    /// <summary>Upper case with the active language's rules (Turkish dotted İ).</summary>
    public static string Upper(string text) => string.IsNullOrEmpty(text) ? text : text.ToUpper(Culture);

    /// <summary>The pixel font, or the built-in OS-backed font when the active
    /// language needs glyphs the pixel font does not have.</summary>
    public static Font PickFont(Font pixelFont)
    {
        if (UsePixelFont && pixelFont != null) return pixelFont;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    /// <summary>Switch language at runtime; already-built UI keeps its text
    /// until it is rebuilt.</summary>
    public static void SetLanguage(string code)
    {
        try { PlayerPrefs.SetString(PrefsKey, code ?? ""); } catch { /* prefs unavailable */ }
        _active = null;
        _language = null;
    }

    private static void EnsureLoaded()
    {
        if (_language != null) return;

        _source ??= Load(SourceLanguage, out _);

        string requested = null;
        try { requested = PlayerPrefs.GetString(PrefsKey, ""); } catch { /* prefs unavailable */ }
        if (string.IsNullOrEmpty(requested)) requested = DetectSystemLocale();

        string code = Resolve(requested);
        if (NeedsShaping.Contains(code))
        {
            Debug.Log($"[Loc] '{code}' needs text shaping legacy uGUI lacks - showing English");
            code = SourceLanguage;
        }

        bool pixel = true;
        _active = code == SourceLanguage ? _source : Load(code, out pixel);
        if (_active == null)
        {
            code = SourceLanguage;
            _active = _source;
            pixel = true;
        }

        _language = code;
        _pixelFont = pixel;
        try { _culture = CultureInfo.GetCultureInfo(code); }
        catch (CultureNotFoundException) { _culture = CultureInfo.InvariantCulture; }
    }

    private static Dictionary<string, string> Load(string code, out bool pixelFont)
    {
        pixelFont = true;
        var asset = Resources.Load<TextAsset>("Localization/" + code);
        if (asset == null) return null;

        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in asset.text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == '@')
            {
                if (line.Trim() == "@font=system") pixelFont = false;
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            table[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Replace("\\n", "\n");
        }
        return table;
    }

    /// <summary>Maps a platform locale tag onto one of <see cref="Catalogues"/>
    /// (English when there is none).</summary>
    public static string Resolve(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return SourceLanguage;
        var parts = tag.Replace('_', '-').ToLowerInvariant().Split('-');
        string lang = parts[0];
        bool Has(string part) => Array.IndexOf(parts, part, 1) > 0;

        switch (lang)
        {
            case "zh":
                return Has("hant") || Has("tw") || Has("hk") || Has("mo") ? "zh-Hant" : "zh-Hans";
            case "pt":
                // Bare "pt" and Brazil -> pt-BR; Portugal, Angola, Mozambique... -> pt-PT
                return parts.Length == 1 || Has("br") ? "pt-BR" : "pt-PT";
            case "no":
            case "nn":
                return "nb";
            case "iw":
                return "he";
            case "in":
                return "id";
        }

        foreach (var code in Catalogues)
            if (code == lang) return code;
        return SourceLanguage;
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern string _DoodlebugsPreferredLanguage();
#endif

    private static string DetectSystemLocale()
    {
#if UNITY_IOS && !UNITY_EDITOR
        try
        {
            var tag = _DoodlebugsPreferredLanguage();
            if (!string.IsNullOrEmpty(tag)) return tag;
        }
        catch (Exception e) { Debug.LogWarning($"[Loc] iOS locale lookup failed: {e.Message}"); }
#elif UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var locale = new AndroidJavaClass("java.util.Locale");
            using var current = locale.CallStatic<AndroidJavaObject>("getDefault");
            var tag = current.Call<string>("toLanguageTag");
            if (!string.IsNullOrEmpty(tag)) return tag;
        }
        catch (Exception e) { Debug.LogWarning($"[Loc] Android locale lookup failed: {e.Message}"); }
#endif
        return FromSystemLanguage(Application.systemLanguage);
    }

    private static string FromSystemLanguage(SystemLanguage language)
    {
        switch (language)
        {
            case SystemLanguage.Arabic: return "ar";
            case SystemLanguage.Catalan: return "ca";
            case SystemLanguage.ChineseSimplified: return "zh-Hans";
            case SystemLanguage.ChineseTraditional: return "zh-Hant";
            case SystemLanguage.Chinese: return "zh-Hans";
            case SystemLanguage.Czech: return "cs";
            case SystemLanguage.Danish: return "da";
            case SystemLanguage.Dutch: return "nl";
            case SystemLanguage.Finnish: return "fi";
            case SystemLanguage.French: return "fr";
            case SystemLanguage.German: return "de";
            case SystemLanguage.Greek: return "el";
            case SystemLanguage.Hebrew: return "he";
            case SystemLanguage.Hungarian: return "hu";
            case SystemLanguage.Indonesian: return "id";
            case SystemLanguage.Italian: return "it";
            case SystemLanguage.Japanese: return "ja";
            case SystemLanguage.Korean: return "ko";
            case SystemLanguage.Norwegian: return "nb";
            case SystemLanguage.Polish: return "pl";
            case SystemLanguage.Portuguese: return "pt-BR";
            case SystemLanguage.Romanian: return "ro";
            case SystemLanguage.Russian: return "ru";
            case SystemLanguage.SerboCroatian: return "hr";
            case SystemLanguage.Slovak: return "sk";
            case SystemLanguage.Slovenian: return "sl";
            case SystemLanguage.Spanish: return "es";
            case SystemLanguage.Swedish: return "sv";
            case SystemLanguage.Thai: return "th";
            case SystemLanguage.Turkish: return "tr";
            case SystemLanguage.Ukrainian: return "uk";
            case SystemLanguage.Vietnamese: return "vi";
            default: return SourceLanguage;
        }
    }
}
