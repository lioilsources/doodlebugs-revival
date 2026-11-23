using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings, in its own class so GameHUD stops growing (ROADMAP §3.9), on
/// the phone-first MenuCanvas (Prompts/30). CONTROLS: POINT / RATE / GYRO
/// (Prompts/28 D4) with RECENTER for gyro. Phase 2's shake / music / SFX /
/// haptics rows belong here. Five taps on the title open the Flight Lab.
/// THEME opens the UI theme picker (Prompts/31).
/// </summary>
public class SettingsOverlay : MonoBehaviour
{
    public static SettingsOverlay Instance { get; private set; }

    public static bool IsOpen => Instance != null && Instance._panel != null && Instance._panel.activeSelf;

    private GameObject _panel;
    private readonly Ui.Panel[] _options = new Ui.Panel[3];
    private readonly Text[] _optionLabels = new Text[3];
    private Text _hint;
    private GameObject _recenter;
    private int _titleTaps;
    private float _lastTitleTap;

    private static readonly ControlScheme[] Schemes = { ControlScheme.Pointer, ControlScheme.Joystick, ControlScheme.Gyro };
    private static readonly string[] SchemeNames = { "POINT", "RATE", "GYRO" };

    public static SettingsOverlay Create(Canvas _ = null)
    {
        var mc = MenuCanvas.Instance ?? MenuCanvas.Create();
        var go = new GameObject("SettingsOverlay");
        go.transform.SetParent(mc.Root, false);
        UiKit.Stretch(go);
        return go.AddComponent<SettingsOverlay>();
    }

    private void Awake()
    {
        Instance = this;
        UiTheme.Changed += OnThemeChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UiTheme.Changed -= OnThemeChanged;
    }

    /// <summary>Prompts/31: rebuild in the new theme, stay open.</summary>
    private void OnThemeChanged(UiThemeDef _)
    {
        if (_panel == null) return;
        bool open = _panel.activeSelf;
        Destroy(_panel);
        _panel = null;
        _leanToggle = null;
        _hint = null;
        if (!open) return;
        Build();
        Refresh();
        _panel.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Open()
    {
        if (_panel == null) Build();
        Refresh();
        transform.SetAsLastSibling();
        _panel.SetActive(true);
        UiTween.Scale(_card, Vector3.one * 0.92f, Vector3.one, 0.22f);
        SfxManager.PlayTick();
        Telemetry.Log("settings_open");
    }

    public void Close()
    {
        if (_panel != null) _panel.SetActive(false);
        SfxManager.PlayTick();
    }

    private RectTransform _card;

    private void Build()
    {
        _panel = new GameObject("Panel");
        _panel.transform.SetParent(transform, false);
        UiKit.Stretch(_panel);
        var dim = _panel.AddComponent<Image>();
        dim.color = UiTheme.WithAlpha(UiTheme.Active.VeilBottom, 0.85f);   // swallows touches

        var card = Ui.Card(_panel.transform, "Card", UiTheme.Paper);
        _card = card.Root;
        Ui.Place(card.Root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 320f));
        var face = card.Face.transform;

        var title = Ui.Text(face, "Title", "SETTINGS", UiTheme.Display, UiTheme.SizeTitle, UiTheme.Ink);
        Ui.TopBand(title.rectTransform, 12f, 36f, 20f, 20f);
        title.raycastTarget = true;
        var tap = title.gameObject.AddComponent<Button>();
        tap.transition = Selectable.Transition.None;
        tap.onClick.AddListener(TitleTap);

        // THEME (Prompts/31 §3.5): the picker sheet
        var theme = Ui.Card(face, "Theme", UiTheme.Frame, UiTheme.RadiusButton, 4f);
        Ui.Place(theme.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -10f), new Vector2(230f, 48f));
        var themeLabel = Ui.Text(theme.Face.transform, "Label", "THEME · " + UiTheme.Active.Name, UiTheme.Label, 16,
            UiTheme.FrameText, TextAnchor.MiddleCenter);
        themeLabel.resizeTextForBestFit = true;
        themeLabel.resizeTextMinSize = UiTheme.MinText;
        themeLabel.resizeTextMaxSize = themeLabel.fontSize;
        Ui.Press(theme.Face, () => ThemeSheet.Open(transform));

        bool mobile = InputManager.Instance != null && InputManager.Instance.IsMobile();
        if (mobile)
        {
            var label = Ui.Text(face, "ControlsLabel", "CONTROLS", UiTheme.Label, 15, UiTheme.TextDim);
            Ui.TopBand(label.rectTransform, 58f, 20f, 20f, 20f);

            var row = Ui.Node(face, "Options");
            Ui.TopBand(row, 82f, 56f, 20f, 20f);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            for (int i = 0; i < Schemes.Length; i++)
            {
                var opt = Ui.Card(row, SchemeNames[i], UiTheme.Paper, UiTheme.RadiusButton, 4f);
                _optionLabels[i] = Ui.Text(opt.Face.transform, "Label", SchemeNames[i], UiTheme.Display, 24,
                    UiTheme.Ink, TextAnchor.MiddleCenter);
                var captured = Schemes[i];
                Ui.Press(opt.Face, () => Select(captured));
                _options[i] = opt;
            }

            _hint = Ui.Text(face, "Hint", "", UiTheme.Body, 14, UiTheme.TextDim, TextAnchor.UpperLeft);
            Ui.TopBand(_hint.rectTransform, 150f, 60f, 20f, 20f);

            var recenter = Ui.BigButton(face, "Recenter", "RECENTER", UiTheme.Gadget, Recenter);
            recenter.Label.fontSize = 20;
            Ui.Place(recenter.Panel.Root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 18f), new Vector2(170f, 48f));
            _recenter = recenter.Panel.Root.gameObject;

