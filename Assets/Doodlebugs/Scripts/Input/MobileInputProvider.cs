using UnityEngine;

/// <summary>
/// Mobile input provider that receives state from TouchControlsUI buttons
/// </summary>
public class MobileInputProvider : IInputProvider
{
    // Current input state (set by TouchControlsUI)
    private float horizontalInput = 0f;
    private bool shootPressed = false;
    private bool shootConsumed = false;

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
        // Reset shoot consumed flag when button is released
        if (!shootPressed)
        {
            shootConsumed = false;
        }
    }

    // Called by TouchControlsUI
    public void SetLeftPressed(bool pressed)
    {
        if (pressed)
            horizontalInput = -1f;
        else if (horizontalInput < 0)
            horizontalInput = 0f;
    }

    public void SetRightPressed(bool pressed)
    {
        if (pressed)
            horizontalInput = 1f;
        else if (horizontalInput > 0)
            horizontalInput = 0f;
    }

    public void SetShootPressed(bool pressed)
    {
        shootPressed = pressed;
    }
}
