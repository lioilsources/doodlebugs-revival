using UnityEngine;

/// <summary>
/// Singleton that provides the correct input provider based on platform
/// </summary>
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    private IInputProvider inputProvider;
    private MobileInputProvider mobileProvider;

    [SerializeField] private bool forceMobileInput = false; // For testing in editor

    public IInputProvider InputProvider => inputProvider;
    public MobileInputProvider MobileProvider => mobileProvider;

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
            inputProvider = mobileProvider;
            Debug.Log("InputManager: Using MobileInputProvider");
        }
        else
        {
            inputProvider = new DesktopInputProvider();
            Debug.Log("InputManager: Using DesktopInputProvider");
        }
    }

    private void Update()
    {
        inputProvider?.UpdateInput();
    }

    public bool IsMobile()
    {
        return mobileProvider != null;
    }
}
