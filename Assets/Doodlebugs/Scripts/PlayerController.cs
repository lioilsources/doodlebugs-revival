using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour, IDamagable
{
    // TODO annotations for Unity Editor
    public Transform plane;
    public Transform leftPoint, rightPoint, forwardPoint;
    Rigidbody2D rb;
    public float speed = 5f, rotateSpeed = 50f;

    public GameObject hitEffect;

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update() {
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
        rb.velocity = transform.right * speed;
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

    void OnTriggerEnter2D(Collider2D collider)
    {
        if (!IsServer)
            return;

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
