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
    [Tooltip("Ad-wall strips that rotate independently of the background. They " +
             "are the exception, not the rule: a round shows one only with " +
             "adStripChance probability, otherwise the map's own terrain wins. " +
             "A profile with no foreground of its own always gets an ad wall.")]
    [SerializeField] private Sprite[] adStrips;

    [Tooltip("Probability that a round replaces the map's terrain with an ad " +
             "wall. The ad art is a treat - at 1.0 it was the only foreground " +
             "anyone ever saw and the per-map terrain never showed at all.")]
    [Range(0f, 1f)]
    [SerializeField] private float adStripChance = 0.1f;

    private NetworkVariable<int> _backgroundIndex = new NetworkVariable<int>(-1);

    /// <summary>Replicated index of the round's background; -1 before the
    /// first selection. Cosmetic systems (cloud skins) key off it so every
    /// client derives the same look with no netcode of their own.</summary>
    public int BackgroundIndex => _backgroundIndex.Value;

    // Separate from the background index on purpose: which ad wall shows is
    // independent of which map is up. -1 means "no ad wall this round" - the
    // normal case now that every map has terrain of its own.
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
    /// Rolls for this round's advertising wall and syncs the result. Most
    /// rounds come back empty (-1) and the map keeps its own terrain; see
    /// adStripChance.
    /// </summary>
    public void SelectRandomAdStrip()
    {
        if (!IsServer)
        {
            return;
        }

        if (adStrips == null || adStrips.Length == 0 || Random.value > adStripChance)
        {
            _adStripIndex.Value = -1;
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
    /// The map's own terrain, or this round's ad wall when the roll came up
    /// for one. A profile with no foreground authored still falls back to an
    /// ad strip rather than the runtime placeholder silhouette.
    /// </summary>
    private Sprite ResolveForeground(BackgroundProfile profile)
    {
        int i = _adStripIndex.Value;
        bool adRoundWon = i >= 0 && adStrips != null && i < adStrips.Length && adStrips[i] != null;
        if (adRoundWon)
        {
            return adStrips[i];
        }

        if (profile.foregroundSprite != null)
        {
            return profile.foregroundSprite;
        }

        return adStrips != null && adStrips.Length > 0 ? adStrips[0] : null;
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
