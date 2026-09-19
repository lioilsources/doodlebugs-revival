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
/// opt-in from the settings overlay. Persisted in PlayerPrefs until
/// PlayerProfile (ROADMAP Phase 0) lands - the profile migration reads
/// <see cref="SchemeKey"/>. Desktop never reads this: keyboard and gamepad
/// are picked by InputManager from the devices present.
/// </summary>
public static class ControlSettings
{
    public const string SchemeKey = "settings.controlScheme";

    private static ControlScheme? _scheme;

    public static ControlScheme Scheme
    {
        get
        {
            if (_scheme == null)
            {
                int raw = PlayerPrefs.GetInt(SchemeKey, (int)ControlScheme.Joystick);
                _scheme = Enum.IsDefined(typeof(ControlScheme), raw)
                    ? (ControlScheme)raw
                    : ControlScheme.Joystick;
            }
            return _scheme.Value;
        }
    }

    /// <summary>True once the player has chosen; false = still the default.</summary>
    public static bool IsSaved => PlayerPrefs.HasKey(SchemeKey);

    /// <summary>Raised after the scheme changed and was saved.</summary>
    public static event Action<ControlScheme> Changed;

    public static void Set(ControlScheme scheme, string source = "settings")
    {
        if (scheme == Scheme && IsSaved) return;

        _scheme = scheme;
        PlayerPrefs.SetInt(SchemeKey, (int)scheme);
        PlayerPrefs.Save();
        Telemetry.Log("input_scheme_set", ("scheme", scheme), ("source", source));
        Changed?.Invoke(scheme);
    }

    /// <summary>Boot-time event: which scheme this session starts on.</summary>
    public static void LogBoot() =>
        Telemetry.Log("input_scheme_set", ("scheme", Scheme), ("source", IsSaved ? "saved" : "default"));
}
