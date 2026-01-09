using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gamepad input using Unity New Input System
/// Left stick Y: rotation (up=right, down=left)
/// Right stick Y: throttle (up=faster, down=slower)
/// Works with Xbox, PlayStation, MFi (iOS), and generic controllers
/// </summary>
public class GamepadInputProvider : IInputProvider
{
    private const float DEADZONE = 0.15f;
    private const float ROTATION_SENSITIVITY = 0.5f;
    private const float THROTTLE_SENSITIVITY = 1.0f;

    private bool _rightTriggerPressed = false;
    private bool _leftTriggerPressed = false;

    public float GetHorizontalInput()
    {
        if (Gamepad.current == null) return 0f;

        // Left stick Y axis controls rotation
        // Up (negative Y) = turn right, Down (positive Y) = turn left
        float raw = -Gamepad.current.leftStick.y.ReadValue();
        return ApplyDeadzone(raw) * ROTATION_SENSITIVITY;
    }

    public float GetVerticalInput()
    {
        if (Gamepad.current == null) return 0f;

        // Right stick Y axis controls throttle
        float raw = Gamepad.current.rightStick.y.ReadValue();
        return ApplyDeadzone(raw) * THROTTLE_SENSITIVITY;
    }

    public bool GetShootInput()
    {
        if (Gamepad.current == null) return false;

        // Face buttons (A/B/X/Y or Cross/Circle/Square/Triangle)
        if (Gamepad.current.buttonNorth.wasPressedThisFrame ||
            Gamepad.current.buttonSouth.wasPressedThisFrame ||
            Gamepad.current.buttonEast.wasPressedThisFrame ||
            Gamepad.current.buttonWest.wasPressedThisFrame)
        {
            return true;
        }

        // Shoulder buttons
        if (Gamepad.current.leftShoulder.wasPressedThisFrame ||
            Gamepad.current.rightShoulder.wasPressedThisFrame)
        {
            return true;
        }

        // Right trigger with edge detection
        float rightTrigger = Gamepad.current.rightTrigger.ReadValue();
        if (rightTrigger > 0.3f && !_rightTriggerPressed)
        {
            _rightTriggerPressed = true;
            return true;
        }
        if (rightTrigger < 0.1f) _rightTriggerPressed = false;

        // Left trigger with edge detection
        float leftTrigger = Gamepad.current.leftTrigger.ReadValue();
        if (leftTrigger > 0.3f && !_leftTriggerPressed)
        {
            _leftTriggerPressed = true;
            return true;
        }
        if (leftTrigger < 0.1f) _leftTriggerPressed = false;

        return false;
    }

    public void UpdateInput()
    {
        // No smoothing needed - analog sticks provide natural smoothing
    }

    private float ApplyDeadzone(float value)
    {
        if (Mathf.Abs(value) < DEADZONE)
            return 0f;

        float sign = Mathf.Sign(value);
        float magnitude = Mathf.Abs(value);
        return sign * (magnitude - DEADZONE) / (1f - DEADZONE);
    }

    /// <summary>
    /// Check if any gamepad is connected
    /// </summary>
    public static bool IsGamepadConnected()
    {
        return Gamepad.current != null;
    }

    /// <summary>
    /// Check if gamepad is actively being used
    /// </summary>
    public static bool IsGamepadActive()
    {
        if (Gamepad.current == null) return false;

        if (Gamepad.current.wasUpdatedThisFrame)
        {
            // Check stick movement
            if (Gamepad.current.leftStick.ReadValue().magnitude > 0.5f ||
                Gamepad.current.rightStick.ReadValue().magnitude > 0.5f)
                return true;

            // Check buttons
            if (Gamepad.current.buttonNorth.isPressed ||
                Gamepad.current.buttonSouth.isPressed ||
                Gamepad.current.buttonEast.isPressed ||
                Gamepad.current.buttonWest.isPressed ||
                Gamepad.current.leftShoulder.isPressed ||
                Gamepad.current.rightShoulder.isPressed ||
                Gamepad.current.leftTrigger.ReadValue() > 0.3f ||
                Gamepad.current.rightTrigger.ReadValue() > 0.3f)
                return true;
        }
        return false;
    }
}
