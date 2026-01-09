using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mobile input provider using gyroscope for rotation and touch-anywhere for shooting
/// Uses new Input System for both gyro and touch
/// </summary>
public class MobileInputProvider : IInputProvider
{
    // Gyro settings
    private float deadZone = 0.1f;
    private float maxTilt = 0.4f;
    private float neutralTiltY = -0.7f; // 45 degree hold angle (sin(45°) ≈ 0.707)

    // New Input System sensors
    private GravitySensor gravitySensor;
    private bool gyroAvailable = false;

    // Input state
    private float horizontalInput = 0f;
    private float verticalInput = 0f;
    private bool shootPressed = false;
    private bool shootConsumed = false;

    public void Initialize()
    {
        // Try new Input System GravitySensor first
        gravitySensor = GravitySensor.current;
        if (gravitySensor != null)
        {
            InputSystem.EnableDevice(gravitySensor);
            gyroAvailable = true;
            Debug.Log("[MobileInputProvider] GravitySensor enabled (New Input System)");
        }
        // Fallback to old Input system
        else if (SystemInfo.supportsGyroscope)
        {
            Input.gyro.enabled = true;
            gyroAvailable = true;
            Debug.Log("[MobileInputProvider] Gyroscope enabled (Legacy Input)");
        }
        else
        {
            gyroAvailable = false;
            Debug.LogWarning("[MobileInputProvider] Gyroscope not supported");
        }
    }

    public float GetHorizontalInput()
    {
        return horizontalInput;
    }

    public float GetVerticalInput()
    {
        return verticalInput;
    }

    public bool GetShootInput()
    {
        // Return true only once per press
        if (shootPressed && !shootConsumed)
        {
            shootConsumed = true;
            return true;
        }
        return false;
    }

    public void UpdateInput()
    {
        // Gyro rotation - tilt left/right
        if (gyroAvailable)
        {
            Vector3 gravity = GetGravity();

            // Horizontal: tilt phone left/right
            float tiltX = gravity.x;
            if (Mathf.Abs(tiltX) < deadZone)
            {
                horizontalInput = 0f;
            }
            else
            {
                // Map tilt to -1..1 range (left tilt = negative = turn left)
                horizontalInput = Mathf.Clamp(tiltX / maxTilt, -1f, 1f);
            }

            // Vertical: tilt phone forward/backward (relative to 45° hold angle)
            // Forward (away from self) = positive = speed up
            // Backward (towards self) = negative = slow down
            float tiltY = gravity.y - neutralTiltY;
            if (Mathf.Abs(tiltY) < deadZone)
            {
                verticalInput = 0f;
            }
            else
            {
                verticalInput = Mathf.Clamp(tiltY / maxTilt, -1f, 1f);
            }
        }

        // Touch anywhere = shoot
        CheckTouchShoot();

        // Reset shoot consumed flag when no touch
        if (!shootPressed)
        {
            shootConsumed = false;
        }
    }

    private Vector3 GetGravity()
    {
        // Prefer new Input System
        if (gravitySensor != null)
        {
            return gravitySensor.gravity.ReadValue();
        }
        // Fallback to legacy
        return Input.gyro.gravity;
    }

    private void CheckTouchShoot()
    {
        // Try new Input System Touchscreen first
        var touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            var primaryTouch = touchscreen.primaryTouch;
            if (primaryTouch.press.wasPressedThisFrame)
            {
                shootPressed = true;
                return;
            }
        }
        // Fallback to legacy Input
        else if (Input.touchCount > 0)
        {
            UnityEngine.Touch touch = Input.GetTouch(0);
            if (touch.phase == UnityEngine.TouchPhase.Began)
            {
                shootPressed = true;
                return;
            }
        }
        shootPressed = false;
    }
}
