using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// Bullet Prefab script
public class Bullet : NetworkBehaviour
{
    public GameObject hitEffect;

    // Track who shot this bullet for scoring
    private NetworkVariable<ulong> _shooterClientId = new NetworkVariable<ulong>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Set the shooter's client ID. Call from server after spawning.
    /// </summary>
    public void SetShooter(ulong shooterClientId)
    {
        if (IsServer)
        {
            _shooterClientId.Value = shooterClientId;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer) return;

        if (other.gameObject.name != "Space")
        {
            var damagable = other.gameObject.GetComponent<IDamagable>();
            if (damagable != null)
            {
                // Check if this is a player hit (for scoring)
                var targetPlayer = other.gameObject.GetComponent<PlayerController>();
                if (targetPlayer != null)
                {
                    // Only score if hitting opponent (not self)
                    if (targetPlayer.OwnerClientId != _shooterClientId.Value)
                    {
                        // Add score to shooter (server-side call)
                        if (ScoreManager.Instance != null)
                        {
                            ScoreManager.Instance.AddScore(_shooterClientId.Value);
                        }
                    }
                }

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
