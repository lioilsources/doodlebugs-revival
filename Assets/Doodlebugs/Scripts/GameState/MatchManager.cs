using System;
using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Round flow: first player to reach the kill target - or the best player when
/// the time limit runs out - wins the round. Results are shown during a short
/// intermission, then the server resets stats, respawns everyone and a new
/// round starts.
///
/// Server-authoritative; end-of-round is synced to clients through
/// PlayerController.SyncMatchEndClientRpc (same routing pattern ScoreManager
/// uses for scores). Created at runtime by GameSetup - no scene wiring.
/// </summary>
public class MatchManager : MonoBehaviour
{
    public const int KillTarget = 10;
    public const float TimeLimitSeconds = 180f;
    public const int IntermissionSeconds = 8;

    public static MatchManager Instance { get; private set; }

    public struct MatchResult
    {
        public ulong WinnerClientId;
        public int WinnerLocalPlayerIndex;
        public int WinnerKills;
    }

    /// <summary>True between round end and the next round start (results shown).</summary>
    public bool IsIntermission { get; private set; }

    public MatchResult LastResult { get; private set; }

    /// <summary>Fired on every client when the round ends.</summary>
    public event Action<MatchResult> OnMatchEnded;

    /// <summary>Fired once per second during intermission with seconds left.</summary>
    public event Action<int> OnIntermissionTick;

    private Coroutine _intermissionCoroutine;

    // Server-side guard: kill-target and time-limit checks can both fire in the
    // same frame - end the round only once.
    private bool _roundEnding;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        StartCoroutine(SubscribeWhenReady());
    }

    private IEnumerator SubscribeWhenReady()
    {
        while (ScoreManager.Instance == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        ScoreManager.Instance.OnScoreChanged += OnScoreChanged;
        ScoreManager.Instance.OnMatchStarted += OnMatchStarted;
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
            ScoreManager.Instance.OnMatchStarted -= OnMatchStarted;
        }
    }

    private void Update()
    {
        // Time-limit check (server decides)
        if (!IsServer()) return;
        if (IsIntermission) return;

        var score = ScoreManager.Instance;
        if (score == null || !score.MatchStarted) return;

        if (score.MatchTime >= TimeLimitSeconds)
        {
            EndRound();
        }
    }

    private static bool IsServer() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    // Kill-target check (OnScoreChanged fires on the server for every kill)
    private void OnScoreChanged(ulong clientId, int localPlayerIndex, int newKills)
    {
        if (!IsServer() || IsIntermission) return;
        if (ScoreManager.Instance == null || !ScoreManager.Instance.MatchStarted) return;

        if (newKills >= KillTarget)
        {
            EndRound();
        }
    }

    // A new round started (server restart or a new player joining resets the
    // match) - leave intermission and cancel any pending local countdown.
    private void OnMatchStarted()
    {
        IsIntermission = false;
        _roundEnding = false;
        if (_intermissionCoroutine != null)
        {
            StopCoroutine(_intermissionCoroutine);
            _intermissionCoroutine = null;
        }
    }

    /// <summary>Server-only: end the round now and broadcast the result.</summary>
    private void EndRound()
    {
        if (_roundEnding) return;
        _roundEnding = true;

        var score = ScoreManager.Instance;
        if (score == null) return;

        // Winner: most kills, then fewest deaths, then fewest collisions
        ulong winnerClient = 0;
        int winnerLocalIdx = 0;
        ScoreManager.PlayerStats best = null;

        foreach (var entry in score.AllStats)
        {
            var stats = entry.Value;
            bool better = best == null ||
                          stats.Kills > best.Kills ||
                          (stats.Kills == best.Kills && stats.Deaths < best.Deaths) ||
                          (stats.Kills == best.Kills && stats.Deaths == best.Deaths &&
                           stats.PlaneCollisions < best.PlaneCollisions);
            if (better)
            {
                best = stats;
                ScoreManager.ParsePlayerId(entry.Key, out winnerClient, out winnerLocalIdx);
            }
        }

        int winnerKills = best?.Kills ?? 0;
        Debug.Log($"[MatchManager] Round over - winner {winnerClient}_{winnerLocalIdx} with {winnerKills} kills");

        // Broadcast through any server-owned player (existing sync pattern)
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            if (player.IsServer)
            {
                player.SyncMatchEndClientRpc(winnerClient, winnerLocalIdx, winnerKills);
                break;
            }
        }

        // Server drives the actual restart after the intermission
        StartCoroutine(ServerRestartAfterIntermission());
    }

    /// <summary>Called on every client (incl. host) via PlayerController ClientRpc.</summary>
    public void HandleMatchEndFromServer(ulong winnerClientId, int winnerLocalPlayerIndex, int winnerKills)
    {
        LastResult = new MatchResult
        {
            WinnerClientId = winnerClientId,
            WinnerLocalPlayerIndex = winnerLocalPlayerIndex,
            WinnerKills = winnerKills
        };

        IsIntermission = true;
        ScoreManager.Instance?.FreezeMatch();
        SfxManager.PlayMatchEnd();

        OnMatchEnded?.Invoke(LastResult);

        if (_intermissionCoroutine != null) StopCoroutine(_intermissionCoroutine);
        _intermissionCoroutine = StartCoroutine(IntermissionCountdown());
    }

    // Cosmetic countdown on every client; the real restart comes from the server.
    private IEnumerator IntermissionCountdown()
    {
        for (int s = IntermissionSeconds; s > 0; s--)
        {
            OnIntermissionTick?.Invoke(s);
            if (s <= 3) SfxManager.PlayTick();
            yield return new WaitForSeconds(1f);
        }
        _intermissionCoroutine = null;
    }

    private IEnumerator ServerRestartAfterIntermission()
    {
        yield return new WaitForSeconds(IntermissionSeconds);

        if (!IsServer()) yield break;

        // Fresh planes for everyone, then reset stats + timer (syncs to clients)
        foreach (var player in FindObjectsOfType<PlayerController>())
        {
            player.ServerRespawn();
        }
        ScoreManager.Instance?.RestartMatch();
        Debug.Log("[MatchManager] New round started");
    }
}
