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
    /// Called every frame to update input state (for touch tracking)
    /// </summary>
    void UpdateInput();
}
