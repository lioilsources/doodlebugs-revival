using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gravity (gyro) axis source for the Gyro control scheme, wrapped by
/// TouchInputProvider. Shooting moved to the on-screen triggers (Prompts/26
/// D9) - this class no longer reads touches at all.
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

    /// <summary>Run the sensor only while someone flies by it - a joystick
    /// player should not pay for gravity sampling.</summary>
    public void SetSensorEnabled(bool enabled)
    {
        if (gravitySensor == null) return;
        if (enabled) InputSystem.EnableDevice(gravitySensor);
        else InputSystem.DisableDevice(gravitySensor);
        if (enabled) _hasSample = false;   // no stale reading after a pause
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

    // Firing is the triggers' job (TouchInputProvider); the axis source never shoots.
    public bool GetShootInput() => false;

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
    }

    /// <summary>
    /// Smooth dead-zone + expo curve. Continuous at the dead-zone edge
    /// (the old code jumped from 0 straight to deadZone/maxTilt).
    /// </summary>
    private float ApplyResponse(float tilt) => Curve(tilt, deadZone, maxTilt, responseExpo);

    /// <summary>The same curve for any axis source - the on-screen stick
    /// uses it with its own dead zone and expo.</summary>
    internal static float Curve(float value, float deadZone, float max, float expo)
    {
        float magnitude = Mathf.Abs(value);
        if (magnitude <= deadZone) return 0f;

        float t = Mathf.InverseLerp(deadZone, max, magnitude); // 0 at edge, 1 at max
        t = Mathf.Pow(t, expo);
        return Mathf.Sign(value) * Mathf.Clamp01(t);
    }

    private Vector3 GetGravity()
    {
        if (gravitySensor != null)
        {
            return gravitySensor.gravity.ReadValue();
        }
        return Vector3.zero;
    }
}
