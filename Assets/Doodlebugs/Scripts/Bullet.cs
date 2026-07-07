using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// Bullet Prefab script
public class Bullet : NetworkBehaviour
{
    // The bullet spawns at the fire point, which can still overlap the shooter's
    // own collider (or be caught by it in a tight turn) on the first physics
    // steps - ignore the shooter for this long after spawn.
    private const float SelfHitGracePeriod = 0.25f;

    public GameObject hitEffect;

    private float _spawnTime;

    // Optional lifetime for short-range weapons (0 = unlimited). Server-only.
    private float _lifetime;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _spawnTime = Time.time;

        // Bullet replicates to every client - this doubles as the shot sound
        SfxManager.PlayShoot();
    }

    /// <summary>Despawn the bullet after this many seconds (short-range weapons). Server-only.</summary>
    public void SetLifetime(float seconds)
    {
        if (IsServer)
        {
            _lifetime = seconds;
        }
    }

    private void Update()
    {
        if (!IsServer || _lifetime <= 0f) return;

        if (Time.time - _spawnTime >= _lifetime && NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }

    // Track who shot this bullet for scoring
    private NetworkVariable<ulong> _shooterClientId = new NetworkVariable<ulong>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> _shooterLocalPlayerIndex = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Variable damage (affected by shooter's DamageMultiplier)
    private NetworkVariable<int> _damage = new NetworkVariable<int>(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Set bullet damage. Call from server after spawning.
    /// </summary>
    public void SetDamage(int damage)
    {
        if (IsServer)
        {
            _damage.Value = damage;
        }
    }

    /// <summary>
    /// Set the shooter's client ID and local player index. Call from server after spawning.
    /// </summary>
    public void SetShooter(ulong shooterClientId, int localPlayerIndex = 0)
    {
        if (IsServer)
        {
            _shooterClientId.Value = shooterClientId;
            _shooterLocalPlayerIndex.Value = localPlayerIndex;
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
                // Set last attacker on target for kill attribution
                var targetPlayer = other.gameObject.GetComponent<PlayerController>();
                if (targetPlayer != null)
                {
                    int targetLocalIdx = targetPlayer.LocalPlayerIndex >= 0 ? targetPlayer.LocalPlayerIndex : 0;
                    bool isSamePlayer = targetPlayer.OwnerClientId == _shooterClientId.Value &&
                                        targetLocalIdx == _shooterLocalPlayerIndex.Value;

                    if (isSamePlayer && Time.time - _spawnTime < SelfHitGracePeriod)
                    {
                        // Fresh bullet still leaving the shooter's own plane -
                        // pass through without damage or despawn.
                        return;
                    }

                    if (!isSamePlayer)
                    {
                        targetPlayer.SetLastAttacker(_shooterClientId.Value, _shooterLocalPlayerIndex.Value);
                    }
                }

                // Apply damage through IDamagable pipeline
                damagable.Hit(_damage.Value);
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
