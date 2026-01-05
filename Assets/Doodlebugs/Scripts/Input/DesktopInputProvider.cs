using UnityEngine;

/// <summary>
/// Desktop input using keyboard (arrows/WASD for rotation, Space for shoot)
/// </summary>
public class DesktopInputProvider : IInputProvider
{
    public float GetHorizontalInput()
    {
        return Input.GetAxis("Horizontal");
    }

    public float GetVerticalInput()
    {
        return Input.GetAxis("Vertical");
    }

    public bool GetShootInput()
    {
        return Input.GetKeyDown(KeyCode.Space);
    }

    public void UpdateInput()
    {
        // Desktop input doesn't need per-frame updates
    }
}
