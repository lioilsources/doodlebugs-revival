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

    // Round end = results screen, then the hangar (weapon draft + ready check).
    // The next round starts when every connected client is ready, or after
    // the auto-start timeout - nobody can block the lobby.
    public const int ResultsSeconds = 4;
    public const int HangarSeconds = 30;

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

    /// <summary>Fired on every client when the hangar opens (after the results).</summary>
    public event Action OnHangarOpened;

    /// <summary>Fired once per second while the hangar is open, with seconds to auto-start.</summary>
    public event Action<int> OnHangarTick;

    /// <summary>Fired on every client when someone presses READY.</summary>
    public event Action<ulong> OnClientReadyChanged;

    private Coroutine _intermissionCoroutine;

    // Server-side guard: kill-target and time-limit checks can both fire in the
    // same frame - end the round only once.
    private bool _roundEnding;

    // Server-side hangar ready set (client ids that pressed READY)
    private readonly System.Collections.Generic.HashSet<ulong> _readyClients = new();

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
        _readyClients.Clear();
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
        BroadcastThroughServerPlayer(p => p.SyncMatchEndClientRpc(winnerClient, winnerLocalIdx, winnerKills));

        // Server drives the actual restart (results -> hangar -> all ready/timeout)
        StartCoroutine(ServerHangarFlow());
    }

    private void BroadcastThroughServerPlayer(Action<PlayerController> rpc)
    {
        foreach (var player in FindObjectsOfType<PlayerController>())
        {
            if (player.IsServer)
            {
                rpc(player);
                break;
            }
        }
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
        _intermissionCoroutine = StartCoroutine(ClientIntermissionFlow());
    }

    // Cosmetic flow on every client: results screen, then the hangar with its
    // auto-start countdown. The real restart always comes from the server.
    private IEnumerator ClientIntermissionFlow()
    {
        yield return new WaitForSeconds(ResultsSeconds);

        OnHangarOpened?.Invoke();

        for (int s = HangarSeconds; s > 0; s--)
        {
            OnHangarTick?.Invoke(s);
            if (s <= 3) SfxManager.PlayTick();
            yield return new WaitForSeconds(1f);
        }
        _intermissionCoroutine = null;
    }

    // --- hangar ready check (server) ---

    /// <summary>Server-side: a client pressed READY in the hangar.</summary>
    public void ServerSetClientReady(ulong clientId)
    {
        if (!IsServer() || !IsIntermission) return;
        if (!_readyClients.Add(clientId)) return;

        Debug.Log($"[MatchManager] Client {clientId} is ready ({_readyClients.Count} total)");
        BroadcastThroughServerPlayer(p => p.SyncHangarReadyClientRpc(clientId));
    }

    /// <summary>Called on every client via PlayerController ClientRpc.</summary>
    public void HandleClientReadyFromServer(ulong clientId)
    {
        OnClientReadyChanged?.Invoke(clientId);
    }

    private bool AllClientsReady()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return false;
        foreach (ulong id in nm.ConnectedClientsIds)
        {
            if (!_readyClients.Contains(id)) return false;
        }
        return true;
    }

    private IEnumerator ServerHangarFlow()
    {
        _readyClients.Clear();

        // Results screen, then the hangar window
        yield return new WaitForSeconds(ResultsSeconds);

        float deadline = Time.time + HangarSeconds;
        while (Time.time < deadline)
        {
            if (AllClientsReady())
            {
                Debug.Log("[MatchManager] All clients ready - starting early");
                break;
            }
            yield return new WaitForSeconds(0.25f);
        }

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
