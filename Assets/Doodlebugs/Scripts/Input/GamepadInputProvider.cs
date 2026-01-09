using UnityEngine;

/// <summary>
/// Gamepad input using controller
/// Left stick Y: rotation (up=right, down=left)
/// Right stick Y: throttle (up=faster, down=slower)
/// Works with Xbox, PlayStation, and generic controllers
/// </summary>
public class GamepadInputProvider : IInputProvider
{
    // Deadzone to prevent stick drift
    private const float DEADZONE = 0.15f;

    // Sensitivity multipliers (lower = less sensitive)
    private const float ROTATION_SENSITIVITY = 0.5f;  // Reduce rotation sensitivity to 50%
    private const float THROTTLE_SENSITIVITY = 1.0f;  // Full throttle sensitivity

    public float GetHorizontalInput()
    {
        // Left stick Y axis controls rotation
        // Up (negative Y) = turn right (positive horizontal)
        // Down (positive Y) = turn left (negative horizontal)
        float raw = -Input.GetAxis("Vertical");
        return ApplyDeadzone(raw) * ROTATION_SENSITIVITY;
    }

    public float GetVerticalInput()
    {
        // Right stick Y axis (axis 3) controls throttle
        // Forward (up) = speed up, Backward (down) = slow down
        float raw = GetAxisSafe("RightStickAxis3");
        return ApplyDeadzone(raw) * THROTTLE_SENSITIVITY;
    }

    public bool GetShootInput()
    {
        // Check joystick buttons, excluding D-pad (buttons 6-9)
        for (int i = 0; i < 20; i++)
        {
            // Skip D-pad buttons (6, 7, 8, 9)
            if (i >= 6 && i <= 9) continue;

            KeyCode buttonCode = (KeyCode)(KeyCode.JoystickButton0 + i);
            if (Input.GetKeyDown(buttonCode))
            {
                Debug.Log($"[GamepadInput] Shoot triggered by JoystickButton{i}");
                return true;
            }
        }

        // Check triggers - configured in InputManager.asset
        // RightTrigger (axis 9), LeftTrigger (axis 10), Triggers (axis 2 - combined)
        float rightTrigger = GetAxisSafe("RightTrigger");
        float leftTrigger = GetAxisSafe("LeftTrigger");
        float combinedTriggers = GetAxisSafe("Triggers");

        // Right trigger
        if (rightTrigger > 0.3f && !_rightTriggerPressed)
        {
            _rightTriggerPressed = true;
            Debug.Log($"[GamepadInput] Shoot triggered by RightTrigger: {rightTrigger}");
            return true;
        }
        if (rightTrigger < 0.1f) _rightTriggerPressed = false;

        // Left trigger
        if (leftTrigger > 0.3f && !_leftTriggerPressed)
        {
            _leftTriggerPressed = true;
            Debug.Log($"[GamepadInput] Shoot triggered by LeftTrigger: {leftTrigger}");
            return true;
        }
        if (leftTrigger < 0.1f) _leftTriggerPressed = false;

        // Combined triggers (some controllers use single axis)
        if (Mathf.Abs(combinedTriggers) > 0.3f && !_combinedTriggerPressed)
        {
            _combinedTriggerPressed = true;
            Debug.Log($"[GamepadInput] Shoot triggered by Triggers: {combinedTriggers}");
            return true;
        }
        if (Mathf.Abs(combinedTriggers) < 0.1f) _combinedTriggerPressed = false;

        return false;
    }

    private bool _rightTriggerPressed = false;
    private bool _leftTriggerPressed = false;
    private bool _combinedTriggerPressed = false;

    /// <summary>
    /// Safely get axis value, returns 0 if axis doesn't exist
    /// </summary>
    private float GetAxisSafe(string axisName)
    {
        try
        {
            return Input.GetAxis(axisName);
        }
        catch
        {
            return 0f;
        }
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
        // Check left stick movement
        if (Mathf.Abs(Input.GetAxis("Horizontal")) > 0.5f ||
            Mathf.Abs(Input.GetAxis("Vertical")) > 0.5f)
            return true;

        // Check right stick movement (axis 3)
        try
        {
            if (Mathf.Abs(Input.GetAxis("RightStickAxis3")) > 0.5f)
                return true;
        }
        catch { }

        // Check common gamepad buttons
        for (int i = 0; i < 20; i++)
        {
            if (Input.GetKey((KeyCode)(KeyCode.JoystickButton0 + i)))
                return true;
        }

        return false;
    }
}
