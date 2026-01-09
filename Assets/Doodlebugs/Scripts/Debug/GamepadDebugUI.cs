using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;

/// <summary>
/// On-screen debug display for gamepad - shows connection status and input values
/// Enable via Inspector or by triple-tapping screen
/// </summary>
public class GamepadDebugUI : MonoBehaviour
{
    [SerializeField] private bool showDebug = true;
    [SerializeField] private int fontSize = 24;

    private GUIStyle style;
    private int tapCount = 0;
    private float lastTapTime = 0;

    void Start()
    {
        style = new GUIStyle();
        style.fontSize = fontSize;
        style.normal.textColor = Color.white;
        style.normal.background = MakeTexture(2, 2, new Color(0, 0, 0, 0.7f));
        style.padding = new RectOffset(10, 10, 10, 10);

        // Enable Input System gamepad support
        InputSystem.onDeviceChange += OnDeviceChange;

        // Log current state
        Debug.Log($"[GamepadDebug] Start - Gamepad.current: {Gamepad.current}, Gamepad.all.Count: {Gamepad.all.Count}");
        Debug.Log($"[GamepadDebug] InputSystem.devices.Count: {InputSystem.devices.Count}");
        foreach (var device in InputSystem.devices)
        {
            Debug.Log($"[GamepadDebug] Device: {device.displayName} ({device.GetType().Name})");
        }
    }

    void OnDestroy()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        Debug.Log($"[GamepadDebug] Device change: {device.displayName} - {change}");
        if (device is Gamepad)
        {
            Debug.Log($"[GamepadDebug] Gamepad detected! {device.displayName}");
        }
    }

    void Update()
    {
        // Triple tap to toggle debug
        if (Input.touchCount == 3 && Input.GetTouch(0).phase == UnityEngine.TouchPhase.Began)
        {
            showDebug = !showDebug;
        }

        // Or triple click with mouse (for editor testing)
        if (Input.GetMouseButtonDown(0))
        {
            if (Time.time - lastTapTime < 0.3f)
            {
                tapCount++;
                if (tapCount >= 3)
                {
                    showDebug = !showDebug;
                    tapCount = 0;
                }
            }
            else
            {
                tapCount = 1;
            }
            lastTapTime = Time.time;
        }
    }

    void OnGUI()
    {
        if (!showDebug) return;

        string info = "=== GAMEPAD DEBUG ===\n\n";

        // Connection status
        var gamepad = Gamepad.current;
        if (gamepad != null)
        {
            info += $"Status: CONNECTED\n";
            info += $"Name: {gamepad.displayName}\n";
            info += $"Device: {gamepad.device.description.product}\n\n";

            // Sticks
            Vector2 leftStick = gamepad.leftStick.ReadValue();
            Vector2 rightStick = gamepad.rightStick.ReadValue();
            info += $"Left Stick:  X={leftStick.x:F2} Y={leftStick.y:F2}\n";
            info += $"Right Stick: X={rightStick.x:F2} Y={rightStick.y:F2}\n\n";

            // Triggers
            float lt = gamepad.leftTrigger.ReadValue();
            float rt = gamepad.rightTrigger.ReadValue();
            info += $"Triggers: LT={lt:F2} RT={rt:F2}\n\n";

            // Buttons
            info += "Buttons: ";
            if (gamepad.buttonNorth.isPressed) info += "N ";
            if (gamepad.buttonSouth.isPressed) info += "S ";
            if (gamepad.buttonEast.isPressed) info += "E ";
            if (gamepad.buttonWest.isPressed) info += "W ";
            if (gamepad.leftShoulder.isPressed) info += "LB ";
            if (gamepad.rightShoulder.isPressed) info += "RB ";
            if (gamepad.dpad.up.isPressed) info += "Up ";
            if (gamepad.dpad.down.isPressed) info += "Down ";
            if (gamepad.dpad.left.isPressed) info += "Left ";
            if (gamepad.dpad.right.isPressed) info += "Right ";
            info += "\n";
        }
        else
        {
            info += "Status: NOT CONNECTED\n\n";
            info += "Looking for gamepads...\n";

            // List all input devices
            var devices = InputSystem.devices;
            info += $"\nAll devices ({devices.Count}):\n";
            foreach (var device in devices)
            {
                info += $"  - {device.displayName} ({device.GetType().Name})\n";
            }
        }

        // InputManager status
        info += $"\n--- InputManager ---\n";
        if (InputManager.Instance != null)
        {
            info += $"Provider: {InputManager.Instance.InputProvider?.GetType().Name ?? "null"}\n";
            info += $"IsUsingGamepad: {InputManager.Instance.IsUsingGamepad}\n";
            info += $"Horizontal: {InputManager.Instance.InputProvider?.GetHorizontalInput():F2}\n";
            info += $"Vertical: {InputManager.Instance.InputProvider?.GetVerticalInput():F2}\n";
        }
        else
        {
            info += "InputManager: null\n";
        }

        // Input System details
        info += $"\n--- Input System ---\n";
        info += $"Gamepad.current: {(Gamepad.current != null ? Gamepad.current.displayName : "none")}\n";
        info += $"Gamepad.all count: {Gamepad.all.Count}\n";

        // List all gamepads
        if (Gamepad.all.Count > 0)
        {
            foreach (var gp in Gamepad.all)
            {
                info += $"  - {gp.displayName} ({gp.deviceId})\n";
            }
        }

        // Old Input system joystick check
        info += $"\n--- Legacy Input ---\n";
        string[] joysticks = Input.GetJoystickNames();
        info += $"Joysticks: {joysticks.Length}\n";
        for (int i = 0; i < joysticks.Length; i++)
        {
            info += $"  [{i}]: '{joysticks[i]}'\n";
        }

        info += "\n(Triple-tap to hide)";

        // Draw background box and text (right side to avoid phone notch)
        float width = 400;
        float height = 500;
        float x = Screen.width - width - 10;
        GUI.Box(new Rect(x, 10, width, height), "");
        GUI.Label(new Rect(x, 10, width, height), info, style);
    }

    private Texture2D MakeTexture(int width, int height, Color color)
    {
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;

        Texture2D texture = new Texture2D(width, height);
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
