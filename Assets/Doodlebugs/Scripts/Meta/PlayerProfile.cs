using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The local player's persistent state (ROADMAP Phase 0, Prompts/27 WP2): one
/// JSON file in <see cref="Application.persistentDataPath"/>, loaded once on
/// first access and saved atomically (write <c>profile.json.tmp</c>, then
/// replace). Never throws on a bad file: an unreadable profile is renamed to
/// <c>profile.json.bad</c> and a fresh one starts.
///
/// Per device, not per player: couch seats on one desktop share it. Save
/// points are round end (<see cref="ProfileRecorder"/>), settings changes and
/// the app going to the background.
/// </summary>
public static class PlayerProfile
{
    public const int SchemaVersion = 1;
    public const string FileName = "profile.json";

    /// <summary>Lifetime counters of the local planes.</summary>
    [Serializable]
    public class Stats
    {
        public int kills;
        public int deaths;
        public int rounds;
        public int roundsWon;
        public int soloRounds;
        public int lanRounds;
        public float playSeconds;
    }

    [Serializable]
    public class Settings
    {
        public int controlScheme;
        /// <summary>False until the player picked a scheme (the default is not a choice).</summary>
        public bool controlSchemeSaved;
        public bool analyticsOptOut;
    }

    [Serializable]
    public class Solo
    {
        /// <summary>Quick-play bot tier (Prompts/27 S4), 0 = Rookie.</summary>
        public int tier;
        /// <summary>Most recent solo rounds, oldest first: 1 = won, 0 = lost.</summary>
        public List<int> lastResults = new List<int>();
    }

    [Serializable]
    public class Data
    {
        public int schemaVersion = SchemaVersion;
        public Stats stats = new Stats();
        public Settings settings = new Settings();
        public Solo solo = new Solo();
        /// <summary>Unix seconds (UTC) of the first finished round; 0 = none yet.
        /// Gates analytics start and, later, permission prompts.</summary>
        public long firstSuccessAt;
    }

    public const int LastResultsKept = 10;

    private static Data _data;
    private static bool _dirty;

    /// <summary>The loaded profile (loads on first access).</summary>
    public static Data Current
    {
        get
        {
            if (_data == null) Load();
            return _data;
        }
    }

    public static bool HasFirstSuccess => Current.firstSuccessAt != 0;

    /// <summary>Raised after <see cref="Current"/> changed and was saved.</summary>
    public static event Action Saved;

    private static string PathOf(string suffix = "") =>
        Path.Combine(Application.persistentDataPath, FileName + suffix);

    /// <summary>Marks the profile changed; <see cref="Save"/> writes it.</summary>
    public static void MarkDirty() => _dirty = true;

    /// <summary>Stamps the first finished round (idempotent).</summary>
    public static void MarkFirstSuccess()
    {
        if (Current.firstSuccessAt != 0) return;
        Current.firstSuccessAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _dirty = true;
    }

    /// <summary>Appends a solo round result to the rolling window.</summary>
    public static void PushSoloResult(bool won)
    {
        var list = Current.solo.lastResults;
        list.Add(won ? 1 : 0);
        while (list.Count > LastResultsKept) list.RemoveAt(0);
        _dirty = true;
    }

    public static void Load()
    {
        string path = PathOf();
        string tmp = PathOf(".tmp");
        // A crash between "delete old" and "move new" in the fallback save
        // path leaves only the tmp file - it is complete, so take it.
        if (!File.Exists(path) && File.Exists(tmp))
        {
            try { File.Move(tmp, path); } catch (Exception e) { Debug.LogWarning($"[Profile] tmp recovery failed: {e.Message}"); }
        }

        if (!File.Exists(path))
        {
            _data = new Data();
            ImportLegacyPrefs(_data);
            _dirty = true;
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<Data>(json);
            if (data == null) throw new InvalidDataException("empty profile");
            if (data.schemaVersion > SchemaVersion)
                throw new InvalidDataException($"profile from a newer build (v{data.schemaVersion})");
            Migrate(data);
            Sanitize(data);
            _data = data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Profile] Unreadable {path}: {e.Message} - starting fresh");
            try
            {
                string bad = PathOf(".bad");
                if (File.Exists(bad)) File.Delete(bad);
                File.Move(path, bad);
            }
            catch (Exception moveError) { Debug.LogWarning($"[Profile] Could not keep the bad file: {moveError.Message}"); }

            _data = new Data();
            ImportLegacyPrefs(_data);
            _dirty = true;
            Save();
            Telemetry.Log("profile_reset", ("reason", e.GetType().Name));
        }
    }

    /// <summary>Writes the profile if anything changed since the last save.</summary>
    public static void Save()
    {
        if (_data == null || !_dirty) return;
        string path = PathOf();
        string tmp = PathOf(".tmp");
        try
        {
            File.WriteAllText(tmp, JsonUtility.ToJson(_data, true));
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch (PlatformNotSupportedException) { File.Delete(path); File.Move(tmp, path); }
            }
            else
            {
                File.Move(tmp, path);
            }
            _dirty = false;
            Saved?.Invoke();
        }
        catch (Exception e)
        {
            // Full disk, sandbox hiccup: keep playing, retry on the next save point.
            Debug.LogWarning($"[Profile] Save failed: {e.Message}");
        }
    }

    // One switch per schema bump. v1 is the first version, so nothing to do yet.
    private static void Migrate(Data data)
    {
        switch (data.schemaVersion)
        {
            case SchemaVersion:
                break;
            default:
                // Files written before schemaVersion existed deserialize as 0.
                data.schemaVersion = SchemaVersion;
                _dirty = true;
                break;
        }
    }

    // JsonUtility leaves missing objects null when the file omits them.
    private static void Sanitize(Data data)
    {
        data.stats ??= new Stats();
        data.settings ??= new Settings();
        data.solo ??= new Solo();
        data.solo.lastResults ??= new List<int>();
    }

    // PlayerPrefs settings that predate the profile (Prompts/26).
    // IAPManager's purchase cache stays in PlayerPrefs until Phase 4.
    private static void ImportLegacyPrefs(Data data)
    {
        try
        {
            if (PlayerPrefs.HasKey(ControlSettings.SchemeKey))
            {
                data.settings.controlScheme = PlayerPrefs.GetInt(ControlSettings.SchemeKey, 0);
                data.settings.controlSchemeSaved = true;
            }
        }
        catch (Exception e) { Debug.LogWarning($"[Profile] Legacy prefs import failed: {e.Message}"); }
    }

#if UNITY_EDITOR
    /// <summary>Editor/test hook: forget the in-memory copy so the next access reloads.</summary>
    public static void ForgetLoaded() { _data = null; _dirty = false; }
#endif
}
