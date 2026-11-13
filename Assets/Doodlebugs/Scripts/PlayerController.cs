using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Doodlebugs.Network;

public class PlayerController : NetworkBehaviour, IDamagable
{
    // TODO annotations for Unity Editor
    public Transform plane;
    public Transform leftPoint, rightPoint, forwardPoint;
    Rigidbody2D rb;
    ClientNetworkTransform networkTransform;

    // Flight model v2 (Prompts/28): every number comes from FlightTuning.
    // The pilot-maturity profiles never applied (scores reset each round
    // below their 10/20-kill thresholds) and are no longer read (D2).
    private static FlightPreset P => FlightTuning.Active;
    private float defaultSpeed => P.CruiseSpeed;
    private float minSpeed => P.MinSpeed;
    // Run-upgrade ENGINE levels raise the speed cap (synced, works for owner input)
    private float SpeedMul => planeStats?.SpeedMultiplier ?? 1f;
    private float maxSpeed => P.MaxSpeed * SpeedMul;
    private float maxGravity => P.MaxGravity;
    private float gravityIncreaseRate => P.GravityIncreaseRate;
    private float engineRestartMin => P.EngineRestartMin;
    private float engineRestartMax => P.EngineRestartMax;
    private float HandlingFactor => planeStats != null ? planeStats.Handling : 1f;

    // Owner-side flight state
    private float _omega;                 // deg/s, + = clockwise (same sign as the rotation input)
    private float _snapUntil, _snapReadyAt;
    private float _lastNeutralAt = -1f, _lastPosAt = -1f, _lastNegAt = -1f;
    private Vector2 _externalVelocity;    // knockback (Prompts/29), decays
    private float _spinKick;              // degrees still to apply, + = counter-clockwise
    private float _boostUntil;            // nitro / ram
    private const float ImpulseDecay = 14f;

    /// <summary>Owner-side: a snap turn just started (sound + haptic hook).</summary>
    public event System.Action OnSnapTurn;

    /// <summary>Every client: the synced engine state flipped (true = off).</summary>
    public event System.Action<bool> OnEngineStateChanged;

    /// <summary>Owner-side, 0..1: how hard the plane is turning right now.</summary>
    public float TurnLoad01 => Mathf.Clamp01(Mathf.Abs(_omega) / Mathf.Max(1f, P.TurnRate));

    // LEAN (Prompts/32): owner-side, last FixedUpdate. No network state - the
    // bonus is computed on the owner, whose movement is authoritative.
    private float _leanInput;       // meter from the provider, -1..1
    private float _leanAligned;     // meter in the stick's direction, 0..1
    private float _leanUsable;      // ... scaled by stick deflection, 0..1

    /// <summary>Owner-side, -1..1: the LEAN meter as the provider reports it.</summary>
    public float LeanInput => _leanInput;

    /// <summary>Owner-side, 0..1: how much of the LEAN bonus applies right
    /// now (engine on, stick turning the meter's way, times stick deflection).</summary>
    public float Lean01 => _leanUsable;

    /// <summary>Owner-side, 0..1: the meter in the stick's direction,
    /// whatever the deflection - 1 = "full lean" for the feedback channels.</summary>
    public float LeanAligned01 => _leanAligned;

    public bool IsBoosting => Time.time < _boostUntil;

    // RECOVER combo (stall assist): an owner-side autopilot that flips the
    // nose into the relight window, lets the engine catch, then pulls out
    // level in the direction the plane was travelling. Player input is
    // ignored until it is level or the timeout runs out.
    private const float RecoverTimeout = 4f;
    private const float RecoverLevelTolerance = 6f;
    private bool _recovering;
    private float _recoverDir;            // +1 = level out flying right, -1 = left
    private float _recoverUntil;
    private float _recoverStartedAt;

    /// <summary>Owner-side: the RECOVER autopilot is flying the plane.</summary>
    public bool IsRecovering => _recovering;

