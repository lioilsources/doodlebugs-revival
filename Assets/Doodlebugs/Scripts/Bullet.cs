using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// Bullet Prefab script
public class Bullet : NetworkBehaviour
{
    public GameObject hitEffect;

    private void Start() {
        //Debug.Log($"Start Bullet {OwnerClientId}");
    }

    void OnTriggerEnter2D(Collider2D other) {

        Debug.Log($"OnTriggerEnter2D other#{other} NetworkObjectId#{NetworkObjectId}");

        var networkBehaviours = other.gameObject.GetComponents<NetworkBehaviour>();
        if (networkBehaviours.Length == 0)
        {
            Debug.Log($"other#{other} is not NetworkObject");
            return;
        }

        var otherOwnerClientId = networkBehaviours[0].OwnerClientId;
        var targetClientId = otherOwnerClientId;

        Debug.Log($"Bullet trigger#{other} otherOwnerClientId#{otherOwnerClientId}");

        if (other.gameObject.name != "Space") {

            var effect = Instantiate(hitEffect, transform.position, Quaternion.identity);
            Destroy(effect, 0.8f);
            Destroy(gameObject);

            //NetworkObjectDespawner.DespawnNetworkObject(NetworkObject);
        }

        
        Debug.Log($"Bullet triggered owner: {IsOwner} shooter: {OwnerClientId} target: {targetClientId}");
    }
}
