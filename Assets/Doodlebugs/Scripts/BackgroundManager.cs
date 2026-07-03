using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages dynamic background and foreground selection at game start.
/// Randomly selects a background profile and synchronizes across all clients
/// via NetworkVariable so late-joining clients also receive the selection.
/// </summary>
public class BackgroundManager : NetworkBehaviour
{
    public static BackgroundManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private SpriteRenderer backgroundRenderer;

    [Header("Available Backgrounds")]
    [SerializeField] private BackgroundProfile[] profiles;

    private NetworkVariable<int> _backgroundIndex = new NetworkVariable<int>(-1);

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

        _backgroundIndex.OnValueChanged += OnBackgroundIndexChanged;

        // Late-joining client: apply already-selected background
        if (_backgroundIndex.Value >= 0)
        {
            ApplyBackground(_backgroundIndex.Value);
        }

        if (IsServer)
        {
            SelectRandomBackground();
        }
    }

    public override void OnNetworkDespawn()
    {
        _backgroundIndex.OnValueChanged -= OnBackgroundIndexChanged;
        base.OnNetworkDespawn();
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

        if (profiles.Length > 1 && newIndex == _backgroundIndex.Value)
        {
            newIndex = (newIndex + 1) % profiles.Length;
        }

        Debug.Log($"[BackgroundManager] Server selected background index: {newIndex}");
        _backgroundIndex.Value = newIndex;
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

        _backgroundIndex.Value = index;
    }

    private void OnBackgroundIndexChanged(int oldValue, int newValue)
    {
        ApplyBackground(newValue);
    }

    private void ApplyBackground(int index)
    {
        if (profiles == null || index < 0 || index >= profiles.Length)
        {
            Debug.LogWarning($"[BackgroundManager] Invalid background index: {index}");
            return;
        }

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
                profile.foregroundBottomOffset,
                profile.foregroundScale
            );
        }
    }

    /// <summary>
    /// Gets the current background index.
    /// </summary>
    public int CurrentBackgroundIndex => _backgroundIndex.Value;

    /// <summary>
    /// Gets the total number of available backgrounds.
    /// </summary>
    public int BackgroundCount => profiles?.Length ?? 0;
}
