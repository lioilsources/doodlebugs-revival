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

    // Profile-aware properties
    private PilotMaturityProfile Profile => PilotMaturityManager.Instance?.CurrentProfile;
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

    /// <summary>
    /// Get the display name for this player (device name, shortened)
    /// </summary>
    public string PlayerName => netPlayerName.Value.ToString();

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

    public GameObject hitEffect;

    // Visual effects controller
    private PlaneVisualEffects visualEffects;

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
        SetPlaneColor();

        // Cache visual effects reference
        visualEffects = GetComponent<PlaneVisualEffects>();

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

    // Player colors for up to 4 players
    private static readonly Color[] PlayerColors = new Color[]
    {
        new Color(0.3f, 0.5f, 1f),    // Blue (Player 1 / Host)
        new Color(1f, 0.3f, 0.3f),    // Red (Player 2) - original sprite color, no shader needed
        new Color(0.3f, 0.9f, 0.3f),  // Green (Player 3)
        new Color(1f, 0.8f, 0.2f)     // Yellow (Player 4)
    };

    private void SetPlaneColor()
    {
        if (plane == null) return;

        var spriteRenderer = plane.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) return;

        int playerIndex = (int)OwnerClientId;

        // Player 2 (index 1) keeps original red color - no shader needed
        if (playerIndex == 1)
        {
            Debug.Log($"[PlayerController] Player 2 keeps original red color");
            return;
        }

        // All other players get color replaced via shader
        Color targetColor = playerIndex < PlayerColors.Length ? PlayerColors[playerIndex] : Color.white;

        Shader colorReplaceShader = Shader.Find("Custom/ColorReplace");
        if (colorReplaceShader != null)
        {
            Material mat = new Material(colorReplaceShader);
            mat.SetTexture("_MainTex", spriteRenderer.sprite.texture);
            mat.SetColor("_SourceColor", Color.red);
            mat.SetColor("_TargetColor", targetColor);
            mat.SetFloat("_Threshold", 0.4f);
            spriteRenderer.material = mat;
            Debug.Log($"[PlayerController] Applied {GetColorName(playerIndex)} color shader to Player {playerIndex + 1}");
        }
        else
        {
            Debug.LogError("[PlayerController] ColorReplace shader not found! Make sure it's in Always Included Shaders.");
        }
    }

    private string GetColorName(int playerIndex)
    {
        switch (playerIndex)
        {
            case 0: return "blue";
            case 1: return "red";
            case 2: return "green";
            case 3: return "yellow";
            default: return "white";
        }
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
    /// </summary>
    private string ShortenDeviceName(string deviceModel, string fallback, int maxLength)
    {
        if (string.IsNullOrEmpty(deviceModel) || deviceModel == "unknown")
            return $"{fallback} #{(int)OwnerClientId + 1}";

        // Remove common prefixes
        string name = deviceModel
            .Replace("Apple ", "")
            .Replace("Samsung ", "")
            .Replace("Google ", "")
            .Replace("OnePlus ", "OP ")
            .Replace("Xiaomi ", "")
            .Trim();

        // Truncate if too long
        if (name.Length > maxLength)
            name = name.Substring(0, maxLength);

        return name;
    }

    void Update()
    {
        Vector3 forward = transform.TransformDirection(Vector3.left) * 10;
        Debug.DrawRay(transform.position, forward, Color.green);
    }

    private float _lastLogTime = 0f;

    void FixedUpdate() {
        if (!IsOwner) return;

        // Debug log every 2 seconds
        if (Time.time - _lastLogTime > 2f)
        {
            _lastLogTime = Time.time;
            Debug.Log($"[PlayerController] FixedUpdate: speed={speed}, engineOff={engineOff}, rb.velocity={rb?.linearVelocity}, IsOwner={IsOwner}");
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
        ScoreManager.Instance?.AddDeath(OwnerClientId);
        RespawnWithExplosionClientRpc();
    }

    private void HandleMovement()
    {
        float horizontalInput;
        float verticalInput;
        if (InputManager.Instance != null && InputManager.Instance.InputProvider != null)
        {
            horizontalInput = InputManager.Instance.InputProvider.GetHorizontalInput();
            verticalInput = InputManager.Instance.InputProvider.GetVerticalInput();
        }
        else
        {
            horizontalInput = Input.GetAxis("Horizontal");
            verticalInput = Input.GetAxis("Vertical");
            if (Time.frameCount % 300 == 0) // Log every 5 seconds at 60fps
            {
                Debug.LogWarning($"[PlayerController] InputManager not available, using fallback input. Instance: {InputManager.Instance != null}");
            }
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

        // Rotation speed proportional to plane speed
        // Faster rotation when engine is off (multiplier varies by profile)
        float speedFactor = rb.linearVelocity.magnitude / defaultSpeed;  // 1.0 at defaultSpeed
        float currentRotateSpeed = engineOff
            ? rotateSpeed * engineOffRotateMultiplier
            : rotateSpeed * speedFactor;

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

        visualEffects?.TriggerDamageFlash();
        RespawnWithExplosionClientRpc();
    }

    [ClientRpc]
    private void RespawnWithExplosionClientRpc()
    {
        // Show explosion effect on all clients
        if (hitEffect != null)
        {
            // Spawn at plane position but with z=0 to ensure visibility
            Vector3 explosionPos = new Vector3(transform.position.x, transform.position.y, 0f);
            var effect = Instantiate(hitEffect, explosionPos, Quaternion.identity);

            // Ensure explosion is on top (visible) - set sorting order
            var spriteRenderer = effect.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingOrder = 100;
            }
            // Also check particle system renderer
            var particleRenderer = effect.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer != null)
            {
                particleRenderer.sortingOrder = 100;
            }

            Destroy(effect, 0.5f);
        }
        else
        {
            Debug.LogWarning("[PlayerController] hitEffect is null!");
        }

        // Only owner can teleport (ClientNetworkTransform = owner authority)
        if (!IsOwner) return;

        // Spawn behind a random cloud if available
        Vector3 newPos;
        if (CloudManager.Instance != null && CloudManager.Instance.AreCloudsReady())
        {
            var cloudPos = CloudManager.Instance.GetRandomCloudPosition();
            // Spawn behind cloud (left of it) with slight random offset
            float offsetX = Random.Range(-5f, -2f);
            float offsetY = Random.Range(-1f, 1f);
            newPos = new Vector3(cloudPos.x + offsetX, cloudPos.y + offsetY, 0f);
        }
        else
        {
            // Fallback: different spawn position for each player
            float spawnX = (OwnerClientId == 0) ? -15f : 15f;
            newPos = new Vector3(spawnX, 10f, 0f);
        }

        // Both players face right (z=0), they spawn on opposite sides
        Quaternion newRotation = Quaternion.Euler(0, 0, 0);

        // Owner does the teleport (ClientNetworkTransform = owner authority)
        if (networkTransform != null)
        {
            networkTransform.Teleport(newPos, newRotation, transform.localScale);
        }
        else
        {
            transform.position = newPos;
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

        Debug.Log($"[PlayerController] Respawned player {OwnerClientId} at {newPos}, speed={speed}");
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
            // Bullet hit - stats handled by Bullet.cs (gives kill to shooter)
            visualEffects?.TriggerDamageFlash();
            RespawnWithExplosionClientRpc();
        }

        if (collider.gameObject.CompareTag("Respawn") || collider.gameObject.CompareTag("Ground"))
        {
            // Crashed into ground/obstacle - record death
            ScoreManager.Instance?.AddDeath(OwnerClientId);
            visualEffects?.TriggerDamageFlash();
            RespawnWithExplosionClientRpc();
        }

        if (collider.gameObject.CompareTag("Player"))
        {
            // Collided with another plane
            ScoreManager.Instance?.AddPlaneCollision(OwnerClientId);
            visualEffects?.TriggerDamageFlash();
            RespawnWithExplosionClientRpc();
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
    public void SyncScoreClientRpc(ulong scorerClientId, int newScore)
    {
        // Update local ScoreManager on clients
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.UpdateScoreFromServer(scorerClientId, newScore);
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
    public void SyncStatsClientRpc(ulong clientId, int kills, int deaths, int planeCollisions)
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.UpdateStatsFromServer(clientId, kills, deaths, planeCollisions);
        }
    }
}
