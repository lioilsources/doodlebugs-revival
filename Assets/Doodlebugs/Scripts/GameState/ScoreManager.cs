using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Manages game scores, statistics, and match timer.
/// Singleton that runs on all clients, server-authoritative for scoring.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    /// <summary>
    /// Player statistics (kills, deaths, collisions)
    /// </summary>
    public class PlayerStats
    {
        public int Kills;           // Enemy planes shot down
        public int Deaths;          // Crashed into ground/obstacles or flew out of bounds
        public int PlaneCollisions; // Collided with another plane
    }

    // Player stats dictionary - key is "clientId_localPlayerIndex"
    private Dictionary<string, PlayerStats> _playerStats = new Dictionary<string, PlayerStats>();

    // Legacy score properties for backward compatibility
    public int Player1Score => GetStats(0, 0).Kills;
    public int Player2Score => GetStats(1, 0).Kills;

    /// <summary>
    /// Create unique player ID from clientId and localPlayerIndex
    /// </summary>
    public static string GetPlayerId(ulong clientId, int localPlayerIndex)
    {
        return $"{clientId}_{localPlayerIndex}";
    }

    /// <summary>
    /// Parse a player ID back into clientId and localPlayerIndex.
    /// </summary>
    public static void ParsePlayerId(string playerId, out ulong clientId, out int localPlayerIndex)
    {
        clientId = 0;
        localPlayerIndex = 0;
        int split = playerId.IndexOf('_');
        if (split <= 0) return;
        ulong.TryParse(playerId.Substring(0, split), out clientId);
        int.TryParse(playerId.Substring(split + 1), out localPlayerIndex);
    }

    /// <summary>
    /// All tracked player stats, keyed by "clientId_localPlayerIndex" (read-only).
    /// </summary>
    public IReadOnlyDictionary<string, PlayerStats> AllStats => _playerStats;

    // Match timer
    public float MatchTime { get; private set; }
    public bool MatchStarted { get; private set; }

    // Events for UI - now include localPlayerIndex for couch co-op support
    public event Action<ulong, int, int> OnScoreChanged; // (clientId, localPlayerIndex, newKills)
    public event Action<ulong, int, PlayerStats> OnStatsChanged; // (clientId, localPlayerIndex, stats)
    public event Action OnMatchStarted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // NOTE: matches are no longer auto-started on client connect - MatchManager
    // owns the game phase and calls RestartMatch() when a battle actually begins.
    // A client joining a running battle must NOT reset anyone's score.

    private void StartMatch()
    {
        MatchStarted = true;
        MatchTime = 0f;
        _playerStats.Clear(); // Reset all stats

        Debug.Log("[ScoreManager] Match started!");
        OnMatchStarted?.Invoke();

        // Sync to all clients
        SyncMatchStartToClients();
    }

    /// <summary>
    /// Get or create stats for a player (supports local co-op)
    /// </summary>
    public PlayerStats GetStats(ulong clientId, int localPlayerIndex = 0)
    {
        string playerId = GetPlayerId(clientId, localPlayerIndex);
        if (!_playerStats.TryGetValue(playerId, out var stats))
        {
            stats = new PlayerStats();
            _playerStats[playerId] = stats;
        }
        return stats;
    }

    private void SyncMatchStartToClients()
    {
        // Find any player to send RPC through
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            if (player.IsServer && !player.IsBot)
            {
                player.SyncMatchStartClientRpc();
                break;
            }
        }
    }

    /// <summary>
    /// Called by PlayerController ClientRpc to reset and start match on clients
    /// </summary>
    public void StartMatchFromServer()
    {
        // Always reset - new player connected means new match
        MatchStarted = true;
        MatchTime = 0f;
        _playerStats.Clear();

        Debug.Log("[ScoreManager] Match reset and started (from server sync)!");
        OnMatchStarted?.Invoke();
    }

    private void Update()
    {
        // Update timer on all clients when match is running
        if (MatchStarted)
        {
            MatchTime += Time.deltaTime;
        }
    }

    /// <summary>
    /// Add kill score for a player. Call from server when bullet hits opponent.
    /// </summary>
    public void AddScore(ulong scorerClientId, int localPlayerIndex = 0)
    {
        if (PlayerController.IsBotIdentity(scorerClientId, localPlayerIndex)) return;   // the warm-up bot is not a player
        // Only server can add scores, but this method is called directly from Bullet
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[ScoreManager] AddScore called on client - ignored");
            return;
        }

        var stats = GetStats(scorerClientId, localPlayerIndex);
        stats.Kills++;
        Debug.Log($"[ScoreManager] Player {scorerClientId}_{localPlayerIndex} scored kill! Total kills: {stats.Kills}");

        // Check for maturity level upgrade (per-player, not global)
        var player = FindPlayerByClientAndIndex(scorerClientId, localPlayerIndex);
        if (player != null)
        {
            player.CheckMaturityUpgrade(stats.Kills);
        }

        // Fire events
        OnScoreChanged?.Invoke(scorerClientId, localPlayerIndex, stats.Kills);
        OnStatsChanged?.Invoke(scorerClientId, localPlayerIndex, stats);

        // Sync to all clients
        SyncScoreToClients(scorerClientId, localPlayerIndex, stats.Kills);
    }

    /// <summary>
    /// Record death for a player (ground crash or out of bounds). Call from server.
    /// </summary>
    public void AddDeath(ulong clientId, int localPlayerIndex = 0)
    {
        if (PlayerController.IsBotIdentity(clientId, localPlayerIndex)) return;   // the warm-up bot is not a player
        if (!NetworkManager.Singleton.IsServer) return;

        var stats = GetStats(clientId, localPlayerIndex);
        stats.Deaths++;
        Debug.Log($"[ScoreManager] Player {clientId}_{localPlayerIndex} died! Total deaths: {stats.Deaths}");

        OnStatsChanged?.Invoke(clientId, localPlayerIndex, stats);
        SyncStatsToClients(clientId, localPlayerIndex, stats);
    }

    /// <summary>
    /// Record plane collision for a player. Call from server.
    /// </summary>
    public void AddPlaneCollision(ulong clientId, int localPlayerIndex = 0)
    {
        if (PlayerController.IsBotIdentity(clientId, localPlayerIndex)) return;   // the warm-up bot is not a player
        if (!NetworkManager.Singleton.IsServer) return;

        var stats = GetStats(clientId, localPlayerIndex);
        stats.PlaneCollisions++;
        Debug.Log($"[ScoreManager] Player {clientId}_{localPlayerIndex} collided with plane! Total collisions: {stats.PlaneCollisions}");

        OnStatsChanged?.Invoke(clientId, localPlayerIndex, stats);
        SyncStatsToClients(clientId, localPlayerIndex, stats);
    }

    private void SyncScoreToClients(ulong scorerClientId, int localPlayerIndex, int newScore)
    {
        // Find any player to send RPC through
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            if (player.IsServer && !player.IsBot)
            {
                player.SyncScoreClientRpc(scorerClientId, localPlayerIndex, newScore);
                break;
            }
        }
    }

    private void SyncStatsToClients(ulong clientId, int localPlayerIndex, PlayerStats stats)
    {
        // Find any player to send RPC through
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            if (player.IsServer && !player.IsBot)
            {
                player.SyncStatsClientRpc(clientId, localPlayerIndex, stats.Kills, stats.Deaths, stats.PlaneCollisions);
                break;
            }
        }
    }

    /// <summary>
    /// Called by PlayerController ClientRpc to update score on clients
    /// </summary>
    public void UpdateScoreFromServer(ulong scorerClientId, int localPlayerIndex, int newScore)
    {
        var stats = GetStats(scorerClientId, localPlayerIndex);
        stats.Kills = newScore;
        OnScoreChanged?.Invoke(scorerClientId, localPlayerIndex, newScore);
    }

    /// <summary>
    /// Called by PlayerController ClientRpc to update all stats on clients
    /// </summary>
    public void UpdateStatsFromServer(ulong clientId, int localPlayerIndex, int kills, int deaths, int planeCollisions)
    {
        var stats = GetStats(clientId, localPlayerIndex);
        stats.Kills = kills;
        stats.Deaths = deaths;
        stats.PlaneCollisions = planeCollisions;
        OnStatsChanged?.Invoke(clientId, localPlayerIndex, stats);
    }

    /// <summary>
    /// Get score (kills) for a specific player
    /// </summary>
    public int GetScore(ulong clientId, int localPlayerIndex = 0)
    {
        return GetStats(clientId, localPlayerIndex).Kills;
    }

    /// <summary>
    /// Format match time as M:SS.d
    /// </summary>
    public string GetFormattedTime()
    {
        int minutes = Mathf.FloorToInt(MatchTime / 60f);
        int seconds = Mathf.FloorToInt(MatchTime % 60f);
        int tenths = Mathf.FloorToInt((MatchTime * 10f) % 10f);
        return $"{minutes}:{seconds:D2}.{tenths}";
    }

    /// <summary>
    /// Find a PlayerController by clientId and localPlayerIndex.
    /// </summary>
    private PlayerController FindPlayerByClientAndIndex(ulong clientId, int localPlayerIndex)
    {
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            if (player.OwnerClientId == clientId && player.LocalPlayerIndex == localPlayerIndex)
            {
                return player;
            }
            // For network players (non-local), localPlayerIndex is -1
            if (player.OwnerClientId == clientId && localPlayerIndex == 0 && player.LocalPlayerIndex == -1)
            {
                return player;
            }
        }
        return null;
    }

    /// <summary>
    /// Reset match (call from server only)
    /// </summary>
    public void ResetMatch()
    {
        _playerStats.Clear();
        MatchTime = 0f;
        MatchStarted = false;
    }

    /// <summary>
    /// Freeze the timer at round end but keep stats for the results screen.
    /// Called locally on every client by MatchManager.
    /// </summary>
    public void FreezeMatch()
    {
        MatchStarted = false;
    }

    /// <summary>
    /// Start a fresh round: reset stats + timer and sync to clients.
    /// Server-only (called by MatchManager after the intermission).
    /// </summary>
    public void RestartMatch()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        StartMatch();
    }

    /// <summary>
    /// Adopt the running match state WITHOUT clearing stats. Used by a late
    /// joiner receiving the server snapshot, and by the freeze-to-waiting
    /// transition when a battle loses its opponents.
    /// </summary>
    public void SetMatchStateFromServer(bool started, float matchTime)
    {
        MatchStarted = started;
        MatchTime = matchTime;
    }

    /// <summary>
    /// Server-only: push the complete score table + match state to one client
    /// (late-join snapshot). Routed through the given server-side player, same
    /// pattern as the broadcast sync methods, but targeted.
    /// </summary>
    public void SyncFullStateTo(PlayerController via, ulong targetClientId)
    {
        if (!NetworkManager.Singleton.IsServer || via == null) return;

        var target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } }
        };

        foreach (var entry in _playerStats)
        {
            ParsePlayerId(entry.Key, out ulong clientId, out int localPlayerIndex);
            via.SyncStatsClientRpc(clientId, localPlayerIndex,
                entry.Value.Kills, entry.Value.Deaths, entry.Value.PlaneCollisions, target);
        }

        via.SyncMatchStateClientRpc(MatchStarted, MatchTime, target);
    }

    /// <summary>
    /// Format remaining time until the round time limit as M:SS.
    /// </summary>
    public string GetFormattedRemainingTime(float timeLimit)
    {
        float remaining = Mathf.Max(0f, timeLimit - MatchTime);
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.CeilToInt(remaining % 60f);
        if (seconds == 60) { minutes++; seconds = 0; }
        return $"{minutes}:{seconds:D2}";
    }
}
