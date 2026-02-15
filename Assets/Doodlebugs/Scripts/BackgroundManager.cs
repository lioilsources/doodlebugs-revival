using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages dynamic background and foreground selection at game start.
/// Randomly selects a background profile and synchronizes across all clients.
/// </summary>
public class BackgroundManager : NetworkBehaviour
{
    public static BackgroundManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private SpriteRenderer backgroundRenderer;

    [Header("Available Backgrounds")]
    [SerializeField] private BackgroundProfile[] profiles;

    private int _currentBackgroundIndex = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            SelectRandomBackground();
        }
    }

    /// <summary>
    /// Selects a random background and syncs to all clients.
    /// Call this from server when starting a new game.
    /// </summary>
    public void SelectRandomBackground()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[BackgroundManager] SelectRandomBackground called on client - ignoring");
            return;
        }

        if (profiles == null || profiles.Length == 0)
        {
            Debug.LogWarning("[BackgroundManager] No background profiles configured");
            return;
        }

        int newIndex = Random.Range(0, profiles.Length);

        if (profiles.Length > 1 && newIndex == _currentBackgroundIndex)
        {
            newIndex = (newIndex + 1) % profiles.Length;
        }

        Debug.Log($"[BackgroundManager] Server selected background index: {newIndex}");
        SetBackgroundClientRpc(newIndex);
    }

    /// <summary>
    /// Manually set a specific background (for testing or UI selection).
    /// </summary>
    public void SetBackground(int index)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[BackgroundManager] SetBackground called on client - ignoring");
            return;
        }

        if (profiles == null || index < 0 || index >= profiles.Length)
        {
            Debug.LogWarning($"[BackgroundManager] Invalid background index: {index}");
            return;
        }

        SetBackgroundClientRpc(index);
    }

    [ClientRpc]
    private void SetBackgroundClientRpc(int index)
    {
        if (profiles == null || index < 0 || index >= profiles.Length)
        {
            Debug.LogWarning($"[BackgroundManager] Invalid background index received: {index}");
            return;
        }

        _currentBackgroundIndex = index;
        var profile = profiles[index];

        if (backgroundRenderer != null && profile.backgroundSprite != null)
        {
            backgroundRenderer.sprite = profile.backgroundSprite;
            Debug.Log($"[BackgroundManager] Background changed to: {profile.backgroundSprite.name}");
        }
        else
        {
            Debug.LogWarning("[BackgroundManager] No background renderer or sprite assigned");
        }

        if (ForegroundScroller.Instance != null)
        {
            ForegroundScroller.Instance.SetForeground(
                profile.foregroundSprite,
                profile.foregroundScrollSpeed,
                profile.foregroundYPosition,
                profile.foregroundScale
            );
        }
    }

    /// <summary>
    /// Gets the current background index.
    /// </summary>
    public int CurrentBackgroundIndex => _currentBackgroundIndex;

    /// <summary>
    /// Gets the total number of available backgrounds.
    /// </summary>
    public int BackgroundCount => profiles?.Length ?? 0;
}
