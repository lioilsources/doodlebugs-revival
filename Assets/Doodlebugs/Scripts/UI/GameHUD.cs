using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Game HUD displaying scores and speed bars for all players on the left side.
/// Supports dynamic player count (up to 4 players).
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

    [Header("Player Colors")]
    [SerializeField] private Color[] playerColors = new Color[]
    {
        new Color(0.3f, 0.5f, 1f),    // Blue (Player 1)
        new Color(1f, 0.3f, 0.3f),    // Red (Player 2)
        new Color(0.3f, 0.9f, 0.3f),  // Green (Player 3)
        new Color(1f, 0.8f, 0.2f)     // Yellow (Player 4)
    };

    // Player UI elements - dynamically created
    private class PlayerHUDEntry
    {
        public ulong clientId;
        public PlayerController player;
        public GameObject container;
        public Text scoreText;
        public Text statsText; // Ground crashes, plane collisions, out of bounds
        public Image speedBarFill;
        public Image speedBarBg;
        public Vector3 originalScoreScale;
        public Coroutine pulseCoroutine;
        public string lastKnownName; // Track name changes from NetworkVariable sync
    }

    private List<PlayerHUDEntry> _playerEntries = new List<PlayerHUDEntry>();
    private Dictionary<ulong, PlayerHUDEntry> _playerEntriesById = new Dictionary<ulong, PlayerHUDEntry>();

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
    }

    private void OnDisable()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
            ScoreManager.Instance.OnStatsChanged -= OnStatsChanged;
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

        foreach (var player in players)
        {
            if (!_playerEntriesById.ContainsKey(player.OwnerClientId))
            {
                // New player - create HUD entry
                CreatePlayerHUDEntry(player);
            }
            else
            {
                // Update player reference (in case of respawn)
                var entry = _playerEntriesById[player.OwnerClientId];
                entry.player = player;

                // Check if player name was updated (NetworkVariable sync may arrive after HUD creation)
                string currentName = player.PlayerName;
                if (!string.IsNullOrEmpty(currentName) && currentName != entry.lastKnownName)
                {
                    entry.lastKnownName = currentName;
                    // Update score text with new name, preserving the score
                    if (entry.scoreText != null)
                    {
                        string scoreText = entry.scoreText.text;
                        int colonIndex = scoreText.LastIndexOf(':');
                        string scorePart = colonIndex >= 0 ? scoreText.Substring(colonIndex) : ": 0";
                        entry.scoreText.text = $"{currentName}{scorePart}";
                    }
                }
            }
        }

        // Sort entries by client ID
        _playerEntries.Sort((a, b) => a.clientId.CompareTo(b.clientId));

        // Reorder UI elements
        for (int i = 0; i < _playerEntries.Count; i++)
        {
            _playerEntries[i].container.transform.SetSiblingIndex(i);
        }
    }

    private void CreatePlayerHUDEntry(PlayerController player)
    {
        var entry = new PlayerHUDEntry
        {
            clientId = player.OwnerClientId,
            player = player
        };

        int playerIndex = (int)player.OwnerClientId;
        Color playerColor = playerIndex < playerColors.Length ? playerColors[playerIndex] : Color.white;

        // Create container for this player's stats
        var container = new GameObject($"Player{playerIndex}Stats");
        container.transform.SetParent(playerStatsContainer, false);
        entry.container = container;

        var containerRect = container.AddComponent<RectTransform>();
        containerRect.sizeDelta = new Vector2(400, 170); // Increased for 2x larger fonts

        var containerLayout = container.AddComponent<VerticalLayoutGroup>();
        containerLayout.spacing = 1;
        containerLayout.childAlignment = TextAnchor.UpperRight;
        containerLayout.childControlWidth = true;
        containerLayout.childControlHeight = false;

        // Create score text (with player indicator)
        var scoreObj = new GameObject("Score");
        scoreObj.transform.SetParent(container.transform, false);

        var scoreRect = scoreObj.AddComponent<RectTransform>();
        scoreRect.sizeDelta = new Vector2(400, 80);

        entry.scoreText = scoreObj.AddComponent<Text>();
        // Use device name if available, fallback to P# format
        string displayName = !string.IsNullOrEmpty(player.PlayerName) ? player.PlayerName : $"P{playerIndex + 1}";
        entry.lastKnownName = displayName;
        entry.scoreText.text = $"{displayName}: 0";
        entry.scoreText.fontSize = 72; // Large - kills are the most important stat
        entry.scoreText.alignment = TextAnchor.MiddleRight;
        entry.scoreText.horizontalOverflow = HorizontalWrapMode.Overflow;
        entry.scoreText.verticalOverflow = VerticalWrapMode.Overflow;
        entry.scoreText.color = playerColor;
        entry.scoreText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        entry.originalScoreScale = entry.scoreText.transform.localScale;

        // Create stats text (crashes, collisions, out of bounds)
        var statsObj = new GameObject("Stats");
        statsObj.transform.SetParent(container.transform, false);

        var statsRect = statsObj.AddComponent<RectTransform>();
        statsRect.sizeDelta = new Vector2(400, 60);

        entry.statsText = statsObj.AddComponent<Text>();
        entry.statsText.text = "D:0 C:0";
        entry.statsText.fontSize = 52;
        entry.statsText.alignment = TextAnchor.MiddleRight;
        entry.statsText.horizontalOverflow = HorizontalWrapMode.Overflow;
        entry.statsText.verticalOverflow = VerticalWrapMode.Overflow;
        entry.statsText.color = new Color(playerColor.r, playerColor.g, playerColor.b, 0.7f);
        entry.statsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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
        _playerEntriesById[player.OwnerClientId] = entry;

        // Update legacy references for backward compatibility
        if (playerIndex == 0)
        {
            p1ScoreText = entry.scoreText;
            p1SpeedBarFill = entry.speedBarFill;
            p1SpeedBarBg = entry.speedBarBg;
        }
        else if (playerIndex == 1)
        {
            p2ScoreText = entry.scoreText;
            p2SpeedBarFill = entry.speedBarFill;
            p2SpeedBarBg = entry.speedBarBg;
        }

        Debug.Log($"[GameHUD] Created HUD entry for Player {playerIndex + 1} (ClientId: {player.OwnerClientId})");
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

    private void OnScoreChanged(ulong scorerClientId, int newScore)
    {
        UpdateScoreDisplay(scorerClientId, newScore);
        PlayScoreEffect(scorerClientId);
    }

    private void OnStatsChanged(ulong clientId, ScoreManager.PlayerStats stats)
    {
        UpdateStatsDisplay(clientId, stats);
    }

    private void UpdateScoreDisplay(ulong clientId, int score)
    {
        if (_playerEntriesById.TryGetValue(clientId, out var entry) && entry.scoreText != null)
        {
            // Use device name if available, fallback to P# format
            string displayName = entry.player != null && !string.IsNullOrEmpty(entry.player.PlayerName)
                ? entry.player.PlayerName
                : $"P{(int)clientId + 1}";
            entry.scoreText.text = $"{displayName}: {score}";
        }
    }

    private void UpdateStatsDisplay(ulong clientId, ScoreManager.PlayerStats stats)
    {
        if (_playerEntriesById.TryGetValue(clientId, out var entry) && entry.statsText != null)
        {
            // D = Deaths (crashes + out of bounds), C = Plane Collisions
            entry.statsText.text = $"D:{stats.Deaths} C:{stats.PlaneCollisions}";
        }
    }

    private void PlayScoreEffect(ulong clientId)
    {
        if (!_playerEntriesById.TryGetValue(clientId, out var entry)) return;
        if (entry.scoreText == null) return;

        // Stop any running pulse
        if (entry.pulseCoroutine != null)
            StopCoroutine(entry.pulseCoroutine);

        // Start pulse animation
        entry.pulseCoroutine = StartCoroutine(PulseAnimation(entry.scoreText.transform, entry.originalScoreScale));

        // Spawn floating +1 text
        int playerIndex = (int)clientId;
        SpawnFloatingText(entry.scoreText.transform.position, playerIndex);
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

    private void SpawnFloatingText(Vector3 position, int playerIndex)
    {
        if (floatingTextPrefab == null) return;

        // Spawn slightly above the score
        Vector3 spawnPos = position + Vector3.up * 30f;
        var floatingText = Instantiate(floatingTextPrefab, spawnPos, Quaternion.identity, transform);

        // Set color based on player
        var text = floatingText.GetComponent<Text>();
        if (text != null)
        {
            text.text = "+1";
            text.color = playerIndex < playerColors.Length ? playerColors[playerIndex] : Color.white;
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
