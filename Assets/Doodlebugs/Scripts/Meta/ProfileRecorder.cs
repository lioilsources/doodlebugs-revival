using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Feeds <see cref="PlayerProfile"/> from the round flow on THIS device
/// (Prompts/27 WP2). Listens to <see cref="MatchManager.OnMatchEnded"/>, which
/// fires on every client, and reads the replicated score table - no server
/// involvement, so LAN clients keep their own stats too. Also the profile's
/// background save point (<see cref="OnApplicationPause"/>).
/// Created at runtime, no scene wiring.
/// </summary>
public class ProfileRecorder : MonoBehaviour
{
    private static ProfileRecorder _instance;
    private MatchManager _match;

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
        if (_match != null) return;
        _match = MatchManager.Instance;
        if (_match != null) _match.OnMatchEnded += HandleMatchEnded;
    }

    private void OnDestroy()
    {
        if (_match != null) _match.OnMatchEnded -= HandleMatchEnded;
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
        return !PlayerController.IsBotIdentity(clientId, localPlayerIndex);
    }

    private void HandleMatchEnded(MatchManager.MatchResult result)
    {
        var score = ScoreManager.Instance;
        if (score == null) return;

        var profile = PlayerProfile.Current;
        int kills = 0, deaths = 0, rows = 0;
        foreach (var entry in score.AllStats)
        {
            ScoreManager.ParsePlayerId(entry.Key, out ulong clientId, out int idx);
            if (!IsLocalHumanRow(clientId, idx)) continue;
            kills += entry.Value.Kills;
            deaths += entry.Value.Deaths;
            rows++;
        }
        // A device with no plane in the table (still parked in a late-join
        // hangar) did not play this round.
        if (rows == 0) return;

        bool won = IsLocalHumanRow(result.WinnerClientId, result.WinnerLocalPlayerIndex);
        bool solo = _match != null && _match.IsSoloSession;

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
