using UnityEngine;

/// <summary>
/// The phone's input provider (Prompts/26). Axes come from the on-screen
/// stick or, in the Gyro scheme, from <see cref="MobileInputProvider"/>'s
/// gravity pipeline; the trigger buttons feed one held flag per weapon slot.
/// It owns no UI - <see cref="TouchControls"/> writes into it - so
/// PlayerController and Shooting keep seeing the same four-method interface
/// they see on desktop.
///
/// Hold-to-fire (D6): a held trigger reports "pressed" every frame and
/// Shooting's cooldown gate turns that into shots at the weapon's cadence.
/// A tap is one frame of "pressed" - one shot, if the weapon is charged.
///
/// LEAN (Prompts/32): a <see cref="LeanSensor"/> runs next to the stick while
/// <see cref="ControlSettings.Lean"/> is on and the scheme is not Gyro (both
/// read the same hand motion). It is reset on every scheme change, release
/// and focus loss, so the meter never carries over a pause.
/// </summary>
public class TouchInputProvider : IInputProvider
{
    public const int Slots = 2;

    private readonly MobileInputProvider _gyro;
    private ControlScheme _scheme;
    private Vector2 _stick;
    private Vector2 _rawStick;   // unshaped stick vector, -1..1 per axis (POINT)
    private readonly bool[] _held = new bool[Slots];
    private readonly LeanSensor _lean = new LeanSensor();

    public TouchInputProvider(MobileInputProvider gyro, ControlScheme scheme)
    {
        _gyro = gyro;
        SetScheme(scheme);
    }

    public ControlScheme Scheme => _scheme;

    /// <summary>The gravity source (RECENTER lives on it).</summary>
    public MobileInputProvider Gyro => _gyro;

    /// <summary>The LEAN meter source (Flight Lab live row, HUD arc).</summary>
    public LeanSensor LeanSensor => _lean;

    /// <summary>LEAN applies right now: switched on, a stick scheme, a sensor.</summary>
    public bool LeanActive => _lean.Enabled && _lean.Available;

    public void SetScheme(ControlScheme scheme)
    {
        _scheme = scheme;
        _stick = Vector2.zero;
        _rawStick = Vector2.zero;

        // Sensors only run when someone flies by them - a joystick player
        // should not pay for gravity sampling. The Gyro scheme and the LEAN
        // fallback share the GravitySensor, so switch everything off first
        // and only then on.
        bool gyro = scheme == ControlScheme.Gyro;
        bool lean = ControlSettings.Lean && !gyro;
        if (!gyro) _gyro?.SetSensorEnabled(false);
        if (!lean) _lean.SetEnabled(false);
        if (gyro)
        {
            _gyro?.SetSensorEnabled(true);
            _gyro?.Recenter();
        }
        if (lean) _lean.SetEnabled(true);
    }

    /// <summary>ControlSettings.Lean flipped: re-apply the sensors.</summary>
    public void RefreshLean() => SetScheme(_scheme);

    // --- TouchControls seam --------------------------------------------------

    /// <summary>Stick axes, already through the response curve, -1..1 each.</summary>
    public void SetStick(Vector2 axes) => _stick = axes;

    /// <summary>The stick as the thumb holds it, before any curve (POINT).</summary>
    public void SetRawStick(Vector2 raw) => _rawStick = raw;

    /// <summary>The on-screen stick is shown in RATE and POINT.</summary>
    public bool UsesStick => _scheme != ControlScheme.Gyro;

    private bool _recover;

    /// <summary>Double-tap on the stick (TouchControls).</summary>
    public void RequestRecover() => _recover = true;

    public bool ConsumeRecover()
    {
        bool r = _recover;
        _recover = false;
        return r;
    }

    public void SetHeld(int slot, bool held)
    {
        if ((uint)slot < Slots) _held[slot] = held;
    }

    /// <summary>Controls hidden or app lost focus: nothing stays pressed.</summary>
    public void ReleaseAll()
    {
        _stick = Vector2.zero;
        _rawStick = Vector2.zero;
        for (int i = 0; i < Slots; i++) _held[i] = false;
        _recover = false;
        _lean.Reset();
    }

    // --- IInputProvider --------------------------------------------------------

    public float GetHorizontalInput() =>
        _scheme == ControlScheme.Gyro && _gyro != null ? _gyro.GetHorizontalInput() : _stick.x;

    public float GetVerticalInput() =>
        _scheme == ControlScheme.Gyro && _gyro != null ? _gyro.GetVerticalInput() : _stick.y;

    public bool GetShootInput() => _held[0];

    public bool GetShootInput(int slot) => (uint)slot < Slots && _held[slot];

    public bool GetShootHeld(int slot) => GetShootInput(slot);

    public float GetLean() => _scheme != ControlScheme.Gyro && _lean.Enabled ? _lean.Lean : 0f;

    public bool TryGetHeadingIntent(out float headingDeg, out float push)
    {
        headingDeg = 0f;
        push = 0f;
        if (_scheme != ControlScheme.Pointer) return false;

        float m = Mathf.Clamp01(_rawStick.magnitude);
        if (m >= FlightTuning.PointDeadZone)
        {
            headingDeg = Mathf.Atan2(_rawStick.y, _rawStick.x) * Mathf.Rad2Deg;
            push = Mathf.Max(0.001f, Mathf.InverseLerp(FlightTuning.PointDeadZone, 1f, m));
        }
        return true;
    }

    public void UpdateInput()
    {
        if (_scheme == ControlScheme.Gyro) _gyro?.UpdateInput();
        else _lean.Update(Time.unscaledDeltaTime);
    }
}
