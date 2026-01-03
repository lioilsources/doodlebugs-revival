using UnityEngine;

/// <summary>
/// Mobile input provider using gyroscope for rotation and touch-anywhere for shooting
/// </summary>
public class MobileInputProvider : IInputProvider
{
    // Gyro settings
    private float deadZone = 0.1f;
    private float maxTilt = 0.4f;
    private bool gyroAvailable = false;

    // Input state
    private float horizontalInput = 0f;
    private bool shootPressed = false;
    private bool shootConsumed = false;

    public void Initialize()
    {
        if (SystemInfo.supportsGyroscope)
        {
            Input.gyro.enabled = true;
            gyroAvailable = true;
            Debug.Log("[MobileInputProvider] Gyroscope enabled");
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
            float tilt = Input.gyro.gravity.x;
            if (Mathf.Abs(tilt) < deadZone)
            {
                horizontalInput = 0f;
            }
            else
            {
                // Map tilt to -1..1 range (left tilt = negative = turn left)
                horizontalInput = Mathf.Clamp(tilt / maxTilt, -1f, 1f);
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

    private void CheckTouchShoot()
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                shootPressed = true;
                return;
            }
        }
        shootPressed = false;
    }
}
