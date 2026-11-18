using UnityEngine;

/// <summary>
/// The UI theme of this device (Prompts/31 D1: per device, never networked).
/// PlayerPrefs until PlayerProfile (ROADMAP Phase 0) absorbs it - the
/// migration reads <see cref="Key"/>. The value is the theme's string key,
/// not its id, so a reordered catalog can never swap someone's theme.
/// </summary>
public static class ThemeSettings
{
    public const string Key = "settings.uiTheme";

    public static bool IsSaved => PlayerPrefs.HasKey(Key);

    /// <summary>Boot: apply the saved theme before any screen is built.</summary>
    public static void Boot()
    {
        var def = UiThemeCatalog.Get(PlayerPrefs.GetString(Key, UiThemeCatalog.Default.Key));
        UiTheme.Apply(def);
        Telemetry.Log("ui_theme_set", ("theme", def.Key), ("source", IsSaved ? "saved" : "default"));
    }

    public static void Set(UiThemeDef def, string source = "settings")
    {
        if (def == null || (def == UiTheme.Active && IsSaved)) return;
        PlayerPrefs.SetString(Key, def.Key);
        PlayerPrefs.Save();
        Telemetry.Log("ui_theme_set", ("theme", def.Key), ("source", source));
        UiTheme.Apply(def);
    }
}
