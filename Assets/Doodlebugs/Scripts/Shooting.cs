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

    void Update()
    {
        if (!IsOwner) return;

        if (Input.GetKeyDown(KeyCode.Space)) {
            ShootServerRpc(firePoint.position, firePoint.rotation);
#if UNITY_EDITOR
            Debug.Log($"Space {OwnerClientId}");
#endif
        }

        // shooting Birds
        if (Input.GetKeyDown(KeyCode.S))
        {
            SpawnBirdServerRpc();
        }
    }

    [ServerRpc]
    void ShootServerRpc(Vector3 position, Quaternion rotation)
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
            rb.AddForce((rotation * Vector3.right) * bulletForce, ForceMode2D.Impulse);
        }
    }

    [ServerRpc]
    public void SpawnBirdServerRpc()
    {
        Debug.Log($"GameManager.SpawnBird {OwnerClientId}");
        var birdPrefab = GameManager.Instance.birdPrefab;
        NetworkObjectSpawner.SpawnNewNetworkObject(birdPrefab, birdPrefab.transform.position);
    }
}
