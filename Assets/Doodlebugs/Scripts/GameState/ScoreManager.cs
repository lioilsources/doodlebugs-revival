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

    // Player stats dictionary (supports up to 4 players)
    private Dictionary<ulong, PlayerStats> _playerStats = new Dictionary<ulong, PlayerStats>();

    // Legacy score properties for backward compatibility
    public int Player1Score => GetStats(0).Kills;
    public int Player2Score => GetStats(1).Kills;

    // Match timer
    public float MatchTime { get; private set; }
    public bool MatchStarted { get; private set; }

    // Events for UI
    public event Action<ulong, int> OnScoreChanged; // (scorerClientId, newKills)
    public event Action<ulong, PlayerStats> OnStatsChanged; // (clientId, stats)
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

    private void Start()
    {
        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // Reset and start new match when second player connects (server only)
        if (NetworkManager.Singleton.IsServer &&
            NetworkManager.Singleton.ConnectedClients.Count >= 2)
        {
            // Always reset and start fresh match when 2 players are connected
            StartMatch();
        }
    }

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
    /// Get or create stats for a player
    /// </summary>
    public PlayerStats GetStats(ulong clientId)
    {
        if (!_playerStats.TryGetValue(clientId, out var stats))
        {
            stats = new PlayerStats();
            _playerStats[clientId] = stats;
        }
        return stats;
    }

    private void SyncMatchStartToClients()
    {
        // Find any player to send RPC through
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            if (player.IsServer)
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
    public void AddScore(ulong scorerClientId)
    {
        // Only server can add scores, but this method is called directly from Bullet
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[ScoreManager] AddScore called on client - ignored");
            return;
        }

        var stats = GetStats(scorerClientId);
        stats.Kills++;
        Debug.Log($"[ScoreManager] Player {scorerClientId} scored kill! Total kills: {stats.Kills}");

        // Fire events
        OnScoreChanged?.Invoke(scorerClientId, stats.Kills);
        OnStatsChanged?.Invoke(scorerClientId, stats);

        // Sync to all clients
        SyncScoreToClients(scorerClientId, stats.Kills);
    }

    /// <summary>
    /// Record death for a player (ground crash or out of bounds). Call from server.
    /// </summary>
    public void AddDeath(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        var stats = GetStats(clientId);
        stats.Deaths++;
        Debug.Log($"[ScoreManager] Player {clientId} died! Total deaths: {stats.Deaths}");

        OnStatsChanged?.Invoke(clientId, stats);
        SyncStatsToClients(clientId, stats);
    }

    /// <summary>
    /// Record plane collision for a player. Call from server.
    /// </summary>
    public void AddPlaneCollision(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        var stats = GetStats(clientId);
        stats.PlaneCollisions++;
        Debug.Log($"[ScoreManager] Player {clientId} collided with plane! Total collisions: {stats.PlaneCollisions}");

        OnStatsChanged?.Invoke(clientId, stats);
        SyncStatsToClients(clientId, stats);
    }

    private void SyncScoreToClients(ulong scorerClientId, int newScore)
    {
        // Find any player to send RPC through
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            if (player.IsServer)
            {
                player.SyncScoreClientRpc(scorerClientId, newScore);
                break;
            }
        }
    }

    private void SyncStatsToClients(ulong clientId, PlayerStats stats)
    {
        // Find any player to send RPC through
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            if (player.IsServer)
            {
                player.SyncStatsClientRpc(clientId, stats.Kills, stats.Deaths, stats.PlaneCollisions);
                break;
            }
        }
    }

    /// <summary>
    /// Called by PlayerController ClientRpc to update score on clients
    /// </summary>
    public void UpdateScoreFromServer(ulong scorerClientId, int newScore)
    {
        var stats = GetStats(scorerClientId);
        stats.Kills = newScore;
        OnScoreChanged?.Invoke(scorerClientId, newScore);
    }

    /// <summary>
    /// Called by PlayerController ClientRpc to update all stats on clients
    /// </summary>
    public void UpdateStatsFromServer(ulong clientId, int kills, int deaths, int planeCollisions)
    {
        var stats = GetStats(clientId);
        stats.Kills = kills;
        stats.Deaths = deaths;
        stats.PlaneCollisions = planeCollisions;
        OnStatsChanged?.Invoke(clientId, stats);
    }

    /// <summary>
    /// Get score (kills) for a specific client
    /// </summary>
    public int GetScore(ulong clientId)
    {
        return GetStats(clientId).Kills;
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
    /// Reset match (call from server only)
    /// </summary>
    public void ResetMatch()
    {
        _playerStats.Clear();
        MatchTime = 0f;
        MatchStarted = false;
    }
}
