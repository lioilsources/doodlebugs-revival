using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Game HUD: per-player panels (top-right), round timer (top-center) and the
/// round-over results overlay.
///
/// The local device's own planes get a DETAILED panel (segmented shield /
/// health / speed bars + active power-up chips). Remote opponents get a
/// COMPACT panel (name + score + tiny shield/health pips) to save screen
/// space on mobile.
///
/// Everything is built programmatically - no prefabs or scene wiring.
/// Uses the Press Start 2P pixel font from Resources/Fonts (OFL licensed).
/// </summary>
public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

    [Header("Player Stats Container (Right Side)")]
    [SerializeField] private RectTransform playerStatsContainer;

    [Header("Kill Feed (Top Left)")]
    [SerializeField] private RectTransform killFeedContainer;
    private const int KillFeedMaxLines = 4;
    private const float KillFeedLineSeconds = 4.5f;

    [Header("Match Timer")]
    [SerializeField] private Text matchTimeText;
    [SerializeField] private Text matchGoalText;

    [Header("Score Effect")]
    [SerializeField] private float pulseDuration = 0.2f;
    [SerializeField] private float pulseScale = 1.3f;
    [SerializeField] private GameObject floatingTextPrefab;

    [Header("Speed Bar Settings")]
    [SerializeField] private float minSpeed = 2f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private Color engineOnColor = new Color(0.35f, 0.95f, 0.35f);
    [SerializeField] private Color engineOffColor = new Color(0.55f, 0.55f, 0.55f);

    [Header("Stat Bar Colors")]
    [SerializeField] private Color shieldBarColor = new Color(0.30f, 0.50f, 1.00f);
    [SerializeField] private Color healthBarColor = new Color(0.95f, 0.25f, 0.25f);

    private static readonly Color SegmentOffColor = new Color(0.10f, 0.10f, 0.10f, 0.85f);
    private static readonly Color RowBgColor = new Color(0.04f, 0.04f, 0.04f, 0.55f);
    private static readonly Color TimerWarnColor = new Color(1f, 0.35f, 0.25f);

    private const int DetailedNameFontSize = 16;
    private const int CompactNameFontSize = 12;
    private const int ChipFontSize = 12;
    private const int SpeedSegments = 12;

    // --- pixel font -------------------------------------------------------

    private static Font _pixelFont;
    private static Font PixelFont
    {
        get
        {
            if (_pixelFont == null)
            {
                _pixelFont = Resources.Load<Font>("Fonts/PressStart2P");
                if (_pixelFont == null)
                {
                    _pixelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
            }
            return _pixelFont;
        }
    }

    private static Sprite _dmgSprite;
    private static Sprite _repairSprite;

    // --- per-player entry ---------------------------------------------------

    private class PlayerHUDEntry
    {
        public ulong clientId;
        public int localPlayerIndex;
        public string uniqueId;
        public PlayerController player;
        public GameObject container;
        public Text scoreText;
        public bool detailed;            // full panel (this device's plane)
        public bool isLocalPlayer;       // couch co-op pilot on this machine

        // Detailed panel parts (null on compact entries)
        public Image[] shieldSegments;
        public Image[] healthSegments;
        public Image[] speedSegments;
        public GameObject dmgChip;
        public Text dmgChipText;
        public GameObject repairChip;
        public Text repairChipText;

        // Compact panel parts (null on detailed entries)
        public Image[] shieldPips;
        public Image[] healthPips;

        public Vector3 originalScoreScale;
        public Coroutine pulseCoroutine;
        public string lastKnownName;
    }

    private readonly List<PlayerHUDEntry> _playerEntries = new List<PlayerHUDEntry>();
    private readonly Dictionary<string, PlayerHUDEntry> _playerEntriesById =
        new Dictionary<string, PlayerHUDEntry>();

    // --- results overlay ----------------------------------------------------

    private GameObject _resultsOverlay;
    private Text _resultsWinnerText;
    private Text _resultsStandingsText;
    private Text _resultsCountdownText;

    private string GetPlayerUniqueId(ulong clientId, int localPlayerIndex)
    {
        return $"{clientId}_{localPlayerIndex}";
    }

    private string GetPlayerUniqueId(PlayerController player)
    {
        int localIdx = player.LocalPlayerIndex >= 0 ? player.LocalPlayerIndex : 0;
        return GetPlayerUniqueId(player.OwnerClientId, localIdx);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (playerStatsContainer == null)
        {
            CreatePlayerStatsContainer();
        }

        if (killFeedContainer == null)
        {
            CreateKillFeedContainer();
        }

        if (matchTimeText != null)
            matchTimeText.text = "-:--";
    }

    private void OnEnable()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged += OnScoreChanged;
            ScoreManager.Instance.OnStatsChanged += OnStatsChanged;
            ScoreManager.Instance.OnMatchStarted += OnMatchStarted;
        }
        else
        {
            StartCoroutine(WaitForScoreManager());
        }

        if (LocalPlayerManager.Instance != null)
        {
            LocalPlayerManager.Instance.OnPlayerToggled += OnLocalPlayerToggled;
        }
        else
        {
            StartCoroutine(WaitForLocalPlayerManager());
        }

        StartCoroutine(WaitForMatchManager());
    }

    private void OnDisable()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
            ScoreManager.Instance.OnStatsChanged -= OnStatsChanged;
            ScoreManager.Instance.OnMatchStarted -= OnMatchStarted;
        }

        if (LocalPlayerManager.Instance != null)
        {
            LocalPlayerManager.Instance.OnPlayerToggled -= OnLocalPlayerToggled;
        }

        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.OnMatchEnded -= OnMatchEnded;
            MatchManager.Instance.OnIntermissionTick -= OnIntermissionTick;
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
        ScoreManager.Instance.OnMatchStarted += OnMatchStarted;
    }

    private IEnumerator WaitForLocalPlayerManager()
    {
        while (LocalPlayerManager.Instance == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        LocalPlayerManager.Instance.OnPlayerToggled += OnLocalPlayerToggled;
    }

    private IEnumerator WaitForMatchManager()
    {
        while (MatchManager.Instance == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        MatchManager.Instance.OnMatchEnded += OnMatchEnded;
        MatchManager.Instance.OnIntermissionTick += OnIntermissionTick;
    }

    private void OnLocalPlayerToggled(int localPlayerIndex, bool enabled)
    {
        if (NetworkManager.Singleton == null) return;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        string uniqueId = GetPlayerUniqueId(localClientId, localPlayerIndex);

        if (!enabled && _playerEntriesById.TryGetValue(uniqueId, out var entry))
        {
            if (entry.container != null)
            {
                Destroy(entry.container);
            }
            _playerEntriesById.Remove(uniqueId);
            _playerEntries.Remove(entry);
        }
    }

    private void Update()
    {
        UpdatePlayerEntries();
        UpdateAllBars();
        UpdateTimer();
    }

    // --- layout construction ------------------------------------------------

    private void CreatePlayerStatsContainer()
    {
        var containerObj = new GameObject("PlayerStatsContainer");
        containerObj.transform.SetParent(transform, false);

        playerStatsContainer = containerObj.AddComponent<RectTransform>();
        // Anchor to top-right (avoids phone notch on left side)
        playerStatsContainer.anchorMin = new Vector2(1, 1);
        playerStatsContainer.anchorMax = new Vector2(1, 1);
        playerStatsContainer.pivot = new Vector2(1, 1);
        playerStatsContainer.anchoredPosition = new Vector2(-60, -20);
        playerStatsContainer.sizeDelta = new Vector2(420, 700);

        var layout = containerObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.UpperRight;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private void UpdatePlayerEntries()
    {
        var players = FindObjectsOfType<PlayerController>();

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
                CreatePlayerHUDEntry(player);
            }
            else
            {
                var entry = _playerEntriesById[uniqueId];
                entry.player = player;

                // Ownership may resolve after spawn - rebuild with the right style
                bool mine = IsMine(player);
                if (mine != entry.detailed)
                {
                    if (entry.container != null) Destroy(entry.container);
                    _playerEntriesById.Remove(uniqueId);
                    _playerEntries.Remove(entry);
                    CreatePlayerHUDEntry(player);
                    continue;
                }

                // Name may sync late via NetworkVariable
                string currentName = player.PlayerName;
                if (entry.isLocalPlayer && !string.IsNullOrEmpty(currentName))
                {
                    currentName = System.Text.RegularExpressions.Regex.Replace(currentName, @"\s*#\d+$", "");
                }
                if (!string.IsNullOrEmpty(currentName) && currentName != entry.lastKnownName)
                {
                    entry.lastKnownName = currentName;
                    UpdatePlayerDisplay(entry.clientId, entry.localPlayerIndex);
                }
            }
        }

        // Own planes first, then rank by kills desc / deaths asc / collisions asc
        _playerEntries.Sort((a, b) =>
        {
            if (a.detailed != b.detailed) return a.detailed ? -1 : 1;

            var statsA = ScoreManager.Instance?.GetStats(a.clientId, a.localPlayerIndex);
            var statsB = ScoreManager.Instance?.GetStats(b.clientId, b.localPlayerIndex);

            int cmp = (statsB?.Kills ?? 0).CompareTo(statsA?.Kills ?? 0);
            if (cmp != 0) return cmp;

            cmp = (statsA?.Deaths ?? 0).CompareTo(statsB?.Deaths ?? 0);
            if (cmp != 0) return cmp;

            return (statsA?.PlaneCollisions ?? 0).CompareTo(statsB?.PlaneCollisions ?? 0);
        });

        for (int i = 0; i < _playerEntries.Count; i++)
        {
            _playerEntries[i].container.transform.SetSiblingIndex(i);
        }
    }

    /// <summary>Planes controlled from this device get the detailed panel.</summary>
    private static bool IsMine(PlayerController player)
    {
        return player.IsOwner;
    }

    private void CreatePlayerHUDEntry(PlayerController player)
    {
        int localIdx = player.LocalPlayerIndex >= 0 ? player.LocalPlayerIndex : 0;
        string uniqueId = GetPlayerUniqueId(player.OwnerClientId, localIdx);
        bool detailed = IsMine(player);

        var entry = new PlayerHUDEntry
        {
            clientId = player.OwnerClientId,
            localPlayerIndex = localIdx,
            uniqueId = uniqueId,
            player = player,
            detailed = detailed,
            isLocalPlayer = player.IsLocalPlayer
        };

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

        var container = new GameObject($"Player_{uniqueId}_Stats");
        container.transform.SetParent(playerStatsContainer, false);
        entry.container = container;

        var containerRect = container.AddComponent<RectTransform>();
        containerRect.sizeDelta = new Vector2(400, detailed ? 96 : 34);

        var containerLayout = container.AddComponent<VerticalLayoutGroup>();
        containerLayout.spacing = 3;
        containerLayout.childAlignment = TextAnchor.UpperRight;
        containerLayout.childControlWidth = true;
        containerLayout.childControlHeight = false;

        // --- name + score line ---
        string baseName = !string.IsNullOrEmpty(player.PlayerName) ? player.PlayerName : $"P{colorIndex + 1}";
        if (player.IsLocalPlayer)
        {
            baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"\s*#\d+$", "");
        }
        entry.lastKnownName = baseName;

        var scoreObj = new GameObject("Score");
        scoreObj.transform.SetParent(container.transform, false);
        var scoreRect = scoreObj.AddComponent<RectTransform>();
        scoreRect.sizeDelta = new Vector2(400, detailed ? 22 : 16);

        entry.scoreText = scoreObj.AddComponent<Text>();
        entry.scoreText.font = PixelFont;
        entry.scoreText.fontSize = detailed ? DetailedNameFontSize : CompactNameFontSize;
        entry.scoreText.alignment = TextAnchor.MiddleRight;
        entry.scoreText.horizontalOverflow = HorizontalWrapMode.Overflow;
        entry.scoreText.verticalOverflow = VerticalWrapMode.Overflow;
        entry.scoreText.color = playerColor;
        entry.originalScoreScale = entry.scoreText.transform.localScale;

        if (detailed)
        {
            // --- segmented bars: shield 3 / health 3 / speed 12 ---
            entry.shieldSegments = CreateSegmentRow(container.transform, "ShieldBar", 3, 12f);
            entry.healthSegments = CreateSegmentRow(container.transform, "HealthBar", 3, 12f);
            entry.speedSegments = CreateSegmentRow(container.transform, "SpeedBar", SpeedSegments, 8f);

            // --- active power-up chips row ---
            var chipsRow = new GameObject("PowerUpChips");
            chipsRow.transform.SetParent(container.transform, false);
            var chipsRect = chipsRow.AddComponent<RectTransform>();
            chipsRect.sizeDelta = new Vector2(400, 22);

            var chipsLayout = chipsRow.AddComponent<HorizontalLayoutGroup>();
            chipsLayout.spacing = 10;
            chipsLayout.childAlignment = TextAnchor.MiddleRight;
            chipsLayout.childControlWidth = false;
            chipsLayout.childControlHeight = false;
            chipsLayout.childForceExpandWidth = false;

            entry.repairChip = CreateChip(chipsRow.transform, "RepairChip", GetRepairSprite(),
                new Color(1f, 0.85f, 0.2f), out entry.repairChipText);
            entry.dmgChip = CreateChip(chipsRow.transform, "DmgChip", GetDmgSprite(),
                new Color(1f, 0.45f, 0.2f), out entry.dmgChipText);
        }
        else
        {
            // --- compact: one thin row with 3 shield + 3 health pips ---
            var pipsRow = new GameObject("Pips");
            pipsRow.transform.SetParent(container.transform, false);
            var pipsRect = pipsRow.AddComponent<RectTransform>();
            pipsRect.sizeDelta = new Vector2(400, 8);

            // right-aligned block, 6 pips of 24px + gaps
            entry.shieldPips = CreatePipGroup(pipsRow.transform, "ShieldPips", 3, 244f);
            entry.healthPips = CreatePipGroup(pipsRow.transform, "HealthPips", 3, 322f);
        }

        UpdateScoreLine(entry, ScoreManager.Instance?.GetStats(entry.clientId, entry.localPlayerIndex));

        _playerEntries.Add(entry);
        _playerEntriesById[uniqueId] = entry;
    }

    /// <summary>A full-width row of evenly spaced segments (Quake-style blocks).</summary>
    private Image[] CreateSegmentRow(Transform parent, string name, int count, float height)
    {
        var rowObj = new GameObject(name);
        rowObj.transform.SetParent(parent, false);
        var rowRect = rowObj.AddComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(400, height);

        var bg = rowObj.AddComponent<Image>();
        bg.color = RowBgColor;

        var segments = new Image[count];
        const float gap = 0.008f; // relative gap between segments
        for (int i = 0; i < count; i++)
        {
            var segObj = new GameObject($"Seg{i}");
            segObj.transform.SetParent(rowObj.transform, false);
            var segRect = segObj.AddComponent<RectTransform>();
            segRect.anchorMin = new Vector2((float)i / count + gap, 0.15f);
            segRect.anchorMax = new Vector2((float)(i + 1) / count - gap, 0.85f);
            segRect.offsetMin = Vector2.zero;
            segRect.offsetMax = Vector2.zero;

            segments[i] = segObj.AddComponent<Image>();
            segments[i].color = SegmentOffColor;
        }
        return segments;
    }

    /// <summary>Small pip group for compact opponent rows, at a fixed x offset.</summary>
    private Image[] CreatePipGroup(Transform parent, string name, int count, float xOffset)
    {
        const float pipWidth = 22f;
        const float pipGap = 4f;

        var pips = new Image[count];
        for (int i = 0; i < count; i++)
        {
            var pipObj = new GameObject($"{name}{i}");
            pipObj.transform.SetParent(parent, false);
            var rect = pipObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 0.5f);
            rect.sizeDelta = new Vector2(pipWidth, 0);
            rect.anchoredPosition = new Vector2(xOffset + i * (pipWidth + pipGap), 0);

            pips[i] = pipObj.AddComponent<Image>();
            pips[i].color = SegmentOffColor;
        }
        return pips;
    }

    /// <summary>Icon + short text chip for an active power-up effect.</summary>
    private GameObject CreateChip(Transform parent, string name, Sprite icon, Color textColor, out Text chipText)
    {
        var chipObj = new GameObject(name);
        chipObj.transform.SetParent(parent, false);
        var chipRect = chipObj.AddComponent<RectTransform>();
        chipRect.sizeDelta = new Vector2(120, 22);

        var iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(chipObj.transform, false);
        var iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0, 0.5f);
        iconRect.anchorMax = new Vector2(0, 0.5f);
        iconRect.pivot = new Vector2(0, 0.5f);
        iconRect.sizeDelta = new Vector2(20, 20);
        iconRect.anchoredPosition = Vector2.zero;

        var iconImage = iconObj.AddComponent<Image>();
        if (icon != null) iconImage.sprite = icon;
        iconImage.preserveAspect = true;

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(chipObj.transform, false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.offsetMin = new Vector2(24, 0);
        textRect.offsetMax = Vector2.zero;

        chipText = textObj.AddComponent<Text>();
        chipText.font = PixelFont;
        chipText.fontSize = ChipFontSize;
        chipText.alignment = TextAnchor.MiddleLeft;
        chipText.horizontalOverflow = HorizontalWrapMode.Overflow;
        chipText.color = textColor;

        chipObj.SetActive(false);
        return chipObj;
    }

    private static Sprite GetDmgSprite()
    {
        if (_dmgSprite == null)
            _dmgSprite = Resources.Load<Sprite>("Sprites/PowerUps/powerup_damage");
        return _dmgSprite;
    }

    private static Sprite GetRepairSprite()
    {
        if (_repairSprite == null)
            _repairSprite = Resources.Load<Sprite>("Sprites/PowerUps/powerup_repair");
        return _repairSprite;
    }

    // --- kill feed ------------------------------------------------------------

    private void CreateKillFeedContainer()
    {
        var feedObj = new GameObject("KillFeed");
        feedObj.transform.SetParent(transform, false);

        killFeedContainer = feedObj.AddComponent<RectTransform>();
        // Top-left, offset right to clear phone notches
        killFeedContainer.anchorMin = new Vector2(0, 1);
        killFeedContainer.anchorMax = new Vector2(0, 1);
        killFeedContainer.pivot = new Vector2(0, 1);
        killFeedContainer.anchoredPosition = new Vector2(60, -20);
        killFeedContainer.sizeDelta = new Vector2(600, 140);

        var layout = feedObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    /// <summary>
    /// Add a kill-feed line. Called on every client via PlayerController RPC.
    /// </summary>
    public void PushKillFeed(ulong victimClientId, int victimLocalIdx,
        ulong killerClientId, int killerLocalIdx, bool hasKiller, KillCause cause)
    {
        if (killFeedContainer == null) return;

        string victim = ColoredName(victimClientId, victimLocalIdx);
        string line;
        switch (cause)
        {
            case KillCause.Shot when hasKiller:
                line = $"{ColoredName(killerClientId, killerLocalIdx)} > {victim}";
                break;
            case KillCause.Collision when hasKiller:
                line = $"{ColoredName(killerClientId, killerLocalIdx)} >< {victim}";
                break;
            case KillCause.Collision:
                line = $"{victim} MIDAIR CRASH";
                break;
            case KillCause.Ground:
                line = $"{victim} CRASHED";
                break;
            case KillCause.OutOfBounds:
                line = $"{victim} LOST";
                break;
            default:
                line = $"{victim} DOWN";
                break;
        }

        // Cap the number of visible lines - drop the oldest
        // (detach first: Destroy is deferred, childCount wouldn't shrink this frame)
        while (killFeedContainer.childCount >= KillFeedMaxLines)
        {
            var oldest = killFeedContainer.GetChild(0);
            oldest.SetParent(null);
            Destroy(oldest.gameObject);
        }

        var lineObj = new GameObject("FeedLine");
        lineObj.transform.SetParent(killFeedContainer, false);
        var lineRect = lineObj.AddComponent<RectTransform>();
        lineRect.sizeDelta = new Vector2(600, 18);

        var text = lineObj.AddComponent<Text>();
        text.font = PixelFont;
        text.fontSize = 12;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.color = Color.white;
        text.text = line;

        StartCoroutine(FadeAndRemoveFeedLine(text));
    }

    private IEnumerator FadeAndRemoveFeedLine(Text text)
    {
        yield return new WaitForSeconds(KillFeedLineSeconds);

        const float fadeTime = 0.6f;
        float elapsed = 0f;
        while (elapsed < fadeTime && text != null)
        {
            elapsed += Time.deltaTime;
            var c = text.color;
            c.a = 1f - elapsed / fadeTime;
            text.color = c;
            yield return null;
        }
        if (text != null) Destroy(text.gameObject);
    }

    private string ColoredName(ulong clientId, int localPlayerIndex)
    {
        string name = ResolvePlayerName(clientId, localPlayerIndex);
        Color color = PlayerColorManager.Instance != null
            ? PlayerColorManager.Instance.GetColor(clientId, localPlayerIndex)
            : PlayerColorManager.GetColorByIndex((int)clientId + localPlayerIndex);
        return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{name}</color>";
    }

    private string ResolvePlayerName(ulong clientId, int localPlayerIndex)
    {
        string uniqueId = GetPlayerUniqueId(clientId, localPlayerIndex);
        if (_playerEntriesById.TryGetValue(uniqueId, out var entry) &&
            !string.IsNullOrEmpty(entry.lastKnownName))
        {
            return entry.lastKnownName;
        }
        return $"P{(int)clientId + localPlayerIndex + 1}";
    }

    // --- per-frame updates ---------------------------------------------------

    private void UpdateAllBars()
    {
        foreach (var entry in _playerEntries)
        {
            var stats = entry.player != null ? entry.player.PlaneStats : null;

            if (entry.detailed)
            {
                UpdateSegments(entry.shieldSegments, stats?.Shield ?? 0, shieldBarColor);
                UpdateSegments(entry.healthSegments, stats?.Health ?? 0, healthBarColor);
                UpdateSpeedSegments(entry);
                UpdateChips(entry, stats);
            }
            else
            {
                UpdateSegments(entry.shieldPips, stats?.Shield ?? 0, shieldBarColor);
                UpdateSegments(entry.healthPips, stats?.Health ?? 0, healthBarColor);
            }
        }
    }

    private static void UpdateSegments(Image[] segments, int value, Color onColor)
    {
        if (segments == null) return;
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            segments[i].color = i < value ? onColor : SegmentOffColor;
        }
    }

    private void UpdateSpeedSegments(Image[] segments, float normalized, Color onColor)
    {
        if (segments == null) return;
        float scaled = normalized * segments.Length;
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            float fill = Mathf.Clamp01(scaled - i);
            if (fill >= 1f) segments[i].color = onColor;
            else if (fill > 0f)
            {
                // partial segment - dimmed
                var c = Color.Lerp(SegmentOffColor, onColor, fill);
                segments[i].color = c;
            }
            else segments[i].color = SegmentOffColor;
        }
    }

    private void UpdateSpeedSegments(PlayerHUDEntry entry)
    {
        if (entry.speedSegments == null) return;

        float normalized = 0f;
        Color color = engineOffColor;
        if (entry.player != null)
        {
            normalized = Mathf.InverseLerp(minSpeed, maxSpeed, entry.player.Speed);
            color = entry.player.IsEngineOff ? engineOffColor : engineOnColor;
        }
        UpdateSpeedSegments(entry.speedSegments, normalized, color);
    }

    private void UpdateChips(PlayerHUDEntry entry, PlaneStats stats)
    {
        if (stats == null)
        {
            entry.dmgChip?.SetActive(false);
            entry.repairChip?.SetActive(false);
            return;
        }

        // Damage boost: icon + multiplier + remaining seconds
        bool dmgActive = stats.DamageMultiplier > 1.05f;
        if (entry.dmgChip != null && entry.dmgChip.activeSelf != dmgActive)
            entry.dmgChip.SetActive(dmgActive);
        if (dmgActive && entry.dmgChipText != null)
        {
            int secs = Mathf.CeilToInt(stats.DamageBoostTimeRemaining);
            entry.dmgChipText.text = secs > 0
                ? $"x{stats.DamageMultiplier:F1} {secs}s"
                : $"x{stats.DamageMultiplier:F1}";
        }

        // Degraded handling: wrench + percentage
        bool repairNeeded = stats.Handling < 0.95f;
        if (entry.repairChip != null && entry.repairChip.activeSelf != repairNeeded)
            entry.repairChip.SetActive(repairNeeded);
        if (repairNeeded && entry.repairChipText != null)
        {
            entry.repairChipText.text = $"{Mathf.RoundToInt(stats.Handling * 100f)}%";
        }
    }

    private void UpdateTimer()
    {
        if (matchTimeText == null || ScoreManager.Instance == null) return;

        if (MatchManager.Instance != null && MatchManager.Instance.IsIntermission)
        {
            matchTimeText.text = "0:00";
            matchTimeText.color = TimerWarnColor;
            return;
        }

        if (ScoreManager.Instance.MatchStarted)
        {
            float remaining = MatchManager.TimeLimitSeconds - ScoreManager.Instance.MatchTime;
            matchTimeText.text = ScoreManager.Instance.GetFormattedRemainingTime(MatchManager.TimeLimitSeconds);
            matchTimeText.color = remaining <= 30f ? TimerWarnColor : Color.white;
        }
        else
        {
            matchTimeText.text = "-:--";
            matchTimeText.color = Color.white;
        }
    }

    // --- score events ---------------------------------------------------------

    private void OnScoreChanged(ulong scorerClientId, int localPlayerIndex, int newScore)
    {
        UpdatePlayerDisplay(scorerClientId, localPlayerIndex);
        PlayScoreEffect(scorerClientId, localPlayerIndex);
    }

    private void OnStatsChanged(ulong clientId, int localPlayerIndex, ScoreManager.PlayerStats stats)
    {
        UpdatePlayerDisplay(clientId, localPlayerIndex);
    }

    private void UpdatePlayerDisplay(ulong clientId, int localPlayerIndex)
    {
        string uniqueId = GetPlayerUniqueId(clientId, localPlayerIndex);
        if (_playerEntriesById.TryGetValue(uniqueId, out var entry) && entry.scoreText != null)
        {
            UpdateScoreLine(entry, ScoreManager.Instance?.GetStats(clientId, localPlayerIndex));
        }
    }

    private void UpdateScoreLine(PlayerHUDEntry entry, ScoreManager.PlayerStats stats)
    {
        string baseName = entry.lastKnownName ?? $"P{entry.localPlayerIndex + 1}";
        int kills = stats?.Kills ?? 0;
        int deaths = stats?.Deaths ?? 0;
        int collisions = stats?.PlaneCollisions ?? 0;
        string levelAbbr = GetMaturityAbbreviation(kills);

        // "deviceName#N#L K|D|C" for local co-op pilots, "deviceName#L K|D|C" for remote
        string displayName = entry.isLocalPlayer
            ? $"{baseName}#{entry.localPlayerIndex + 1}#{levelAbbr}"
            : $"{baseName}#{levelAbbr}";

        entry.scoreText.text = $"{displayName} {kills}|{deaths}|{collisions}";
    }

    /// <summary>
    /// Maturity level abbreviation: N = Novice (0-9), A = Advanced (10-19), E = Expert (20+)
    /// </summary>
    private string GetMaturityAbbreviation(int kills)
    {
        if (kills >= 20) return "E";
        if (kills >= 10) return "A";
        return "N";
    }

    private void PlayScoreEffect(ulong clientId, int localPlayerIndex)
    {
        string uniqueId = GetPlayerUniqueId(clientId, localPlayerIndex);
        if (!_playerEntriesById.TryGetValue(uniqueId, out var entry)) return;
        if (entry.scoreText == null) return;

        if (entry.pulseCoroutine != null)
            StopCoroutine(entry.pulseCoroutine);

        entry.pulseCoroutine = StartCoroutine(PulseAnimation(entry.scoreText.transform, entry.originalScoreScale));

        SpawnFloatingText(entry.scoreText.transform.position, clientId, localPlayerIndex);
    }

    private IEnumerator PulseAnimation(Transform target, Vector3 originalScale)
    {
        float elapsed = 0f;
        float halfDuration = pulseDuration / 2f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            target.localScale = Vector3.Lerp(originalScale, originalScale * pulseScale, t);
            yield return null;
        }

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

        Vector3 spawnPos = position + Vector3.up * 30f;
        var floatingText = Instantiate(floatingTextPrefab, spawnPos, Quaternion.identity, transform);

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

            rectTransform.anchoredPosition = startPos + Vector3.up * (50f * t);

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

    // --- round results overlay -------------------------------------------------

    private void OnMatchEnded(MatchManager.MatchResult result)
    {
        EnsureResultsOverlay();

        // Winner name + color
        string winnerName = "P?";
        string winnerId = GetPlayerUniqueId(result.WinnerClientId, result.WinnerLocalPlayerIndex);
        if (_playerEntriesById.TryGetValue(winnerId, out var winnerEntry) && winnerEntry.lastKnownName != null)
        {
            winnerName = winnerEntry.lastKnownName;
        }

        Color winnerColor = PlayerColorManager.Instance != null
            ? PlayerColorManager.Instance.GetColor(result.WinnerClientId, result.WinnerLocalPlayerIndex)
            : PlayerColorManager.GetColorByIndex((int)result.WinnerClientId + result.WinnerLocalPlayerIndex);

        _resultsWinnerText.text = $"WINNER: {winnerName} ({result.WinnerKills})";
        _resultsWinnerText.color = winnerColor;

        // Standings from current HUD ranking (already sorted by performance)
        var lines = new List<string>();
        var ranked = new List<PlayerHUDEntry>(_playerEntries);
        ranked.Sort((a, b) =>
        {
            var sa = ScoreManager.Instance?.GetStats(a.clientId, a.localPlayerIndex);
            var sb = ScoreManager.Instance?.GetStats(b.clientId, b.localPlayerIndex);
            int cmp = (sb?.Kills ?? 0).CompareTo(sa?.Kills ?? 0);
            if (cmp != 0) return cmp;
            cmp = (sa?.Deaths ?? 0).CompareTo(sb?.Deaths ?? 0);
            if (cmp != 0) return cmp;
            return (sa?.PlaneCollisions ?? 0).CompareTo(sb?.PlaneCollisions ?? 0);
        });
        for (int i = 0; i < ranked.Count && i < 8; i++)
        {
            var stats = ScoreManager.Instance?.GetStats(ranked[i].clientId, ranked[i].localPlayerIndex);
            lines.Add($"{i + 1}. {ranked[i].lastKnownName}  {stats?.Kills ?? 0}|{stats?.Deaths ?? 0}|{stats?.PlaneCollisions ?? 0}");
        }
        _resultsStandingsText.text = string.Join("\n", lines);

        _resultsCountdownText.text = $"NEXT ROUND IN {MatchManager.IntermissionSeconds}";
        _resultsOverlay.SetActive(true);
    }

    private void OnIntermissionTick(int secondsLeft)
    {
        if (_resultsCountdownText != null)
        {
            _resultsCountdownText.text = $"NEXT ROUND IN {secondsLeft}";
        }
    }

    private void OnMatchStarted()
    {
        if (_resultsOverlay != null)
        {
            _resultsOverlay.SetActive(false);
        }
    }

    private void EnsureResultsOverlay()
    {
        if (_resultsOverlay != null) return;

        _resultsOverlay = new GameObject("ResultsOverlay");
        _resultsOverlay.transform.SetParent(transform, false);

        var overlayRect = _resultsOverlay.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        var bg = _resultsOverlay.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.75f);

        CreateOverlayText("Title", "ROUND OVER", 32, new Vector2(0, 140), Color.white);
        _resultsWinnerText = CreateOverlayText("Winner", "", 22, new Vector2(0, 80), Color.white);
        _resultsStandingsText = CreateOverlayText("Standings", "", 14, new Vector2(0, -20), Color.white);
        _resultsStandingsText.alignment = TextAnchor.UpperCenter;
        var standingsRect = _resultsStandingsText.GetComponent<RectTransform>();
        standingsRect.sizeDelta = new Vector2(900, 220);
        _resultsCountdownText = CreateOverlayText("Countdown", "", 18, new Vector2(0, -160), new Color(1f, 0.85f, 0.3f));

        _resultsOverlay.SetActive(false);
    }

    private Text CreateOverlayText(string name, string content, int fontSize, Vector2 anchoredPos, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(_resultsOverlay.transform, false);

        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(900, 60);

        var text = obj.AddComponent<Text>();
        text.text = content;
        text.font = PixelFont;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;

        return text;
    }

    // --- static factory ----------------------------------------------------------

    /// <summary>
    /// Create HUD UI programmatically. Called by GameSetup after scene load.
    /// </summary>
    public static GameHUD CreateHUD(Canvas canvas)
    {
        var hudObj = new GameObject("GameHUD");
        hudObj.transform.SetParent(canvas.transform, false);
        var hudRect = hudObj.AddComponent<RectTransform>();
        hudRect.anchorMin = Vector2.zero;
        hudRect.anchorMax = Vector2.one;
        hudRect.offsetMin = Vector2.zero;
        hudRect.offsetMax = Vector2.zero;

        var hud = hudObj.AddComponent<GameHUD>();

        // Match timer (top-center) + round goal below it
        hud.matchTimeText = CreateStaticText(hudObj.transform, "MatchTime", "-:--",
            new Vector2(0.5f, 1), new Vector2(0, -40), 28, Color.white);
        hud.matchGoalText = CreateStaticText(hudObj.transform, "MatchGoal", $"FIRST TO {MatchManager.KillTarget}",
            new Vector2(0.5f, 1), new Vector2(0, -72), 12, new Color(1f, 1f, 1f, 0.6f));

        hud.floatingTextPrefab = CreateFloatingTextPrefab();

        return hud;
    }

    private static Text CreateStaticText(Transform parent, string name, string content,
        Vector2 anchor, Vector2 anchoredPos, int fontSize, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(400, 40);

        var textComp = obj.AddComponent<Text>();
        textComp.text = content;
        textComp.font = PixelFont;
        textComp.fontSize = fontSize;
        textComp.alignment = TextAnchor.MiddleCenter;
        textComp.horizontalOverflow = HorizontalWrapMode.Overflow;
        textComp.verticalOverflow = VerticalWrapMode.Overflow;
        textComp.color = color;

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
        text.fontSize = 24;
        text.alignment = TextAnchor.MiddleCenter;
        text.font = PixelFont;

        obj.SetActive(true);
        return obj;
    }
}
