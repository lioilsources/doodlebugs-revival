using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Singleton that provides the correct input provider based on platform
/// Automatically switches between keyboard and gamepad on desktop
/// Supports New Input System for iOS MFi controllers
/// </summary>
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    private IInputProvider inputProvider;
    private MobileInputProvider mobileProvider;

    // Desktop providers for auto-switching
    private DesktopInputProvider desktopProvider;
    private GamepadInputProvider gamepadProvider;

    [SerializeField] private bool forceMobileInput = false;
    [SerializeField] private bool forceGamepadInput = false;

    private bool isUsingGamepad = false;
    private float lastInputCheckTime = 0f;
    private const float INPUT_CHECK_INTERVAL = 0.5f;

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
        SubscribeToDeviceChanges();
    }

    private void SubscribeToDeviceChanges()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is Gamepad)
        {
            if (change == InputDeviceChange.Added)
            {
                Debug.Log($"[InputManager] Gamepad connected: {device.displayName}");
                if (!isUsingGamepad)
                {
                    SwitchToGamepad();
                }
            }
            else if (change == InputDeviceChange.Removed)
            {
                Debug.Log($"[InputManager] Gamepad disconnected: {device.displayName}");
                if (isUsingGamepad && Gamepad.current == null)
                {
                    SwitchToDefaultInput();
                }
            }
        }
    }

    private void SwitchToGamepad()
    {
        inputProvider = gamepadProvider;
        isUsingGamepad = true;
        Debug.Log("[InputManager] Switched to GamepadInputProvider (device connected)");
    }

    private void SwitchToDefaultInput()
    {
        if (mobileProvider != null)
        {
            inputProvider = mobileProvider;
            Debug.Log("[InputManager] Switched to MobileInputProvider (gamepad disconnected)");
        }
        else if (desktopProvider != null)
        {
            inputProvider = desktopProvider;
            Debug.Log("[InputManager] Switched to DesktopInputProvider (gamepad disconnected)");
        }
        isUsingGamepad = false;
    }

    private void OnDestroy()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    private void InitializeInputProvider()
    {
        bool isMobile = Application.platform == RuntimePlatform.Android ||
                        Application.platform == RuntimePlatform.IPhonePlayer ||
                        forceMobileInput;

        // Log connected gamepads
        Debug.Log($"[InputManager] Platform: {Application.platform}, IsMobile: {isMobile}");
        Debug.Log($"[InputManager] Gamepad.current: {Gamepad.current?.displayName ?? "none"}");

        gamepadProvider = new GamepadInputProvider();

        if (isMobile)
        {
            mobileProvider = new MobileInputProvider();
            mobileProvider.Initialize();

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
            desktopProvider = new DesktopInputProvider();

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

        if (mobileProvider != null && isUsingGamepad)
        {
            mobileProvider.UpdateInput();
        }

        CheckInputDeviceSwitch();
        DebugInputState();
    }

    private void DebugInputState()
    {
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonNorth.wasPressedThisFrame)
                Debug.Log($"[InputManager] Button North pressed! Provider: {inputProvider?.GetType().Name}");
            if (Gamepad.current.buttonSouth.wasPressedThisFrame)
                Debug.Log($"[InputManager] Button South pressed! Provider: {inputProvider?.GetType().Name}");
            if (Gamepad.current.buttonEast.wasPressedThisFrame)
                Debug.Log($"[InputManager] Button East pressed! Provider: {inputProvider?.GetType().Name}");
            if (Gamepad.current.buttonWest.wasPressedThisFrame)
                Debug.Log($"[InputManager] Button West pressed! Provider: {inputProvider?.GetType().Name}");
        }

        if (Time.time - _lastDebugTime > 5f)
        {
            _lastDebugTime = Time.time;
            string providerName = inputProvider?.GetType().Name ?? "null";
            bool gamepadConnected = GamepadInputProvider.IsGamepadConnected();
            string gamepadName = Gamepad.current?.displayName ?? "none";
            Debug.Log($"[InputManager] Status: Provider={providerName}, IsUsingGamepad={isUsingGamepad}, GamepadConnected={gamepadConnected}, Gamepad={gamepadName}");
        }
    }

    private void CheckInputDeviceSwitch()
    {
        if (Time.time - lastInputCheckTime < INPUT_CHECK_INTERVAL)
            return;

        lastInputCheckTime = Time.time;

        bool gamepadActive = GamepadInputProvider.IsGamepadActive();
        bool isMobilePlatform = mobileProvider != null;

        if (isMobilePlatform)
        {
            bool touchActive = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed;

            if (gamepadActive && !isUsingGamepad)
            {
                inputProvider = gamepadProvider;
                isUsingGamepad = true;
                Debug.Log("InputManager: Switched to GamepadInputProvider (mobile)");
            }
            else if (touchActive && isUsingGamepad)
            {
                inputProvider = mobileProvider;
                isUsingGamepad = false;
                Debug.Log("InputManager: Switched to MobileInputProvider");
            }
        }
        else
        {
            bool keyboardActive = Keyboard.current != null && Keyboard.current.anyKey.isPressed && !gamepadActive;

            if (gamepadActive && !isUsingGamepad)
            {
                inputProvider = gamepadProvider;
                isUsingGamepad = true;
                Debug.Log("InputManager: Switched to GamepadInputProvider");
            }
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
