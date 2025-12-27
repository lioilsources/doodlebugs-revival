using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

// PlayerHolder Prefab script
public class Shooting : NetworkBehaviour
{
    public Transform firePoint;
    public GameObject bulletPrefab;
    public float bulletForce = 20f;

    public override void OnNetworkSpawn()
    {
        Debug.Log($"Plane Spawn OwnerClientId#{OwnerClientId} NetworkObjectId#{NetworkObjectId}");
    }

    Rigidbody2D planeRb;

    void Start()
    {
        planeRb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (!IsOwner) return;

        bool shootPressed = InputManager.Instance != null
            ? InputManager.Instance.InputProvider.GetShootInput()
            : Input.GetKeyDown(KeyCode.Space);

        if (shootPressed) {
            float planeSpeed = planeRb != null ? planeRb.velocity.magnitude : 0f;
            ShootServerRpc(firePoint.position, firePoint.rotation, planeSpeed);
        }
    }

    [ServerRpc]
    void ShootServerRpc(Vector3 position, Quaternion rotation, float planeSpeed)
    {
        // Instantiate and spawn bullet on the server, then apply force server-side
        var bullet = Instantiate(bulletPrefab, position, rotation);
        var netObj = bullet.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn(true);
        }
        var rb = bullet.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            // Bullet force = base force + plane speed
            float totalForce = bulletForce + planeSpeed;
            rb.AddForce((rotation * Vector3.right) * totalForce, ForceMode2D.Impulse);
        }
    }
}
