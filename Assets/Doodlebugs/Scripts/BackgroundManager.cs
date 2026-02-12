using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages dynamic background selection at game start.
/// Randomly selects a background and synchronizes across all clients.
/// </summary>
public class BackgroundManager : NetworkBehaviour
{
    public static BackgroundManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private SpriteRenderer backgroundRenderer;

    [Header("Available Backgrounds")]
    [SerializeField] private Sprite[] backgrounds;

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

        // Server selects random background when network starts
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

        if (backgrounds == null || backgrounds.Length == 0)
        {
            Debug.LogWarning("[BackgroundManager] No backgrounds configured");
            return;
        }

        int newIndex = Random.Range(0, backgrounds.Length);

        // Avoid selecting the same background twice in a row if possible
        if (backgrounds.Length > 1 && newIndex == _currentBackgroundIndex)
        {
            newIndex = (newIndex + 1) % backgrounds.Length;
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

        if (backgrounds == null || index < 0 || index >= backgrounds.Length)
        {
            Debug.LogWarning($"[BackgroundManager] Invalid background index: {index}");
            return;
        }

        SetBackgroundClientRpc(index);
    }

    [ClientRpc]
    private void SetBackgroundClientRpc(int index)
    {
        if (backgrounds == null || index < 0 || index >= backgrounds.Length)
        {
            Debug.LogWarning($"[BackgroundManager] Invalid background index received: {index}");
            return;
        }

        _currentBackgroundIndex = index;

        if (backgroundRenderer != null)
        {
            backgroundRenderer.sprite = backgrounds[index];
            Debug.Log($"[BackgroundManager] Background changed to: {backgrounds[index].name}");
        }
        else
        {
            Debug.LogWarning("[BackgroundManager] No background renderer assigned");
        }
    }

    /// <summary>
    /// Gets the current background index.
    /// </summary>
    public int CurrentBackgroundIndex => _currentBackgroundIndex;

    /// <summary>
    /// Gets the total number of available backgrounds.
    /// </summary>
    public int BackgroundCount => backgrounds?.Length ?? 0;
}
