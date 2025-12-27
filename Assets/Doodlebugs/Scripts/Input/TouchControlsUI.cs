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

        mobileInput = InputManager.Instance.MobileProvider;

        // Hide touch controls on desktop
        if (!InputManager.Instance.IsMobile())
        {
            gameObject.SetActive(false);
            return;
        }

        SetupButton(leftButton, OnLeftDown, OnLeftUp);
        SetupButton(rightButton, OnRightDown, OnRightUp);
        SetupButton(shootButton, OnShootDown, OnShootUp);
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
