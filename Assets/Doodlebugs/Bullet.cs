using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// Bullet Prefab script
public class Bullet : NetworkBehaviour
{
    public GameObject hitEffect;

    private void Start() {
        Debug.Log($"Start Bullet {OwnerClientId}");
    }

    void OnTriggerEnter2D(Collider2D other) {
        //if (!IsServer)
        //    return;

        if (other.gameObject.name != "Plane" && other.gameObject.name != "Space") {
            var effect = Instantiate(hitEffect, transform.position, Quaternion.identity);
            Destroy(effect, 0.8f);
            Destroy(gameObject);

            //NetworkObjectDespawner.DespawnNetworkObject(NetworkObject);
        }

        Debug.Log($"Bullet trigger {other}");
    }
}
