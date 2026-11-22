using System;
using UnityEngine;

/// <summary>How a phone flies the plane.</summary>
public enum ControlScheme
{
    Joystick = 0,   // "RATE" in the UI: the stick sets the turn rate
    Gyro = 1,
    Pointer = 2,    // "POINT": the stick points where to fly (Prompts/28 D4)
}

/// <summary>
/// The touch control scheme (Prompts/26 D1): POINT by default for new installs
/// (Prompts/28 D4), RATE and gyro as an opt-in from the settings overlay.
/// Persisted in <see cref="PlayerProfile"/>; <see cref="SchemeKey"/> is the
/// PlayerPrefs key it used before, which the profile imports once. LEAN still
/// lives in PlayerPrefs (<see cref="LeanKey"/>). Desktop never reads this: keyboard and gamepad
/// are picked by InputManager from the devices present.
/// </summary>
public static class ControlSettings
{
    /// <summary>Legacy PlayerPrefs key (pre-profile builds); read only by the import.</summary>
    public const string SchemeKey = "settings.controlScheme";
    public const string LeanKey = "settings.lean";

    private static ControlScheme? _scheme;
    private static bool? _lean;

    public static ControlScheme Scheme
    {
        get
        {
            if (_scheme == null)
            {
                int raw = PlayerProfile.Current.settings.controlScheme;
                _scheme = Enum.IsDefined(typeof(ControlScheme), raw)
                    ? (ControlScheme)raw
                    : ControlScheme.Pointer;
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

    // --- LEAN (Prompts/32 D4/D5) ----------------------------------------------

    /// <summary>LEAN default: on wherever the device has a gyroscope (D5).
    /// The bonus only adds, so nobody loses by having it on.</summary>
    public static bool LeanDefault => UnityEngine.InputSystem.Gyroscope.current != null;

    /// <summary>LEAN on (POINT / RATE only - the Gyro scheme ignores it).</summary>
    public static bool Lean
    {
        get
        {
            if (_lean == null)
                _lean = PlayerPrefs.HasKey(LeanKey) ? PlayerPrefs.GetInt(LeanKey) != 0 : LeanDefault;
            return _lean.Value;
        }
    }

    public static bool IsLeanSaved => PlayerPrefs.HasKey(LeanKey);

    /// <summary>Raised after LEAN was switched and saved.</summary>
    public static event Action<bool> LeanChanged;

    public static void SetLean(bool on, string source = "settings")
    {
        if (on == Lean && IsLeanSaved) return;

        _lean = on;
        PlayerPrefs.SetInt(LeanKey, on ? 1 : 0);
        PlayerPrefs.Save();
        Telemetry.Log("input_lean_set", ("on", on), ("source", source));
        LeanChanged?.Invoke(on);
    }

    /// <summary>Boot-time events: which scheme and LEAN state this session starts on.</summary>
    public static void LogBoot()
    {
        Telemetry.Log("input_scheme_set", ("scheme", Scheme), ("source", IsSaved ? "saved" : "default"));
        Telemetry.Log("input_lean_set", ("on", Lean), ("source", IsLeanSaved ? "saved" : "default"));
    }
}
