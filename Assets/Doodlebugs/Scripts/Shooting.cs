﻿using System.Collections;
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
            ShootServerRpc();
            
            Debug.Log($"Space {OwnerClientId}");
        }

        // shooting Birds
        if (Input.GetKeyDown(KeyCode.S))
        {
            SpawnBirdServerRpc();
        }
    }

    [ServerRpc]
    void ShootServerRpc() {
        AddForceClientRpc();
    }

    [ClientRpc]
    private void AddForceClientRpc()
    {
        Debug.Log($"AddForce {OwnerClientId}");

        var bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        //bullet.GetComponent<NetworkObject>().Spawn(true);

        var rb = bullet.GetComponent<Rigidbody2D>();
        rb.AddForce(firePoint.right * bulletForce, ForceMode2D.Impulse);
    }

    [ServerRpc]
    public void SpawnBirdServerRpc()
    {
        Debug.Log($"GameManager.SpawnBird {OwnerClientId}");
        var birdPrefab = GameManager.Instance.birdPrefab;
        NetworkObjectSpawner.SpawnNewNetworkObject(birdPrefab, birdPrefab.transform.position);
    }
}
