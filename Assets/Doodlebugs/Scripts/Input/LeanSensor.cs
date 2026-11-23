using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// LEAN source (Prompts/32 §4.3): how fast the phone is being ROLLED around
/// the axis that comes out of the screen, fed into the leaking meter of
/// <see cref="LeanModel"/>. A plain class like MobileInputProvider, owned by
/// TouchInputProvider.
///
/// Sign: + = clockwise as the player sees the screen (same as the rotation
/// input x and PlayerController._omega).
///
/// Primary source: the Input System <see cref="UnityEngine.InputSystem.Gyroscope"/>,
/// angularVelocity in rad/s. ASSUMPTION (verify on device in the Flight Lab
/// live row): z follows the right-hand rule with z pointing out of the
/// display toward the player, as on both iOS (CoreMotion) and Android
/// (SensorManager), so a clockwise roll reads NEGATIVE z and the swing rate
/// is -z. Orientation is locked LandscapeLeft and the Input System compensates
/// sensor axes for screen orientation, which only rotates x/y - z keeps its
/// meaning. <see cref="FlightTuning.LeanGyroSign"/> flips it per device if a
/// platform disagrees.
///
/// Fallback without a gyroscope: the roll angle atan2(gravity.x, -gravity.y)
/// from the GravitySensor, differentiated over time (clockwise roll moves
/// gravity toward +x in screen coordinates, so + = clockwise as well). Only
/// valid while the phone is not lying flat (|gravity.z| &lt; 0.85); flat it
/// reads 0. With neither sensor <see cref="Available"/> is false.
/// </summary>
public class LeanSensor
{
    private const float FlatGravityZ = 0.85f;
    private const float MaxRateDeg = 1500f;   // a spike past this is a glitch, not a hand

    private UnityEngine.InputSystem.Gyroscope _gyro;
    private GravitySensor _gravity;
    private bool _enabled;

    private float _prevAngle;
    private bool _hasAngle;

    /// <summary>True when the device has a gyroscope or a gravity sensor.</summary>
    public bool Available => GyroDevice != null || GravityDevice != null;

    /// <summary>True when the primary (gyroscope) source exists.</summary>
    public bool HasGyroscope => GyroDevice != null;

    public bool Enabled => _enabled;

    /// <summary>Last roll rate, deg/s, + = clockwise (after the sign fix).</summary>
    public float Rate { get; private set; }

    /// <summary>Last raw gyro z, deg/s, before any sign fix (Flight Lab row).</summary>
    public float RawGyroZ { get; private set; }

    /// <summary>Meter state, degrees of stored swing.</summary>
    public float Swing { get; private set; }

    /// <summary>Meter value, -1..1, + = clockwise.</summary>
    public float Lean { get; private set; }

    private UnityEngine.InputSystem.Gyroscope GyroDevice => _gyro ??= UnityEngine.InputSystem.Gyroscope.current;
    private GravitySensor GravityDevice => _gravity ??= GravitySensor.current;

    /// <summary>Run the sensor only while LEAN is wanted. Always re-applies
    /// the device state: the Gyro scheme toggles the same GravitySensor.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (GyroDevice != null)
        {
            if (enabled) InputSystem.EnableDevice(GyroDevice);
            else InputSystem.DisableDevice(GyroDevice);
        }
        else if (GravityDevice != null)
        {
            if (enabled) InputSystem.EnableDevice(GravityDevice);
            else InputSystem.DisableDevice(GravityDevice);
        }
        Reset();
    }

    /// <summary>Empty meter, no stale angle (scheme change, release, focus loss).</summary>
    public void Reset()
    {
        Swing = 0f;
        Lean = 0f;
        Rate = 0f;
        RawGyroZ = 0f;
        _hasAngle = false;
    }

    public void Update(float dt)
    {
        if (!_enabled || dt <= 0f)
        {
            if (Swing != 0f) Reset();
            return;
        }

        Rate = Mathf.Clamp(ReadRate(dt), -MaxRateDeg, MaxRateDeg);
        var p = FlightTuning.Active;
        Swing = LeanModel.Step(Swing, Rate, dt, p);
        Lean = LeanModel.Lean01(Swing, p);
    }

    private float ReadRate(float dt)
    {
        var gyro = GyroDevice;
        if (gyro != null && gyro.enabled)
        {
            RawGyroZ = gyro.angularVelocity.ReadValue().z * Mathf.Rad2Deg;
            return -RawGyroZ * FlightTuning.LeanGyroSign;
        }

        var gravity = GravityDevice;
        if (gravity == null || !gravity.enabled) return 0f;
        Vector3 g = gravity.gravity.ReadValue();
        if (Mathf.Abs(g.z) >= FlatGravityZ || g.sqrMagnitude < 0.01f)
        {
            _hasAngle = false;   // flat on a table: the roll angle means nothing
            return 0f;
        }
        float angle = Mathf.Atan2(g.x, -g.y) * Mathf.Rad2Deg;
        float rate = _hasAngle ? Mathf.DeltaAngle(_prevAngle, angle) / dt : 0f;
        _prevAngle = angle;
        _hasAngle = true;
        return rate;
    }
}
