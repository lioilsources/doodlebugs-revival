using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// One set of flight numbers (Prompts/28 §3). Every plane - human, couch
/// pilot and bot - flies by the same preset; movement is owner-authoritative,
/// so the numbers must match on every device of a LAN match.
/// </summary>
public class FlightPreset
{
    public string Name;

    public float MinSpeed;        // engine cuts below this
    public float CornerSpeed;     // tightest, fastest turn
    public float CruiseSpeed;     // spawn / respawn speed
    public float MaxSpeed;
    public float BoostSpeed;      // nitro cap (Prompts/29)

    public float TurnRate;        // deg/s at corner speed, full stick
    public float EnvAtMin;        // turn-rate envelope at MinSpeed (fraction of TurnRate)
    public float EnvAtMax;        // ... and at MaxSpeed
    public float TurnAttack;      // seconds, time constant toward a larger rate
    public float TurnRelease;     // seconds, toward a smaller rate

    public float SnapBoost;       // 1 = snap off
    public float SnapSeconds;
    public float SnapCooldown;

    public float TurnDrag;        // 1/s, speed bled above corner speed in a hard turn
    public float ThrottleRate;    // u/s^2
    public float ClimbDrag;
    public float DiveBoost;
    public float StallTurnRate;   // deg/s with the engine off

    // Engine-off fall and relight window - unchanged from the old Novice
    // profile, which is the only one that ever flew.
    public float MaxGravity = 0.125f;
    public float GravityIncreaseRate = 0.0875f;
    public float EngineRestartMin = -0.91f;
    public float EngineRestartMax = -0.41f;

    public FlightPreset Clone() => (FlightPreset)MemberwiseClone();
}

/// <summary>
/// Where the flight numbers come from. Three compiled presets; the Flight Lab
/// (hidden in SETTINGS) can override them, but only while the match sits in
/// WaitingForPlayers - a battle always flies the compiled preset (D7), so an
/// override can never make two LAN devices fly by different rules.
/// </summary>
public static class FlightTuning
{
    public static readonly FlightPreset ClassicPlus = new FlightPreset
    {
        Name = "CLASSIC+",
        MinSpeed = 2.5f, CornerSpeed = 5f, CruiseSpeed = 6f, MaxSpeed = 10f, BoostSpeed = 13f,
        TurnRate = 150f, EnvAtMin = 0.75f, EnvAtMax = 0.70f, TurnAttack = 0.05f, TurnRelease = 0.04f,
        SnapBoost = 1f, SnapSeconds = 0f, SnapCooldown = 0f,
        TurnDrag = 0.5f, ThrottleRate = 7f, ClimbDrag = 1f, DiveBoost = 3f, StallTurnRate = 320f,
    };

    public static readonly FlightPreset Arcade = new FlightPreset
    {
        Name = "ARCADE",
        MinSpeed = 3f, CornerSpeed = 6f, CruiseSpeed = 7.5f, MaxSpeed = 13f, BoostSpeed = 17f,
        TurnRate = 210f, EnvAtMin = 0.70f, EnvAtMax = 0.62f, TurnAttack = 0.07f, TurnRelease = 0.05f,
        SnapBoost = 1.5f, SnapSeconds = 0.18f, SnapCooldown = 0.8f,
        TurnDrag = 0.9f, ThrottleRate = 9f, ClimbDrag = 1.4f, DiveBoost = 3.5f, StallTurnRate = 320f,
    };

    public static readonly FlightPreset Ace = new FlightPreset
    {
        Name = "ACE",
        MinSpeed = 3.5f, CornerSpeed = 7f, CruiseSpeed = 9f, MaxSpeed = 16f, BoostSpeed = 20f,
        TurnRate = 260f, EnvAtMin = 0.65f, EnvAtMax = 0.55f, TurnAttack = 0.08f, TurnRelease = 0.05f,
        SnapBoost = 1.6f, SnapSeconds = 0.20f, SnapCooldown = 0.6f,
        TurnDrag = 1.1f, ThrottleRate = 11f, ClimbDrag = 1.6f, DiveBoost = 4f, StallTurnRate = 320f,
    };

    public static readonly FlightPreset[] Presets = { ClassicPlus, Arcade, Ace };

    /// <summary>The compiled default (D1).</summary>
    public static FlightPreset Compiled => Arcade;

    // --- stick feel (§4) - input only, so device-local and allowed in battle
    public static float PointDeadZone = 0.22f;
    public static float PointGainDeg = 22f;
    public static float RateTurnExpo = 1.8f;
    public static float RateTurnDeadZone = 0.10f;

    public static readonly string[] StickFields = { "PointDeadZone", "PointGainDeg", "RateTurnExpo", "RateTurnDeadZone" };

