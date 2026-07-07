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

    // Base values (can be overridden by maturity profile)
    private float baseRotateSpeed = 200f;
    private float defaultSpeed = 5f;
    private float baseMaxSpeed = 20f;
    private float minSpeed = 2f;
    private float climbDrag = 1f;       // how fast speed decreases when climbing
    private float diveBoost = 3f;       // how fast speed increases when diving
    private float throttleRate = 5f;    // how fast throttle changes speed
    private float baseMaxGravity = 0.5f;
    private float baseGravityIncreaseRate = 0.35f;  // how fast gravity increases
    private float baseEngineOffRotateMultiplier = 16f;  // Novice default: 50 * 16 = 800
    private float baseEngineRestartMin = -0.8f;
    private float baseEngineRestartMax = -0.6f;

    // Profile-aware properties (uses player's own maturity level, not global)
    private PilotMaturityProfile Profile => PilotMaturityManager.Instance?.GetProfile(_maturityLevel);
    private float rotateSpeed => Profile?.rotateSpeed ?? baseRotateSpeed;
    private float maxSpeed => Profile?.maxSpeed ?? baseMaxSpeed;
    private float maxGravity => Profile?.maxGravity ?? baseMaxGravity;
    private float gravityIncreaseRate => Profile?.gravityIncreaseRate ?? baseGravityIncreaseRate;
    private float engineOffRotateMultiplier => Profile?.engineOffRotateMultiplier ?? baseEngineOffRotateMultiplier;
    private float engineRestartMin => Profile?.engineRestartMinRotation ?? baseEngineRestartMin;
    private float engineRestartMax => Profile?.engineRestartMaxRotation ?? baseEngineRestartMax;

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
    public void CheckMaturityUpgrade(int totalKills)
    {
        PilotMaturityLevel newLevel = _maturityLevel;

        if (totalKills >= 20)
            newLevel = PilotMaturityLevel.Expert;
        else if (totalKills >= 10)
            newLevel = PilotMaturityLevel.Advanced;

        if (newLevel != _maturityLevel)
        {
            _maturityLevel = newLevel;
            Debug.Log($"[PlayerController] Player {OwnerClientId}_{LocalPlayerIndex} maturity upgraded to {newLevel}");
            OnMaturityChanged?.Invoke(newLevel);
        }
    }

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

    // Public accessors for EngineAudio
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
    private ulong _lastAttackerClientId;
    private int _lastAttackerLocalPlayerIndex;
    private bool _hasLastAttacker;
    private float _lastAttackerTime;

    // Cached boundary references
    private Collider2D leftBoundary;
    private Collider2D rightBoundary;
    private BoxCollider2D planeCollider;

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        networkTransform = GetComponent<ClientNetworkTransform>();
        planeCollider = GetComponent<BoxCollider2D>();

        // Cache boundary references (get Collider2D to use bounds)
        var leftObj = GameObject.Find("Left");
        var rightObj = GameObject.Find("Right");
        if (leftObj != null) leftBoundary = leftObj.GetComponent<Collider2D>();
        if (rightObj != null) rightBoundary = rightObj.GetComponent<Collider2D>();

        // Limit FPS for stability
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Subscribe to local player index changes to update color when set
        netLocalPlayerIndex.OnValueChanged += OnLocalPlayerIndexChanged;

        // Set initial color (may be updated when LocalPlayerIndex is set)
        SetPlaneColor();

        // Cache visual effects reference
        visualEffects = GetComponent<PlaneVisualEffects>();

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

        // Initialize movement for owner (deferred to avoid NetworkVariable timing issues)
        if (IsOwner)
        {
            StartCoroutine(InitializeOwnerDelayed());
        }
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

        // Set player display name based on device/platform
        netPlayerName.Value = new Unity.Collections.FixedString64Bytes(GetDeviceDisplayName());
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
    }

    private void SetPlaneColor()
    {
        if (plane == null) return;

        var spriteRenderer = plane.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) return;

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
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        netLocalPlayerIndex.OnValueChanged -= OnLocalPlayerIndexChanged;
    }

    /// <summary>
    /// Called when local player index changes - update plane color and visuals
    /// </summary>
    private void OnLocalPlayerIndexChanged(int previousValue, int newValue)
    {
        Debug.Log($"[PlayerController] LocalPlayerIndex changed from {previousValue} to {newValue}");
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
    private IInputProvider GetInputProvider()
    {
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
        // Player flew out of bounds - record death
        int localIdx = LocalPlayerIndex >= 0 ? LocalPlayerIndex : 0;
        ScoreManager.Instance?.AddDeath(OwnerClientId, localIdx);
        SyncKillFeedClientRpc(OwnerClientId, localIdx, 0, 0, false, (byte)KillCause.OutOfBounds);
        HandleDeathAndRespawn();
    }

    /// <summary>
    /// Common death handler - shows explosion, resets stats, and requests respawn via CloudManager.
    /// </summary>
    private void HandleDeathAndRespawn()
    {
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

        var inputProvider = GetInputProvider();
        if (inputProvider != null)
        {
            horizontalInput = inputProvider.GetHorizontalInput();
            verticalInput = inputProvider.GetVerticalInput();
        }
        else if (Time.frameCount % 300 == 0) // Log every 5 seconds at 60fps
        {
            Debug.LogWarning($"[PlayerController] No input provider available. LocalPlayerIndex={LocalPlayerIndex}");
        }

        rotatePlane(horizontalInput);
        movePlane(verticalInput);
    }

    private void movePlane(float throttleInput)
    {
        if (engineOff)
        {
            // Engine OFF - gravity gradually increases
            currentGravity = Mathf.MoveTowards(currentGravity, maxGravity, gravityIncreaseRate * Time.fixedDeltaTime);

            // Add gravity to current velocity
            rb.linearVelocity += Vector2.down * currentGravity * Time.fixedDeltaTime * 60f;

            // Check for dive to restart engine
            var rotation = plane.transform.rotation.z;
            if (rotation > engineRestartMin && rotation < engineRestartMax)
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
            {
                // Climbing - loses speed
                speed -= verticalFactor * climbDrag * Time.fixedDeltaTime;
            }
            else
            {
                // Diving - gains speed
                speed -= verticalFactor * diveBoost * Time.fixedDeltaTime;
            }

            // Throttle input: forward tilt = speed up, backward tilt = slow down
            if (throttleInput != 0)
            {
                speed += throttleInput * throttleRate * Time.fixedDeltaTime;
            }

            // Clamp speed
            speed = Mathf.Clamp(speed, minSpeed, maxSpeed);

            // If speed drops below minimum, turn off engine
            if (speed <= minSpeed)
            {
                EngineOff();
            }

            rb.linearVelocity = transform.right * speed;
        }
    }

    private void EngineOn()
    {
        // Cannot turn on engine in space
        if (inSpace) return;

        // Keep speed from the fall
        speed = rb.linearVelocity.magnitude;
        engineOff = false;
        currentGravity = 0f;
    }

    private void EngineOff()
    {
        engineOff = true;
        // Speed is not lost immediately - keep velocity
    }

    private void rotatePlane(float x)
    {
        float angle;
        Vector2 direction = new Vector2(0, 0);

        // turn left/right
        if (x < 0)
        {
            direction = (Vector2)leftPoint.position - rb.position;
        }
        if (x > 0)
        {
            direction = (Vector2)rightPoint.position - rb.position;
        }

        direction.Normalize();
        angle = Vector3.Cross(direction, transform.up).z;

        // Rotation speed proportional to plane speed, scaled by handling
        // Faster rotation when engine is off (multiplier varies by profile)
        float handlingFactor = planeStats != null ? planeStats.Handling : 1f;
        float speedFactor = rb.linearVelocity.magnitude / defaultSpeed;  // 1.0 at defaultSpeed
        float currentRotateSpeed = engineOff
            ? rotateSpeed * engineOffRotateMultiplier * handlingFactor
            : rotateSpeed * speedFactor * handlingFactor;

        // turn on/off - proportional to input strength
        if (x != 0)
        {
            rb.angularVelocity = -currentRotateSpeed * angle * Mathf.Abs(x);
        }
        else
        {
            rb.angularVelocity = 0;
        }

        angle = Mathf.Atan2(
            forwardPoint.position.y - plane.transform.position.y,
            forwardPoint.position.x - plane.transform.position.x
        ) * Mathf.Rad2Deg;

        plane.transform.rotation = Quaternion.Euler(new Vector3(0, 0, angle));
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
        // Record death for this player
        int localIdx = LocalPlayerIndex >= 0 ? LocalPlayerIndex : 0;
        ScoreManager.Instance?.AddDeath(OwnerClientId, localIdx);

        // Attribute kill to last attacker (only recent hits count)
        bool credited = _hasLastAttacker && Time.time - _lastAttackerTime <= KillAttributionWindow;
        if (credited)
        {
            ScoreManager.Instance?.AddScore(_lastAttackerClientId, _lastAttackerLocalPlayerIndex);
        }
        _hasLastAttacker = false;

        SyncKillFeedClientRpc(OwnerClientId, localIdx,
            _lastAttackerClientId, _lastAttackerLocalPlayerIndex, credited, (byte)cause);

        // Spawn power-up at death position
        if (PowerUpManager.Instance != null)
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

        // Vibrate on the device(s) controlling this plane
        if (IsOwner)
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

        if (collider.name == "Space")
        {
            SpaceClientRpc();
        }

        if (collider.name == "Left" && rightBoundary != null)
        {
            // Hit left edge -> wrap to right side
            float margin = 0.1f;
            float halfWidth = GetPlaneHalfWidth();
            float targetX = rightBoundary.bounds.min.x - halfWidth - margin;
            WrapToPositionClientRpc(targetX);
        }

        if (collider.name == "Right" && leftBoundary != null)
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
            // Crashed into ground/obstacle - instant kill, no power-up drop
            int localIdx = LocalPlayerIndex >= 0 ? LocalPlayerIndex : 0;
            ScoreManager.Instance?.AddDeath(OwnerClientId, localIdx);
            SyncKillFeedClientRpc(OwnerClientId, localIdx, 0, 0, false, (byte)KillCause.Ground);
            HandleDeathAndRespawn();
        }

        if (collider.gameObject.CompareTag("Player"))
        {
            // Collided with another plane - instant kill, drops power-up
            int localIdx = LocalPlayerIndex >= 0 ? LocalPlayerIndex : 0;
            ScoreManager.Instance?.AddPlaneCollision(OwnerClientId, localIdx);
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

        if (collider.name == "Space")
        {
            LeaveSpaceClientRpc();
        }
    }

    private void HandleExitGame()
    {
        //if (Input.GetKeyDown(KeyCode.Escape))
        //{
        //    // Exit the network state and return to the menu
        //    if (IsServer) // Host
        //    {
        //        // All player should shutdown and exit
        //        StartCoroutine(HostShutdown());
        //    }
        //    else
        //    {
        //        Shutdown();
        //    }
        //}
    }

    IEnumerator HostShutdown()
    {
        // Tell the clients to shutdown
        ShutdownClientRpc();

        // Wait some time for the message to get to clients
        yield return new WaitForSeconds(0.5f);

        // Shutdown server/host
        Shutdown();
    }

    // Shutdown the network session and load the menu scene
    void Shutdown()
    {
        NetworkManager.Singleton.Shutdown();
        // TODO
        //LoadingSceneManager.Instance.LoadScene(SceneName.Menu, false);
    }

    [ClientRpc]
    void ShutdownClientRpc()
    {
        if (IsServer)
            return;

        Shutdown();
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
    /// Sync player stats to all clients. Called by ScoreManager.
    /// </summary>
    [ClientRpc]
    public void SyncStatsClientRpc(ulong clientId, int localPlayerIndex, int kills, int deaths, int planeCollisions)
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.UpdateStatsFromServer(clientId, localPlayerIndex, kills, deaths, planeCollisions);
        }
    }

    /// <summary>
    /// Sync round end (winner) to all clients. Called by MatchManager.
    /// </summary>
    [ClientRpc]
    public void SyncMatchEndClientRpc(ulong winnerClientId, int winnerLocalPlayerIndex, int winnerKills)
    {
        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.HandleMatchEndFromServer(winnerClientId, winnerLocalPlayerIndex, winnerKills);
        }
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
