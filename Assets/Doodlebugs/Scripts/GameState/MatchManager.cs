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
    // Safety net, not the normal path: the round starts when everyone has
    // pressed READY. 30 s was fine when the hangar held one row of weapons;
    // with 25 airframes x 50 liveries to browse it fired while people were
    // still in the picker, which read as the game ignoring READY entirely.
    public const int HangarSeconds = 120;

    // Run = best-of-5: first client to win this many rounds takes the run,
    // then the podium shows and everything (upgrades, weapons, wins) resets.
    public const int RoundWinsTarget = 3;
    public const int PodiumSeconds = 10;

    // Hangar-as-lobby: the first player waits (and may warm-up fly) in
    // WaitingForPlayers; the 2nd connect starts a short pre-battle countdown
    // (READY skips it early); a client joining a running battle gets a
    // personal hangar and deploys after LateJoinSeconds at the latest.
    public const int PreBattleSeconds = 120;
    public const int LateJoinSeconds = 10;

    /// <summary>Server-authoritative game phase, mirrored to clients.</summary>
    public enum GamePhase : byte
    {
        WaitingForPlayers = 0,
        PreBattleCountdown = 1,
        Battle = 2,
        Intermission = 3,
        Podium = 4
    }

    /// <summary>Which flavor of the hangar overlay to show. Searching is a
    /// purely client-side boot mode (discovery running) - never sent by the
    /// server.</summary>
    public enum HangarMode { Waiting, PreBattle, LateJoin, Intermission, Searching }

    public GamePhase Phase { get; private set; } = GamePhase.WaitingForPlayers;

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

    /// <summary>Fired when a hangar overlay should open (mode + countdown seconds, 0 = none).</summary>
    public event Action<HangarMode, int> OnHangarOpened;

    /// <summary>Fired locally when the network session ends (host quit) - close all overlays.</summary>
    public event Action OnSessionReset;

    /// <summary>Fired once per second while the hangar is open, with seconds to auto-start.</summary>
    public event Action<int> OnHangarTick;

    /// <summary>Fired on every client when someone presses READY.</summary>
    public event Action<ulong> OnClientReadyChanged;

    /// <summary>Fired on every client when local run points / upgrade levels change.</summary>
    public event Action OnRunStateChanged;

    /// <summary>Fired on every client when a run ends (arg = winner clientId).</summary>
    public event Action<ulong> OnRunEnded;

    private Coroutine _intermissionCoroutine;
    private Coroutine _localCountdownCoroutine;   // cosmetic pre-battle/late-join ticks
    private Coroutine _serverRoundFlowCoroutine;  // stored so freeze can cancel it
    private Coroutine _preBattleCoroutine;

    // Server-side guard: kill-target and time-limit checks can both fire in the
    // same frame - end the round only once.
    private bool _roundEnding;

    // Server-side hangar ready set (client ids that pressed READY)
    private readonly System.Collections.Generic.HashSet<ulong> _readyClients = new();

    // Server-side: clients sitting in the late-join hangar -> auto-deploy time
    private readonly System.Collections.Generic.Dictionary<ulong, float> _lateJoinDeadlines = new();
    private readonly System.Collections.Generic.List<ulong> _deadlineScratch = new();
    private float _preBattleDeadline;

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

        while (NetworkManager.Singleton == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += OnNetClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnNetClientDisconnected;
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
            ScoreManager.Instance.OnMatchStarted -= OnMatchStarted;
        }
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnNetClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnNetClientDisconnected;
        }
    }

    // --- game phase (hangar-as-lobby) ---------------------------------------

    /// <summary>
    /// Server-side rule for PlayerController.OnNetworkSpawn: remote clients'
    /// auto-spawned planes start parked in the hangar and are deployed by this
    /// manager (battle start, round restart or late-join deploy). Host planes
    /// (incl. couch co-op) never park - the host warm-up flies while waiting.
    /// </summary>
    public bool ShouldSpawnHiddenInHangar(ulong ownerClientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return false;
        return ownerClientId != NetworkManager.ServerClientId;
    }

    private void OnServerStarted()
    {
        if (!IsServer()) return;
        Phase = GamePhase.WaitingForPlayers;
        BackgroundManager.Instance?.ServerResetWarmUpRotation();
        Debug.Log("[MatchManager] Phase -> WaitingForPlayers (host started)");
        StartCoroutine(OpenWaitingHangarDelayed());
    }

    // Small delay so the host's P1 plane exists before the weapon cards read it
    private IEnumerator OpenWaitingHangarDelayed()
    {
        yield return new WaitForSeconds(0.5f);
        if (Phase == GamePhase.WaitingForPlayers)
        {
            OnHangarOpened?.Invoke(HangarMode.Waiting, 0);
        }
    }

    private void OnNetClientConnected(ulong clientId)
    {
        if (!IsServer()) return;

        switch (Phase)
        {
            case GamePhase.WaitingForPlayers:
                if (NetworkManager.Singleton.ConnectedClientsIds.Count >= 2)
                {
                    StartPreBattleCountdown();
                }
                break;

            case GamePhase.Battle:
                // Late joiner: personal hangar with a deploy deadline. The
                // hangar-open RPC is sent with the snapshot reply so it always
                // arrives after the client is ready to receive it.
                if (clientId != NetworkManager.ServerClientId)
                {
                    _lateJoinDeadlines[clientId] = Time.time + LateJoinSeconds;
                    Debug.Log($"[MatchManager] Client {clientId} joined mid-battle - late-join hangar");
                }
                break;

            // PreBattleCountdown: plane spawns parked and deploys with everyone;
            // the countdown does not restart.
            // Intermission/Podium: parked plane deploys with the round restart.
        }
    }

    private void OnNetClientDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;

        // A client losing its host: reset local mirrors, close overlays.
        if (nm != null && !nm.IsServer)
        {
            HandleLocalSessionEnded();
            return;
        }

        if (!IsServer()) return;

        _lateJoinDeadlines.Remove(clientId);
        _readyClients.Remove(clientId);

        // The leaver may still be counted at callback time - exclude it.
        int remaining = 0;
        foreach (ulong id in nm.ConnectedClientsIds)
        {
            if (id != clientId) remaining++;
        }

        if (remaining < 2 && Phase != GamePhase.WaitingForPlayers)
        {
            ServerFreezeToWaiting();
        }
    }

    /// <summary>Server: 2nd player arrived - everyone picks a weapon, then battle.</summary>
    private void StartPreBattleCountdown()
    {
        Phase = GamePhase.PreBattleCountdown;
        _readyClients.Clear();
        _preBattleDeadline = Time.time + PreBattleSeconds;
        Debug.Log("[MatchManager] Phase -> PreBattleCountdown");

        BroadcastThroughServerPlayer(p =>
            p.SyncGamePhaseClientRpc((byte)GamePhase.PreBattleCountdown, PreBattleSeconds));

        _preBattleCoroutine = StartCoroutine(ServerPreBattleFlow());
    }

    private IEnumerator ServerPreBattleFlow()
    {
        while (Time.time < _preBattleDeadline)
        {
            if (AllClientsReady())
            {
                Debug.Log("[MatchManager] All clients ready - starting battle early");
                break;
            }
            yield return new WaitForSeconds(0.25f);
        }
        _preBattleCoroutine = null;
        if (!IsServer()) yield break;
        ServerStartBattle();
    }

    /// <summary>Server: deploy everyone and start the first round.</summary>
    private void ServerStartBattle()
    {
        Phase = GamePhase.Battle;
        _lateJoinDeadlines.Clear();
        Debug.Log("[MatchManager] Phase -> Battle");

        // Start scoring and close every hangar FIRST: RestartMatch's broadcast
        // is the ONLY signal that closes the PreBattle overlay. It must not sit
        // behind anything that can throw - a silent respawn exception used to
        // strand every device on "BATTLE IN 1" with the battle never starting.
        ScoreManager.Instance?.RestartMatch();

        // Fresh arena for the first battle (synced NetworkVariable)
        try { BackgroundManager.Instance?.SelectRandomBackground(); }
        catch (Exception e) { Debug.LogException(e); }

        foreach (var player in FindObjectsOfType<PlayerController>())
        {
            if (player.IsBot) continue;   // despawned by now; never deploy it into a battle
            SafeDeploy(player);
        }
    }

    // Un-park and respawn one plane; a single bad plane must never abort the
    // caller's whole battle-start loop.
    private static void SafeDeploy(PlayerController player)
    {
        try
        {
            player.NetInHangar.Value = false;
            player.ServerRespawn();
        }
        catch (Exception e)
        {
            Debug.LogError($"[MatchManager] Deploy failed for client {player.OwnerClientId}");
            Debug.LogException(e);
        }
    }

    /// <summary>
    /// Server: too few players to fight - freeze the battle back to the
    /// waiting hangar. Scores stop (timer frozen), run state is KEPT.
    /// </summary>
    private void ServerFreezeToWaiting()
    {
        Debug.Log("[MatchManager] Not enough players - Phase -> WaitingForPlayers");

        if (_serverRoundFlowCoroutine != null)
        {
            StopCoroutine(_serverRoundFlowCoroutine);
            _serverRoundFlowCoroutine = null;
        }
        if (_preBattleCoroutine != null)
        {
            StopCoroutine(_preBattleCoroutine);
            _preBattleCoroutine = null;
        }

        _readyClients.Clear();
        _lateJoinDeadlines.Clear();
        _roundEnding = false;
        IsIntermission = false;
        Phase = GamePhase.WaitingForPlayers;
        BackgroundManager.Instance?.ServerResetWarmUpRotation();

        float frozenTime = ScoreManager.Instance != null ? ScoreManager.Instance.MatchTime : 0f;
        BroadcastThroughServerPlayer(p =>
        {
            p.SyncMatchStateClientRpc(false, frozenTime);
            p.SyncGamePhaseClientRpc((byte)GamePhase.WaitingForPlayers, 0f);
        });
    }

    /// <summary>Local session ended (host quit) - reset client-side mirrors.</summary>
    private void HandleLocalSessionEnded()
    {
        Phase = GamePhase.WaitingForPlayers;
        BackgroundManager.Instance?.ServerResetWarmUpRotation();
        IsIntermission = false;
        IsRunOver = false;
        _clientRoundWins.Clear();
        if (_intermissionCoroutine != null)
        {
            StopCoroutine(_intermissionCoroutine);
            _intermissionCoroutine = null;
        }
        if (_localCountdownCoroutine != null)
        {
            StopCoroutine(_localCountdownCoroutine);
            _localCountdownCoroutine = null;
        }
        OnSessionReset?.Invoke();
    }

    private void Update()
    {
        if (!IsServer()) return;

        // Auto-deploy late joiners whose hangar time ran out
        if (_lateJoinDeadlines.Count > 0)
        {
            _deadlineScratch.Clear();
            foreach (var entry in _lateJoinDeadlines)
            {
                if (Time.time >= entry.Value) _deadlineScratch.Add(entry.Key);
            }
            foreach (ulong clientId in _deadlineScratch)
            {
                ServerDeploy(clientId);
            }
        }

        // Waiting lobby: rotate the arena so the FLY warm-up does not sit on
        // one map for the whole wait.
        if (Phase == GamePhase.WaitingForPlayers)
        {
            BackgroundManager.Instance?.ServerTickWarmUpRotation();
            BotManager.Instance?.ServerTickWarmUp();
        }
        else
        {
            // Polling the phase here rather than hooking every `Phase = ...`
            // site means no future phase can leave the bot alive by accident.
            BotManager.Instance?.ServerEnsureDespawned();
        }

        // Time-limit check (server decides)
        if (IsIntermission) return;

        var score = ScoreManager.Instance;
        if (score == null || !score.MatchStarted) return;

        if (score.MatchTime >= TimeLimitSeconds)
        {
            EndRound();
        }
    }

    // --- late-join deploy (server) -------------------------------------------

    /// <summary>Server-side: a late joiner pressed JOIN in their hangar.</summary>
    public void ServerRequestDeploy(ulong clientId)
    {
        if (!IsServer()) return;
        if (!_lateJoinDeadlines.ContainsKey(clientId)) return;
        ServerDeploy(clientId);
    }

    private void ServerDeploy(ulong clientId)
    {
        _lateJoinDeadlines.Remove(clientId);

        // Round ended meanwhile - the parked plane deploys with everyone at restart
        if (Phase != GamePhase.Battle) return;

        foreach (var player in FindObjectsOfType<PlayerController>())
        {
            if (player.OwnerClientId != clientId) continue;
            if (!player.NetInHangar.Value) continue; // already deployed

            // NetworkVariable write first, then the respawn teleport (risk:
            // the owner must re-enable physics before the position lands)
            SafeDeploy(player);
        }
        Debug.Log($"[MatchManager] Client {clientId} deployed into the running battle");
    }

    /// <summary>
    /// Server-side: full game-state snapshot for a (re)connecting client,
    /// requested by its own PlayerController once it is provably spawned.
    /// Replies are targeted RPCs routed through that same player object.
    /// </summary>
    public void ServerSendSnapshotTo(ulong clientId, PlayerController via)
    {
        if (!IsServer() || via == null) return;

        var target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        float remaining = Phase == GamePhase.PreBattleCountdown
            ? Mathf.Max(0f, _preBattleDeadline - Time.time)
            : 0f;
        via.SyncGamePhaseClientRpc((byte)Phase, remaining, target);

        ScoreManager.Instance?.SyncFullStateTo(via, clientId);

        foreach (var entry in _roundWins)
        {
            via.SyncRoundWinsClientRpc(entry.Key, entry.Value, target);
        }

        SyncRunStateTo(clientId);

        if (_lateJoinDeadlines.TryGetValue(clientId, out float deadline))
        {
            int seconds = Mathf.Max(1, Mathf.CeilToInt(deadline - Time.time));
            via.SyncLateJoinHangarClientRpc(seconds, target);
        }

        Debug.Log($"[MatchManager] Sent state snapshot to client {clientId} (phase {Phase})");
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

    // A new round started (server-driven restart) - leave intermission and
    // cancel any pending local countdowns.
    private void OnMatchStarted()
    {
        Phase = GamePhase.Battle;
        IsIntermission = false;
        _roundEnding = false;
        _readyClients.Clear();
        if (_intermissionCoroutine != null)
        {
            StopCoroutine(_intermissionCoroutine);
            _intermissionCoroutine = null;
        }
        if (_localCountdownCoroutine != null)
        {
            StopCoroutine(_localCountdownCoroutine);
            _localCountdownCoroutine = null;
        }
    }

    /// <summary>Server-only: end the round now and broadcast the result.</summary>
    private void EndRound()
    {
        if (_roundEnding) return;
        _roundEnding = true;

        // Anyone still picking in the late-join hangar merges into the
        // intermission and deploys with everyone at the round restart.
        _lateJoinDeadlines.Clear();

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

        Phase = runOver ? GamePhase.Podium : GamePhase.Intermission;

        // Server drives the actual restart (results -> hangar/podium -> next round)
        _serverRoundFlowCoroutine = StartCoroutine(ServerEndRoundFlow(runOver));
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
            // Never route a phase RPC through the bot: it despawns on the very
            // connect that triggers the pre-battle broadcast.
            if (player.IsServer && !player.IsBot)
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

        Phase = runOver ? GamePhase.Podium : GamePhase.Intermission;
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
            OnHangarOpened?.Invoke(HangarMode.Intermission, HangarSeconds);
            for (int s = HangarSeconds; s > 0; s--)
            {
                OnHangarTick?.Invoke(s);
                if (s <= 3) SfxManager.PlayTick();
                yield return new WaitForSeconds(1f);
            }
        }
        _intermissionCoroutine = null;
    }

    // --- client-side phase handlers (called via PlayerController ClientRpcs) ---

    /// <summary>Adopt the server's game phase; open the matching hangar UI.</summary>
    public void HandlePhaseFromServer(GamePhase phase, float secondsRemaining)
    {
        Phase = phase;

        switch (phase)
        {
            case GamePhase.WaitingForPlayers:
                // Freeze-to-waiting: leave any intermission cosmetics behind
                IsIntermission = false;
                _roundEnding = false;
                if (_intermissionCoroutine != null)
                {
                    StopCoroutine(_intermissionCoroutine);
                    _intermissionCoroutine = null;
                }
                StopLocalCountdown();
                OnHangarOpened?.Invoke(HangarMode.Waiting, 0);
                break;

            case GamePhase.PreBattleCountdown:
                int seconds = Mathf.Max(1, Mathf.CeilToInt(secondsRemaining));
                OnHangarOpened?.Invoke(HangarMode.PreBattle, seconds);
                StartLocalCountdown(seconds);
                break;

            // Battle arrives via SyncMatchStartClientRpc (OnMatchStarted),
            // Intermission/Podium via SyncMatchEndClientRpc - phase mirror only.
        }
    }

    /// <summary>Open the personal late-join hangar (this client only).</summary>
    public void HandleLateJoinHangarFromServer(int seconds)
    {
        OnHangarOpened?.Invoke(HangarMode.LateJoin, seconds);
        StartLocalCountdown(seconds);
    }

    /// <summary>Round-wins snapshot entry for a late joiner.</summary>
    public void HandleRoundWinsFromServer(ulong clientId, int wins)
    {
        _clientRoundWins[clientId] = wins;
    }

    private void StartLocalCountdown(int seconds)
    {
        StopLocalCountdown();
        _localCountdownCoroutine = StartCoroutine(LocalCountdownFlow(seconds));
    }

    private void StopLocalCountdown()
    {
        if (_localCountdownCoroutine != null)
        {
            StopCoroutine(_localCountdownCoroutine);
            _localCountdownCoroutine = null;
        }
    }

    // Cosmetic countdown for the pre-battle / late-join hangars; the real
    // deadline lives on the server.
    private IEnumerator LocalCountdownFlow(int seconds)
    {
        for (int s = seconds; s > 0; s--)
        {
            OnHangarTick?.Invoke(s);
            if (s <= 3) SfxManager.PlayTick();
            yield return new WaitForSeconds(1f);
        }
        _localCountdownCoroutine = null;
    }

    // --- hangar ready check (server) ---

    /// <summary>Server-side: a client pressed READY in the hangar.</summary>
    public void ServerSetClientReady(ulong clientId)
    {
        if (!IsServer()) return;
        // READY works in the intermission hangar and the pre-battle lobby
        if (!IsIntermission && Phase != GamePhase.PreBattleCountdown) return;
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

        // Rotate the arena for the next round now - the terrain rebuild pops
        // behind the hangar/podium overlay instead of mid-battle
        BackgroundManager.Instance?.SelectRandomBackground();

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

        // Reset stats + timer and close the hangars FIRST (see ServerStartBattle
        // - the close broadcast must never sit behind a throwing respawn), then
        // fresh planes for everyone - including anyone still parked in the
        // hangar (late joiners whose round ended while picking)
        Phase = GamePhase.Battle;
        ScoreManager.Instance?.RestartMatch();
        foreach (var player in FindObjectsOfType<PlayerController>())
        {
            if (player.IsBot) continue;   // despawned by now; never deploy it into a battle
            SafeDeploy(player);
        }
        _serverRoundFlowCoroutine = null;
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