    private static readonly float[] StickDefaults = { 0.22f, 22f, 1.8f, 0.10f };

    public static float GetStick(int i) => i switch { 0 => PointDeadZone, 1 => PointGainDeg, 2 => RateTurnExpo, _ => RateTurnDeadZone };

    public static void SetStick(int i, float v, bool save = true)
    {
        switch (i)
        {
            case 0: PointDeadZone = Mathf.Clamp(v, 0.05f, 0.6f); break;
            case 1: PointGainDeg = Mathf.Clamp(v, 5f, 60f); break;
            case 2: RateTurnExpo = Mathf.Clamp(v, 1f, 3f); break;
            default: RateTurnDeadZone = Mathf.Clamp(v, 0f, 0.4f); break;
        }
        if (save) { PlayerPrefs.SetFloat("flightlab.stick." + i, GetStick(i)); PlayerPrefs.Save(); }
    }

    public static void ResetStick()
    {
        for (int i = 0; i < StickDefaults.Length; i++)
        {
            SetStick(i, StickDefaults[i], false);
            PlayerPrefs.DeleteKey("flightlab.stick." + i);
        }
    }

    /// <summary>Boot: load saved stick feel and log the active preset's check table.</summary>
    public static void Boot()
    {
        for (int i = 0; i < StickDefaults.Length; i++)
            if (PlayerPrefs.HasKey("flightlab.stick." + i)) SetStick(i, PlayerPrefs.GetFloat("flightlab.stick." + i), false);
        Debug.Log(FlightModel.Describe(Compiled));
        Telemetry.Log("flight_preset", ("name", Compiled.Name), ("lab_override", Lab != null));
    }

    // --- Flight Lab --------------------------------------------------------

    private const string LabKey = "flightlab.json";
    private static FlightPreset _lab;
    private static bool _labLoaded;

    /// <summary>Lab overrides (null = none). Persisted per device.</summary>
    public static FlightPreset Lab
    {
        get
        {
            if (!_labLoaded)
            {
                _labLoaded = true;
                _lab = Parse(PlayerPrefs.GetString(LabKey, ""));
            }
            return _lab;
        }
    }

    public static void SetLab(FlightPreset preset)
    {
        _lab = preset;
        _labLoaded = true;
        if (preset == null) PlayerPrefs.DeleteKey(LabKey);
        else PlayerPrefs.SetString(LabKey, ToJson(preset));
        PlayerPrefs.Save();
    }

    /// <summary>True while lab numbers may apply: lobby warm-up only.</summary>
    public static bool LabAllowed
    {
        get
        {
            var mm = MatchManager.Instance;
            return mm == null || mm.Phase == MatchManager.GamePhase.WaitingForPlayers;
        }
    }

    public static bool LabActive => Lab != null && LabAllowed;

    /// <summary>The preset every movement step reads.</summary>
    public static FlightPreset Active => LabActive ? Lab : Compiled;

    // --- serialisation (tiny, no JsonUtility on a non-serializable class) ---

    private static readonly string[] Fields =
    {
        "MinSpeed", "CornerSpeed", "CruiseSpeed", "MaxSpeed", "BoostSpeed", "TurnRate",
        "EnvAtMin", "EnvAtMax", "TurnAttack", "TurnRelease", "SnapBoost", "SnapSeconds",
        "SnapCooldown", "TurnDrag", "ThrottleRate", "ClimbDrag", "DiveBoost", "StallTurnRate",
    };

    public static IReadOnlyList<string> FieldNames => Fields;

    public static float Get(FlightPreset p, string field) =>
        (float)typeof(FlightPreset).GetField(field).GetValue(p);

    public static void Set(FlightPreset p, string field, float value) =>
        typeof(FlightPreset).GetField(field).SetValue(p, value);

    public static string ToJson(FlightPreset p)
    {
        var sb = new StringBuilder("{\"Name\":\"").Append(p.Name).Append('"');
        foreach (var f in Fields)
        {
            sb.Append(",\"").Append(f).Append("\":")
              .Append(Get(p, f).ToString("0.###", CultureInfo.InvariantCulture));
        }
        return sb.Append('}').ToString();
    }

    private static FlightPreset Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var p = Compiled.Clone();
        p.Name = "LAB";
        foreach (var f in Fields)
        {
            int i = json.IndexOf("\"" + f + "\":", System.StringComparison.Ordinal);
            if (i < 0) continue;
            i += f.Length + 3;
            int j = i;
            while (j < json.Length && "-0123456789.eE".IndexOf(json[j]) >= 0) j++;
            if (float.TryParse(json.Substring(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                Set(p, f, v);
            }
        }
        return p;
    }
}
