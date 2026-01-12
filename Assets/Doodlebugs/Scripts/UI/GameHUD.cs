using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Game HUD displaying scores and speed bars for all players.
/// Supports dynamic player count (up to 20 players).
/// Players sorted by performance: kills (desc) → deaths (asc) → collisions (asc).
/// Format: "deviceName#N#L K|D|C" for local players, "deviceName#L K|D|C" for remote.
/// L = maturity level: N=Novice (0-9 kills), A=Advanced (10-19), E=Expert (20+)
/// </summary>
public class GameHUD : MonoBehaviour
{
    [Header("Player Stats Container (Left Side)")]
    [SerializeField] private RectTransform playerStatsContainer;

    [Header("Match Timer")]
    [SerializeField] private Text matchTimeText;

    [Header("Score Effect")]
    [SerializeField] private float pulseDuration = 0.2f;
    [SerializeField] private float pulseScale = 1.3f;
    [SerializeField] private GameObject floatingTextPrefab;

    [Header("Speed Bar Settings")]
    [SerializeField] private float minSpeed = 2f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private Color engineOnColor = new Color(0.2f, 0.8f, 0.2f); // Green
    [SerializeField] private Color engineOffColor = new Color(0.5f, 0.5f, 0.5f); // Gray

    // Player colors now managed by PlayerColorManager

    // Player UI elements - dynamically created
    private class PlayerHUDEntry
    {
        public ulong clientId;
        public int localPlayerIndex;
        public string uniqueId; // "clientId_localPlayerIndex"
        public PlayerController player;
        public GameObject container;
        public Text scoreText;
        public Image speedBarFill;
        public Image speedBarBg;
        public Vector3 originalScoreScale;
        public Coroutine pulseCoroutine;
        public string lastKnownName; // Track name changes from NetworkVariable sync
        public bool isLocalPlayer;   // True for local co-op players on this machine
    }

    private List<PlayerHUDEntry> _playerEntries = new List<PlayerHUDEntry>();
    private Dictionary<string, PlayerHUDEntry> _playerEntriesById = new Dictionary<string, PlayerHUDEntry>();

    private string GetPlayerUniqueId(ulong clientId, int localPlayerIndex)
    {
        return $"{clientId}_{localPlayerIndex}";
    }

    private string GetPlayerUniqueId(PlayerController player)
    {
        int localIdx = player.LocalPlayerIndex >= 0 ? player.LocalPlayerIndex : 0;
        return GetPlayerUniqueId(player.OwnerClientId, localIdx);
    }

    // Legacy references for backward compatibility
    [HideInInspector] public Text p1ScoreText;
    [HideInInspector] public Image p1SpeedBarFill;
    [HideInInspector] public Image p1SpeedBarBg;
    [HideInInspector] public Text p2ScoreText;
    [HideInInspector] public Image p2SpeedBarFill;
    [HideInInspector] public Image p2SpeedBarBg;

    private void Start()
    {
        // Create player stats container if not assigned
        if (playerStatsContainer == null)
        {
            CreatePlayerStatsContainer();
        }

        // Initialize timer display
        if (matchTimeText != null)
            matchTimeText.text = "0:00.0";
    }

    private void OnEnable()
    {
        // Subscribe to score and stats changes
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged += OnScoreChanged;
            ScoreManager.Instance.OnStatsChanged += OnStatsChanged;
        }
        else
        {
            StartCoroutine(WaitForScoreManager());
        }

