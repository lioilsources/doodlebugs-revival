using System;
using UnityEngine;

/// <summary>How a phone flies the plane.</summary>
public enum ControlScheme
{
    Joystick = 0,
    Gyro = 1,
}

/// <summary>
/// The touch control scheme (Prompts/26 D1): joystick by default, gyro as an
/// opt-in from the settings overlay. Persisted in <see cref="PlayerProfile"/>;
/// <see cref="SchemeKey"/> is the PlayerPrefs key it used before, which the
/// profile imports once. Desktop never reads this: keyboard and gamepad
/// are picked by InputManager from the devices present.
/// </summary>
public static class ControlSettings
{
    /// <summary>Legacy PlayerPrefs key (pre-profile builds); read only by the import.</summary>
    public const string SchemeKey = "settings.controlScheme";

    private static ControlScheme? _scheme;

    public static ControlScheme Scheme
    {
        get
        {
            if (_scheme == null)
            {
                int raw = PlayerProfile.Current.settings.controlScheme;
                _scheme = Enum.IsDefined(typeof(ControlScheme), raw)
                    ? (ControlScheme)raw
                    : ControlScheme.Joystick;
            }
            return _scheme.Value;
        }
    }

    /// <summary>True once the player has chosen; false = still the default.</summary>
    public static bool IsSaved => PlayerProfile.Current.settings.controlSchemeSaved;

    /// <summary>Raised after the scheme changed and was saved.</summary>
    public static event Action<ControlScheme> Changed;

    public static void Set(ControlScheme scheme, string source = "settings")
    {
        if (scheme == Scheme && IsSaved) return;

        _scheme = scheme;
        PlayerProfile.Current.settings.controlScheme = (int)scheme;
        PlayerProfile.Current.settings.controlSchemeSaved = true;
        PlayerProfile.MarkDirty();
        PlayerProfile.Save();
        Telemetry.Log("input_scheme_set", ("scheme", scheme), ("source", source));
        Changed?.Invoke(scheme);
    }

    /// <summary>Boot-time event: which scheme this session starts on.</summary>
    public static void LogBoot() =>
        Telemetry.Log("input_scheme_set", ("scheme", Scheme), ("source", IsSaved ? "saved" : "default"));
}
