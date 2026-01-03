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
    public float rotateSpeed = 200f;

    private float defaultSpeed = 5f;
    private float maxSpeed = 20f;
    private float minSpeed = 2f;
    private float climbDrag = 1f;       // how fast speed decreases when climbing
    private float diveBoost = 3f;       // how fast speed increases when diving
    private float throttleRate = 5f;    // how fast throttle changes speed
    private float maxGravity = 0.5f;
    private float gravityIncreaseRate = 0.35f;  // how fast gravity increases

    private float minRotateSpeed = 1f;
    private float maxRotateSpeed = 50f;

    // Synchronized state across network
    private NetworkVariable<float> netSpeed = new NetworkVariable<float>(5f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netEngineOff = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netInSpace = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netGravity = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

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

        // Client keeps original red, host gets red replaced with blue
        if (OwnerClientId == 0)
        {
            // Load the color replace shader
            Shader colorReplaceShader = Shader.Find("Custom/ColorReplace");
            if (colorReplaceShader != null)
            {
                Material mat = new Material(colorReplaceShader);
                mat.SetTexture("_MainTex", spriteRenderer.sprite.texture);
                mat.SetColor("_SourceColor", Color.red);
                mat.SetColor("_TargetColor", Color.blue);
                mat.SetFloat("_Threshold", 0.4f);
                spriteRenderer.material = mat;
                Debug.Log($"[PlayerController] Applied blue color shader to host");
            }
            else
            {
                Debug.LogError("[PlayerController] ColorReplace shader not found! Make sure it's in Always Included Shaders.");
            }
        }
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
            if (rotation > -0.8 && rotation < -0.6)
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
        // Extremely fast rotation when engine is off
        float speedFactor = rb.linearVelocity.magnitude / defaultSpeed;  // 1.0 at defaultSpeed
        float currentRotateSpeed = engineOff
            ? rotateSpeed * 4f
            : rotateSpeed * speedFactor;

        // turn on/off
        if (x != 0)
        {
            rb.angularVelocity = -currentRotateSpeed * angle;
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

        // Different spawn position for each player to avoid immediate collision
        float spawnX = (OwnerClientId == 0) ? -15f : 15f;
        Vector3 newPos = new Vector3(spawnX, 10f, 0f);

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
            RespawnWithExplosionClientRpc();
        }

        if (collider.gameObject.CompareTag("Respawn") || collider.gameObject.CompareTag("Ground"))
        {
            RespawnWithExplosionClientRpc();
        }

        if (collider.gameObject.CompareTag("Player"))
        {
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
}
