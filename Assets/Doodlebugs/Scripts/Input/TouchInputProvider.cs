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
/// </summary>
public class TouchInputProvider : IInputProvider
{
    public const int Slots = 2;

    private readonly MobileInputProvider _gyro;
    private ControlScheme _scheme;
    private Vector2 _stick;
    private readonly bool[] _held = new bool[Slots];

    public TouchInputProvider(MobileInputProvider gyro, ControlScheme scheme)
    {
        _gyro = gyro;
        SetScheme(scheme);
    }

    public ControlScheme Scheme => _scheme;

    /// <summary>The gravity source (RECENTER lives on it).</summary>
    public MobileInputProvider Gyro => _gyro;

    public void SetScheme(ControlScheme scheme)
    {
        _scheme = scheme;
        _stick = Vector2.zero;

        // The sensor only runs when someone flies by it - a joystick player
        // should not pay for gravity sampling.
        bool gyro = scheme == ControlScheme.Gyro;
        _gyro?.SetSensorEnabled(gyro);
        if (gyro) _gyro?.Recenter();
    }

    // --- TouchControls seam --------------------------------------------------

    /// <summary>Stick axes, already through the response curve, -1..1 each.</summary>
    public void SetStick(Vector2 axes) => _stick = axes;

    public void SetHeld(int slot, bool held)
    {
        if ((uint)slot < Slots) _held[slot] = held;
    }

    /// <summary>Controls hidden or app lost focus: nothing stays pressed.</summary>
    public void ReleaseAll()
    {
        _stick = Vector2.zero;
        for (int i = 0; i < Slots; i++) _held[i] = false;
    }

    // --- IInputProvider --------------------------------------------------------

    public float GetHorizontalInput() =>
        _scheme == ControlScheme.Gyro && _gyro != null ? _gyro.GetHorizontalInput() : _stick.x;

    public float GetVerticalInput() =>
        _scheme == ControlScheme.Gyro && _gyro != null ? _gyro.GetVerticalInput() : _stick.y;

    public bool GetShootInput() => _held[0];

    public bool GetShootInput(int slot) => (uint)slot < Slots && _held[slot];

    public void UpdateInput()
    {
        if (_scheme == ControlScheme.Gyro) _gyro?.UpdateInput();
    }
}
