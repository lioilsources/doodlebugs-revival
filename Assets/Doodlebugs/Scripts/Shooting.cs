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

    // --- Weapon state (server-write) ---
    // Current weapon (may be crate-boosted); resets to the selected weapon on death.
    public NetworkVariable<int> NetWeaponId = new NetworkVariable<int>((int)WeaponType.MG,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Weapon chosen in the hangar - survives deaths, reset only by a new pick.
    private int _selectedWeaponId = (int)WeaponType.MG;

    public WeaponProfile CurrentWeapon => WeaponProfile.Get(NetWeaponId.Value);

    // Profile-aware modifiers (player's own maturity level scales ROF/force)
    private PilotMaturityProfile Profile =>
        playerController != null && PilotMaturityManager.Instance != null
            ? PilotMaturityManager.Instance.GetProfile(playerController.MaturityLevel)
            : null;

    // Maturity ROF as a multiplier normalized to the Novice default (0.4s):
    // Novice = 1.0, Advanced = 0.75, Expert = 0.625 with the stock profiles.
    private float maturityRofMultiplier => (Profile?.fireRateCooldown ?? 0.4f) / 0.4f;
    private float maturityForceMultiplier => Profile?.bulletForceMultiplier ?? 1f;

    // Run-upgrade FIRE RATE levels shorten the cooldown
    private float runRofMultiplier => playerController?.PlaneStats?.RofMultiplier ?? 1f;

    private float fireRateCooldown => CurrentWeapon.Cooldown * maturityRofMultiplier / runRofMultiplier;

    // Reference to PlayerController for local player index
    private PlayerController playerController;

    // Fire rate cooldown tracking
    private float _lastFireTime;

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

        if (shootPressed && Time.time >= _lastFireTime + fireRateCooldown)
        {
            _lastFireTime = Time.time;
            float planeSpeed = planeRb != null ? planeRb.linearVelocity.magnitude : 0f;
            int localPlayerIndex = playerController != null && playerController.LocalPlayerIndex >= 0
                ? playerController.LocalPlayerIndex : 0;
            ShootServerRpc(firePoint.position, firePoint.rotation, planeSpeed, localPlayerIndex);
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
    void ShootServerRpc(Vector3 position, Quaternion rotation, float planeSpeed, int localPlayerIndex, ServerRpcParams rpcParams = default)
    {
        // Get shooter's client ID from RPC sender
        ulong shooterClientId = rpcParams.Receive.SenderClientId;

        var weapon = CurrentWeapon;

        // Damage multiplier from PlaneStats power-up
        int bulletDamage = weapon.Damage;
        var planeStats = playerController?.PlaneStats;
        if (planeStats != null)
        {
            bulletDamage = Mathf.Max(1, Mathf.RoundToInt(weapon.Damage * planeStats.DamageMultiplier));
        }

        // Fire all pellets in an even cone around the aim direction
        for (int i = 0; i < weapon.PelletCount; i++)
        {
            float offset = 0f;
            if (weapon.PelletCount > 1)
            {
                float t = (float)i / (weapon.PelletCount - 1); // 0..1
                offset = Mathf.Lerp(-weapon.SpreadDegrees / 2f, weapon.SpreadDegrees / 2f, t);
            }
            Quaternion pelletRotation = rotation * Quaternion.Euler(0f, 0f, offset);

            SpawnBullet(pelletRotation, position, planeSpeed, shooterClientId, localPlayerIndex,
                bulletDamage, weapon);
        }
    }

    private void SpawnBullet(Quaternion rotation, Vector3 position, float planeSpeed,
        ulong shooterClientId, int localPlayerIndex, int damage, WeaponProfile weapon)
    {
        var bullet = Instantiate(bulletPrefab, position, rotation);
        var netObj = bullet.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn(true);
        }

        var bulletScript = bullet.GetComponent<Bullet>();
        if (bulletScript != null)
        {
            bulletScript.SetShooter(shooterClientId, localPlayerIndex);
            bulletScript.SetDamage(damage);
            if (weapon.BulletLifetime > 0f)
            {
                bulletScript.SetLifetime(weapon.BulletLifetime);
            }
        }

        var rb = bullet.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = weapon.GravityScale;

            // Bullet force = (base force * weapon * maturity) + plane speed
            float totalForce = baseBulletForce * weapon.ForceMultiplier * maturityForceMultiplier
                               + planeSpeed;
            rb.AddForce((rotation * Vector3.right) * totalForce, ForceMode2D.Impulse);
        }
    }

    // --- Weapon management (server-side) ---

    /// <summary>
    /// Weapon crate pickup: climb one tier of the current weapon's chain.
    /// Lost on death (weapon resets to the hangar selection). Server-only.
    /// </summary>
    public void UpgradeWeaponTier()
    {
        if (!IsServer) return;

        var next = CurrentWeapon.UpgradesTo;
        if (next.HasValue)
        {
            NetWeaponId.Value = (int)next.Value;
            Debug.Log($"[Shooting] Player {OwnerClientId} weapon upgraded to {next.Value}");
        }
    }

    /// <summary>Reset the crate-boosted weapon back to the hangar pick. Server-only.</summary>
    public void ResetWeaponToSelected()
    {
        if (!IsServer) return;
        NetWeaponId.Value = _selectedWeaponId;
    }

    /// <summary>Server-side: apply a validated hangar selection.</summary>
    public void ServerSetSelectedWeapon(int weaponId)
    {
        if (!IsServer) return;
        if (weaponId < 0 || weaponId >= WeaponProfile.Count) return;

        _selectedWeaponId = weaponId;
        NetWeaponId.Value = weaponId;
        Debug.Log($"[Shooting] Player {OwnerClientId} selected weapon {(WeaponType)weaponId}");
    }

    /// <summary>Owner asks the server to equip a weapon picked in the hangar.</summary>
    [ServerRpc]
    public void RequestSelectWeaponServerRpc(int weaponId)
    {
        ServerSetSelectedWeapon(weaponId);
    }
}
