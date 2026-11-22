using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Feeds <see cref="PlayerProfile"/> from the round flow on THIS device
/// (Prompts/27 WP2). Listens to <see cref="MatchManager.OnMatchEnded"/>, which
/// fires on every client, and reads the replicated score table - no server
/// involvement, so LAN clients keep their own stats too. Also the profile's
/// background save point (<see cref="OnApplicationPause"/>), and the
/// per-device round telemetry (Prompts/27 §11: round_start, round_end,
/// time_to_first_shot). Created at runtime, no scene wiring.
/// </summary>
public class ProfileRecorder : MonoBehaviour
{
    private static ProfileRecorder _instance;
    private MatchManager _match;
    private ScoreManager _score;
    private static bool _firstShotLogged;
    private static bool _roundRunning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject(nameof(ProfileRecorder));
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ProfileRecorder>();
        // Load now, at boot: one small file, far off the 15-second budget,
        // and it keeps the first round end from paying for the read.
        _ = PlayerProfile.Current;
    }

    private void Update()
    {
        // MatchManager is a scene object; bind once it exists.
        if (_score == null && ScoreManager.Instance != null)
        {
            _score = ScoreManager.Instance;
            _score.OnMatchStarted += HandleRoundStarted;
        }
        if (_match != null) return;
        _match = MatchManager.Instance;
        if (_match != null) _match.OnMatchEnded += HandleMatchEnded;
    }

    private static string Mode =>
        MatchManager.Instance != null && MatchManager.Instance.IsSoloSession ? "solo" : "party";

    private static int BotCount()
    {
        int n = 0;
        foreach (var p in PlayerController.All) if (p.IsCombatBot) n++;
        return n;
    }

    private static int BotTierNow()
    {
        foreach (var p in PlayerController.All) if (p.IsCombatBot) return p.NetBotTier.Value;
        return -1;
    }

    private static string LocalWeapon()
    {
        foreach (var p in PlayerController.All)
        {
            if (!p.IsOwner || p.IsBot) continue;
            var shooting = p.GetComponent<Shooting>();
            if (shooting != null) return shooting.CurrentWeapon.Type.ToString();
        }
        return "?";
    }

    private static string Arena => BackgroundManager.Instance != null ? BackgroundManager.Instance.CurrentArenaKey : "?";

    private void HandleRoundStarted()
    {
        _roundRunning = true;
        Telemetry.Log("round_start", ("mode", Mode), ("bots", BotCount()), ("tier", BotTierNow()),
            ("arena", Arena), ("weapon", LocalWeapon()));
    }

    /// <summary>This device's first shot since launch (the 15-second rule's
    /// measurement). Called by Shooting for human planes only.</summary>
    public static void ReportLocalShot()
    {
        if (_firstShotLogged) return;
        _firstShotLogged = true;
        Telemetry.Log("time_to_first_shot", ("seconds", Time.realtimeSinceStartup.ToString("0.0")), ("mode", Mode));
    }

    /// <summary>The player left a running solo round from the HOME button.</summary>
    public static void LogRoundQuit()
    {
        if (!_roundRunning) return;
        _roundRunning = false;
        var score = ScoreManager.Instance;
        CountLocal(score, out int kills, out int deaths, out _);
        Telemetry.Log("round_end", ("result", "quit"), ("kills", kills), ("deaths", deaths),
            ("duration", score != null ? score.MatchTime.ToString("0") : "0"), ("weapon", LocalWeapon()),
            ("arena", Arena), ("mode", Mode), ("tier", BotTierNow()));
    }

    private static void CountLocal(ScoreManager score, out int kills, out int deaths, out int rows)
    {
        kills = deaths = rows = 0;
        if (score == null) return;
        foreach (var entry in score.AllStats)
        {
            ScoreManager.ParsePlayerId(entry.Key, out ulong clientId, out int idx);
            if (!IsLocalHumanRow(clientId, idx)) continue;
            kills += entry.Value.Kills;
            deaths += entry.Value.Deaths;
            rows++;
        }
    }

    private void OnDestroy()
    {
        if (_match != null) _match.OnMatchEnded -= HandleMatchEnded;
        if (_score != null) _score.OnMatchStarted -= HandleRoundStarted;
        PlayerProfile.Save();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) PlayerProfile.Save();
    }

    private void OnApplicationQuit() => PlayerProfile.Save();

    /// <summary>A score row that belongs to a human on this device.</summary>
    public static bool IsLocalHumanRow(ulong clientId, int localPlayerIndex)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || clientId != nm.LocalClientId) return false;
        return !PlayerController.IsAnyBotIdentity(localPlayerIndex);
    }

    private void HandleMatchEnded(MatchManager.MatchResult result)
    {
        var score = ScoreManager.Instance;
        if (score == null) return;

        var profile = PlayerProfile.Current;
        CountLocal(score, out int kills, out int deaths, out int rows);
        _roundRunning = false;
        // A device with no plane in the table (still parked in a late-join
        // hangar) did not play this round.
        if (rows == 0) return;

        bool won = IsLocalHumanRow(result.WinnerClientId, result.WinnerLocalPlayerIndex);
        bool solo = _match != null && _match.IsSoloSession;
        Telemetry.Log("round_end", ("result", won ? "won" : "lost"), ("kills", kills), ("deaths", deaths),
            ("duration", score.MatchTime.ToString("0")), ("weapon", LocalWeapon()), ("arena", Arena),
            ("mode", solo ? "solo" : "party"), ("tier", BotTierNow()));

        profile.stats.kills += kills;
        profile.stats.deaths += deaths;
        profile.stats.rounds++;
        if (won) profile.stats.roundsWon++;
        if (solo) profile.stats.soloRounds++;
        else profile.stats.lanRounds++;
        profile.stats.playSeconds += score.MatchTime;
        PlayerProfile.MarkFirstSuccess();
        PlayerProfile.MarkDirty();
        PlayerProfile.Save();
    }
}