    // Synchronized state across network
    private NetworkVariable<float> netSpeed = new NetworkVariable<float>(5f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netEngineOff = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netInSpace = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netGravity = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Player display name (synced from device name)
    private NetworkVariable<Unity.Collections.FixedString64Bytes> netPlayerName =
        new NetworkVariable<Unity.Collections.FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

    // Local player index for couch co-op (-1 = network player, 0-3 = local player)
    private NetworkVariable<int> netLocalPlayerIndex = new NetworkVariable<int>(-1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // True while this plane waits in the hangar (pre-battle lobby or late-join
    // pick screen): hidden, frozen and non-collidable on every client.
    // MatchManager deploys it by flipping this back to false.
    public NetworkVariable<bool> NetInHangar = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>True while the plane is parked in the hangar (not deployed).</summary>
    public bool InHangar => NetInHangar.Value;

    // --- warm-up bot (Prompts/25) --------------------------------------------
    //
    // The bot is an ordinary PlaneHolder flown by BotBrain instead of a
    // device. Everything that keys on a player's identity - HUD panels, score
    // rows, look claims, kill credit, READY - has to be able to tell it apart,
    // so the flag is synced (a late joiner's HUD must skip it from frame one)
    // and its couch-co-op index is a reserved value that can never collide
    // with a real pilot (0-3) or fold onto the host's P1 key (-1 -> 0).
    public NetworkVariable<bool> NetIsBot = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Any AI-flown plane (warm-up or combat). Use it for "not the
    /// local human's plane" (haptics, touch controls, look claims, RPC
    /// carriers); use <see cref="IsWarmupBot"/> for "not a participant".</summary>
    public bool IsBot => NetIsBot.Value;

    /// <summary>The scoreless lobby bot (Prompts/25): no score, no row, no feed credit.</summary>
    public bool IsWarmupBot => NetIsBot.Value && netLocalPlayerIndex.Value == BotLocalPlayerIndex;

    /// <summary>A bot that plays the round like a pilot (Prompts/27 WP3): it
    /// scores, has a compact HUD row and its own run state.</summary>
    public bool IsCombatBot => NetIsBot.Value && IsCombatBotIdentity(netLocalPlayerIndex.Value);

    public const int BotLocalPlayerIndex = 99;
    public const string BotDisplayName = "BOT";

    // Combat bots are host-owned like the warm-up bot; their couch index is a
    // reserved seat 90..97, so (0, 90+seat) never collides with a real pilot
    // (0-3) or with the warm-up bot (99).
    public const int CombatBotIndexBase = 90;
    public const int MaxCombatBots = 8;
    public const int BotTierCount = 4;

    /// <summary>Whether a (clientId, localPlayerIndex) pair names the warm-up
    /// bot - the shape bullets and score calls carry the attacker in.</summary>
    public static bool IsWarmupBotIdentity(ulong clientId, int localPlayerIndex) =>
        localPlayerIndex == BotLocalPlayerIndex;

    /// <summary>Whether a local index is a combat-bot seat.</summary>
    public static bool IsCombatBotIdentity(int localPlayerIndex) =>
        localPlayerIndex >= CombatBotIndexBase && localPlayerIndex < CombatBotIndexBase + MaxCombatBots;

    /// <summary>Any bot identity (warm-up or combat).</summary>
    public static bool IsAnyBotIdentity(int localPlayerIndex) =>
        localPlayerIndex == BotLocalPlayerIndex || IsCombatBotIdentity(localPlayerIndex);

    /// <summary>Combat bot difficulty (0 Rookie .. 3 Baron), server-write. Synced so
    /// every client builds the bot's name in its own language (Prompts/27 S6).</summary>
    public NetworkVariable<byte> NetBotTier = new NetworkVariable<byte>(0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Every spawned plane on this peer (humans and bots), for the
    /// bot brain's sensing and the HUD - no FindObjectsByType per tick.</summary>
    public static readonly System.Collections.Generic.List<PlayerController> All =
        new System.Collections.Generic.List<PlayerController>();

    /// <summary>"ACE 2": tier name in the local language plus the seat number.</summary>
    public static string CombatBotName(int tier, int seat) =>
        $"{Loc.T("bot.tier." + Mathf.Clamp(tier, 0, BotTierCount - 1))} {seat + 1}";

    /// <summary>The name to show for this plane on this device.</summary>
    public string DisplayName => IsCombatBot
        ? CombatBotName(NetBotTier.Value, netLocalPlayerIndex.Value - CombatBotIndexBase)
        : PlayerName;

    // Set by BotManager on the host; consulted before any device provider so
    // a host-owned bot never mirrors the host's own stick and trigger.
    private IInputProvider _inputOverride;
    public void SetInputOverride(IInputProvider provider) => _inputOverride = provider;

    /// <summary>Server-side: fired at the top of every death path, before the
    /// explosion RPC. BotManager uses it to replace the bot with a fresh one.</summary>
    public event System.Action OnServerDeath;

    /// <summary>Server, right after Spawn(): turn this plane into the bot.
    /// NetIsBot goes first so the claim coroutine and the index-change
    /// handler already see it. Not SetLocalPlayerIndex - that would name
    /// the plane "P100" for a frame.</summary>
    public void ServerSetBotIdentity() => ServerSetBotIdentity(-1, 0);

    /// <summary>Server, right after Spawn(): seat -1 = the warm-up bot,
    /// 0..MaxCombatBots-1 = a combat bot at that seat with that tier.</summary>
    public void ServerSetBotIdentity(int combatSeat, byte tier)
    {
        if (!IsServer) return;
        NetIsBot.Value = true;
        NetBotTier.Value = tier;
        netLocalPlayerIndex.Value = combatSeat >= 0 ? CombatBotIndexBase + combatSeat : BotLocalPlayerIndex;
        // Owner-write, and the server IS the owner of a server-spawned object.
        netPlayerName.Value = new Unity.Collections.FixedString64Bytes(BotDisplayName);
    }

    // Read-only flight state for the bot brain. Heading: 0 = flying right,
    // 90 = straight up, -90 = straight down, +-180 = flying left - velocity is
    // hard-set to transform.right while the engine runs, so this IS the
    // direction of travel.
    public float HeadingDegrees => Mathf.Atan2(transform.right.y, transform.right.x) * Mathf.Rad2Deg;
    public bool IsInSpace => inSpace;
    public float MaxFlightSpeed => maxSpeed;
    public float MinFlightSpeed => minSpeed;

    /// <summary>The heading band inside which a dead engine relights, in
    /// degrees. movePlane tests the visual child's quaternion z, and
    /// z = sin(heading / 2), hence the asin.</summary>
    public (float minDeg, float maxDeg) EngineRestartHeadingWindow =>
        (2f * Mathf.Asin(Mathf.Clamp(engineRestartMin, -1f, 1f)) * Mathf.Rad2Deg,
         2f * Mathf.Asin(Mathf.Clamp(engineRestartMax, -1f, 1f)) * Mathf.Rad2Deg);

    /// <summary>Full-stick turn radius at a speed with the engine on, world units.</summary>
    public float TurnRadiusAt(float speed) => FlightModel.TurnRadius(P, speed, HandlingFactor);

    /// <summary>Turn radius at the current speed.</summary>
    public float EngineOnTurnRadius => TurnRadiusAt(Speed);

    public float CruiseFlightSpeed => P.CruiseSpeed;

    /// <summary>
    /// Get the display name for this player (device name, shortened)
    /// </summary>
    public string PlayerName => netPlayerName.Value.ToString();

    /// <summary>
    /// Local player index for couch co-op (-1 = network player, 0-3 = local player)
    /// </summary>
    public int LocalPlayerIndex => netLocalPlayerIndex.Value;

    /// <summary>
    /// Check if this is a local couch co-op player
    /// </summary>
    public bool IsLocalPlayer => netLocalPlayerIndex.Value >= 0;

    // Per-player maturity tracking
    private PilotMaturityLevel _maturityLevel = PilotMaturityLevel.Novice;

    /// <summary>
    /// Event fired when this player's maturity level changes.
    /// </summary>
    public event System.Action<PilotMaturityLevel> OnMaturityChanged;

    /// <summary>
    /// Get this player's current maturity level.
    /// </summary>
    public PilotMaturityLevel MaturityLevel => _maturityLevel;

    /// <summary>
    /// Check and upgrade maturity level based on kills.
    /// Thresholds: 10 kills = Advanced, 20 kills = Expert
    /// </summary>
    [System.Obsolete("Pilot maturity no longer affects flight (Prompts/28 D2)")]
    public void CheckMaturityUpgrade(int totalKills) { }

    // Local accessors for network variables
    private float speed
    {
        get => netSpeed.Value;
        set => netSpeed.Value = value;
    }
    private bool engineOff
    {
        get => netEngineOff.Value;
        set => netEngineOff.Value = value;
    }
    private bool inSpace
    {
        get => netInSpace.Value;
        set => netInSpace.Value = value;
    }
    private float currentGravity
    {
        get => netGravity.Value;
        set => netGravity.Value = value;
    }

    // Public accessors for the HUD speed bar
    public bool IsEngineOff => engineOff;
    public float Speed => speed;

    /// <summary>
    /// Get the PlaneStats component (for Shooting, HUD, etc.)
    /// </summary>
    public PlaneStats PlaneStats => planeStats;

    public GameObject hitEffect;

    // Visual effects controller
    private PlaneVisualEffects visualEffects;

    // Plane stats (shield, health, handling, damage)
    private PlaneStats planeStats;

    // Last attacker tracking (for kill attribution)
    // Attribution expires so a shield-hit from long ago doesn't credit that
    // shooter when the victim later dies to something unrelated.
    private const float KillAttributionWindow = 4f;
    // A knockback has to be what put the plane into the ground, not a hit long before
    private const float SlamAttributionWindow = 2.5f;
    private float _lastKnockbackTime = -999f;

    private bool IsSelf(ulong clientId, int localIdx) =>
        clientId == OwnerClientId && localIdx == (LocalPlayerIndex >= 0 ? LocalPlayerIndex : 0);
    private ulong _lastAttackerClientId;
    private int _lastAttackerLocalPlayerIndex;
    private bool _hasLastAttacker;
    private float _lastAttackerTime;

    // Cached boundary references
    private Collider2D leftBoundary;
    private Collider2D rightBoundary;
    private BoxCollider2D planeCollider;

    // Skin selection - the sprite side of SetPlaneColor()
    private PlaneAppearance appearance;

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        networkTransform = GetComponent<ClientNetworkTransform>();
        planeCollider = GetComponent<BoxCollider2D>();

        // Cache boundary references (Collider2D, for bounds)
        leftBoundary = ArenaBounds.Left;
        rightBoundary = ArenaBounds.Right;

        // Limit FPS for stability
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!All.Contains(this)) All.Add(this);

        // Subscribe to local player index changes to update color when set
        netLocalPlayerIndex.OnValueChanged += OnLocalPlayerIndexChanged;
        netEngineOff.OnValueChanged += OnEngineOffChanged;

        appearance = GetComponent<PlaneAppearance>();
        if (appearance != null)
        {
            appearance.NetSkinId.OnValueChanged += OnSkinChanged;
            appearance.NetModelId.OnValueChanged += OnModelChanged;
        }

        // Cache visual effects reference first - SetPlaneColor tells it
        // about the sprite it just put on the renderer
        visualEffects = GetComponent<PlaneVisualEffects>();

        // Set initial shape + skin + color (may be updated when LocalPlayerIndex is set)
        SetPlaneColor();

        // Cache plane stats reference
        planeStats = GetComponent<PlaneStats>();

        // Ensure Rigidbody is initialized
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }
        if (networkTransform == null)
        {
            networkTransform = GetComponent<ClientNetworkTransform>();
        }

        // Hangar state: the server hides remote clients' auto-spawned planes
        // until MatchManager deploys them (pre-battle lobby or late join).
        if (IsServer && MatchManager.Instance != null &&
            MatchManager.Instance.ShouldSpawnHiddenInHangar(OwnerClientId))
        {
            NetInHangar.Value = true;
        }
        NetInHangar.OnValueChanged += OnInHangarChanged;
        ApplyHangarState(NetInHangar.Value);

        // Initialize movement for owner (deferred to avoid NetworkVariable timing issues)
        if (IsOwner)
        {
            StartCoroutine(InitializeOwnerDelayed());
        }
    }

