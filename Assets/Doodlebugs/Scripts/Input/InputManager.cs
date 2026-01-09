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

        if (isMobile)
        {
            mobileProvider = new MobileInputProvider();
            mobileProvider.Initialize();
            inputProvider = mobileProvider;
            Debug.Log("InputManager: Using MobileInputProvider");
        }
        else
        {
            // Initialize both desktop providers
            desktopProvider = new DesktopInputProvider();
            gamepadProvider = new GamepadInputProvider();

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

    private void Update()
    {
        inputProvider?.UpdateInput();

        // On desktop, check for input device switching
        if (mobileProvider == null && !forceMobileInput)
        {
            CheckInputDeviceSwitch();
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

        // Check if user is using keyboard
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

    public bool IsMobile()
    {
        return mobileProvider != null;
    }
}
