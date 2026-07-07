using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mobile input provider using gyroscope for rotation and touch-anywhere for shooting.
/// Uses new Input System for both gyro and touch.
///
/// Gyro pipeline: raw gravity → low-pass filter (kills sensor jitter) →
/// smooth dead-zone remap (no jump at the dead-zone edge) → expo response
/// curve (fine control near center, full authority at the edges). The
/// neutral pitch (hold angle) auto-calibrates from the first stable readings
/// so any comfortable grip works.
/// </summary>
public class MobileInputProvider : IInputProvider
{
    // Gyro tuning.
    // deadZone is small because readings are low-pass filtered (raw sensor
    // noise no longer needs a wide dead band). maxTilt ~0.35 ≈ 20° of tilt
    // for full input. responseExpo > 1 gives fine control near center.
    // smoothingHalflife = time to close half the gap to the raw reading.
    private float deadZone = 0.05f;
    private float maxTilt = 0.35f;
    private float responseExpo = 1.6f;
    private float smoothingHalflife = 0.05f;

    // Neutral pitch (how the player holds the phone). Auto-calibrated,
    // clamped to a sane range; fallback is a 45° hold (sin 45° ≈ 0.707).
    private float neutralTiltY = -0.7f;
    private const float NeutralMin = -0.95f;
    private const float NeutralMax = -0.20f;
    private bool _neutralCalibrated = false;
    private float _calibrationTimer = 0f;
    private const float CalibrationSeconds = 0.5f;

    // New Input System sensors
    private GravitySensor gravitySensor;
    private bool gyroAvailable = false;

    // Filtered gravity
    private Vector3 _smoothedGravity;
    private bool _hasSample = false;

    // Input state
    private float horizontalInput = 0f;
    private float verticalInput = 0f;
    private bool shootPressed = false;
    private bool shootConsumed = false;

    public void Initialize()
    {
        // Use new Input System GravitySensor
        gravitySensor = GravitySensor.current;
        if (gravitySensor != null)
        {
            InputSystem.EnableDevice(gravitySensor);
            gyroAvailable = true;
            Debug.Log("[MobileInputProvider] GravitySensor enabled");
        }
        else
        {
            gyroAvailable = false;
            Debug.LogWarning("[MobileInputProvider] GravitySensor not available");
        }
    }

    /// <summary>
    /// Re-capture the current hold angle as neutral (e.g. after the player
    /// settles into a new position).
    /// </summary>
    public void Recenter()
    {
        _neutralCalibrated = false;
        _calibrationTimer = 0f;
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
        if (gyroAvailable)
        {
            Vector3 raw = GetGravity();

            // Low-pass filter: framerate-independent exponential smoothing
            if (!_hasSample)
            {
                _smoothedGravity = raw;
                _hasSample = true;
            }
            else
            {
                float k = 1f - Mathf.Pow(0.5f, Time.deltaTime / smoothingHalflife);
                _smoothedGravity = Vector3.Lerp(_smoothedGravity, raw, k);
            }

            // Auto-calibrate the neutral hold angle from the first stable window
            if (!_neutralCalibrated)
            {
                _calibrationTimer += Time.deltaTime;
                if (_calibrationTimer >= CalibrationSeconds)
                {
                    float captured = Mathf.Clamp(_smoothedGravity.y, NeutralMin, NeutralMax);
                    // Only trust the capture when the phone is held roughly upright-ish
                    if (_smoothedGravity.y > NeutralMin && _smoothedGravity.y < NeutralMax)
                    {
                        neutralTiltY = captured;
                    }
                    _neutralCalibrated = true;
                    Debug.Log($"[MobileInputProvider] Neutral hold angle calibrated: {neutralTiltY:F2}");
                }
            }

            // Horizontal: tilt phone left/right (roll is absolute vs gravity)
            horizontalInput = ApplyResponse(_smoothedGravity.x);

            // Vertical: tilt forward/backward relative to the calibrated hold angle
            verticalInput = ApplyResponse(_smoothedGravity.y - neutralTiltY);
        }

        // Touch anywhere = shoot
        CheckTouchShoot();

        // Reset shoot consumed flag when no touch
        if (!shootPressed)
        {
            shootConsumed = false;
        }
    }

    /// <summary>
    /// Smooth dead-zone + expo curve. Continuous at the dead-zone edge
    /// (the old code jumped from 0 straight to deadZone/maxTilt).
    /// </summary>
    private float ApplyResponse(float tilt)
    {
        float magnitude = Mathf.Abs(tilt);
        if (magnitude <= deadZone) return 0f;

        float t = Mathf.InverseLerp(deadZone, maxTilt, magnitude); // 0 at edge, 1 at maxTilt
        t = Mathf.Pow(t, responseExpo);
        return Mathf.Sign(tilt) * Mathf.Clamp01(t);
    }

    private Vector3 GetGravity()
    {
        if (gravitySensor != null)
        {
            return gravitySensor.gravity.ReadValue();
        }
        return Vector3.zero;
    }

    private void CheckTouchShoot()
    {
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
        shootPressed = false;
    }
}