    private void OnInHangarChanged(bool previous, bool current)
    {
        ApplyHangarState(current);

        // A late-joiner deploying mid-battle: no OnMatchStarted fires to close
        // the personal LateJoin hangar, so drive it off the owner's own deploy
        // (parked -> deployed). Guarded to the LateJoin overlay inside the HUD.
        if (IsOwner && previous && !current)
        {
            GameHUD.Instance?.OnLocalPlayerDeployed();
        }
    }

    /// <summary>
    /// Park/unpark the plane visually and physically. Runs on every client
    /// from the NetInHangar sync (and once on spawn with the initial value).
    /// </summary>
    private void ApplyHangarState(bool hidden)
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (planeCollider == null) planeCollider = GetComponent<BoxCollider2D>();
        if (visualEffects == null) visualEffects = GetComponent<PlaneVisualEffects>();

        if (planeCollider != null) planeCollider.enabled = !hidden;
        if (rb != null)
        {
            rb.simulated = !hidden;
            if (!hidden && IsOwner)
            {
                // Give the freshly deployed plane forward motion; the deploy
                // teleport (RespawnAtPositionClientRpc) lands right after
                rb.linearVelocity = transform.right * Mathf.Max(speed, defaultSpeed);
            }
        }

        visualEffects?.SetHangarHidden(hidden);
    }

    private IEnumerator InitializeOwnerDelayed()
    {
        yield return null; // Wait one frame

        Debug.Log($"[PlayerController] InitializeOwnerDelayed called, IsOwner={IsOwner}, OwnerClientId={OwnerClientId}, rb={rb != null}");

        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
            Debug.Log($"[PlayerController] rb was null, got component: {rb != null}");
        }

        // Set player display name based on device/platform - unless this is
        // the bot, which was named by ServerSetBotIdentity before this ran
        netPlayerName.Value = new Unity.Collections.FixedString64Bytes(IsBot ? BotDisplayName : GetDeviceDisplayName());
        Debug.Log($"[PlayerController] Set player name to: {netPlayerName.Value}");

        speed = defaultSpeed;
        engineOff = false;
        inSpace = false;
        currentGravity = 0f;

        if (rb != null)
        {
            rb.linearVelocity = transform.right * speed;
            Debug.Log($"[PlayerController] Set velocity to {rb.linearVelocity}, speed={speed}");
        }
        else
        {
            Debug.LogError("[PlayerController] rb is still null!");
        }

        // Ask the server for the current game state (phase, scores, timer,
        // late-join hangar) now that this player object is provably spawned
        // and able to receive the targeted reply RPCs.
        if (!IsServer)
        {
            RequestStateSnapshotServerRpc();
        }
    }

    private void SetPlaneColor()
    {
        if (plane == null) return;

        var spriteRenderer = plane.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) return;

        // Shape + skin swap happens first so the ColorReplace material below
        // reads the chosen sprite's texture, not the default BiPlane1 one.
        // Every shape passes the same envelope gate (tools/planes/gate.py)
        // and keeps its tail-fin accent region pure red, so the shader step
        // further down and the shared BoxCollider2D hitbox need no changes.
        if (appearance != null)
        {
            var lookSprite = PlaneModelCatalog.LoadSprite(appearance.NetModelId.Value, appearance.NetSkinId.Value);
            if (lookSprite != null) spriteRenderer.sprite = lookSprite;
        }

        // Get color from PlayerColorManager using clientId + localPlayerIndex
        int localIdx = LocalPlayerIndex >= 0 ? LocalPlayerIndex : 0;
        Color targetColor;
        string colorName;

        if (PlayerColorManager.Instance != null)
        {
            targetColor = PlayerColorManager.Instance.GetColor(OwnerClientId, localIdx);
            colorName = PlayerColorManager.Instance.GetColorName(OwnerClientId, localIdx);
        }
        else
        {
            // Fallback if manager not available
            int fallbackIndex = (int)OwnerClientId + localIdx;
            targetColor = PlayerColorManager.GetColorByIndex(fallbackIndex);
            colorName = PlayerColorManager.GetColorNameByIndex(fallbackIndex);
        }

        // Apply color via shader
        Shader colorReplaceShader = Shader.Find("Custom/ColorReplace");
        if (colorReplaceShader != null)
        {
            Material mat = new Material(colorReplaceShader);
            mat.SetTexture("_MainTex", spriteRenderer.sprite.texture);
            mat.SetColor("_SourceColor", Color.red);
            mat.SetColor("_TargetColor", targetColor);
            mat.SetFloat("_Threshold", 0.4f);
            spriteRenderer.material = mat;
            Debug.Log($"[PlayerController] Applied {colorName} color to player (client={OwnerClientId}, local={localIdx})");
        }
        else
        {
            Debug.LogError("[PlayerController] ColorReplace shader not found! Make sure it's in Always Included Shaders.");
        }

        // Owner glow outline + cached damage-flash material follow the sprite
        if (visualEffects == null) visualEffects = GetComponent<PlaneVisualEffects>();
        visualEffects?.OnPlaneSpriteChanged();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        All.Remove(this);
        netLocalPlayerIndex.OnValueChanged -= OnLocalPlayerIndexChanged;
        netEngineOff.OnValueChanged -= OnEngineOffChanged;
        if (appearance != null)
        {
            appearance.NetSkinId.OnValueChanged -= OnSkinChanged;
            appearance.NetModelId.OnValueChanged -= OnModelChanged;
        }
    }

    private void OnEngineOffChanged(bool previous, bool current)
    {
        if (previous != current) OnEngineStateChanged?.Invoke(current);
    }

    /// <summary>Called when the server applies a new hangar skin / shape pick.</summary>
    private void OnSkinChanged(int previousValue, int newValue) => SetPlaneColor();
    private void OnModelChanged(int previousValue, int newValue) => SetPlaneColor();

    /// <summary>
    /// Called when local player index changes - update plane color and visuals
    /// </summary>
    private void OnLocalPlayerIndexChanged(int previousValue, int newValue)
    {
        Debug.Log($"[PlayerController] LocalPlayerIndex changed from {previousValue} to {newValue}");
        // The skin claim is keyed by (client, local index), so a couch pilot
        // whose index just landed has to move its claim with it.
        if (IsServer) appearance?.ServerEnsureClaim();
        SetPlaneColor();

        // Update glow visibility (local players don't get outline)
        if (visualEffects != null)
        {
            visualEffects.UpdateGlowVisibility();
        }
    }

    /// <summary>
    /// Set the local player index (called by LocalPlayerManager after spawn)
    /// Only server can set this value.
    /// </summary>
    public void SetLocalPlayerIndex(int index)
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[PlayerController] SetLocalPlayerIndex can only be called on server");
            return;
        }

        netLocalPlayerIndex.Value = index;
        Debug.Log($"[PlayerController] Set localPlayerIndex to {index}");

        // Update player name to reflect local player
        if (index >= 0)
        {
            netPlayerName.Value = new Unity.Collections.FixedString64Bytes($"P{index + 1}");
        }
        // Note: OnLocalPlayerIndexChanged callback will update visuals on all clients
    }

    /// <summary>
    /// Get the input provider for this player (local or network)
    /// </summary>
    public IInputProvider GetInputProvider()
    {
        // A scripted pilot (the warm-up bot) wins over every device
        if (_inputOverride != null) return _inputOverride;

        // Local couch co-op player - use LocalPlayerManager
        if (IsLocalPlayer && LocalPlayerManager.Instance != null)
        {
            return LocalPlayerManager.Instance.GetInputProvider(LocalPlayerIndex);
        }

        // Network player - use InputManager singleton
        if (InputManager.Instance != null && InputManager.Instance.InputProvider != null)
        {
            return InputManager.Instance.InputProvider;
        }

        return null;
    }

    /// <summary>
    /// Get a short display name based on the device/platform.
    /// Uses device model for mobile (e.g., "iPhone 14", "Pixel 7").
    /// Falls back to platform + player number for desktop.
    /// </summary>
    private string GetDeviceDisplayName()
    {
        int playerNum = (int)OwnerClientId + 1;
        string deviceModel = SystemInfo.deviceModel;

        switch (Application.platform)
        {
            case RuntimePlatform.IPhonePlayer:
                // iOS: deviceModel returns internal name like "iPhone14,2"
                // Convert to friendly name or use as-is
                return ShortenDeviceName(deviceModel, "iPhone", 12);

            case RuntimePlatform.Android:
                // Android: deviceModel returns manufacturer + model like "Samsung SM-G998B"
                return ShortenDeviceName(deviceModel, "Android", 12);

            case RuntimePlatform.OSXPlayer:
            case RuntimePlatform.OSXEditor:
                return $"macOS #{playerNum}";

            case RuntimePlatform.WindowsPlayer:
            case RuntimePlatform.WindowsEditor:
                return $"Windows #{playerNum}";

            case RuntimePlatform.LinuxPlayer:
            case RuntimePlatform.LinuxEditor:
                return $"Linux #{playerNum}";

            case RuntimePlatform.WebGLPlayer:
                return $"Web #{playerNum}";

            default:
                return $"Player #{playerNum}";
        }
    }

    /// <summary>
    /// Shorten device name to fit in HUD, with fallback.
    /// Always appends OwnerClientId to uniquely identify the player.
    /// </summary>
    private string ShortenDeviceName(string deviceModel, string fallback, int maxLength)
    {
        int clientNum = (int)OwnerClientId + 1;

        if (string.IsNullOrEmpty(deviceModel) || deviceModel == "unknown")
            return $"{fallback}#{clientNum}";

        // Remove common prefixes and clean up device names
        string name = deviceModel
            .Replace("Apple ", "")
            .Replace("Samsung ", "")
            .Replace("Google ", "")
            .Replace("OnePlus ", "OP ")
            .Replace("Xiaomi ", "")
            .Trim();

        // iOS: Remove variant suffix (e.g., "iPhone14,2" -> "iPhone14")
        int commaIndex = name.IndexOf(',');
        if (commaIndex > 0)
            name = name.Substring(0, commaIndex);

        // Truncate if too long
        if (name.Length > maxLength)
            name = name.Substring(0, maxLength);

        // Always append client number for unique identification
        return $"{name}#{clientNum}";
    }

    void Update()
    {
        Vector3 forward = transform.TransformDirection(Vector3.left) * 10;
        Debug.DrawRay(transform.position, forward, Color.green);
    }

    private float _lastLogTime = 0f;

    void FixedUpdate() {
        // Parked in the hangar - no movement, no out-of-bounds respawn spam
        if (NetInHangar.Value)
        {
            _leanInput = _leanAligned = _leanUsable = 0f;
            _recovering = false;
            return;
        }

        // Check if we should process input:
        // - Regular network player: IsOwner must be true
        // - Local couch co-op player: IsLocalPlayer and we're the host
        bool canProcessInput = IsOwner || (IsLocalPlayer && NetworkManager.Singleton.IsHost);
        if (!canProcessInput) return;

        // Debug log every 2 seconds
        if (Time.time - _lastLogTime > 2f)
        {
            _lastLogTime = Time.time;
            Debug.Log($"[PlayerController] FixedUpdate: speed={speed}, engineOff={engineOff}, rb.velocity={rb?.linearVelocity}, IsOwner={IsOwner}, LocalPlayerIndex={LocalPlayerIndex}");
        }

        HandleMovement();
        CheckOutOfBounds();
    }

    // Safety check - respawn if plane somehow escaped the play area
    private void CheckOutOfBounds()
    {
        float safetyMargin = 50f;
        bool outOfBounds = false;

        // Check if plane is way outside the boundaries
        if (leftBoundary != null && transform.position.x < leftBoundary.bounds.min.x - safetyMargin)
            outOfBounds = true;
        if (rightBoundary != null && transform.position.x > rightBoundary.bounds.max.x + safetyMargin)
            outOfBounds = true;
        // Check lower vertical bound only (can fly into space freely)
        if (transform.position.y < -safetyMargin)
            outOfBounds = true;

        if (outOfBounds)
        {
            RequestRespawnServerRpc();
        }
    }

    [ServerRpc]
    private void RequestRespawnServerRpc()
    {
        // Player flew out of bounds - record death (the warm-up bot is not a player)
        int localIdx = LocalPlayerIndex >= 0 ? LocalPlayerIndex : 0;
        if (!IsWarmupBot) ScoreManager.Instance?.AddDeath(OwnerClientId, localIdx);
        SyncKillFeedClientRpc(OwnerClientId, localIdx, 0, 0, false, (byte)KillCause.OutOfBounds);
        HandleDeathAndRespawn();
    }

    /// <summary>
    /// Common death handler - shows explosion, resets stats, and requests respawn via CloudManager.
    /// </summary>
    private void HandleDeathAndRespawn()
    {
        OnServerDeath?.Invoke();

        visualEffects?.TriggerDamageFlash();
        ShowExplosionClientRpc();

        // Reset plane stats on respawn (server-side); crate-boosted weapon
        // tiers are lost on death - back to the hangar selection
        planeStats?.ResetStats();
        GetComponent<Shooting>()?.ResetWeaponToSelected();

        if (CloudManager.Instance != null)
        {
            CloudManager.Instance.RequestRespawn(this);
        }
        else
        {
            ExecuteRespawnAtPosition(GetFallbackSpawnPosition());
        }
    }

    private void HandleMovement()
    {
        float horizontalInput = 0f;
        float verticalInput = 0f;
        float lean = 0f;

        var inputProvider = GetInputProvider();
        if (inputProvider != null)
        {
            if (inputProvider.TryGetHeadingIntent(out float headingDeg, out float push))
            {
                PointIntent(headingDeg, push, out horizontalInput, out verticalInput);
            }
            else
            {
                horizontalInput = inputProvider.GetHorizontalInput();
                verticalInput = inputProvider.GetVerticalInput();
            }
            lean = IsBot ? 0f : inputProvider.GetLean();

            // Read every step so a request made with the engine running is
            // dropped, not kept for the next stall.
            if (inputProvider.ConsumeRecover() && engineOff && !_recovering && !IsBot) StartRecover();
        }
        else if (Time.frameCount % 300 == 0) // Log every 5 seconds at 60fps
        {
            Debug.LogWarning($"[PlayerController] No input provider available. LocalPlayerIndex={LocalPlayerIndex}");
        }

        if (_recovering && RecoverIntent(out float rx, out float rt))
        {
            horizontalInput = rx;
            verticalInput = rt;
            lean = 0f;
        }

        rotatePlane(horizontalInput, lean);
        movePlane(verticalInput);
    }

    private void StartRecover()
    {
        _recovering = true;
        float vx = rb != null ? rb.linearVelocity.x : 0f;
        _recoverDir = Mathf.Abs(vx) > 0.5f ? Mathf.Sign(vx) : (transform.right.x >= 0f ? 1f : -1f);
        _recoverStartedAt = Time.time;
        _recoverUntil = Time.time + RecoverTimeout;

        SfxManager.PlayConfirm();
        SfxManager.Haptic(HapticKind.Medium);
        BannerOverlay.Show("RECOVER!", UiTheme.Highlight);
        Telemetry.Log("stall_recover", ("result", "start"));
    }

    private void EndRecover(bool level)
    {
        _recovering = false;
        if (level) SfxManager.Haptic(HapticKind.Success);
        Telemetry.Log("stall_recover", ("result", level ? "level" : "timeout"),
            ("seconds", Mathf.Round((Time.time - _recoverStartedAt) * 10f) / 10f));
    }

    /// <summary>The autopilot's stick: engine dead = turn to the middle of
    /// the relight window, over the nose toward the travel side; engine on =
    /// pull out to level flight at full throttle. False once it is done.</summary>
    private bool RecoverIntent(out float x, out float throttle)
    {
        x = 0f;
        throttle = 0f;
        if (Time.time > _recoverUntil)
        {
            EndRecover(false);
            return false;
        }

        float target;
        if (engineOff)
        {
            var (minDeg, maxDeg) = EngineRestartHeadingWindow;
            target = (minDeg + maxDeg) * 0.5f;
        }
        else target = _recoverDir > 0f ? 0f : 180f;

        float delta = Mathf.DeltaAngle(HeadingDegrees, target);   // + = counter-clockwise
        if (engineOff && Mathf.Abs(delta) > 150f)
        {
            // Nose up: tip over toward the side the plane is moving
            delta = Mathf.Abs(delta) * -_recoverDir;
        }
        else if (!engineOff && Mathf.Abs(delta) <= RecoverLevelTolerance)
        {
            EndRecover(true);
            return false;
        }

        x = -Mathf.Clamp(delta / FlightTuning.PointGainDeg, -1f, 1f);
        throttle = engineOff ? 0f : 1f;
        return true;
    }

    /// <summary>POINT scheme (Prompts/28 §4): the stick names a direction of
    /// flight; turn toward it the short way at the edge of the envelope, and
    /// throttle to corner speed in the turn, to cruise..max on the straight.
    /// push == 0 = no direction: fly straight at cruise.</summary>
    private void PointIntent(float headingDeg, float push, out float x, out float throttle)
    {
        float target;
        if (push <= 0f || float.IsNaN(headingDeg))
        {
            x = 0f;
            target = P.CruiseSpeed;
        }
        else
        {
            float delta = Mathf.DeltaAngle(HeadingDegrees, headingDeg);   // + = counter-clockwise
            if (Mathf.Abs(delta) > 165f)
            {
                // Dead astern: keep turning the way we already turn, from rest
                // take the arc that lifts the nose (never dive into the ground).
                float sign = _omega < -1f ? 1f : _omega > 1f ? -1f
                    : (transform.right.x >= 0f ? 1f : -1f);
                delta = Mathf.Abs(delta) * sign;
            }
            x = -Mathf.Clamp(delta / FlightTuning.PointGainDeg, -1f, 1f);

            float straight = Mathf.Lerp(P.CruiseSpeed, P.MaxSpeed, push);
            target = Mathf.Lerp(straight, P.CornerSpeed, Mathf.InverseLerp(20f, 70f, Mathf.Abs(delta)));
        }
        throttle = Mathf.Clamp((target - speed) / 1.5f, -1f, 1f);
    }

    private void movePlane(float throttleInput)
    {
        float dt = Time.fixedDeltaTime;

        if (engineOff)
        {
            // Engine OFF - gravity gradually increases
            currentGravity = Mathf.MoveTowards(currentGravity, maxGravity, gravityIncreaseRate * dt);

            // Add gravity to current velocity (+ any knockback received)
            rb.linearVelocity += Vector2.down * currentGravity * dt * 60f + _externalVelocity;
            _externalVelocity = Vector2.zero;

            // Check for dive to restart engine
            var rotation = plane.transform.rotation.z;
            if (rotation > engineRestartMin && rotation < engineRestartMax && Time.time >= _relightLockedUntil)
            {
                // Diving - turn on engine and keep speed
                EngineOn();
            }
        }
        else
        {
            // Engine ON - speed changes based on flight angle
            float verticalFactor = transform.right.y;  // -1 (down) to +1 (up)
            if (verticalFactor > 0)
                speed -= verticalFactor * P.ClimbDrag * dt;    // climbing loses speed
            else
                speed -= verticalFactor * P.DiveBoost * dt;    // diving gains speed

            // Throttle input: right / forward tilt = speed up
            if (throttleInput != 0)
                speed += throttleInput * P.ThrottleRate * dt;

            // A hard turn above corner speed bleeds energy (never below it)
            speed = FlightModel.TurnBleed(P, speed, _omega, HandlingFactor, dt);

            float cap = maxSpeed;
            if (IsBoosting)
            {
                cap = P.BoostSpeed * SpeedMul;
                speed = Mathf.MoveTowards(speed, cap, 40f * dt);
            }
            else if (speed > cap)
            {
                // Coming off a boost: ease back down instead of snapping
                speed = Mathf.Max(cap, speed - 8f * dt);
                cap = speed;
            }
            speed = Mathf.Clamp(speed, minSpeed, cap);

            // If speed drops below minimum, turn off engine
            if (speed <= minSpeed && !IsBoosting)
            {
                EngineOff();
            }

            rb.linearVelocity = (Vector2)transform.right * speed + _externalVelocity;
            _externalVelocity = Vector2.MoveTowards(_externalVelocity, Vector2.zero, ImpulseDecay * dt);
        }
    }

    private void EngineOn()
    {
        // Cannot turn on engine in space
        if (inSpace) return;

        // Keep speed from the fall
        speed = Mathf.Max(minSpeed + 0.5f, rb.linearVelocity.magnitude);
        engineOff = false;
        currentGravity = 0f;
    }

    private void EngineOff()
    {
        engineOff = true;
        // Speed is not lost immediately - keep velocity
    }

    private void rotatePlane(float x, float lean = 0f)
    {
        float dt = Time.fixedDeltaTime;
        float now = Time.time;
        float ax = Mathf.Abs(x);

        // Snap: a flick to full deflection from neutral or from the opposite
        // side doubles down for a moment. Keyboard taps count as flicks; the
        // bot ramps its input and never qualifies, but is excluded anyway.
        if (!engineOff && !IsBot && P.SnapBoost > 1f && ax >= 0.85f && now >= _snapReadyAt)
        {
            float lastOpposite = x > 0f ? _lastNegAt : _lastPosAt;
            if (now - _lastNeutralAt <= 0.10f || now - lastOpposite <= 0.10f)
            {
                _snapUntil = now + P.SnapSeconds;
                _snapReadyAt = now + P.SnapCooldown;
                OnSnapTurn?.Invoke();
            }
        }
        if (ax < 0.2f) _lastNeutralAt = now;
        if (x >= 0.5f) _lastPosAt = now;
        if (x <= -0.5f) _lastNegAt = now;

        float target;
        if (engineOff)
        {
            target = x * P.StallTurnRate * HandlingFactor;
        }
        else
        {
            float snapMul = now < _snapUntil ? P.SnapBoost : 1f;
            // LEAN (Prompts/32 D1/D3): only with the engine on, only in the
            // stick's direction; snap x lean never beyond 1.8x.
            float leanMul = LeanModel.Multiplier(P, lean, x);
            target = x * FlightModel.TurnRateAt(P, speed, HandlingFactor)
                   * Mathf.Min(snapMul * leanMul, LeanModel.MaxTurnMultiplier);
        }
        _leanInput = lean;
        _leanAligned = engineOff || x == 0f ? 0f : Mathf.Max(0f, lean * Mathf.Sign(x));
        _leanUsable = engineOff ? 0f : LeanModel.Usable(lean, x);
        _omega = FlightModel.StepOmega(P, _omega, target, dt);

        // Knockback spin (Prompts/29): dissolved into the heading over ~0.2 s
        float kick = 0f;
        if (Mathf.Abs(_spinKick) > 0.01f)
        {
            float take = _spinKick * (1f - Mathf.Exp(-dt / 0.07f));
            _spinKick -= take;
            kick = take / dt;
        }
        else _spinKick = 0f;

        rb.angularVelocity = -_omega + kick;

        float angle = Mathf.Atan2(
            forwardPoint.position.y - plane.transform.position.y,
            forwardPoint.position.x - plane.transform.position.x
        ) * Mathf.Rad2Deg;

        plane.transform.rotation = Quaternion.Euler(new Vector3(0, 0, angle));
    }

    // --- external motion (Prompts/29: knockback, nitro, EMP) -----------------

    /// <summary>Server: push this plane (explosion, shockwave). The owner
    /// applies it - movement is owner-authoritative.</summary>
    public void ServerApplyImpulse(Vector2 impulse, float spinDeg, ulong attackerClientId, int attackerLocalIdx, bool hasAttacker)
    {
        if (!IsServer) return;
        if (planeStats != null && (planeStats.IsInvulnerable || planeStats.IsDodging)) return;
        if (hasAttacker)
        {
            SetLastAttacker(attackerClientId, attackerLocalIdx);
            _lastKnockbackTime = Time.time;
        }
        ApplyImpulseClientRpc(impulse, spinDeg);
    }

    [ClientRpc]
    private void ApplyImpulseClientRpc(Vector2 impulse, float spinDeg)
    {
        bool controls = IsOwner || (IsLocalPlayer && NetworkManager.Singleton.IsHost);
        if (!controls) return;
        _externalVelocity += impulse;
        _spinKick += spinDeg;
    }

    /// <summary>Server: nitro for this many seconds; relights a dead engine.</summary>
    public void ServerBoost(float seconds)
    {
        if (!IsServer) return;
        BoostClientRpc(seconds);
    }

    [ClientRpc]
    private void BoostClientRpc(float seconds)
    {
        bool controls = IsOwner || (IsLocalPlayer && NetworkManager.Singleton.IsHost);
        if (!controls) return;
        _boostUntil = Time.time + seconds;
        if (engineOff && !inSpace)
        {
            speed = Mathf.Max(P.CornerSpeed, rb != null ? rb.linearVelocity.magnitude : 0f);
            engineOff = false;
            currentGravity = 0f;
        }
    }

    /// <summary>Server: kill the engine (EMP); no relight for lockSeconds.</summary>
    public void ServerForceEngineOff(float lockSeconds)
    {
        if (!IsServer) return;
        ForceEngineOffClientRpc(lockSeconds);
    }

    private float _relightLockedUntil;

    [ClientRpc]
    private void ForceEngineOffClientRpc(float lockSeconds)
    {
        bool controls = IsOwner || (IsLocalPlayer && NetworkManager.Singleton.IsHost);
        if (!controls) return;
        _boostUntil = 0f;
        _relightLockedUntil = Time.time + lockSeconds;
        EngineOff();
    }

    public void Hit(int damage)
    {
        if (!IsServer)
            return;

        if (planeStats != null)
        {
            bool dead = planeStats.TakeDamage(damage);
            if (dead)
            {
                HandleCombatDeath(KillCause.Shot);
            }
            else
            {
                visualEffects?.TriggerDamageFlash(planeStats.LastDamageAbsorbedByShield);
            }
        }
        else
        {
            // Fallback: instant kill if PlaneStats not attached
            HandleCombatDeath(KillCause.Shot);
        }
    }

    /// <summary>
    /// Set the last attacker for kill attribution. Called by Bullet before Hit().
    /// </summary>
    public void SetLastAttacker(ulong clientId, int localPlayerIndex)
    {
        _lastAttackerClientId = clientId;
        _lastAttackerLocalPlayerIndex = localPlayerIndex;
        _hasLastAttacker = true;
        _lastAttackerTime = Time.time;
    }

    /// <summary>
    /// Handle death from combat (bullet or plane collision). Drops power-up.
    /// </summary>
    private void HandleCombatDeath(KillCause cause)
    {
        // Record death for this player (the warm-up bot is not a player)
        int localIdx = LocalPlayerIndex >= 0 ? LocalPlayerIndex : 0;
        if (!IsWarmupBot) ScoreManager.Instance?.AddDeath(OwnerClientId, localIdx);

        // Attribute kill to last attacker (only recent hits count). A warm-up
        // bot kill stays credited for the feed line but never scores; combat
        // bots score and are scored on like anyone else.
        bool credited = _hasLastAttacker && Time.time - _lastAttackerTime <= KillAttributionWindow &&
                        !IsSelf(_lastAttackerClientId, _lastAttackerLocalPlayerIndex);
        if (credited && !IsWarmupBot && !IsWarmupBotIdentity(_lastAttackerClientId, _lastAttackerLocalPlayerIndex))
        {
            ScoreManager.Instance?.AddScore(_lastAttackerClientId, _lastAttackerLocalPlayerIndex);
        }
        _hasLastAttacker = false;

        SyncKillFeedClientRpc(OwnerClientId, localIdx,
            _lastAttackerClientId, _lastAttackerLocalPlayerIndex, credited, (byte)cause);

        // Spawn power-up at death position - not for the warm-up bot, or the
        // warm-up sky fills with crates nobody earned
        if (PowerUpManager.Instance != null && !IsWarmupBot)
        {
            PowerUpManager.Instance.SpawnPowerUp(transform.position);
        }

        HandleDeathAndRespawn();
    }

    /// <summary>
    /// Broadcast a kill-feed line to all clients. Server-only caller.
    /// </summary>
    [ClientRpc]
    private void SyncKillFeedClientRpc(ulong victimClientId, int victimLocalIdx,
        ulong killerClientId, int killerLocalIdx, bool hasKiller, byte cause)
    {
        GameHUD.Instance?.PushKillFeed(victimClientId, victimLocalIdx,
            killerClientId, killerLocalIdx, hasKiller, (KillCause)cause);
    }

    /// <summary>
    /// Get fallback spawn position when CloudManager is not available.
    /// Uses LocalPlayerIndex to differentiate local co-op players.
    /// </summary>
    private Vector3 GetFallbackSpawnPosition()
    {
        int playerIndex = LocalPlayerIndex >= 0 ? LocalPlayerIndex : (int)OwnerClientId;
        float[] positions = { -15f, -8f, 8f, 15f };
        return new Vector3(positions[playerIndex % positions.Length], 10f, 0f);
    }

    /// <summary>
    /// Called by CloudManager when a spawn position is assigned.
    /// </summary>
    public void ExecuteRespawnAtPosition(Vector3 position)
    {
        RespawnAtPositionClientRpc(position);
    }

    [ClientRpc]
    private void ShowExplosionClientRpc()
    {
        SfxManager.PlayExplosion();

        // Vibrate on the device(s) controlling this plane - the host owns
        // the bot too, and its phone must not buzz for a bot wreck
        if (IsOwner && !IsBot)
        {
            SfxManager.Haptic();
        }

        // Falling burning wreck (local visual; the live plane teleports away
        // to its respawn point). Speed is a synced NetworkVariable, so the
        // wreck inherits roughly the real flight velocity on every client.
        var planeSprite = plane != null ? plane.GetComponent<SpriteRenderer>() : null;
        if (planeSprite != null)
        {
            Vector2 wreckVelocity = (Vector2)(transform.right * Speed * 0.6f);
            WreckEffect.Spawn(planeSprite, transform.position,
                plane != null ? plane.rotation : transform.rotation,
                wreckVelocity, hitEffect);
        }

        if (hitEffect != null)
        {
            Vector3 explosionPos = new Vector3(transform.position.x, transform.position.y, 0f);
            var effect = Instantiate(hitEffect, explosionPos, Quaternion.identity);

            var spriteRenderer = effect.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingOrder = 100;
            }
            var particleRenderer = effect.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer != null)
            {
                particleRenderer.sortingOrder = 100;
            }

            Destroy(effect, 0.5f);
        }
    }

    [ClientRpc]
    private void RespawnAtPositionClientRpc(Vector3 position)
    {
        // Only owner can teleport (ClientNetworkTransform = owner authority)
        if (!IsOwner) return;

        Quaternion newRotation = Quaternion.Euler(0, 0, 0);

        if (networkTransform != null)
        {
            networkTransform.Teleport(position, newRotation, transform.localScale);
        }
        else
        {
            transform.position = position;
            transform.rotation = newRotation;
        }

        // Reset state
        _externalVelocity = Vector2.zero;
        _spinKick = 0f;
        _omega = 0f;
        _boostUntil = 0f;
        _relightLockedUntil = 0f;
        _recovering = false;
        speed = defaultSpeed;
        engineOff = false;
        inSpace = false;
        currentGravity = 0f;

        if (rb != null)
        {
            rb.linearVelocity = transform.right * speed;
        }

        Debug.Log($"[PlayerController] Respawned player {OwnerClientId} (local={LocalPlayerIndex}) at {position}");
    }

    [ClientRpc]
    private void LeaveSpaceClientRpc()
    {
        if (!IsOwner) return;
        inSpace = false;
    }

    [ClientRpc]
    private void WrapToPositionClientRpc(float targetX)
    {
        // Only owner can teleport (ClientNetworkTransform = client authority)
        if (!IsOwner) return;

        Vector3 newPos = new Vector3(targetX, transform.position.y, 0f);
        networkTransform.Teleport(newPos, transform.rotation, transform.localScale);
    }

    // Calculate the offset based on collider bounds (accounts for rotation)
    private float GetPlaneHalfWidth()
    {
        if (planeCollider != null)
        {
            return planeCollider.bounds.extents.x;
        }
        return 0.5f; // fallback
    }

    [ClientRpc]
    private void SpaceClientRpc()
    {
        if (!IsOwner) return;
        inSpace = true;
        EngineOff();
    }

    void OnTriggerEnter2D(Collider2D collider)
    {
        if (!IsServer)
            return;

        if (collider.gameObject.layer == LayerMask.NameToLayer("Foreground"))
            return;

        if (ArenaBounds.IsSpace(collider))
        {
            SpaceClientRpc();
        }

        if (ArenaBounds.Is(collider, ArenaBounds.Edge.Left) && rightBoundary != null)
        {
            // Hit left edge -> wrap to right side
            float margin = 0.1f;
            float halfWidth = GetPlaneHalfWidth();
            float targetX = rightBoundary.bounds.min.x - halfWidth - margin;
            WrapToPositionClientRpc(targetX);
        }

        if (ArenaBounds.Is(collider, ArenaBounds.Edge.Right) && leftBoundary != null)
        {
            // Hit right edge -> wrap to left side
            float margin = 0.1f;
            float halfWidth = GetPlaneHalfWidth();
            float targetX = leftBoundary.bounds.max.x + halfWidth + margin;
            WrapToPositionClientRpc(targetX);
        }


        if (collider.gameObject.CompareTag("Bullet"))
        {
            // Bullet hit - damage pipeline handled by Bullet.cs calling Hit()
            // Don't HandleDeathAndRespawn here - Bullet.cs calls damagable.Hit() which handles it
            return;
        }

        if (collider.gameObject.CompareTag("Ground"))
        {
            // Knocked into the ground by someone (Prompts/29 D3): that is a
            // kill for them. Otherwise a plain crash - no power-up drop.
            // HandleCombatDeath already keeps warm-up bot kills out of the score.
            // Only a knockback counts - a crash after a plain hit is a crash.
            bool slammed = _hasLastAttacker && Time.time - _lastKnockbackTime <= SlamAttributionWindow &&
                           !IsSelf(_lastAttackerClientId, _lastAttackerLocalPlayerIndex);
            if (slammed)
            {
                HandleCombatDeath(KillCause.Slammed);
            }
            else
            {
                int localIdx = LocalPlayerIndex >= 0 ? LocalPlayerIndex : 0;
                if (!IsWarmupBot) ScoreManager.Instance?.AddDeath(OwnerClientId, localIdx);
                SyncKillFeedClientRpc(OwnerClientId, localIdx, 0, 0, false, (byte)KillCause.Ground);
                HandleDeathAndRespawn();
            }
        }

        if (collider.gameObject.CompareTag("Player"))
        {
            // Collided with another plane - instant kill, drops power-up.
            // The human half of a bot collision still counts, on its own object.
            int localIdx = LocalPlayerIndex >= 0 ? LocalPlayerIndex : 0;
            if (!IsWarmupBot) ScoreManager.Instance?.AddPlaneCollision(OwnerClientId, localIdx);
            HandleCombatDeath(KillCause.Collision);
        }

        if (collider.gameObject.CompareTag("PowerUp"))
        {
            // Power-up pickup handled by PowerUp.cs OnTriggerEnter2D
            return;
        }

    }

    void OnTriggerExit2D(Collider2D collider)
    {
        if (!IsServer)
            return;

        if (ArenaBounds.IsSpace(collider))
        {
            LeaveSpaceClientRpc();
        }
    }

    /// <summary>
    /// Sync score to all clients. Called by ScoreManager.
    /// </summary>
    [ClientRpc]
    public void SyncScoreClientRpc(ulong scorerClientId, int localPlayerIndex, int newScore)
    {
        // Update local ScoreManager on clients
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.UpdateScoreFromServer(scorerClientId, localPlayerIndex, newScore);
        }
    }

    /// <summary>
    /// Sync match start to all clients. Called by ScoreManager.
    /// </summary>
    [ClientRpc]
    public void SyncMatchStartClientRpc()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.StartMatchFromServer();
        }
    }

    /// <summary>
    /// Sync player stats to all clients (or one, via rpcParams). Called by ScoreManager.
    /// </summary>
    [ClientRpc]
    public void SyncStatsClientRpc(ulong clientId, int localPlayerIndex, int kills, int deaths, int planeCollisions, ClientRpcParams rpcParams = default)
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.UpdateStatsFromServer(clientId, localPlayerIndex, kills, deaths, planeCollisions);
        }
    }

    /// <summary>Client asks for the game-state snapshot once its player object is ready.</summary>
    [ServerRpc]
    public void RequestStateSnapshotServerRpc(ServerRpcParams rpcParams = default)
    {
        MatchManager.Instance?.ServerSendSnapshotTo(rpcParams.Receive.SenderClientId, this);
    }

    /// <summary>Owner pressed JOIN in the late-join hangar - deploy now.</summary>
    [ServerRpc]
    public void RequestDeployServerRpc(ServerRpcParams rpcParams = default)
    {
        MatchManager.Instance?.ServerRequestDeploy(rpcParams.Receive.SenderClientId);
    }

    /// <summary>Game-phase sync, broadcast or targeted. Called by MatchManager.</summary>
    [ClientRpc]
    public void SyncGamePhaseClientRpc(byte phase, float secondsRemaining, ClientRpcParams rpcParams = default)
    {
        MatchManager.Instance?.HandlePhaseFromServer((MatchManager.GamePhase)phase, secondsRemaining);
    }

    /// <summary>Open the personal late-join hangar on the target client. Called by MatchManager.</summary>
    [ClientRpc]
    public void SyncLateJoinHangarClientRpc(int seconds, ClientRpcParams rpcParams = default)
    {
        MatchManager.Instance?.HandleLateJoinHangarFromServer(seconds);
    }

    /// <summary>Adopt match state WITHOUT clearing stats (late-join snapshot / freeze).</summary>
    [ClientRpc]
    public void SyncMatchStateClientRpc(bool started, float matchTime, ClientRpcParams rpcParams = default)
    {
        ScoreManager.Instance?.SetMatchStateFromServer(started, matchTime);
    }

    /// <summary>One round-wins entry (late-join snapshot). Called by MatchManager.</summary>
    [ClientRpc]
    public void SyncRoundWinsClientRpc(ulong clientId, int wins, ClientRpcParams rpcParams = default)
    {
        MatchManager.Instance?.HandleRoundWinsFromServer(clientId, wins);
    }

    /// <summary>
    /// Sync round end (winner + run progress) to all clients. Called by MatchManager.
    /// </summary>
    [ClientRpc]
    public void SyncMatchEndClientRpc(ulong winnerClientId, int winnerLocalPlayerIndex,
        int winnerKills, int winnerRoundWins, bool runOver)
    {
        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.HandleMatchEndFromServer(winnerClientId, winnerLocalPlayerIndex,
                winnerKills, winnerRoundWins, runOver);
        }
    }

    /// <summary>Owner buys a run upgrade in the hangar - forward to the server.</summary>
    [ServerRpc]
    public void RequestBuyUpgradeServerRpc(int upgradeType, ServerRpcParams rpcParams = default)
    {
        MatchManager.Instance?.ServerBuyUpgrade(rpcParams.Receive.SenderClientId, upgradeType);
    }

    /// <summary>Sync a client's run points + upgrade levels. Called by MatchManager.</summary>
    [ClientRpc]
    public void SyncRunStateClientRpc(ulong clientId, int points,
        int shieldLevel, int hullLevel, int fireRateLevel, int engineLevel,
        ClientRpcParams rpcParams = default)
    {
        MatchManager.Instance?.HandleRunStateFromServer(clientId, points,
            shieldLevel, hullLevel, fireRateLevel, engineLevel);
    }

    /// <summary>Run over - clear run state everywhere. Called by MatchManager.</summary>
    [ClientRpc]
    public void SyncRunResetClientRpc()
    {
        MatchManager.Instance?.HandleRunResetFromServer();
    }

    /// <summary>
    /// Round-restart respawn: fresh stats + new position, no death recorded and
    /// no explosion. Server-only (called by MatchManager).
    /// </summary>
    public void ServerRespawn()
    {
        if (!IsServer) return;

        planeStats?.ResetStats();
        // New round starts clean: crate-boosted tiers drop back to the hangar pick
        GetComponent<Shooting>()?.ResetWeaponToSelected();

        if (CloudManager.Instance != null)
        {
            CloudManager.Instance.RequestRespawn(this);
        }
        else
        {
            ExecuteRespawnAtPosition(GetFallbackSpawnPosition());
        }
    }

    /// <summary>Owner pressed READY in the hangar - forward to the server.</summary>
    [ServerRpc]
    public void SetHangarReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        MatchManager.Instance?.ServerSetClientReady(rpcParams.Receive.SenderClientId);
    }

    /// <summary>Broadcast a client's READY state to everyone. Called by MatchManager.</summary>
    [ClientRpc]
    public void SyncHangarReadyClientRpc(ulong readyClientId)
    {
        MatchManager.Instance?.HandleClientReadyFromServer(readyClientId);
    }
}
