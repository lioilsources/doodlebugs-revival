using System.Collections.Generic;
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

    [Tooltip("Seconds between arena changes while the lobby is waiting and a " +
             "player may be out on the FLY warm-up. 0 disables the rotation. " +
             "Each change also re-rolls the ad wall.")]
    [SerializeField] private float warmUpRotateSeconds = 45f;

    [Tooltip("Ad-wall probability for a warm-up rotation. Higher than the " +
             "per-round one: the warm-up is where the walls are meant to be " +
             "seen, a battle wants them occasional.")]
    [Range(0f, 1f)]
    [SerializeField] private float warmUpAdStripChance = 0.6f;

    // Server-only deadline for the warm-up rotation; 0 = not armed yet.
    private float _warmUpRotateAt;

    // The host's arena playlist: profile indices, in the order the run cycles
    // through them. Absent = deselected. Server-write like every other synced
    // choice; the host edits it from the hangar.
    private readonly NetworkList<int> _sceneOrder = new();

    /// <summary>Read-only view for the hangar's scene picker.</summary>
    public NetworkList<int> SceneOrder => _sceneOrder;

    private int _sceneCursor = -1;

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
            if (_sceneOrder.Count == 0) ResetSceneOrderToDefault();
            SelectRandomBackground();
        }
    }

    /// <summary>Every map enabled, premium ones first - they are the reason
    /// someone paid, so they lead the rotation until the host says otherwise.</summary>
    private void ResetSceneOrderToDefault()
    {
        _sceneOrder.Clear();
        if (profiles == null) return;
        for (int i = 0; i < profiles.Length; i++)
        {
            if (profiles[i] != null && profiles[i].isPremium) _sceneOrder.Add(i);
        }
        for (int i = 0; i < profiles.Length; i++)
        {
            if (profiles[i] != null && !profiles[i].isPremium) _sceneOrder.Add(i);
        }
    }

    /// <summary>
    /// Host-only: replace the arena playlist. Indices not present are
    /// deselected. An empty list would leave the round with no arena at all,
    /// so it is refused rather than honoured.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestSetSceneOrderServerRpc(int[] order, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
        {
            Debug.LogWarning("[BackgroundManager] Scene playlist is the host's to set - ignoring client request");
            return;
        }
        if (order == null || order.Length == 0 || profiles == null) return;

        var seen = new HashSet<int>();
        var clean = new List<int>(order.Length);
        foreach (int i in order)
        {
            if (i >= 0 && i < profiles.Length && profiles[i] != null && seen.Add(i)) clean.Add(i);
        }
        if (clean.Count == 0) return;

        _sceneOrder.Clear();
        foreach (int i in clean) _sceneOrder.Add(i);

        _sceneCursor = clean.IndexOf(_backgroundIndex.Value);
        Debug.Log($"[BackgroundManager] Host set the arena playlist: {clean.Count} of {profiles.Length} maps");

        // Mid-battle the arena stays put - swapping the ground under a dogfight
        // would be worse than a stale map. Outside one, the edit has to be
        // visible immediately: the warm-up flight used to keep showing the
        // arena drawn at host start, from the DEFAULT list, so a host who
        // unticked it went on flying over it and the ordering did nothing
        // (nothing advances the playlist while the lobby waits).
        bool inBattle = MatchManager.Instance != null &&
                        MatchManager.Instance.Phase == MatchManager.GamePhase.Battle;
        if (inBattle) return;

        if (_sceneCursor < 0)
        {
            // The arena on screen was just dropped - start the new list at the top.
            _sceneCursor = -1;
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
    /// Advances to the next arena in the host's playlist and syncs it.
    /// Call this from the server when a round starts.
    ///
    /// This used to draw at random; it now walks the playlist in order,
    /// because the host's ordering IS the intended sequence for the run.
    /// The name is kept because four call sites use it and "the next arena"
    /// is what they all actually mean.
    /// </summary>
    public void SelectRandomBackground() => SelectRandomBackground(adStripChance);

    /// <summary>As above, with an explicit ad-wall probability.</summary>
    public void SelectRandomBackground(float adChance)
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

        if (_sceneOrder.Count == 0) ResetSceneOrderToDefault();

        int newIndex;
        if (_sceneOrder.Count > 0)
        {
            _sceneCursor = (_sceneCursor + 1) % _sceneOrder.Count;
            newIndex = _sceneOrder[_sceneCursor];
        }
        else
        {
            newIndex = Mathf.Max(0, _backgroundIndex.Value);
        }

        // Ad strip first: both indices trigger a foreground rebuild, and
        // setting this one last would make every round rebuild the terrain
        // twice.
        SelectRandomAdStrip(adChance);

        Debug.Log($"[BackgroundManager] Arena {_sceneCursor + 1}/{_sceneOrder.Count} -> profile index {newIndex}");
        _backgroundIndex.Value = newIndex;
    }

    /// <summary>
    /// Rolls for this round's advertising wall and syncs the result. Most
    /// rounds come back empty (-1) and the map keeps its own terrain; see
    /// adStripChance.
    /// </summary>
    public void SelectRandomAdStrip() => SelectRandomAdStrip(adStripChance);

    /// <summary>As above, with an explicit probability - the warm-up rotation
    /// wants the walls to actually turn up, a round wants them rare.</summary>
    public void SelectRandomAdStrip(float chance)
    {
        if (!IsServer)
        {
            return;
        }

        if (adStrips == null || adStrips.Length == 0 || Random.value > chance)
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
    /// Server: advance the warm-up arena on a timer while the lobby waits.
    /// One arena for the whole wait goes stale fast, and the FLY warm-up is
    /// the one place a player sits in the world with nothing else happening.
    /// Rotating through SelectRandomBackground means it walks the host's
    /// playlist and re-rolls the ad wall exactly like a round change does,
    /// and both indices are already synced, so every device follows.
    ///
    /// Called from MatchManager's server tick; harmless if the lobby is
    /// empty, and the Waiting hangar is opaque so a rebuild behind it is
    /// invisible anyway.
    /// </summary>
    public void ServerTickWarmUpRotation()
    {
        if (!IsServer || warmUpRotateSeconds <= 0f) return;

        _warmUpRotateAt = _warmUpRotateAt <= 0f
            ? Time.time + warmUpRotateSeconds
            : _warmUpRotateAt;

        if (Time.time < _warmUpRotateAt) return;

        _warmUpRotateAt = Time.time + warmUpRotateSeconds;
        SelectRandomBackground(warmUpAdStripChance);
    }

    /// <summary>Server: forget the warm-up timer, so a fresh wait gets a full
    /// interval rather than rotating the instant it starts.</summary>
    public void ServerResetWarmUpRotation() => _warmUpRotateAt = 0f;

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

    /// <summary>Profile at an index, for the hangar's scene picker.</summary>
    public BackgroundProfile GetProfile(int index) =>
        profiles != null && index >= 0 && index < profiles.Length ? profiles[index] : null;
}
