using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages player colors for up to 10 players (4 local + 4 remote + 2 extra).
/// Colors are assigned in order as players join.
/// </summary>
public class PlayerColorManager : MonoBehaviour
{
    public static PlayerColorManager Instance { get; private set; }

    // 10 distinct colors: pink, blue, purple, yellow, beige, red, brown, orange, green, turquoise
    public static readonly Color[] PlayerColors = new Color[]
    {
        new Color(1.0f, 0.4f, 0.7f),   // 0: Pink
        new Color(0.2f, 0.4f, 1.0f),   // 1: Blue
        new Color(0.6f, 0.2f, 0.8f),   // 2: Purple
        new Color(1.0f, 0.9f, 0.2f),   // 3: Yellow
        new Color(0.96f, 0.87f, 0.7f), // 4: Beige
        new Color(1.0f, 0.2f, 0.2f),   // 5: Red
        new Color(0.55f, 0.27f, 0.07f),// 6: Brown
        new Color(1.0f, 0.5f, 0.0f),   // 7: Orange
        new Color(0.2f, 0.8f, 0.2f),   // 8: Green
        new Color(0.0f, 0.8f, 0.8f)    // 9: Turquoise
    };

    public static readonly string[] ColorNames = new string[]
    {
        "Pink", "Blue", "Purple", "Yellow", "Beige",
        "Red", "Brown", "Orange", "Green", "Turquoise"
    };

    // Maps unique player ID to color index
    private Dictionary<string, int> playerColorAssignments = new Dictionary<string, int>();
    private int nextColorIndex = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Get or assign a color for a player. Uses unique ID combining clientId and localPlayerIndex.
    /// </summary>
    public int GetColorIndex(ulong clientId, int localPlayerIndex = 0)
    {
        string playerId = GetPlayerId(clientId, localPlayerIndex);

        if (playerColorAssignments.TryGetValue(playerId, out int existingIndex))
        {
            return existingIndex;
        }

        // Assign next available color
        int colorIndex = nextColorIndex % PlayerColors.Length;
        playerColorAssignments[playerId] = colorIndex;
        nextColorIndex++;

        Debug.Log($"[PlayerColorManager] Assigned {ColorNames[colorIndex]} to player (client={clientId}, local={localPlayerIndex})");
        return colorIndex;
    }

    /// <summary>
    /// Get color for a player by their IDs.
    /// </summary>
    public Color GetColor(ulong clientId, int localPlayerIndex = 0)
    {
        int index = GetColorIndex(clientId, localPlayerIndex);
        return PlayerColors[index];
    }

    /// <summary>
    /// Get color name for a player.
    /// </summary>
    public string GetColorName(ulong clientId, int localPlayerIndex = 0)
    {
        int index = GetColorIndex(clientId, localPlayerIndex);
        return ColorNames[index];
    }

    /// <summary>
    /// Release a player's color when they disconnect.
    /// </summary>
    public void ReleaseColor(ulong clientId, int localPlayerIndex = 0)
    {
        string playerId = GetPlayerId(clientId, localPlayerIndex);
        if (playerColorAssignments.Remove(playerId))
        {
            Debug.Log($"[PlayerColorManager] Released color for player (client={clientId}, local={localPlayerIndex})");
        }
    }

    /// <summary>
    /// Reset all color assignments (e.g., when starting a new game).
    /// </summary>
    public void ResetAllColors()
    {
        playerColorAssignments.Clear();
        nextColorIndex = 0;
        Debug.Log("[PlayerColorManager] Reset all color assignments");
    }

    private string GetPlayerId(ulong clientId, int localPlayerIndex)
    {
        return $"{clientId}_{localPlayerIndex}";
    }

    /// <summary>
    /// Get color by direct index (0-9).
    /// </summary>
    public static Color GetColorByIndex(int index)
    {
        return PlayerColors[index % PlayerColors.Length];
    }

    /// <summary>
    /// Get color name by direct index (0-9).
    /// </summary>
    public static string GetColorNameByIndex(int index)
    {
        return ColorNames[index % ColorNames.Length];
    }
}
