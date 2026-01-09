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

        bool shootPressed = false;

        // Try InputManager first
        if (InputManager.Instance != null)
        {
            shootPressed = InputManager.Instance.InputProvider.GetShootInput();
        }

        // Keyboard fallback
        if (!shootPressed && Input.GetKeyDown(KeyCode.Space))
        {
            shootPressed = true;
        }

        // Direct gamepad button check as fallback (in case InputManager doesn't use gamepad provider)
        if (!shootPressed)
        {
            for (int i = 0; i < 20; i++)
            {
                // Skip D-pad buttons (6, 7, 8, 9)
                if (i >= 6 && i <= 9) continue;

                if (Input.GetKeyDown((KeyCode)(KeyCode.JoystickButton0 + i)))
                {
                    Debug.Log($"[Shooting] Direct gamepad button {i} pressed");
                    shootPressed = true;
                    break;
                }
            }
        }

        // Direct trigger axis check as fallback
        if (!shootPressed)
        {
            shootPressed = CheckTriggerAxis();
        }

        if (shootPressed) {
            float planeSpeed = planeRb != null ? planeRb.linearVelocity.magnitude : 0f;
            ShootServerRpc(firePoint.position, firePoint.rotation, planeSpeed);
        }
    }

    // Track trigger state for "button down" behavior
    private bool _leftTriggerWasPressed = false;
    private bool _rightTriggerWasPressed = false;

    private bool CheckTriggerAxis()
    {
        // Check trigger axes configured in InputManager.asset
        float rightTrigger = GetAxisSafe("RightTrigger");
        float leftTrigger = GetAxisSafe("LeftTrigger");
        float combinedTriggers = GetAxisSafe("Triggers");

        // Right trigger
        if (rightTrigger > 0.3f && !_rightTriggerWasPressed)
        {
            _rightTriggerWasPressed = true;
            Debug.Log($"[Shooting] Right trigger pressed: {rightTrigger}");
            return true;
        }
        if (rightTrigger < 0.1f) _rightTriggerWasPressed = false;

        // Left trigger
        if (leftTrigger > 0.3f && !_leftTriggerWasPressed)
        {
            _leftTriggerWasPressed = true;
            Debug.Log($"[Shooting] Left trigger pressed: {leftTrigger}");
            return true;
        }
        if (leftTrigger < 0.1f) _leftTriggerWasPressed = false;

        // Combined triggers
        if (Mathf.Abs(combinedTriggers) > 0.3f && !_triggerWasPressed)
        {
            _triggerWasPressed = true;
            Debug.Log($"[Shooting] Combined trigger pressed: {combinedTriggers}");
            return true;
        }
        if (Mathf.Abs(combinedTriggers) < 0.1f) _triggerWasPressed = false;

        return false;
    }

    private bool _triggerWasPressed = false;

    private float GetAxisSafe(string axisName)
    {
        try
        {
            return Input.GetAxisRaw(axisName);
        }
        catch
        {
            return 0f;
        }
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
