using System;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Manages game scores and match timer.
/// Singleton that runs on all clients, server-authoritative for scoring.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    // Scores
    public int Player1Score { get; private set; }
    public int Player2Score { get; private set; }

    // Match timer
    public float MatchTime { get; private set; }
    public bool MatchStarted { get; private set; }

    // Events for UI
    public event Action<ulong, int> OnScoreChanged; // (scorerClientId, newScore)
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
        // Start match when second player connects (server only)
        if (NetworkManager.Singleton.IsServer &&
            NetworkManager.Singleton.ConnectedClients.Count >= 2 &&
            !MatchStarted)
        {
            StartMatch();
        }
    }

    private void StartMatch()
    {
        MatchStarted = true;
        MatchTime = 0f;
        Player1Score = 0;
        Player2Score = 0;

        Debug.Log("[ScoreManager] Match started!");
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
    /// Add score for a player. Call from server when bullet hits opponent.
    /// </summary>
    public void AddScore(ulong scorerClientId)
    {
        // Only server can add scores, but this method is called directly from Bullet
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[ScoreManager] AddScore called on client - ignored");
            return;
        }

        int newScore;
        if (scorerClientId == 0)
        {
            Player1Score++;
            newScore = Player1Score;
            Debug.Log($"[ScoreManager] Player 1 scored! New score: {Player1Score}");
        }
        else
        {
            Player2Score++;
            newScore = Player2Score;
            Debug.Log($"[ScoreManager] Player 2 scored! New score: {Player2Score}");
        }

        // Fire event locally first
        OnScoreChanged?.Invoke(scorerClientId, newScore);

        // Sync to all clients
        SyncScoreToClients(scorerClientId, newScore);
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

    /// <summary>
    /// Called by PlayerController ClientRpc to update score on clients
    /// </summary>
    public void UpdateScoreFromServer(ulong scorerClientId, int newScore)
    {
        if (scorerClientId == 0)
        {
            Player1Score = newScore;
        }
        else
        {
            Player2Score = newScore;
        }

        OnScoreChanged?.Invoke(scorerClientId, newScore);
    }

    /// <summary>
    /// Get score for a specific client
    /// </summary>
    public int GetScore(ulong clientId)
    {
        return clientId == 0 ? Player1Score : Player2Score;
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
        Player1Score = 0;
        Player2Score = 0;
        MatchTime = 0f;
        MatchStarted = false;
    }
}