        // Subscribe to local player toggle events
        if (LocalPlayerManager.Instance != null)
        {
            LocalPlayerManager.Instance.OnPlayerToggled += OnLocalPlayerToggled;
        }
        else
        {
            StartCoroutine(WaitForLocalPlayerManager());
        }
    }

    private void OnDisable()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
            ScoreManager.Instance.OnStatsChanged -= OnStatsChanged;
        }

        if (LocalPlayerManager.Instance != null)
        {
            LocalPlayerManager.Instance.OnPlayerToggled -= OnLocalPlayerToggled;
        }
    }

    private IEnumerator WaitForScoreManager()
    {
        while (ScoreManager.Instance == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        ScoreManager.Instance.OnScoreChanged += OnScoreChanged;
        ScoreManager.Instance.OnStatsChanged += OnStatsChanged;
    }

    private IEnumerator WaitForLocalPlayerManager()
    {
        while (LocalPlayerManager.Instance == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        LocalPlayerManager.Instance.OnPlayerToggled += OnLocalPlayerToggled;
    }

    private void OnLocalPlayerToggled(int localPlayerIndex, bool enabled)
    {
        // Local players are owned by the local client (host)
        if (NetworkManager.Singleton == null) return;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        string uniqueId = GetPlayerUniqueId(localClientId, localPlayerIndex);

        if (!enabled)
        {
            // Player disabled - remove HUD entry immediately
            if (_playerEntriesById.TryGetValue(uniqueId, out var entry))
            {
                Debug.Log($"[GameHUD] Local player {localPlayerIndex + 1} disabled - removing HUD entry");

                if (entry.container != null)
                {
                    Destroy(entry.container);
                }

                _playerEntriesById.Remove(uniqueId);
                _playerEntries.Remove(entry);
            }
        }
        // If enabled, UpdatePlayerEntries() will create the entry when PlayerController spawns
    }

    private void Update()
    {
        // Find and register new players
        UpdatePlayerEntries();

        // Update all player speed bars
        UpdateAllSpeedBars();

        // Update timer
        UpdateTimer();
    }

    private void CreatePlayerStatsContainer()
    {
        var containerObj = new GameObject("PlayerStatsContainer");
        containerObj.transform.SetParent(transform, false);

        playerStatsContainer = containerObj.AddComponent<RectTransform>();
        // Anchor to top-right (avoids phone notch on left side)
        playerStatsContainer.anchorMin = new Vector2(1, 1);
        playerStatsContainer.anchorMax = new Vector2(1, 1);
        playerStatsContainer.pivot = new Vector2(1, 1);
        playerStatsContainer.anchoredPosition = new Vector2(-60, -20); // Extra right padding for rounded screen corners
        playerStatsContainer.sizeDelta = new Vector2(420, 700);

        // Add vertical layout group
        var layout = containerObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10;
        layout.childAlignment = TextAnchor.UpperRight;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private void UpdatePlayerEntries()
    {
        var players = FindObjectsOfType<PlayerController>();

        // Build set of active player IDs
        var activePlayerIds = new HashSet<string>();
        foreach (var player in players)
        {
            activePlayerIds.Add(GetPlayerUniqueId(player));
        }

        // Remove entries for disconnected players
        for (int i = _playerEntries.Count - 1; i >= 0; i--)
        {
            var entry = _playerEntries[i];
            if (!activePlayerIds.Contains(entry.uniqueId))
            {
                // Player disconnected - remove HUD entry
                Debug.Log($"[GameHUD] Removing HUD entry for disconnected player {entry.uniqueId}");

                if (entry.container != null)
                {
                    Destroy(entry.container);
                }

                _playerEntriesById.Remove(entry.uniqueId);
                _playerEntries.RemoveAt(i);
            }
        }

        // Add/update entries for active players
        foreach (var player in players)
        {
            string uniqueId = GetPlayerUniqueId(player);

            if (!_playerEntriesById.ContainsKey(uniqueId))
            {
                // New player - create HUD entry
                CreatePlayerHUDEntry(player);
            }
            else
            {
                // Update player reference (in case of respawn)
                var entry = _playerEntriesById[uniqueId];
                entry.player = player;

                // Check if player name was updated (NetworkVariable sync may arrive after HUD creation)
                string currentName = player.PlayerName;
                // For local players, strip existing " #N" or "#N" suffix
                if (entry.isLocalPlayer && !string.IsNullOrEmpty(currentName))
                {
                    currentName = System.Text.RegularExpressions.Regex.Replace(currentName, @"\s*#\d+$", "");
                }
                if (!string.IsNullOrEmpty(currentName) && currentName != entry.lastKnownName)
                {
                    entry.lastKnownName = currentName;
                    // Refresh display with new name
                    UpdatePlayerDisplay(entry.clientId, entry.localPlayerIndex);
                }
            }
        }

        // Sort entries by performance: kills (desc) → deaths (asc) → collisions (asc)
        _playerEntries.Sort((a, b) =>
        {
            var statsA = ScoreManager.Instance?.GetStats(a.clientId, a.localPlayerIndex);
            var statsB = ScoreManager.Instance?.GetStats(b.clientId, b.localPlayerIndex);

            // 1. Kills descending (more kills = higher rank)
            int cmp = (statsB?.Kills ?? 0).CompareTo(statsA?.Kills ?? 0);
            if (cmp != 0) return cmp;

            // 2. Deaths ascending (fewer deaths = higher rank)
            cmp = (statsA?.Deaths ?? 0).CompareTo(statsB?.Deaths ?? 0);
            if (cmp != 0) return cmp;

            // 3. PlaneCollisions ascending (fewer collisions = higher rank)
            return (statsA?.PlaneCollisions ?? 0).CompareTo(statsB?.PlaneCollisions ?? 0);
        });

        // Reorder UI elements
        for (int i = 0; i < _playerEntries.Count; i++)
        {
            _playerEntries[i].container.transform.SetSiblingIndex(i);
        }
    }

    private void CreatePlayerHUDEntry(PlayerController player)
    {
        int localIdx = player.LocalPlayerIndex >= 0 ? player.LocalPlayerIndex : 0;
        string uniqueId = GetPlayerUniqueId(player.OwnerClientId, localIdx);

        var entry = new PlayerHUDEntry
        {
            clientId = player.OwnerClientId,
            localPlayerIndex = localIdx,
            uniqueId = uniqueId,
            player = player,
            isLocalPlayer = player.IsLocalPlayer
        };

        // Get color from PlayerColorManager using clientId + localPlayerIndex
        Color playerColor;
        int colorIndex;
        if (PlayerColorManager.Instance != null)
        {
            colorIndex = PlayerColorManager.Instance.GetColorIndex(player.OwnerClientId, localIdx);
            playerColor = PlayerColorManager.PlayerColors[colorIndex];
        }
        else
        {
            colorIndex = (int)player.OwnerClientId + localIdx;
            playerColor = PlayerColorManager.GetColorByIndex(colorIndex);
        }

        // Create container for this player's stats
        var container = new GameObject($"Player_{uniqueId}_Stats");
        container.transform.SetParent(playerStatsContainer, false);
        entry.container = container;

        var containerRect = container.AddComponent<RectTransform>();
        containerRect.sizeDelta = new Vector2(400, 55); // Compact for more players

        var containerLayout = container.AddComponent<VerticalLayoutGroup>();
        containerLayout.spacing = 2;
        containerLayout.childAlignment = TextAnchor.UpperRight;
        containerLayout.childControlWidth = true;
        containerLayout.childControlHeight = false;

        // Create score text (with player indicator and all stats on one line)
        var scoreObj = new GameObject("Score");
        scoreObj.transform.SetParent(container.transform, false);

        var scoreRect = scoreObj.AddComponent<RectTransform>();
        scoreRect.sizeDelta = new Vector2(400, 36);

        entry.scoreText = scoreObj.AddComponent<Text>();
        // Use device name if available, fallback to P# format
        string baseName = !string.IsNullOrEmpty(player.PlayerName) ? player.PlayerName : $"P{colorIndex + 1}";
        // For local players, strip existing " #N" or "#N" suffix before adding our own
        if (player.IsLocalPlayer)
        {
            baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"\s*#\d+$", "");
        }
        entry.lastKnownName = baseName;
        // Format: "deviceName#N#L K|D|C" for local players, "deviceName#L K|D|C" for remote
        // L = maturity level (N=Novice, A=Advanced, E=Expert)
        string displayName = player.IsLocalPlayer
            ? $"{baseName}#{localIdx + 1}#N"
            : $"{baseName}#N";
        entry.scoreText.text = $"{displayName} 0|0|0";
        entry.scoreText.fontSize = 32; // Smaller font for compact display
        entry.scoreText.alignment = TextAnchor.MiddleRight;
        entry.scoreText.horizontalOverflow = HorizontalWrapMode.Overflow;
        entry.scoreText.verticalOverflow = VerticalWrapMode.Overflow;
        entry.scoreText.color = playerColor;
        entry.scoreText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        entry.originalScoreScale = entry.scoreText.transform.localScale;

        // Create speed bar background
        var speedBarBgObj = new GameObject("SpeedBarBg");
        speedBarBgObj.transform.SetParent(container.transform, false);

        var speedBarBgRect = speedBarBgObj.AddComponent<RectTransform>();
        speedBarBgRect.sizeDelta = new Vector2(400, 16);

        entry.speedBarBg = speedBarBgObj.AddComponent<Image>();
        entry.speedBarBg.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

        // Create speed bar fill
        var speedBarFillObj = new GameObject("SpeedBarFill");
        speedBarFillObj.transform.SetParent(speedBarBgObj.transform, false);

        var speedBarFillRect = speedBarFillObj.AddComponent<RectTransform>();
        speedBarFillRect.anchorMin = Vector2.zero;
        speedBarFillRect.anchorMax = Vector2.one;
        speedBarFillRect.offsetMin = new Vector2(2, 2);
        speedBarFillRect.offsetMax = new Vector2(-2, -2);

        entry.speedBarFill = speedBarFillObj.AddComponent<Image>();
        entry.speedBarFill.color = engineOnColor;

        // Store entry
        _playerEntries.Add(entry);
        _playerEntriesById[uniqueId] = entry;

        // Update legacy references for backward compatibility
        if (colorIndex == 0)
        {
            p1ScoreText = entry.scoreText;
            p1SpeedBarFill = entry.speedBarFill;
            p1SpeedBarBg = entry.speedBarBg;
        }
        else if (colorIndex == 1)
        {
            p2ScoreText = entry.scoreText;
            p2SpeedBarFill = entry.speedBarFill;
            p2SpeedBarBg = entry.speedBarBg;
        }

        Debug.Log($"[GameHUD] Created HUD entry for P{colorIndex + 1} (ClientId: {player.OwnerClientId}, LocalIdx: {localIdx})");
    }

    private void UpdateAllSpeedBars()
    {
        foreach (var entry in _playerEntries)
        {
            if (entry.player != null && entry.speedBarFill != null)
            {
                float speed = entry.player.Speed;
                float normalizedSpeed = Mathf.InverseLerp(minSpeed, maxSpeed, speed);
                UpdateSpeedBarFill(entry.speedBarFill, normalizedSpeed);
                entry.speedBarFill.color = entry.player.IsEngineOff ? engineOffColor : engineOnColor;
            }
            else if (entry.speedBarFill != null)
            {
                UpdateSpeedBarFill(entry.speedBarFill, 0f);
            }
        }
    }

    private void UpdateSpeedBarFill(Image fillImage, float normalizedValue)
    {
        var rect = fillImage.rectTransform;
        // Left-to-right fill for all players
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(normalizedValue, 1);
    }

    private void UpdateTimer()
    {
        if (matchTimeText != null && ScoreManager.Instance != null)
        {
            if (ScoreManager.Instance.MatchStarted)
            {
                matchTimeText.text = ScoreManager.Instance.GetFormattedTime();
            }
            else
            {
                matchTimeText.text = "0:00.0";
            }
        }
    }

    private void OnScoreChanged(ulong scorerClientId, int localPlayerIndex, int newScore)
    {
        UpdateScoreDisplay(scorerClientId, localPlayerIndex, newScore);
        PlayScoreEffect(scorerClientId, localPlayerIndex);
    }

    private void OnStatsChanged(ulong clientId, int localPlayerIndex, ScoreManager.PlayerStats stats)
    {
        UpdateStatsDisplay(clientId, localPlayerIndex, stats);
    }

    private void UpdateScoreDisplay(ulong clientId, int localPlayerIndex, int score)
    {
        // Update uses combined format - delegate to UpdatePlayerDisplay
        UpdatePlayerDisplay(clientId, localPlayerIndex);
    }

    private void UpdateStatsDisplay(ulong clientId, int localPlayerIndex, ScoreManager.PlayerStats stats)
    {
        // Update uses combined format - delegate to UpdatePlayerDisplay
        UpdatePlayerDisplay(clientId, localPlayerIndex);
    }

    private void UpdatePlayerDisplay(ulong clientId, int localPlayerIndex)
    {
        // Find the specific entry for this player
        string uniqueId = GetPlayerUniqueId(clientId, localPlayerIndex);
        if (_playerEntriesById.TryGetValue(uniqueId, out var entry) && entry.scoreText != null)
        {
            // Use stored base name (already stripped of #N suffix for local players)
            string baseName = entry.lastKnownName ?? $"P{localPlayerIndex + 1}";

            // Get full stats
            var stats = ScoreManager.Instance?.GetStats(clientId, localPlayerIndex);
            int kills = stats?.Kills ?? 0;
            int deaths = stats?.Deaths ?? 0;
            int collisions = stats?.PlaneCollisions ?? 0;

            // Get maturity level abbreviation based on kills
            string levelAbbr = GetMaturityAbbreviation(kills);

            // Format: "deviceName#N#L K|D|C" for local players, "deviceName#L K|D|C" for remote
            string displayName = entry.isLocalPlayer
                ? $"{baseName}#{localPlayerIndex + 1}#{levelAbbr}"
                : $"{baseName}#{levelAbbr}";

            entry.scoreText.text = $"{displayName} {kills}|{deaths}|{collisions}";
        }
    }

    /// <summary>
    /// Get maturity level abbreviation based on kills count.
    /// N = Novice (0-9), A = Advanced (10-19), E = Expert (20+)
    /// </summary>
    private string GetMaturityAbbreviation(int kills)
    {
        if (kills >= 20) return "E";
        if (kills >= 10) return "A";
        return "N";
    }

    private void PlayScoreEffect(ulong clientId, int localPlayerIndex)
    {
        // Find the specific entry for this player
        string uniqueId = GetPlayerUniqueId(clientId, localPlayerIndex);
        if (!_playerEntriesById.TryGetValue(uniqueId, out var entry)) return;
        if (entry.scoreText == null) return;

        // Stop any running pulse
        if (entry.pulseCoroutine != null)
            StopCoroutine(entry.pulseCoroutine);

        // Start pulse animation
        entry.pulseCoroutine = StartCoroutine(PulseAnimation(entry.scoreText.transform, entry.originalScoreScale));

        // Spawn floating +1 text with correct color
        SpawnFloatingText(entry.scoreText.transform.position, clientId, localPlayerIndex);
    }

    private IEnumerator PulseAnimation(Transform target, Vector3 originalScale)
    {
        float elapsed = 0f;
        float halfDuration = pulseDuration / 2f;

        // Scale up
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            target.localScale = Vector3.Lerp(originalScale, originalScale * pulseScale, t);
            yield return null;
        }

        // Scale down
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            target.localScale = Vector3.Lerp(originalScale * pulseScale, originalScale, t);
            yield return null;
        }

        target.localScale = originalScale;
    }

    private void SpawnFloatingText(Vector3 position, ulong clientId, int localPlayerIndex)
    {
        if (floatingTextPrefab == null) return;

        // Spawn slightly above the score
        Vector3 spawnPos = position + Vector3.up * 30f;
        var floatingText = Instantiate(floatingTextPrefab, spawnPos, Quaternion.identity, transform);

        // Set color based on player using PlayerColorManager
        var text = floatingText.GetComponent<Text>();
        if (text != null)
        {
            text.text = "+1";
            if (PlayerColorManager.Instance != null)
            {
                text.color = PlayerColorManager.Instance.GetColor(clientId, localPlayerIndex);
            }
            else
            {
                int fallbackIndex = (int)clientId + localPlayerIndex;
                text.color = PlayerColorManager.GetColorByIndex(fallbackIndex);
            }
        }

        // Animate and destroy
        StartCoroutine(FloatingTextAnimation(floatingText));
    }

    private IEnumerator FloatingTextAnimation(GameObject floatingText)
    {
        if (floatingText == null) yield break;

        var text = floatingText.GetComponent<Text>();
        var rectTransform = floatingText.GetComponent<RectTransform>();

        float duration = 0.8f;
        float elapsed = 0f;
        Vector3 startPos = rectTransform.anchoredPosition;
        Color startColor = text != null ? text.color : Color.white;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Move up
            rectTransform.anchoredPosition = startPos + Vector3.up * (50f * t);

            // Fade out
            if (text != null)
            {
                Color c = startColor;
                c.a = 1f - t;
                text.color = c;
            }

            yield return null;
        }

        Destroy(floatingText);
    }

    /// <summary>
    /// Create HUD UI programmatically. Call this from a setup script if prefab not available.
    /// </summary>
    public static GameHUD CreateHUD(Canvas canvas)
    {
        // Create HUD container
        var hudObj = new GameObject("GameHUD");
        hudObj.transform.SetParent(canvas.transform, false);
        var hudRect = hudObj.AddComponent<RectTransform>();
        hudRect.anchorMin = Vector2.zero;
        hudRect.anchorMax = Vector2.one;
        hudRect.offsetMin = Vector2.zero;
        hudRect.offsetMax = Vector2.zero;

        var hud = hudObj.AddComponent<GameHUD>();

        // Create Match Time (top-center)
        hud.matchTimeText = CreateText(hudObj.transform, "MatchTime", "0:00.0",
            new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40),
            36, TextAnchor.MiddleCenter, Color.white);

        // Create floating text prefab
        hud.floatingTextPrefab = CreateFloatingTextPrefab();

        // Player stats container will be created automatically in Start()

        return hud;
    }

    private static Text CreateText(Transform parent, string name, string text,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos,
        int fontSize, TextAnchor alignment, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(150, 60);

        var textComp = obj.AddComponent<Text>();
        textComp.text = text;
        textComp.fontSize = fontSize;
        textComp.alignment = alignment;
        textComp.color = color;
        textComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return textComp;
    }

    private static GameObject CreateFloatingTextPrefab()
    {
        var obj = new GameObject("FloatingText");
        obj.SetActive(false);

        var rect = obj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(100, 40);

        var text = obj.AddComponent<Text>();
        text.text = "+1";
        text.fontSize = 32;
        text.alignment = TextAnchor.MiddleCenter;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        obj.SetActive(true);
        return obj;
    }
}
