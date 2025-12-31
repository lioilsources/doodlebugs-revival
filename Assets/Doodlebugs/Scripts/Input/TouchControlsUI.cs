using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// UI component for mobile touch controls
/// Attach to a Canvas with Left, Right, and Shoot buttons
/// </summary>
public class TouchControlsUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button leftButton;
    [SerializeField] private Button rightButton;
    [SerializeField] private Button shootButton;

    private MobileInputProvider mobileInput;

    private void Start()
    {
        // Wait for InputManager to initialize
        if (InputManager.Instance == null)
        {
            Debug.LogWarning("TouchControlsUI: InputManager not found");
            return;
        }

        // Hide touch controls on desktop
        if (!InputManager.Instance.IsMobile())
        {
            gameObject.SetActive(false);
            return;
        }

        mobileInput = InputManager.Instance.MobileProvider;

        // Gyro mode: hide all buttons (touch-anywhere for shoot, gyro for rotation)
        if (mobileInput != null && mobileInput.IsGyroAvailable)
        {
            if (leftButton != null) leftButton.gameObject.SetActive(false);
            if (rightButton != null) rightButton.gameObject.SetActive(false);
            if (shootButton != null) shootButton.gameObject.SetActive(false);
            Debug.Log("TouchControlsUI: Gyro mode - buttons hidden");
            return;
        }

        // Fallback: show buttons (no gyro available)
        SetupButton(leftButton, OnLeftDown, OnLeftUp);
        SetupButton(rightButton, OnRightDown, OnRightUp);
        SetupButton(shootButton, OnShootDown, OnShootUp);
        Debug.Log("TouchControlsUI: Button mode - gyro not available");
    }

    private void SetupButton(Button button, System.Action onDown, System.Action onUp)
    {
        if (button == null) return;

        var trigger = button.gameObject.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = button.gameObject.AddComponent<EventTrigger>();

        // Pointer Down
        var entryDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        entryDown.callback.AddListener((data) => onDown());
        trigger.triggers.Add(entryDown);

        // Pointer Up
        var entryUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        entryUp.callback.AddListener((data) => onUp());
        trigger.triggers.Add(entryUp);

        // Pointer Exit (finger moved outside button while held)
        var entryExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        entryExit.callback.AddListener((data) => onUp());
        trigger.triggers.Add(entryExit);
    }

    private void OnLeftDown() => mobileInput?.SetLeftPressed(true);
    private void OnLeftUp() => mobileInput?.SetLeftPressed(false);

    private void OnRightDown() => mobileInput?.SetRightPressed(true);
    private void OnRightUp() => mobileInput?.SetRightPressed(false);

    private void OnShootDown() => mobileInput?.SetShootPressed(true);
    private void OnShootUp() => mobileInput?.SetShootPressed(false);
}
