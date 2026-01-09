using UnityEngine;

/// <summary>
/// Gamepad input using controller (left stick for movement, RB/R1 for shoot)
/// Works with Xbox, PlayStation, and generic controllers
/// </summary>
public class GamepadInputProvider : IInputProvider
{
    // Deadzone to prevent stick drift
    private const float DEADZONE = 0.15f;

    public float GetHorizontalInput()
    {
        float raw = Input.GetAxis("Horizontal");
        return ApplyDeadzone(raw);
    }

    public float GetVerticalInput()
    {
        float raw = Input.GetAxis("Vertical");
        return ApplyDeadzone(raw);
    }

    public bool GetShootInput()
    {
        // Right shoulder button (RB on Xbox, R1 on PlayStation)
        // Unity maps this to joystick button 5 on most controllers
        return Input.GetKeyDown(KeyCode.JoystickButton5) ||
               Input.GetKeyDown(KeyCode.JoystickButton4); // Fallback for some controllers
    }

    public void UpdateInput()
    {
        // No smoothing needed - analog sticks provide natural smoothing
    }

    private float ApplyDeadzone(float value)
    {
        if (Mathf.Abs(value) < DEADZONE)
            return 0f;

        // Remap value outside deadzone to full range
        float sign = Mathf.Sign(value);
        float magnitude = Mathf.Abs(value);
        return sign * (magnitude - DEADZONE) / (1f - DEADZONE);
    }

    /// <summary>
    /// Check if any gamepad input is detected
    /// </summary>
    public static bool IsGamepadConnected()
    {
        string[] joysticks = Input.GetJoystickNames();
        foreach (string name in joysticks)
        {
            if (!string.IsNullOrEmpty(name))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if gamepad is actively being used (any button or significant stick movement)
    /// </summary>
    public static bool IsGamepadActive()
    {
        // Check stick movement
        if (Mathf.Abs(Input.GetAxis("Horizontal")) > 0.5f ||
            Mathf.Abs(Input.GetAxis("Vertical")) > 0.5f)
            return true;

        // Check common gamepad buttons
        for (int i = 0; i < 20; i++)
        {
            if (Input.GetKey((KeyCode)(KeyCode.JoystickButton0 + i)))
                return true;
        }

        return false;
    }
}
