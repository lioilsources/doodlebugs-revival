using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gamepad input using Unity New Input System
/// Left stick Y: rotation (up=right, down=left)
/// Right stick Y: throttle (up=faster, down=slower)
/// Works with Xbox, PlayStation, MFi (iOS), and generic controllers
/// Supports multiple gamepads via gamepadIndex (maps to Gamepad.all[index])
/// </summary>
public class GamepadInputProvider : IInputProvider
{
    private const float DEADZONE = 0.15f;
    private const float ROTATION_SENSITIVITY = 0.5f;
    private const float THROTTLE_SENSITIVITY = 1.0f;

    private int gamepadIndex;
    private bool _rightTriggerPressed = false;
    private bool _leftTriggerPressed = false;

    /// <summary>
    /// Create a gamepad input provider for a specific gamepad index
    /// </summary>
    /// <param name="gamepadIndex">Index into Gamepad.all (0 = first gamepad, 1 = second, etc.)</param>
    public GamepadInputProvider(int gamepadIndex = 0)
    {
        this.gamepadIndex = gamepadIndex;
    }

    /// <summary>
    /// Get the gamepad for this player's index
    /// </summary>
    private Gamepad GetGamepad()
    {
        var gamepads = Gamepad.all;
        if (gamepadIndex >= 0 && gamepadIndex < gamepads.Count)
            return gamepads[gamepadIndex];
        return null;
    }

    public float GetHorizontalInput()
    {
        var gamepad = GetGamepad();
        if (gamepad == null) return 0f;

        // Left stick Y axis controls rotation
        // Up (negative Y) = turn right, Down (positive Y) = turn left
        float raw = -gamepad.leftStick.y.ReadValue();
        return ApplyDeadzone(raw) * ROTATION_SENSITIVITY;
    }

    public float GetVerticalInput()
    {
        var gamepad = GetGamepad();
        if (gamepad == null) return 0f;

        // Right stick Y axis controls throttle
        float raw = gamepad.rightStick.y.ReadValue();
        return ApplyDeadzone(raw) * THROTTLE_SENSITIVITY;
    }

    public bool GetShootInput()
    {
        var gamepad = GetGamepad();
        if (gamepad == null) return false;

        // Face buttons (A/B/X/Y or Cross/Circle/Square/Triangle)
        if (gamepad.buttonNorth.wasPressedThisFrame ||
            gamepad.buttonSouth.wasPressedThisFrame ||
            gamepad.buttonEast.wasPressedThisFrame ||
            gamepad.buttonWest.wasPressedThisFrame)
        {
            return true;
        }

        // Shoulder buttons
        if (gamepad.leftShoulder.wasPressedThisFrame ||
            gamepad.rightShoulder.wasPressedThisFrame)
        {
            return true;
        }

        // Right trigger with edge detection
        float rightTrigger = gamepad.rightTrigger.ReadValue();
        if (rightTrigger > 0.3f && !_rightTriggerPressed)
        {
            _rightTriggerPressed = true;
            return true;
        }
        if (rightTrigger < 0.1f) _rightTriggerPressed = false;

        // Left trigger with edge detection
        float leftTrigger = gamepad.leftTrigger.ReadValue();
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
    /// Get the number of connected gamepads
    /// </summary>
    public static int GetGamepadCount()
    {
        return Gamepad.all.Count;
    }

    /// <summary>
    /// Check if a specific gamepad index is connected
    /// </summary>
    public static bool IsGamepadConnected(int index)
    {
        return index >= 0 && index < Gamepad.all.Count;
    }

    /// <summary>
    /// Check if gamepad is actively being used (any button or significant stick movement)
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
