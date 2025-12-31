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
        if (!IsServer) return;

        if (other.gameObject.name != "Space")
        {
            var damagable = other.gameObject.GetComponent<IDamagable>();
            if (damagable != null)
            {
                // Player handles its own explosion, don't create duplicate
                damagable.Hit(1);
            }
            else
            {
                // Non-damagable object (wall, etc.) - bullet creates explosion
                PlayHitFxClientRpc(transform.position);
            }

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn();
            }
        }
    }

    [ClientRpc]
    private void PlayHitFxClientRpc(Vector3 position)
    {
        if (hitEffect == null) return;
        var effect = Instantiate(hitEffect, position, Quaternion.identity);
        Destroy(effect, 0.8f);
    }
}
