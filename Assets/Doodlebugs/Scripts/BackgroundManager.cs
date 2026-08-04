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

    [Header("Advertising Foregrounds")]
    [Tooltip("Ad-wall strips that rotate independently of the background. When " +
             "any are assigned they replace the profile's own foreground, so a " +
             "new background needs no foreground authored for it.")]
    [SerializeField] private Sprite[] adStrips;

    private NetworkVariable<int> _backgroundIndex = new NetworkVariable<int>(-1);

    // Separate from the background index on purpose: the same map showing a
    // different wall of ads each round is most of where the variety comes from.
    private NetworkVariable<int> _adStripIndex = new NetworkVariable<int>(-1);

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
        _adStripIndex.OnValueChanged += OnAdStripIndexChanged;

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
        _adStripIndex.OnValueChanged -= OnAdStripIndexChanged;
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

        // Ad strip first: both indices trigger a foreground rebuild, and
        // setting this one last would make every round rebuild the terrain
        // twice.
        SelectRandomAdStrip();

        Debug.Log($"[BackgroundManager] Server selected background index: {newIndex}");
        _backgroundIndex.Value = newIndex;
    }

    /// <summary>
    /// Picks the advertising wall for this round and syncs it. Runs alongside
    /// the background selection but keeps its own index, so background and ads
    /// vary independently.
    /// </summary>
    public void SelectRandomAdStrip()
    {
        if (!IsServer || adStrips == null || adStrips.Length == 0)
        {
            return;
        }

        int newIndex = Random.Range(0, adStrips.Length);

        if (adStrips.Length > 1 && newIndex == _adStripIndex.Value)
        {
            newIndex = (newIndex + 1) % adStrips.Length;
        }

        Debug.Log($"[BackgroundManager] Server selected ad strip index: {newIndex}");
        _adStripIndex.Value = newIndex;
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

    private void OnAdStripIndexChanged(int oldValue, int newValue)
    {
        // Re-apply the current background so the foreground is rebuilt with the
        // new strip; the two indices can change in either order.
        if (_backgroundIndex.Value >= 0)
        {
            ApplyBackground(_backgroundIndex.Value);
        }
    }

    /// <summary>
    /// The ad wall to show, or the profile's own foreground when no ad strips
    /// are assigned (which is how the pre-ads maps keep working unchanged).
    /// </summary>
    private Sprite ResolveForeground(BackgroundProfile profile)
    {
        if (adStrips != null && adStrips.Length > 0)
        {
            int i = _adStripIndex.Value;
            if (i < 0 || i >= adStrips.Length)
            {
                i = 0;
            }
            if (adStrips[i] != null)
            {
                return adStrips[i];
            }
        }
        return profile.foregroundSprite;
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
                ResolveForeground(profile),
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