            BuildLeanRow(face);
        }
        else
        {
            var hint = Ui.Text(face, "DesktopHint",
                "KEYBOARD: A / D turn, W / S throttle, SPACE fire, E gadget.\nGAMEPAD: left stick turn, right stick throttle, A / RT fire, LB / LT gadget.",
                UiTheme.Body, 15, UiTheme.TextDim, TextAnchor.UpperLeft);
            Ui.TopBand(hint.rectTransform, 64f, 120f, 20f, 20f);
        }

        var close = Ui.BigButton(face, "Close", "CLOSE", UiTheme.Confirm, Close);
        close.Label.fontSize = 22;
        Ui.Place(close.Panel.Root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 18f), new Vector2(150f, 48f));
        UiAudit.Check(_panel.transform);
    }

    private void TitleTap()
    {
        if (!FlightTuning.LabBuild) return;   // no Flight Lab in store builds (plan 33 U3)
        _titleTaps = Time.unscaledTime - _lastTitleTap < 0.6f ? _titleTaps + 1 : 1;
        _lastTitleTap = Time.unscaledTime;
        if (_titleTaps >= 5)
        {
            _titleTaps = 0;
            FlightLabOverlay.Open();
        }
    }

    private void Refresh()
    {
        if (_hint == null) return;   // desktop panel has no options

        var scheme = ControlSettings.Scheme;
        for (int i = 0; i < Schemes.Length; i++)
        {
            bool on = Schemes[i] == scheme;
            _options[i].SetFace(on ? UiTheme.Pick : UiTheme.Paper);
            _options[i].SetShadow(on ? 6f : 3f);
            _optionLabels[i].color = on ? UiTheme.OnPick : UiTheme.Ink;
        }
        _hint.text = scheme switch
        {
            ControlScheme.Pointer => "POINT: push the stick where you want to fly. The plane turns there the short way and handles the throttle.",
            ControlScheme.Joystick => "RATE: stick up / down turns, right / left is throttle. Most precise for aiming.",
            _ => "GYRO: tilt to fly. RECENTER takes your current hold as level.",
        };
        _recenter.SetActive(scheme == ControlScheme.Gyro);
        RefreshLeanRow();
    }

    private void Select(ControlScheme scheme)
    {
        ControlSettings.Set(scheme);
        Refresh();
        SfxManager.Haptic(HapticKind.Light);
    }

    // --- LEAN toggle (Prompts/32 §4.9) ---------------------------------------------

    private Ui.Panel _leanToggle;
    private Text _leanLabel;
    private Text _leanHint;

    /// <summary>LEAN ON / OFF under the scheme row, with one line of help.
    /// Greyed with the reason in GYRO (same hand motion) and without a sensor.</summary>
    private void BuildLeanRow(Transform face)
    {
        _leanToggle = Ui.Card(face, "Lean", UiTheme.Paper, UiTheme.RadiusButton, 4f);
        Ui.Place(_leanToggle.Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -204f), new Vector2(150f, 48f));
        _leanLabel = Ui.Text(_leanToggle.Face.transform, "Label", "LEAN", UiTheme.Display, 22,
            UiTheme.Ink, TextAnchor.MiddleCenter);
        Ui.Press(_leanToggle.Face, ToggleLean);

        _leanHint = Ui.Text(face, "LeanHint", "", UiTheme.Body, 13, UiTheme.TextDim, TextAnchor.MiddleLeft);
        Ui.TopBand(_leanHint.rectTransform, 204f, 48f, 184f, 20f);
    }

    private static bool LeanSensorAvailable =>
        InputManager.Instance?.TouchProvider?.LeanSensor.Available ?? false;

    private void RefreshLeanRow()
    {
        if (_leanToggle == null) return;
        bool gyro = ControlSettings.Scheme == ControlScheme.Gyro;
        bool sensor = LeanSensorAvailable;
        bool usable = !gyro && sensor;
        bool on = usable && ControlSettings.Lean;

        _leanToggle.SetFace(!usable ? UiTheme.PaperDim : on ? UiTheme.Pick : UiTheme.Paper);
        _leanToggle.SetShadow(on ? 6f : 3f);
        _leanLabel.text = on ? "LEAN ON" : "LEAN OFF";
        _leanLabel.color = !usable ? UiTheme.TextDim : on ? UiTheme.OnPick : UiTheme.Ink;
        int pct = Mathf.RoundToInt(FlightTuning.Active.LeanBonus * 100f);
        _leanHint.text = !sensor ? "LEAN needs a motion sensor - this device has none."
            : gyro ? "LEAN is off in GYRO: both read the same hand motion."
            : $"Roll the phone the way the plane turns: up to +{pct} % turn rate.";
    }

    private void ToggleLean()
    {
        if (ControlSettings.Scheme == ControlScheme.Gyro || !LeanSensorAvailable) return;
        ControlSettings.SetLean(!ControlSettings.Lean);
        RefreshLeanRow();
        SfxManager.Haptic(HapticKind.Light);
    }

    private void Recenter()
    {
        InputManager.Instance?.MobileProvider?.Recenter();
        SfxManager.PlayTick();
    }
}
