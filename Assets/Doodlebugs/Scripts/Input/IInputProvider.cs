/// <summary>
/// Interface for abstracting input across platforms (desktop/mobile)
/// </summary>
public interface IInputProvider
{
    /// <summary>
    /// Returns horizontal input for plane rotation (-1 = left, 0 = none, 1 = right)
    /// </summary>
    float GetHorizontalInput();

    /// <summary>
    /// Returns vertical input for throttle (-1 = slow down, 0 = none, 1 = speed up)
    /// </summary>
    float GetVerticalInput();

    /// <summary>
    /// Returns true on the frame when shoot button is pressed
    /// </summary>
    bool GetShootInput();

    /// <summary>
    /// Shoot input for a weapon slot (0 = primary). Providers with one
    /// trigger get this for free: slot 0 is <see cref="GetShootInput"/>,
    /// every other slot is silent. The touch provider overrides it with one
    /// on-screen trigger per slot (Prompts/26 D4).
    /// </summary>
    bool GetShootInput(int slot) => slot == 0 && GetShootInput();

    /// <summary>
    /// True while the trigger of a slot is HELD (charge / remote-detonate
    /// weapons, Prompts/29). Touch already reports held every frame; edge-
    /// triggered providers override this with the button's pressed state.
    /// </summary>
    bool GetShootHeld(int slot) => GetShootInput(slot);

    /// <summary>
    /// POINT scheme (Prompts/28 §4): instead of axes, a direction of flight in
    /// world degrees (0 = right, 90 = up) and how hard it is pushed (0..1;
    /// 0 = no direction, fly straight at cruise). Returns false for every
    /// axis-based provider, which is all of them except touch in POINT.
    /// </summary>
    bool TryGetHeadingIntent(out float headingDeg, out float push)
    {
        headingDeg = 0f;
        push = 0f;
        return false;
    }

    /// <summary>
    /// Called every frame to update input state (for touch tracking)
    /// </summary>
    void UpdateInput();
}
