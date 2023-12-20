using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using static UnityEngine.GridBrushBase;
using static UnityEngine.GraphicsBuffer;

public class PlayerController : NetworkBehaviour, IDamagable
{
    // TODO annotations for Unity Editor
    public Transform plane;
    public Transform leftPoint, rightPoint, forwardPoint;
    Rigidbody2D rb;
    public float speed = 5f, rotateSpeed = 50f;

    private float targetSpeed = 5f;
    private float maxSpeed = 5f;
    private float minSpeed = 0f;

    private float minRotateSpeed = 1f;
    private float maxRotateSpeed = 50f;

    private bool engineOff = false;

    private float duration = 1f;
    private bool rotationDirection;

    public GameObject hitEffect;

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        Vector3 forward = transform.TransformDirection(Vector3.left) * 10;

        //Debug.Log($"Update: {transform.position}:{forward}");

        
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
        speed = Mathf.SmoothStep(speed, targetSpeed, duration * Time.deltaTime);
        Debug.Log("Speed: " + speed);

        if (speed < 2)
        {
            var down = Vector3.down * 5;
            rb.velocity = down;
            //rb.velocity = new Vector3(0, -5, 0);
            engineOff = true;
        }
        else
        {
            rb.velocity = transform.right * speed;
            engineOff = false;
            duration = 2f;
        }


        Debug.Log($"RotationZ: {plane.transform.rotation.z}"); 

        //Debug.Log("RotationZ: " + transform.rotation.z);
        if (engineOff) {
            // -0.7 (bottom)
            var rotation = plane.transform.rotation.z;

            if (rotation > -0.8 && rotation < -0.6)
            {
                targetSpeed = maxSpeed + 3;
                duration = 10f;
            }
            
        }
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

        // turn on/off
        if (x != 0)
        {
            rb.angularVelocity = -rotateSpeed * angle;
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

        Debug.Log($"rotatePlane{plane.transform.rotation}");
    }

    public void Hit(int damage)
    {
        if (!IsServer)
            return;

        NetworkObjectDespawner.DespawnNetworkObject(NetworkObject);
    }

    [ClientRpc]
    private void RespawnWithExplosionClientRpc()
    {
        var effect = Instantiate(hitEffect, transform.position, Quaternion.identity);
        Destroy(effect, 0.5f);

        transform.position = new Vector3(-10f, 10f, 0f);
    }

    [ClientRpc]
    private void MoveRightClientRpc()
    {
        var right = GameObject.Find("Right");
        Debug.Log($"Right{right.transform.position.x}");
        transform.position = new Vector3(right.transform.position.x - 1.1f, transform.position.y, 0f);
    }

    [ClientRpc]
    private void MoveLeftClientRpc()
    {
        var left = GameObject.Find("Left");

        Debug.Log($"Left{left.transform.position.x}");
        transform.position = new Vector3(left.transform.position.x + 1.1f, transform.position.y, 0f);
    }

    [ClientRpc]
    private void SpaceClientRpc()
    {
        targetSpeed = 0;
    }

    void OnTriggerEnter2D(Collider2D collider)
    {
        if (!IsServer)
            return;


        if (collider.name == "Space")
        {
            Debug.Log($"HIT Space");

            SpaceClientRpc();
        }

        if (collider.name == "Left")
        {
            MoveRightClientRpc();
        }

        if (collider.name == "Right")
        {
            MoveLeftClientRpc();
        }


        if (collider.gameObject.CompareTag("Bullet"))
        {
            Debug.Log($"HIT #server{IsServer}");

            RespawnWithExplosionClientRpc();
        }

        if (collider.gameObject.CompareTag("Respawn"))
        {
            Debug.Log($"Respawn #server{IsServer}");

            RespawnWithExplosionClientRpc();
        }

        if (collider.gameObject.CompareTag("Player"))
        {
            Debug.Log($"Collision #server{IsServer}");

            RespawnWithExplosionClientRpc();
        }

        //If the collider hit a power-up
        //if (collider.gameObject.CompareTag("PowerUpSpecial"))
        //{
        //    // Check if I have space to take the special
        //    if (m_specials.Value < m_maxSpecialPower)
        //    {
        //        // Update var
        //        m_specials.Value++;

        //        // Update UI
        //        playerUI.UpdatePowerUp(m_specials.Value, true);

        //        // Remove the power-up
        //        NetworkObjectDespawner.DespawnNetworkObject(
        //            collider.gameObject.GetComponent<NetworkObject>());
        //    }
        //}
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
