using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

public class PlayerController : NetworkBehaviour, IDamagable
{
    // TODO annotations for Unity Editor
    public Transform plane;
    public Transform leftPoint, rightPoint, forwardPoint;
    Rigidbody2D rb;
    NetworkTransform networkTransform;
    public float speed = 5f, rotateSpeed = 200f;

    private float defaultSpeed = 5f;
    private float maxSpeed = 20f;
    private float minSpeed = 2f;
    private float climbDrag = 1f;       // how fast speed decreases when climbing
    private float diveBoost = 3f;       // how fast speed increases when diving
    private float maxGravity = 0.5f;
    private float gravityIncreaseRate = 0.35f;  // how fast gravity increases

    private float minRotateSpeed = 1f;
    private float maxRotateSpeed = 50f;

    private bool engineOff = false;
    private bool inSpace = false;
    private float currentGravity = 0f;

    public GameObject hitEffect;

    // Cached boundary references
    private Transform leftBoundary;
    private Transform rightBoundary;
    private BoxCollider2D planeCollider;

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        networkTransform = GetComponent<NetworkTransform>();
        planeCollider = GetComponent<BoxCollider2D>();

        // Cache boundary references
        var leftObj = GameObject.Find("Left");
        var rightObj = GameObject.Find("Right");
        if (leftObj != null) leftBoundary = leftObj.transform;
        if (rightObj != null) rightBoundary = rightObj.transform;

        // Limit FPS for stability
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
    }

    void Update()
    {
        Vector3 forward = transform.TransformDirection(Vector3.left) * 10;
        Debug.DrawRay(transform.position, forward, Color.green);
    }

    void FixedUpdate() {
        if (!IsOwner) return;

        HandleMovement();

        //HandleExitGame();
    }

    private void HandleMovement()
    {
        rotatePlane(Input.GetAxis("Horizontal"));
        movePlane();
    }

    private void movePlane()
    {
        if (engineOff)
        {
            // Engine OFF - gravity gradually increases
            currentGravity = Mathf.MoveTowards(currentGravity, maxGravity, gravityIncreaseRate * Time.fixedDeltaTime);

            // Add gravity to current velocity
            rb.velocity += Vector2.down * currentGravity * Time.fixedDeltaTime * 60f;

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

            // Clamp speed
            speed = Mathf.Clamp(speed, minSpeed, maxSpeed);

            // If speed drops below minimum, turn off engine
            if (speed <= minSpeed)
            {
                EngineOff();
            }

            rb.velocity = transform.right * speed;
        }
    }

    private void EngineOn()
    {
        // Cannot turn on engine in space
        if (inSpace) return;

        // Keep speed from the fall
        speed = rb.velocity.magnitude;
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
        float speedFactor = rb.velocity.magnitude / defaultSpeed;  // 1.0 at defaultSpeed
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
        var effect = Instantiate(hitEffect, transform.position, Quaternion.identity);
        Destroy(effect, 0.5f);

        // Only owner can teleport (ClientNetworkTransform = client authority)
        if (!IsOwner) return;

        // Reset position and speed - use Teleport to skip interpolation
        Vector3 newPos = new Vector3(-10f, 10f, 0f);
        networkTransform.Teleport(newPos, transform.rotation, transform.localScale);
        speed = defaultSpeed;
        engineOff = false;
        inSpace = false;
        currentGravity = 0f;
        rb.velocity = transform.right * speed;
    }

    [ClientRpc]
    private void LeaveSpaceClientRpc()
    {
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
            float margin = 0.2f;
            float halfWidth = GetPlaneHalfWidth();
            float targetX = rightBoundary.position.x - halfWidth - margin;
            WrapToPositionClientRpc(targetX);
        }

        if (collider.name == "Right" && leftBoundary != null)
        {
            // Hit right edge -> wrap to left side
            float margin = 0.2f;
            float halfWidth = GetPlaneHalfWidth();
            float targetX = leftBoundary.position.x + halfWidth + margin;
            WrapToPositionClientRpc(targetX);
        }


        if (collider.gameObject.CompareTag("Bullet"))
        {
            RespawnWithExplosionClientRpc();
        }

        if (collider.gameObject.CompareTag("Respawn"))
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
}
