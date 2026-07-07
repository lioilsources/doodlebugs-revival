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
    public const int KillTarget = 3;
    public const float TimeLimitSeconds = 180f;

    // Round end = results screen, then the hangar (weapon draft + ready check).
    // The next round starts when every connected client is ready, or after
    // the auto-start timeout - nobody can block the lobby.
    public const int ResultsSeconds = 4;
    public const int HangarSeconds = 30;

    // Run = best-of-5: first client to win this many rounds takes the run,
    // then the podium shows and everything (upgrades, weapons, wins) resets.
    public const int RoundWinsTarget = 3;
    public const int PodiumSeconds = 10;

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

    /// <summary>Fired on every client when local run points / upgrade levels change.</summary>
    public event Action OnRunStateChanged;

    /// <summary>Fired on every client when a run ends (arg = winner clientId).</summary>
    public event Action<ulong> OnRunEnded;

    private Coroutine _intermissionCoroutine;

    // Server-side guard: kill-target and time-limit checks can both fire in the
    // same frame - end the round only once.
    private bool _roundEnding;

    // Server-side hangar ready set (client ids that pressed READY)
    private readonly System.Collections.Generic.HashSet<ulong> _readyClients = new();

    // --- Run state ---
    // Server-authoritative wallets/levels/wins per client id.
    private readonly System.Collections.Generic.Dictionary<ulong, int> _runPoints = new();
    private readonly System.Collections.Generic.Dictionary<ulong, int[]> _upgradeLevels = new();
    private readonly System.Collections.Generic.Dictionary<ulong, int> _roundWins = new();

    // Client-side mirrors for the HUD.
    public int LocalRunPoints { get; private set; }
    private readonly int[] _localUpgradeLevels = new int[RunUpgrades.TypeCount];
    public int GetLocalUpgradeLevel(RunUpgradeType type) => _localUpgradeLevels[(int)type];

    /// <summary>Round wins per client, known on every client (for results/podium).</summary>
    private readonly System.Collections.Generic.Dictionary<ulong, int> _clientRoundWins = new();
    public System.Collections.Generic.IReadOnlyDictionary<ulong, int> RoundWins => _clientRoundWins;

    /// <summary>True between run end and the next run start (podium shown).</summary>
    public bool IsRunOver { get; private set; }

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

        // Round win + run-over check (best-of-N)
        _roundWins.TryGetValue(winnerClient, out int wins);
        wins++;
        _roundWins[winnerClient] = wins;
        bool runOver = wins >= RoundWinsTarget;

        // Run points: 1 for playing, +1 for top half, +1 for winning the round
        AwardRunPoints(score, winnerClient);

        // Broadcast through any server-owned player (existing sync pattern)
        BroadcastThroughServerPlayer(p =>
            p.SyncMatchEndClientRpc(winnerClient, winnerLocalIdx, winnerKills, wins, runOver));

        // Server drives the actual restart (results -> hangar/podium -> next round)
        StartCoroutine(ServerEndRoundFlow(runOver));
    }

    // Rank every player, take each device's best placement, hand out points.
    private void AwardRunPoints(ScoreManager score, ulong winnerClient)
    {
        var ranked = new System.Collections.Generic.List<
            System.Collections.Generic.KeyValuePair<string, ScoreManager.PlayerStats>>(score.AllStats);
        ranked.Sort((a, b) =>
        {
            int cmp = b.Value.Kills.CompareTo(a.Value.Kills);
            if (cmp != 0) return cmp;
            cmp = a.Value.Deaths.CompareTo(b.Value.Deaths);
            if (cmp != 0) return cmp;
            return a.Value.PlaneCollisions.CompareTo(b.Value.PlaneCollisions);
        });

        var bestRankByClient = new System.Collections.Generic.Dictionary<ulong, int>();
        for (int i = 0; i < ranked.Count; i++)
        {
            ScoreManager.ParsePlayerId(ranked[i].Key, out ulong clientId, out _);
            if (!bestRankByClient.ContainsKey(clientId))
            {
                bestRankByClient[clientId] = i;
            }
        }

        int topHalfCutoff = (bestRankByClient.Count + 1) / 2;

        // Determine each client's placement among clients (by their best plane rank)
        var clients = new System.Collections.Generic.List<
            System.Collections.Generic.KeyValuePair<ulong, int>>(bestRankByClient);
        clients.Sort((a, b) => a.Value.CompareTo(b.Value));

        for (int i = 0; i < clients.Count; i++)
        {
            ulong clientId = clients[i].Key;
            int points = 1;                              // participation
            if (i < topHalfCutoff) points++;             // top half
            if (clientId == winnerClient) points++;      // round winner

            _runPoints.TryGetValue(clientId, out int total);
            _runPoints[clientId] = total + points;

            SyncRunStateTo(clientId);
        }
    }

    private int[] GetUpgradeLevels(ulong clientId)
    {
        if (!_upgradeLevels.TryGetValue(clientId, out var levels))
        {
            levels = new int[RunUpgrades.TypeCount];
            _upgradeLevels[clientId] = levels;
        }
        return levels;
    }

    private void SyncRunStateTo(ulong clientId)
    {
        _runPoints.TryGetValue(clientId, out int points);
        var levels = GetUpgradeLevels(clientId);
        BroadcastThroughServerPlayer(p =>
            p.SyncRunStateClientRpc(clientId, points, levels[0], levels[1], levels[2], levels[3]));
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
    public void HandleMatchEndFromServer(ulong winnerClientId, int winnerLocalPlayerIndex,
        int winnerKills, int winnerRoundWins, bool runOver)
    {
        LastResult = new MatchResult
        {
            WinnerClientId = winnerClientId,
            WinnerLocalPlayerIndex = winnerLocalPlayerIndex,
            WinnerKills = winnerKills
        };

        _clientRoundWins[winnerClientId] = winnerRoundWins;
        IsRunOver = runOver;

        IsIntermission = true;
        ScoreManager.Instance?.FreezeMatch();
        SfxManager.PlayMatchEnd();

        OnMatchEnded?.Invoke(LastResult);

        if (_intermissionCoroutine != null) StopCoroutine(_intermissionCoroutine);
        _intermissionCoroutine = StartCoroutine(ClientIntermissionFlow(runOver, winnerClientId));
    }

    // Cosmetic flow on every client: results screen, then the hangar (or the
    // podium when the run is over). The real restart always comes from the server.
    private IEnumerator ClientIntermissionFlow(bool runOver, ulong winnerClientId)
    {
        yield return new WaitForSeconds(ResultsSeconds);

        if (runOver)
        {
            OnRunEnded?.Invoke(winnerClientId);
            for (int s = PodiumSeconds; s > 0; s--)
            {
                OnHangarTick?.Invoke(s);
                if (s <= 3) SfxManager.PlayTick();
                yield return new WaitForSeconds(1f);
            }
        }
        else
        {
            OnHangarOpened?.Invoke();
            for (int s = HangarSeconds; s > 0; s--)
            {
                OnHangarTick?.Invoke(s);
                if (s <= 3) SfxManager.PlayTick();
                yield return new WaitForSeconds(1f);
            }
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

    private IEnumerator ServerEndRoundFlow(bool runOver)
    {
        _readyClients.Clear();

        // Results screen first
        yield return new WaitForSeconds(ResultsSeconds);

        if (runOver)
        {
            // Podium, then a full run reset - no hangar (nothing to spend on)
            yield return new WaitForSeconds(PodiumSeconds);
            if (!IsServer()) yield break;
            ServerResetRun();
        }
        else
        {
            // Hangar window: start early once every connected client is ready
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
        }

        // Fresh planes for everyone, then reset stats + timer (syncs to clients)
        foreach (var player in FindObjectsOfType<PlayerController>())
        {
            player.ServerRespawn();
        }
        ScoreManager.Instance?.RestartMatch();
        Debug.Log("[MatchManager] New round started");
    }

    // --- run purchases + reset (server) ---

    /// <summary>Server-side: a client wants to buy an upgrade in the hangar.</summary>
    public void ServerBuyUpgrade(ulong clientId, int upgradeType)
    {
        if (!IsServer() || !IsIntermission || IsRunOver) return;
        if (upgradeType < 0 || upgradeType >= RunUpgrades.TypeCount) return;

        var levels = GetUpgradeLevels(clientId);
        _runPoints.TryGetValue(clientId, out int points);

        if (points < RunUpgrades.CostPoints) return;
        if (levels[upgradeType] >= RunUpgrades.MaxLevel) return;

        levels[upgradeType]++;
        _runPoints[clientId] = points - RunUpgrades.CostPoints;

        // Apply to every plane this client owns (couch co-op upgrades together)
        foreach (var player in FindObjectsOfType<PlayerController>())
        {
            if (player.OwnerClientId != clientId) continue;
            player.PlaneStats?.ApplyRunUpgrades(
                levels[(int)RunUpgradeType.Shield],
                levels[(int)RunUpgradeType.Hull],
                levels[(int)RunUpgradeType.FireRate],
                levels[(int)RunUpgradeType.Engine]);
        }

        Debug.Log($"[MatchManager] Client {clientId} bought {(RunUpgradeType)upgradeType} " +
                  $"(level {levels[upgradeType]}, {_runPoints[clientId]} pts left)");
        SyncRunStateTo(clientId);
    }

    /// <summary>Server-side: full reset after the podium - new run from scratch.</summary>
    private void ServerResetRun()
    {
        _runPoints.Clear();
        _upgradeLevels.Clear();
        _roundWins.Clear();

        foreach (var player in FindObjectsOfType<PlayerController>())
        {
            player.PlaneStats?.ApplyRunUpgrades(0, 0, 0, 0);
            player.GetComponent<Shooting>()?.ServerSetSelectedWeapon((int)WeaponType.MG);
        }

        BroadcastThroughServerPlayer(p => p.SyncRunResetClientRpc());
        Debug.Log("[MatchManager] Run reset - new run starts");
    }

    /// <summary>Called on every client via PlayerController ClientRpc.</summary>
    public void HandleRunStateFromServer(ulong clientId, int points,
        int shieldLevel, int hullLevel, int fireRateLevel, int engineLevel)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || clientId != nm.LocalClientId) return;

        LocalRunPoints = points;
        _localUpgradeLevels[(int)RunUpgradeType.Shield] = shieldLevel;
        _localUpgradeLevels[(int)RunUpgradeType.Hull] = hullLevel;
        _localUpgradeLevels[(int)RunUpgradeType.FireRate] = fireRateLevel;
        _localUpgradeLevels[(int)RunUpgradeType.Engine] = engineLevel;

        OnRunStateChanged?.Invoke();
    }

    /// <summary>Called on every client via PlayerController ClientRpc.</summary>
    public void HandleRunResetFromServer()
    {
        LocalRunPoints = 0;
        for (int i = 0; i < _localUpgradeLevels.Length; i++) _localUpgradeLevels[i] = 0;
        _clientRoundWins.Clear();
        IsRunOver = false;

        OnRunStateChanged?.Invoke();
    }
}
