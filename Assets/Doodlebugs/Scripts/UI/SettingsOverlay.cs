using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings, in its own class so GameHUD stops growing (ROADMAP §3.9).
/// One row today - CONTROLS: JOYSTICK / GYRO with RECENTER for gyro
/// (Prompts/26 D12); Phase 2's shake / music / SFX / haptics rows belong
/// here too. Opened from the SETTINGS button in every hangar; rendered above
/// everything else (last sibling of the canvas).
/// </summary>
public class SettingsOverlay : MonoBehaviour
{
    public static SettingsOverlay Instance { get; private set; }

    public static bool IsOpen => Instance != null && Instance._panel != null && Instance._panel.activeSelf;

    private static readonly Color TitleColor = new Color(1f, 0.85f, 0.3f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color OptionIdle = new Color(0.10f, 0.12f, 0.16f, 1f);
    private static readonly Color OptionSelected = new Color(0.35f, 0.22f, 0.08f, 1f);
    private static readonly Color CloseColor = new Color(0.3f, 0.3f, 0.34f, 1f);
    private static readonly Color RecenterColor = new Color(0.12f, 0.3f, 0.45f, 1f);

    private GameObject _panel;
    private Image _joystickBg;
    private Image _gyroBg;
    private Text _hint;
    private GameObject _recenter;

    public static SettingsOverlay Create(Canvas canvas)
    {
        var go = new GameObject("SettingsOverlay");
        go.transform.SetParent(canvas.transform, false);
        UiKit.Stretch(go);
        return go.AddComponent<SettingsOverlay>();
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Open()
    {
        if (_panel == null) Build();
        Refresh();
        transform.SetAsLastSibling();
        _panel.SetActive(true);
        SfxManager.PlayTick();
        Telemetry.Log("settings_open");
    }

    public void Close()
    {
        if (_panel != null) _panel.SetActive(false);
        SfxManager.PlayTick();
    }

    private void Build()
    {
        _panel = new GameObject("Panel");
        _panel.transform.SetParent(transform, false);
        UiKit.Stretch(_panel);
        UiKit.Panel(_panel.transform, "Background", new Color(0f, 0f, 0f, 0.92f), true);

        var centre = new Vector2(0.5f, 0.5f);
        UiKit.Label(_panel.transform, "Title", "SETTINGS", 34, centre, new Vector2(0, 195), TitleColor);

        bool mobile = InputManager.Instance != null && InputManager.Instance.IsMobile();
        if (mobile)
        {
            UiKit.Label(_panel.transform, "ControlsLabel", "CONTROLS", 14, centre, new Vector2(0, 95), Dim);

            (_, _joystickBg, _) = UiKit.FlatButton(_panel.transform, "JoystickOption", "JOYSTICK", 16,
                centre, centre, new Vector2(-115, 40), new Vector2(210, 60), OptionIdle,
                () => Select(ControlScheme.Joystick));
            (_, _gyroBg, _) = UiKit.FlatButton(_panel.transform, "GyroOption", "GYRO", 16,
                centre, centre, new Vector2(115, 40), new Vector2(210, 60), OptionIdle,
                () => Select(ControlScheme.Gyro));

            _hint = UiKit.Label(_panel.transform, "Hint", "", 10, centre, new Vector2(0, -15), Dim,
                new Vector2(900, 30));

            var (recenterButton, _, _) = UiKit.FlatButton(_panel.transform, "Recenter", "RECENTER", 14,
                centre, centre, new Vector2(0, -75), new Vector2(240, 52), RecenterColor, Recenter);
            _recenter = recenterButton.gameObject;
        }
        else
        {
            UiKit.Label(_panel.transform, "DesktopHint",
                "KEYBOARD: A/D TURN, W/S THROTTLE, SPACE FIRE - GAMEPAD: LEFT STICK + ANY BUTTON",
                10, centre, new Vector2(0, 40), Dim, new Vector2(1200, 30));
        }

        // Same slot as the hangar's action button (bottom-right).
        UiKit.FlatButton(_panel.transform, "Close", "CLOSE", 16,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60, 40), new Vector2(180, 68),
            CloseColor, Close);
    }

    private void Refresh()
    {
        if (_joystickBg == null) return;   // desktop panel has no options

        var scheme = ControlSettings.Scheme;
        bool gyro = scheme == ControlScheme.Gyro;
        _joystickBg.color = gyro ? OptionIdle : OptionSelected;
        _gyroBg.color = gyro ? OptionSelected : OptionIdle;
        _hint.text = gyro
            ? "TILT TO FLY. RECENTER TAKES YOUR CURRENT HOLD AS LEVEL."
            : "STICK BOTTOM-RIGHT: UP/DOWN THROTTLE, LEFT/RIGHT TURN.";
        _recenter.SetActive(gyro);
    }

    private void Select(ControlScheme scheme)
    {
        ControlSettings.Set(scheme);
        Refresh();
        SfxManager.PlayTick();
    }

    private void Recenter()
    {
        InputManager.Instance?.MobileProvider?.Recenter();
        SfxManager.PlayTick();
    }
}
