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
    public float baseBulletForce = 20f;

    // Profile-aware bullet settings
    private PilotMaturityProfile Profile => PilotMaturityManager.Instance?.CurrentProfile;
    private float bulletForce => baseBulletForce * (Profile?.bulletForceMultiplier ?? 1f);
    private float bulletGravityScale => Profile?.bulletGravityScale ?? 2f;

    // Reference to PlayerController for local player index
    private PlayerController playerController;

    public override void OnNetworkSpawn()
    {
        Debug.Log($"Plane Spawn OwnerClientId#{OwnerClientId} NetworkObjectId#{NetworkObjectId}");
        playerController = GetComponent<PlayerController>();
    }

    Rigidbody2D planeRb;

    void Start()
    {
        planeRb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        // Check if we should process input:
        // - Regular network player: IsOwner must be true
        // - Local couch co-op player: IsLocalPlayer and we're the host
        bool isLocalPlayer = playerController != null && playerController.IsLocalPlayer;
        bool canProcessInput = IsOwner || (isLocalPlayer && NetworkManager.Singleton.IsHost);
        if (!canProcessInput) return;

        bool shootPressed = GetShootInput();

        if (shootPressed)
        {
            float planeSpeed = planeRb != null ? planeRb.linearVelocity.magnitude : 0f;
            ShootServerRpc(firePoint.position, firePoint.rotation, planeSpeed);
        }
    }

    /// <summary>
    /// Get shoot input from the appropriate provider (local or network)
    /// </summary>
    private bool GetShootInput()
    {
        // Local couch co-op player - use LocalPlayerManager
        if (playerController != null && playerController.IsLocalPlayer)
        {
            if (LocalPlayerManager.Instance != null)
            {
                var provider = LocalPlayerManager.Instance.GetInputProvider(playerController.LocalPlayerIndex);
                if (provider != null)
                {
                    return provider.GetShootInput();
                }
            }
            return false;
        }

        // Network player - use InputManager singleton
        if (InputManager.Instance != null && InputManager.Instance.InputProvider != null)
        {
            return InputManager.Instance.InputProvider.GetShootInput();
        }

        return false;
    }

    [ServerRpc]
    void ShootServerRpc(Vector3 position, Quaternion rotation, float planeSpeed, ServerRpcParams rpcParams = default)
    {
        // Get shooter's client ID from RPC sender
        ulong shooterClientId = rpcParams.Receive.SenderClientId;

        // Instantiate and spawn bullet on the server, then apply force server-side
        var bullet = Instantiate(bulletPrefab, position, rotation);
        var netObj = bullet.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn(true);
        }

        // Set shooter ID for scoring
        var bulletScript = bullet.GetComponent<Bullet>();
        if (bulletScript != null)
        {
            bulletScript.SetShooter(shooterClientId);
        }

        var rb = bullet.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            // Apply gravity scale from profile
            rb.gravityScale = bulletGravityScale;

            // Bullet force = base force + plane speed
            float totalForce = bulletForce + planeSpeed;
            rb.AddForce((rotation * Vector3.right) * totalForce, ForceMode2D.Impulse);
        }
    }
}
