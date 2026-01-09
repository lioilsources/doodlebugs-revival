using UnityEngine;

/// <summary>
/// Singleton that provides the correct input provider based on platform
/// Automatically switches between keyboard and gamepad on desktop
/// </summary>
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    private IInputProvider inputProvider;
    private MobileInputProvider mobileProvider;

    // Desktop providers for auto-switching
    private DesktopInputProvider desktopProvider;
    private GamepadInputProvider gamepadProvider;

    [SerializeField] private bool forceMobileInput = false; // For testing in editor
    [SerializeField] private bool forceGamepadInput = false; // For testing gamepad in editor

    private bool isUsingGamepad = false;
    private float lastInputCheckTime = 0f;
    private const float INPUT_CHECK_INTERVAL = 0.5f; // Check for input device change every 0.5s

    public IInputProvider InputProvider => inputProvider;
    public MobileInputProvider MobileProvider => mobileProvider;
    public bool IsUsingGamepad => isUsingGamepad;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeInputProvider();
    }

    private void InitializeInputProvider()
    {
        bool isMobile = Application.platform == RuntimePlatform.Android ||
                        Application.platform == RuntimePlatform.IPhonePlayer ||
                        forceMobileInput;

        // Log connected joysticks for debugging
        string[] joysticks = Input.GetJoystickNames();
        Debug.Log($"[InputManager] Platform: {Application.platform}, IsMobile: {isMobile}");
        Debug.Log($"[InputManager] Connected joysticks ({joysticks.Length}):");
        for (int i = 0; i < joysticks.Length; i++)
        {
            Debug.Log($"  [{i}]: '{joysticks[i]}'");
        }

        // Always create gamepad provider - gamepads work on both desktop and mobile (BackBone, etc.)
        gamepadProvider = new GamepadInputProvider();

        if (isMobile)
        {
            mobileProvider = new MobileInputProvider();
            mobileProvider.Initialize();

            // Check if gamepad is connected (BackBone, Razer Kishi, etc.)
            if (forceGamepadInput || GamepadInputProvider.IsGamepadConnected())
            {
                inputProvider = gamepadProvider;
                isUsingGamepad = true;
                Debug.Log("InputManager: Using GamepadInputProvider on mobile (gamepad detected)");
            }
            else
            {
                inputProvider = mobileProvider;
                isUsingGamepad = false;
                Debug.Log("InputManager: Using MobileInputProvider");
            }
        }
        else
        {
            // Initialize desktop provider
            desktopProvider = new DesktopInputProvider();

            // Check if gamepad is connected and use it, otherwise keyboard
            if (forceGamepadInput || GamepadInputProvider.IsGamepadConnected())
            {
                inputProvider = gamepadProvider;
                isUsingGamepad = true;
                Debug.Log("InputManager: Using GamepadInputProvider (gamepad detected)");
            }
            else
            {
                inputProvider = desktopProvider;
                isUsingGamepad = false;
                Debug.Log($"InputManager: Using DesktopInputProvider. Platform: {Application.platform}");
            }
        }
    }

    private float _lastDebugTime = 0f;

    private void Update()
    {
        inputProvider?.UpdateInput();

        // Also update mobile provider when using gamepad (to keep gyro data fresh for potential switch)
        if (mobileProvider != null && isUsingGamepad)
        {
            mobileProvider.UpdateInput();
        }

        // Check for input device switching (works on both desktop and mobile)
        CheckInputDeviceSwitch();

        // Debug: log input state periodically and on any gamepad button press
        DebugInputState();
    }

    private void DebugInputState()
    {
        // Check for any gamepad button press
        for (int i = 0; i < 20; i++)
        {
            if (Input.GetKeyDown((KeyCode)(KeyCode.JoystickButton0 + i)))
            {
                Debug.Log($"[InputManager] Button {i} pressed! Provider: {inputProvider?.GetType().Name}, IsUsingGamepad: {isUsingGamepad}");
            }
        }

        // Periodic status (every 5 seconds)
        if (Time.time - _lastDebugTime > 5f)
        {
            _lastDebugTime = Time.time;
            string providerName = inputProvider?.GetType().Name ?? "null";
            bool gamepadConnected = GamepadInputProvider.IsGamepadConnected();
            Debug.Log($"[InputManager] Status: Provider={providerName}, IsUsingGamepad={isUsingGamepad}, GamepadConnected={gamepadConnected}");
        }
    }

    private void CheckInputDeviceSwitch()
    {
        // Don't check every frame for performance
        if (Time.time - lastInputCheckTime < INPUT_CHECK_INTERVAL)
            return;

        lastInputCheckTime = Time.time;

        // Check if user is using gamepad
        bool gamepadActive = GamepadInputProvider.IsGamepadActive();

        bool isMobilePlatform = mobileProvider != null;

        if (isMobilePlatform)
        {
            // Mobile: switch between touch/gyro and gamepad
            bool touchActive = Input.touchCount > 0;

            // Switch to gamepad if gamepad input detected and not already using it
            if (gamepadActive && !isUsingGamepad)
            {
                inputProvider = gamepadProvider;
                isUsingGamepad = true;
                Debug.Log("InputManager: Switched to GamepadInputProvider (mobile)");
            }
            // Switch to touch/gyro if touch detected and currently using gamepad
            else if (touchActive && isUsingGamepad)
            {
                inputProvider = mobileProvider;
                isUsingGamepad = false;
                Debug.Log("InputManager: Switched to MobileInputProvider");
            }
        }
        else
        {
            // Desktop: switch between keyboard and gamepad
            bool keyboardActive = Input.anyKey && !gamepadActive;

            // Switch to gamepad if gamepad input detected and not already using it
            if (gamepadActive && !isUsingGamepad)
            {
                inputProvider = gamepadProvider;
                isUsingGamepad = true;
                Debug.Log("InputManager: Switched to GamepadInputProvider");
            }
            // Switch to keyboard if keyboard input detected and currently using gamepad
            else if (keyboardActive && isUsingGamepad)
            {
                inputProvider = desktopProvider;
                isUsingGamepad = false;
                Debug.Log("InputManager: Switched to DesktopInputProvider");
            }
        }
    }

    public bool IsMobile()
    {
        return mobileProvider != null;
    }
}
