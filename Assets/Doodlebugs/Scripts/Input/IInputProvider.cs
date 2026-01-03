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
    /// Called every frame to update input state (for touch tracking)
    /// </summary>
    void UpdateInput();
}
