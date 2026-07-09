using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Game HUD: per-player panels (bottom-right, where the parallax foreground
/// is calmest), round timer (top-center), the round-over results overlay and
/// the hangar overlay in four modes: Waiting (lobby, FLY warm-up), PreBattle
/// (countdown + READY), LateJoin (personal DEPLOY countdown + JOIN) and
/// Intermission (weapon draft + upgrade shop + READY, between rounds).
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
        public Text weaponText;
        public Shooting shooting;

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
    private Text _resultsWinsText;
    private Text _resultsStandingsText;
    private Text _resultsCountdownText;

    // Run-over podium
    private GameObject _podiumOverlay;
    private Text _podiumCountdownText;

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

        // The arena stays hidden until the battle starts: boot straight into
        // the opaque Searching hangar, before any networking even begins.
        // MatchManager/ConnectionManager events replace it later (Waiting on
        // host start, PreBattle/LateJoin from the server, closed on battle).
        OnHangarOpened(MatchManager.HangarMode.Searching, 0);
        StartCoroutine(WaitForConnectionManager());
    }

    private Doodlebugs.Network.ConnectionManager _connectionManager;

    private IEnumerator WaitForConnectionManager()
    {
        while (Doodlebugs.Network.ConnectionManager.Instance == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        _connectionManager = Doodlebugs.Network.ConnectionManager.Instance;
        _connectionManager.OnStatusMessage += OnConnectionStatus;
        _connectionManager.OnRoleChanged += OnRoleChanged;
    }

    // Discovery status ("Searching for game...", "Connecting...") shown in
    // the Searching hangar instead of the little corner panel
    private void OnConnectionStatus(string message)
    {
        if (_hangarMode != MatchManager.HangarMode.Searching) return;
        if (_hangarCountdownText != null)
        {
            _hangarCountdownText.text = message.ToUpperInvariant();
        }
    }

    // Persistent HOST/CLIENT badge in the hangar overlay.
    private void OnRoleChanged(Doodlebugs.Network.LocalRole role) => UpdateHangarRoleBadge(role);

    private void UpdateHangarRoleBadge(Doodlebugs.Network.LocalRole role)
    {
        if (_hangarRoleText == null) return;
        switch (role)
        {
            case Doodlebugs.Network.LocalRole.Host:
                _hangarRoleText.text = "HOST";
                _hangarRoleText.color = new Color(1f, 0.85f, 0.3f);
                break;
            case Doodlebugs.Network.LocalRole.Client:
                _hangarRoleText.text = "CLIENT";
                _hangarRoleText.color = new Color(0.45f, 0.8f, 1f);
                break;
            default:
                _hangarRoleText.text = "";
                break;
        }
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
            MatchManager.Instance.OnHangarOpened -= OnHangarOpened;
            MatchManager.Instance.OnHangarTick -= OnHangarTick;
            MatchManager.Instance.OnClientReadyChanged -= OnClientReadyChanged;
            MatchManager.Instance.OnRunStateChanged -= OnRunStateChanged;
            MatchManager.Instance.OnRunEnded -= OnRunEnded;
            MatchManager.Instance.OnSessionReset -= OnSessionReset;
        }

        if (_connectionManager != null)
        {
            _connectionManager.OnStatusMessage -= OnConnectionStatus;
            _connectionManager.OnRoleChanged -= OnRoleChanged;
            _connectionManager = null;
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
        MatchManager.Instance.OnHangarOpened += OnHangarOpened;
        MatchManager.Instance.OnHangarTick += OnHangarTick;
        MatchManager.Instance.OnClientReadyChanged += OnClientReadyChanged;
        MatchManager.Instance.OnRunStateChanged += OnRunStateChanged;
        MatchManager.Instance.OnRunEnded += OnRunEnded;
        MatchManager.Instance.OnSessionReset += OnSessionReset;
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
        // Anchor to bottom-right - the parallax foreground is calmest there
        // (and the right side still avoids the phone notch)
        playerStatsContainer.anchorMin = new Vector2(1, 0);
        playerStatsContainer.anchorMax = new Vector2(1, 0);
        playerStatsContainer.pivot = new Vector2(1, 0);
        playerStatsContainer.anchoredPosition = new Vector2(-60, 20);
        playerStatsContainer.sizeDelta = new Vector2(420, 700);

        var layout = containerObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.LowerRight;
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
            // --- segmented bars: shield / health grow with run upgrades (3..5) ---
            entry.shieldSegments = CreateSegmentRow(container.transform, "ShieldBar",
                PlaneStats.AbsoluteMaxSegments, 12f);
            entry.healthSegments = CreateSegmentRow(container.transform, "HealthBar",
                PlaneStats.AbsoluteMaxSegments, 12f);
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

            // Current weapon name (always visible, leftmost chip)
            var weaponObj = new GameObject("WeaponName");
            weaponObj.transform.SetParent(chipsRow.transform, false);
            var weaponRect = weaponObj.AddComponent<RectTransform>();
            weaponRect.sizeDelta = new Vector2(150, 22);
            entry.weaponText = weaponObj.AddComponent<Text>();
            entry.weaponText.font = PixelFont;
            entry.weaponText.fontSize = ChipFontSize;
            entry.weaponText.alignment = TextAnchor.MiddleRight;
            entry.weaponText.horizontalOverflow = HorizontalWrapMode.Overflow;
            entry.weaponText.color = new Color(0.95f, 0.85f, 0.6f);

            entry.repairChip = CreateChip(chipsRow.transform, "RepairChip", GetRepairSprite(),
                new Color(1f, 0.85f, 0.2f), out entry.repairChipText);
            entry.dmgChip = CreateChip(chipsRow.transform, "DmgChip", GetDmgSprite(),
                new Color(1f, 0.45f, 0.2f), out entry.dmgChipText);

            entry.shooting = player.GetComponent<Shooting>();
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
                UpdateSegments(entry.shieldSegments, stats?.Shield ?? 0, shieldBarColor,
                    stats?.MaxShieldValue ?? 3);
                UpdateSegments(entry.healthSegments, stats?.Health ?? 0, healthBarColor,
                    stats?.MaxHealthValue ?? 3);
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

    private static void UpdateSegments(Image[] segments, int value, Color onColor, int maxVisible = -1)
    {
        if (segments == null) return;
        if (maxVisible < 0) maxVisible = segments.Length;
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            if (i >= maxVisible)
            {
                // Slot not unlocked by run upgrades yet - invisible
                segments[i].color = Color.clear;
            }
            else
            {
                segments[i].color = i < value ? onColor : SegmentOffColor;
            }
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
        // Current weapon name (respawn can swap the player object - refresh cache)
        if (entry.weaponText != null && entry.player != null)
        {
            if (entry.shooting == null || entry.shooting.gameObject != entry.player.gameObject)
            {
                entry.shooting = entry.player.GetComponent<Shooting>();
            }
            entry.weaponText.text = entry.shooting != null
                ? entry.shooting.CurrentWeapon.DisplayName
                : "";
        }

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
        // A pending waiting/late-join hangar yields to the results screen
        CloseHangar();

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

        if (_resultsWinsText != null)
        {
            _resultsWinsText.text = BuildRoundWinsLine();
        }

        bool runOver = MatchManager.Instance != null && MatchManager.Instance.IsRunOver;
        _resultsCountdownText.text = runOver ? "RUN OVER!" : "HANGAR OPENING...";
        _resultsOverlay.SetActive(true);
    }

    private void OnMatchStarted()
    {
        if (_resultsOverlay != null)
        {
            _resultsOverlay.SetActive(false);
        }
        if (_podiumOverlay != null)
        {
            Destroy(_podiumOverlay);
            _podiumOverlay = null;
            _podiumCountdownText = null;
        }
        CloseHangar();
    }

    // --- run-over podium --------------------------------------------------------

    private void OnRunEnded(ulong winnerClientId)
    {
        if (_resultsOverlay != null) _resultsOverlay.SetActive(false);

        _podiumOverlay = new GameObject("PodiumOverlay");
        _podiumOverlay.transform.SetParent(transform, false);
        var rect = _podiumOverlay.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var bg = _podiumOverlay.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.88f);

        CreatePodiumText("Title", "RUN OVER", 34, new Vector2(0, 130), new Color(1f, 0.85f, 0.3f));

        Color winnerColor = PlayerColorManager.Instance != null
            ? PlayerColorManager.Instance.GetColor(winnerClientId, 0)
            : PlayerColorManager.GetColorByIndex((int)winnerClientId);
        CreatePodiumText("Champion",
            $"CHAMPION: {ResolvePlayerName(winnerClientId, 0)}", 22, new Vector2(0, 70), winnerColor);

        // Standings by round wins
        var mm = MatchManager.Instance;
        var lines = new List<string>();
        if (mm != null)
        {
            var entries = new List<KeyValuePair<ulong, int>>();
            foreach (var pair in mm.RoundWins) entries.Add(pair);
            entries.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < entries.Count; i++)
            {
                lines.Add($"{i + 1}. {ResolvePlayerName(entries[i].Key, 0)}  {entries[i].Value} WINS");
            }
        }
        var standings = CreatePodiumText("Standings", string.Join("\n", lines), 14,
            new Vector2(0, -40), Color.white);
        standings.alignment = TextAnchor.UpperCenter;
        standings.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 140);

        _podiumCountdownText = CreatePodiumText("Countdown",
            $"NEW RUN IN {MatchManager.PodiumSeconds}", 16, new Vector2(0, -150),
            new Color(1f, 0.85f, 0.3f));
    }

    private Text CreatePodiumText(string name, string content, int fontSize, Vector2 pos, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(_podiumOverlay.transform, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
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
        _resultsWinnerText = CreateOverlayText("Winner", "", 22, new Vector2(0, 85), Color.white);
        _resultsWinsText = CreateOverlayText("Wins", "", 12, new Vector2(0, 52),
            new Color(1f, 1f, 1f, 0.7f));
        _resultsStandingsText = CreateOverlayText("Standings", "", 14, new Vector2(0, -75), Color.white);
        _resultsStandingsText.alignment = TextAnchor.UpperCenter;
        var standingsRect = _resultsStandingsText.GetComponent<RectTransform>();
        standingsRect.sizeDelta = new Vector2(900, 190);
        _resultsCountdownText = CreateOverlayText("Countdown", "", 18, new Vector2(0, -160), new Color(1f, 0.85f, 0.3f));

        _resultsOverlay.SetActive(false);
    }

    /// <summary>"WINS: A 2 · B 1" line built from the synced round-wins map.</summary>
    private string BuildRoundWinsLine()
    {
        var mm = MatchManager.Instance;
        if (mm == null || mm.RoundWins.Count == 0) return "";

        var entries = new List<KeyValuePair<ulong, int>>();
        foreach (var pair in mm.RoundWins) entries.Add(pair);
        entries.Sort((a, b) => b.Value.CompareTo(a.Value));

        var parts = new List<string>();
        foreach (var pair in entries)
        {
            parts.Add($"{ColoredName(pair.Key, 0)} {pair.Value}");
        }
        return $"WINS (TO {MatchManager.RoundWinsTarget}): " + string.Join(" · ", parts);
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

    // --- hangar overlay (weapon draft + ready check) ---------------------------

    private GameObject _hangarOverlay;
    private Text _hangarCountdownText;
    private Text _hangarRoleText;
    private Text _hangarReadyCountText;
    private Button _readyButton;
    private Text _readyButtonText;
    private readonly Dictionary<ulong, Text> _hangarReadyRows = new Dictionary<ulong, Text>();
    private readonly HashSet<ulong> _readyClients = new HashSet<ulong>();
    private readonly List<(GameObject card, Image bg, int weaponId)> _weaponCards =
        new List<(GameObject, Image, int)>();
    private int _selectedWeaponId;
    private bool _localReady;

    private static readonly Color CardIdle = new Color(0.10f, 0.12f, 0.16f, 0.95f);
    private static readonly Color CardSelected = new Color(0.35f, 0.22f, 0.08f, 0.95f);
    private static readonly Color ReadyGreen = new Color(0.35f, 0.85f, 0.35f);

    private MatchManager.HangarMode _hangarMode = MatchManager.HangarMode.Intermission;
    private GameObject _hangarCornerButton;

    private void OnHangarOpened(MatchManager.HangarMode mode, int seconds)
    {
        if (_resultsOverlay != null) _resultsOverlay.SetActive(false);

        _hangarMode = mode;
        BuildHangarOverlay(mode, seconds);
        _hangarOverlay.SetActive(true);
    }

    private void OnHangarTick(int secondsLeft)
    {
        // Shared tick: podium countdown when the run is over, hangar otherwise
        if (_podiumCountdownText != null && _podiumOverlay != null)
        {
            _podiumCountdownText.text = $"NEW RUN IN {secondsLeft}";
            return;
        }
        if (_hangarCountdownText != null)
        {
            switch (_hangarMode)
            {
                case MatchManager.HangarMode.PreBattle:
                    _hangarCountdownText.text = $"BATTLE IN {secondsLeft}";
                    break;
                case MatchManager.HangarMode.LateJoin:
                    _hangarCountdownText.text = $"DEPLOY IN {secondsLeft}";
                    break;
                default:
                    _hangarCountdownText.text = $"AUTO-START {secondsLeft}";
                    break;
            }
        }
    }

    /// <summary>The local late-joiner's own plane just deployed into a running
    /// battle. No OnMatchStarted fires mid-battle, so the personal LateJoin
    /// hangar has to be closed off the owner's own deploy.</summary>
    public void OnLocalPlayerDeployed()
    {
        if (_hangarMode == MatchManager.HangarMode.LateJoin && IsHangarOpen)
        {
            CloseHangar();
        }
    }

    private void OnSessionReset()
    {
        CloseHangar();
        if (_resultsOverlay != null) _resultsOverlay.SetActive(false);
        if (_podiumOverlay != null)
        {
            Destroy(_podiumOverlay);
            _podiumOverlay = null;
            _podiumCountdownText = null;
        }

        // The host is gone and discovery restarts automatically - back to the
        // boot screen instead of staring at a dead arena
        OnHangarOpened(MatchManager.HangarMode.Searching, 0);
    }

    /// <summary>True while any hangar overlay covers the screen (ConnectionUI
    /// hides its corner status panel then - the overlay shows the status).</summary>
    public bool IsHangarOpen => _hangarOverlay != null && _hangarOverlay.activeSelf;

    private void OnClientReadyChanged(ulong clientId)
    {
        _readyClients.Add(clientId);

        if (_hangarReadyRows.TryGetValue(clientId, out var row) && row != null)
        {
            row.text = "READY";
            row.color = ReadyGreen;
        }
        UpdateReadyCount();
    }

    private void CloseHangar()
    {
        if (_hangarOverlay != null)
        {
            Destroy(_hangarOverlay); // rebuilt fresh each round (offers change)
            _hangarOverlay = null;
        }
        if (_hangarCornerButton != null)
        {
            Destroy(_hangarCornerButton);
            _hangarCornerButton = null;
        }
        _hangarReadyRows.Clear();
        _readyClients.Clear();
        _weaponCards.Clear();
        _upgradeCards.Clear();
        _runPointsText = null;
        _localReady = false;
    }

    /// <summary>First PlayerController owned by this device (couch co-op: any of them).</summary>
    private PlayerController FindOwnedPlayer()
    {
        foreach (var p in FindObjectsOfType<PlayerController>())
        {
            if (p.IsOwner) return p;
        }
        return null;
    }

    private void BuildHangarOverlay(MatchManager.HangarMode mode, int seconds)
    {
        CloseHangar();

        _hangarOverlay = new GameObject("HangarOverlay");
        _hangarOverlay.transform.SetParent(transform, false);
        var overlayRect = _hangarOverlay.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        bool searching = mode == MatchManager.HangarMode.Searching;
        bool waiting = mode == MatchManager.HangarMode.Waiting;
        bool preBattle = mode == MatchManager.HangarMode.PreBattle;
        bool lateJoin = mode == MatchManager.HangarMode.LateJoin;
        bool intermission = mode == MatchManager.HangarMode.Intermission;

        var bg = _hangarOverlay.AddComponent<Image>();
        // Searching/Waiting hide the arena completely (nothing to see there
        // yet); the battle/results context stays dimly visible otherwise
        bg.color = new Color(0f, 0f, 0f, searching || waiting ? 1f : 0.82f);

        string title = searching ? "DOODLEBUGS" : lateJoin ? "BATTLE IN PROGRESS" : "HANGAR";
        CreateHangarText("Title", title, 28, new Vector2(0, 195), new Color(1f, 0.85f, 0.3f));

        // Persistent HOST/CLIENT badge above the title so it's clear which device
        // created the lobby vs. joined it, in every hangar mode.
        _hangarRoleText = CreateHangarText("RoleBadge", "", 11, new Vector2(0, 228), Color.white);
        UpdateHangarRoleBadge(_connectionManager != null
            ? _connectionManager.Role
            : Doodlebugs.Network.LocalRole.None);

        // Status line: centered when there is no ready counter next to it
        string statusInit =
            searching ? "SEARCHING FOR GAME..." :
            waiting ? "WAITING FOR PLAYERS..." :
            preBattle ? $"BATTLE IN {seconds}" :
            lateJoin ? $"DEPLOY IN {seconds}" :
            $"AUTO-START {MatchManager.HangarSeconds}";
        bool hasReadyCheck = preBattle || intermission;
        _hangarCountdownText = CreateHangarText("Countdown", statusInit,
            12, new Vector2(hasReadyCheck ? -160 : 0, 160), Color.white);
        if (hasReadyCheck)
        {
            _hangarReadyCountText = CreateHangarText("ReadyCount", "", 12, new Vector2(160, 160),
                new Color(1f, 1f, 1f, 0.6f));
        }

        if (searching)
        {
            // No plane, no weapons yet - just the boot screen with the version
            CreateHangarText("Version", Doodlebugs.UI.ConnectionUI.GameVersion, 10,
                new Vector2(0, -200), new Color(1f, 1f, 1f, 0.35f));
            return;
        }

        string draftLabel = intermission ? "WEAPON FOR NEXT ROUND" : "PICK YOUR WEAPON";
        CreateHangarText("DraftLabel", draftLabel, 11, new Vector2(0, 128),
            new Color(1f, 1f, 1f, 0.6f));

        BuildWeaponCards();
        if (intermission)
        {
            BuildUpgradeShop();
            RefreshUpgradeShop();
        }
        if (hasReadyCheck)
        {
            BuildReadyList();
            BuildReadyButton();
            UpdateReadyCount();
        }
        if (waiting)
        {
            BuildFlyButton();
        }
        if (lateJoin)
        {
            BuildJoinButton();
        }
    }

    // --- waiting-lobby warm-up (FLY out, corner button back) --------------------

    private void BuildFlyButton()
    {
        var (button, _) = BuildHangarActionButton("FlyButton", "FLY",
            new Color(0.12f, 0.3f, 0.45f, 1f));
        button.onClick.AddListener(HideHangarForWarmup);
    }

    private void HideHangarForWarmup()
    {
        if (_hangarOverlay != null) _hangarOverlay.SetActive(false);
        ShowHangarCornerButton();
        SfxManager.PlayTick();
    }

    // Small top-right button to get back into the hangar while warm-up flying
    private void ShowHangarCornerButton()
    {
        if (_hangarCornerButton != null) return;

        _hangarCornerButton = new GameObject("HangarCornerButton");
        _hangarCornerButton.transform.SetParent(transform, false);
        var rect = _hangarCornerButton.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-60, -20);
        rect.sizeDelta = new Vector2(180, 44);

        var bgImage = _hangarCornerButton.AddComponent<Image>();
        bgImage.color = new Color(0.12f, 0.3f, 0.45f, 0.9f);

        var button = _hangarCornerButton.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.onClick.AddListener(() =>
        {
            if (_hangarOverlay != null) _hangarOverlay.SetActive(true);
            if (_hangarCornerButton != null)
            {
                Destroy(_hangarCornerButton);
                _hangarCornerButton = null;
            }
        });

        CreateTextIn(_hangarCornerButton.transform, "Label", "HANGAR", 13, Vector2.zero, Color.white);
    }

    // --- late-join deploy --------------------------------------------------------

    private void BuildJoinButton()
    {
        var (button, label) = BuildHangarActionButton("JoinButton", "JOIN",
            new Color(0.12f, 0.4f, 0.16f, 1f));
        _readyButton = button;
        _readyButtonText = label;
        button.onClick.AddListener(PressJoin);
    }

    private void PressJoin()
    {
        if (_localReady) return;
        _localReady = true;

        if (_readyButtonText != null) _readyButtonText.text = "DEPLOYING...";
        if (_readyButton != null)
        {
            _readyButton.interactable = false;
            var img = _readyButton.targetGraphic as Image;
            if (img != null) img.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        }

        FindOwnedPlayer()?.RequestDeployServerRpc();
    }

    // Shared bottom-right hangar action button (READY / FLY / JOIN)
    private (Button, Text) BuildHangarActionButton(string name, string label, Color color)
    {
        var buttonObj = new GameObject(name);
        buttonObj.transform.SetParent(_hangarOverlay.transform, false);
        var rect = buttonObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-60, 40);
        rect.sizeDelta = new Vector2(300, 68);

        var bgImage = buttonObj.AddComponent<Image>();
        bgImage.color = color;

        var button = buttonObj.AddComponent<Button>();
        button.targetGraphic = bgImage;

        var text = CreateTextIn(buttonObj.transform, "Label", label, 20, Vector2.zero, Color.white);
        return (button, text);
    }

    private void BuildWeaponCards()
    {
        // Offers: keep-current + up to 2 random distinct picks from the draft pool
        var owned = FindOwnedPlayer();
        var shooting = owned != null ? owned.GetComponent<Shooting>() : null;
        int currentId = shooting != null ? shooting.NetWeaponId.Value : (int)WeaponType.MG;
        _selectedWeaponId = currentId;

        var offers = new List<int> { currentId };
        var pool = new List<WeaponType>(WeaponProfile.DraftPool);
        // shuffle
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        foreach (var w in pool)
        {
            if (offers.Count >= 3) break;
            if ((int)w != currentId) offers.Add((int)w);
        }

        const float cardWidth = 240f;
        const float cardHeight = 120f;
        const float gap = 20f;
        float totalWidth = offers.Count * cardWidth + (offers.Count - 1) * gap;
        float startX = -totalWidth / 2f + cardWidth / 2f;

        for (int i = 0; i < offers.Count; i++)
        {
            int weaponId = offers[i];
            var profile = WeaponProfile.Get(weaponId);

            var cardObj = new GameObject($"WeaponCard_{profile.Type}");
            cardObj.transform.SetParent(_hangarOverlay.transform, false);
            var cardRect = cardObj.AddComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = new Vector2(startX + i * (cardWidth + gap), 62f);
            cardRect.sizeDelta = new Vector2(cardWidth, cardHeight);

            var cardBg = cardObj.AddComponent<Image>();
            cardBg.color = weaponId == currentId ? CardSelected : CardIdle;

            var button = cardObj.AddComponent<Button>();
            button.targetGraphic = cardBg;
            int captured = weaponId;
            button.onClick.AddListener(() => SelectWeapon(captured));

            string title = weaponId == currentId ? $"KEEP: {profile.DisplayName}" : profile.DisplayName;
            CreateTextIn(cardObj.transform, "Name", title, 13, new Vector2(0, 34), Color.white);
            CreateTextIn(cardObj.transform, "Desc", profile.Description, 9, new Vector2(0, 8),
                new Color(1f, 1f, 1f, 0.65f));
            string stats = $"DMG {profile.Damage} · ROF {1f / profile.Cooldown:F1}/s" +
                           (profile.PelletCount > 1 ? $" · x{profile.PelletCount}" : "");
            CreateTextIn(cardObj.transform, "Stats", stats, 9, new Vector2(0, -16),
                new Color(1f, 0.75f, 0.4f));
            CreateTextIn(cardObj.transform, "Sel", weaponId == currentId ? "SELECTED" : "", 9,
                new Vector2(0, -42), ReadyGreen);

            _weaponCards.Add((cardObj, cardBg, weaponId));
        }
    }

    private void SelectWeapon(int weaponId)
    {
        if (_localReady) return; // locked in after READY

        _selectedWeaponId = weaponId;

        // Apply to every plane this device owns (couch co-op picks together)
        foreach (var p in FindObjectsOfType<PlayerController>())
        {
            if (!p.IsOwner) continue;
            var shooting = p.GetComponent<Shooting>();
            if (shooting != null)
            {
                shooting.RequestSelectWeaponServerRpc(weaponId);
            }
        }

        // Refresh card highlights
        foreach (var (card, cardBg, id) in _weaponCards)
        {
            cardBg.color = id == weaponId ? CardSelected : CardIdle;
            var sel = card.transform.Find("Sel")?.GetComponent<Text>();
            if (sel != null) sel.text = id == weaponId ? "SELECTED" : "";
        }

        SfxManager.PlayTick();
    }

    // --- run upgrade shop -----------------------------------------------------

    private Text _runPointsText;
    private readonly List<(GameObject card, Image bg, Text levelText, RunUpgradeType type)>
        _upgradeCards = new List<(GameObject, Image, Text, RunUpgradeType)>();

    private void BuildUpgradeShop()
    {
        _upgradeCards.Clear();

        _runPointsText = CreateHangarText("RunPoints", "", 11, new Vector2(0, -18),
            new Color(1f, 0.85f, 0.3f));

        const float cardWidth = 185f;
        const float cardHeight = 95f;
        const float gap = 14f;
        float totalWidth = RunUpgrades.TypeCount * cardWidth + (RunUpgrades.TypeCount - 1) * gap;
        float startX = -totalWidth / 2f + cardWidth / 2f;

        for (int i = 0; i < RunUpgrades.TypeCount; i++)
        {
            var type = (RunUpgradeType)i;

            var cardObj = new GameObject($"UpgradeCard_{type}");
            cardObj.transform.SetParent(_hangarOverlay.transform, false);
            var rect = cardObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(startX + i * (cardWidth + gap), -85f);
            rect.sizeDelta = new Vector2(cardWidth, cardHeight);

            var cardBg = cardObj.AddComponent<Image>();
            cardBg.color = CardIdle;

            var button = cardObj.AddComponent<Button>();
            button.targetGraphic = cardBg;
            var captured = type;
            button.onClick.AddListener(() => BuyUpgrade(captured));

            CreateTextIn(cardObj.transform, "Name", RunUpgrades.DisplayName(type), 11,
                new Vector2(0, 26), Color.white);
            CreateTextIn(cardObj.transform, "Desc", RunUpgrades.Description(type), 9,
                new Vector2(0, 2), new Color(1f, 1f, 1f, 0.65f));
            var levelText = CreateTextIn(cardObj.transform, "Level", "", 9,
                new Vector2(0, -24), new Color(1f, 0.75f, 0.4f));

            _upgradeCards.Add((cardObj, cardBg, levelText, type));
        }
    }

    private void BuyUpgrade(RunUpgradeType type)
    {
        var mm = MatchManager.Instance;
        if (mm == null) return;
        if (mm.LocalRunPoints < RunUpgrades.CostPoints) return;
        if (mm.GetLocalUpgradeLevel(type) >= RunUpgrades.MaxLevel) return;

        FindOwnedPlayer()?.RequestBuyUpgradeServerRpc((int)type);
        SfxManager.PlayTick();
        // UI refreshes when the server confirms via OnRunStateChanged
    }

    private void OnRunStateChanged()
    {
        RefreshUpgradeShop();
    }

    private void RefreshUpgradeShop()
    {
        var mm = MatchManager.Instance;
        if (mm == null || _hangarOverlay == null) return;

        if (_runPointsText != null)
        {
            _runPointsText.text = $"UPGRADES · RUN POINTS: {mm.LocalRunPoints}";
        }

        foreach (var (card, cardBg, levelText, type) in _upgradeCards)
        {
            int level = mm.GetLocalUpgradeLevel(type);
            bool maxed = level >= RunUpgrades.MaxLevel;
            bool affordable = mm.LocalRunPoints >= RunUpgrades.CostPoints;

            levelText.text = maxed ? $"LV {level}/{RunUpgrades.MaxLevel} MAX"
                                   : $"LV {level}/{RunUpgrades.MaxLevel} · COST {RunUpgrades.CostPoints}";
            cardBg.color = maxed ? CardSelected
                : affordable ? CardIdle
                : new Color(0.07f, 0.07f, 0.09f, 0.95f);

            var btn = card.GetComponent<Button>();
            if (btn != null) btn.interactable = !maxed && affordable;
        }
    }

    // --- ready list + button ----------------------------------------------------

    private void BuildReadyList()
    {
        // One row per device (clientId); couch co-op pilots share one READY.
        // Anchored bottom-left so it never collides with the cards.
        var seen = new HashSet<ulong>();
        float y = 46f;
        foreach (var entry in _playerEntries)
        {
            if (!seen.Add(entry.clientId)) continue;

            string name = entry.lastKnownName ?? $"P{entry.clientId + 1}";
            Color color = PlayerColorManager.Instance != null
                ? PlayerColorManager.Instance.GetColor(entry.clientId, 0)
                : PlayerColorManager.GetColorByIndex((int)entry.clientId);

            CreateCornerText($"Name_{entry.clientId}", name, 10, new Vector2(0, 0),
                new Vector2(60, y), color);
            var status = CreateCornerText($"Status_{entry.clientId}", "PICKING...", 10,
                new Vector2(0, 0), new Vector2(300, y), new Color(1f, 1f, 1f, 0.5f));

            _hangarReadyRows[entry.clientId] = status;
            y += 24f;
        }
    }

    private Text CreateCornerText(string name, string content, int fontSize,
        Vector2 anchor, Vector2 pos, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(_hangarOverlay.transform, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0, 0);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(360, 20);

        var text = obj.AddComponent<Text>();
        text.text = content;
        text.font = PixelFont;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;
        return text;
    }

    private void BuildReadyButton()
    {
        var buttonObj = new GameObject("ReadyButton");
        buttonObj.transform.SetParent(_hangarOverlay.transform, false);
        var rect = buttonObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-60, 40);
        rect.sizeDelta = new Vector2(300, 68);

        var bgImage = buttonObj.AddComponent<Image>();
        bgImage.color = new Color(0.12f, 0.4f, 0.16f, 1f);

        _readyButton = buttonObj.AddComponent<Button>();
        _readyButton.targetGraphic = bgImage;
        _readyButton.onClick.AddListener(PressReady);

        _readyButtonText = CreateTextIn(buttonObj.transform, "Label", "READY", 20,
            Vector2.zero, Color.white);
    }

    private void PressReady()
    {
        if (_localReady) return;
        _localReady = true;

        if (_readyButtonText != null) _readyButtonText.text = "WAITING...";
        if (_readyButton != null)
        {
            _readyButton.interactable = false;
            var img = _readyButton.targetGraphic as Image;
            if (img != null) img.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        }

        var owned = FindOwnedPlayer();
        owned?.SetHangarReadyServerRpc();
    }

    private void UpdateReadyCount()
    {
        if (_hangarReadyCountText == null) return;
        _hangarReadyCountText.text = $"READY {_readyClients.Count}/{_hangarReadyRows.Count}";
    }

    private Text CreateHangarText(string name, string content, int fontSize, Vector2 pos,
        Color color, TextAnchor align = TextAnchor.MiddleCenter, float width = 900)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(_hangarOverlay.transform, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(width, 30);

        var text = obj.AddComponent<Text>();
        text.text = content;
        text.font = PixelFont;
        text.fontSize = fontSize;
        text.alignment = align;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;
        return text;
    }

    private Text CreateTextIn(Transform parent, string name, string content, int fontSize,
        Vector2 pos, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(300, 26);

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
