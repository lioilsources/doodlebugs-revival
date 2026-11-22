using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flight Lab (Prompts/28 §6): tune the flight numbers on the phone itself.
/// Hidden - five taps on the SETTINGS title. Flight overrides apply only in
/// the lobby warm-up (WaitingForPlayers); a battle always flies the compiled
/// preset (D7). Stick-feel values are input-only and apply everywhere.
/// COPY puts the numbers on the clipboard so they can become the new preset.
/// </summary>
public class FlightLabOverlay : MonoBehaviour
{
    private static FlightLabOverlay _instance;

    public static bool IsOpen => _instance != null && _instance.gameObject.activeSelf;

    private static readonly (string field, float step)[] Rows =
    {
        ("TurnRate", 10f), ("CornerSpeed", 0.5f), ("CruiseSpeed", 0.5f), ("MaxSpeed", 0.5f),
        ("MinSpeed", 0.25f), ("EnvAtMin", 0.05f), ("EnvAtMax", 0.05f), ("TurnAttack", 0.01f),
        ("SnapBoost", 0.1f), ("TurnDrag", 0.1f), ("ThrottleRate", 0.5f), ("BoostSpeed", 0.5f),
        ("LeanBonus", 0.01f), ("LeanTau", 0.1f), ("LeanFullDeg", 1f),
    };
    private static readonly float[] StickSteps = { 0.02f, 2f, 0.1f, 0.02f, 2f };

    private Text _leanLive;

    private RectTransform _list;
    private Text _status;

    public static void Open()
    {
        var mc = MenuCanvas.Instance;
        if (mc == null) return;
        if (_instance == null)
        {
            var go = new GameObject("FlightLab");
            go.transform.SetParent(mc.Root, false);
            UiKit.Stretch(go);
            _instance = go.AddComponent<FlightLabOverlay>();
            _instance.Build();
        }
        _instance.gameObject.SetActive(true);
        _instance.transform.SetAsLastSibling();
        _instance.Rebuild();
        Telemetry.Log("flightlab_open");
    }

    private void Build()
    {
        var dim = gameObject.AddComponent<Image>();
        dim.color = new Color(0.05f, 0.09f, 0.19f, 0.92f);   // dev tool: always dark

        var title = Ui.Text(transform, "Title", "FLIGHT LAB", UiTheme.Display, UiTheme.SizeTitle, Color.white);
        Ui.TopBand(title.rectTransform, 8f, 36f, 20f, 20f);
        _status = Ui.Text(transform, "Status", "", UiTheme.Body, 13, UiTheme.Highlight);
        Ui.TopBand(_status.rectTransform, 44f, 18f, 20f, 20f);

        // Preset buttons + actions on the right
        var side = Ui.Node(transform, "Side");
        side.anchorMin = new Vector2(1f, 0f);
        side.anchorMax = new Vector2(1f, 1f);
        side.pivot = new Vector2(1f, 0.5f);
        side.offsetMin = new Vector2(-180f, 16f);
        side.offsetMax = new Vector2(-16f, -70f);
        var v = side.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8f;
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childForceExpandHeight = false;
        foreach (var preset in FlightTuning.Presets)
        {
            var p = preset;
            AddSideButton(side, p.Name, UiTheme.Paper, () => { FlightTuning.SetLab(p.Clone()); Rebuild(); });
        }
        AddSideButton(side, "RESET", UiTheme.PaperDim, () => { FlightTuning.SetLab(null); FlightTuning.ResetStick(); Rebuild(); });
        AddSideButton(side, "COPY", UiTheme.Gadget, Copy);
        AddSideButton(side, "CLOSE", UiTheme.Confirm, () => gameObject.SetActive(false));

        // Scrolling list of rows
        var view = Ui.Node(transform, "View");
        Ui.Inset(view, 20f, 46f, 200f, 70f);
        view.gameObject.AddComponent<RectMask2D>();
        var hit = view.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.viewport = view;
        _list = Ui.Node(view, "List");
        _list.anchorMin = new Vector2(0f, 1f);
        _list.anchorMax = new Vector2(1f, 1f);
        _list.pivot = new Vector2(0.5f, 1f);
        _list.offsetMin = Vector2.zero;
        _list.offsetMax = Vector2.zero;
        var lv = _list.gameObject.AddComponent<VerticalLayoutGroup>();
        lv.spacing = 6f;
        lv.childControlHeight = false;
        lv.childControlWidth = true;
        lv.childForceExpandHeight = false;
        _list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = _list;

        // LEAN live row (Prompts/32 §9): a clockwise roll must read + swing
        _leanLive = Ui.Text(transform, "LeanLive", "", UiTheme.Label, 15, UiTheme.Highlight, TextAnchor.MiddleLeft);
        Ui.BottomBand(_leanLive.rectTransform, 12f, 28f, 20f, 200f);
    }

    private void Update()
    {
        if (_leanLive == null) return;
        var touch = InputManager.Instance?.TouchProvider;
        var sensor = touch?.LeanSensor;
        if (sensor == null) { _leanLive.text = "LEAN: no touch input on this device"; return; }
        if (!sensor.Available) { _leanLive.text = "LEAN: no gyroscope or gravity sensor"; return; }
        string src = sensor.HasGyroscope ? "gyro" : "gravity";
        _leanLive.text = sensor.Enabled
            ? $"LEAN [{src}]  gyro z {sensor.RawGyroZ,6:0} deg/s   rate {sensor.Rate,6:0}   swing {sensor.Swing,5:0.0}   lean {sensor.Lean,5:0.00}"
            : $"LEAN [{src}] off (switch it on in SETTINGS, POINT or RATE)";
    }

    private static void AddSideButton(RectTransform parent, string label, Color face, System.Action onClick)
    {
        var card = Ui.Card(parent, label, face, UiTheme.RadiusButton, 4f);
        card.Root.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
        Ui.Text(card.Face.transform, "Label", label, UiTheme.Label, 17, UiTheme.Ink, TextAnchor.MiddleCenter);
        Ui.Press(card.Face, onClick);
    }

    private void Rebuild()
    {
        foreach (Transform c in _list) Destroy(c.gameObject);
        var lab = FlightTuning.Lab;
        var shown = lab ?? FlightTuning.Compiled;
        _status.text = lab == null
            ? $"Compiled preset {FlightTuning.Compiled.Name}. Change a value to start a lab override."
            : FlightTuning.LabAllowed ? "Lab override ACTIVE (lobby only - battles fly the compiled preset)."
                                      : "Lab override saved - not active during a battle.";

        foreach (var (field, step) in Rows)
        {
            string f = field;
            float st = step;
            AddRow(f, FlightTuning.Get(shown, f), v =>
            {
                var p = (FlightTuning.Lab ?? FlightTuning.Compiled).Clone();
                p.Name = "LAB";
                FlightTuning.Set(p, f, Mathf.Max(0f, v));
                FlightTuning.SetLab(p);
            }, st);
        }
        for (int i = 0; i < FlightTuning.StickFields.Length; i++)
        {
            int idx = i;
            AddRow("Stick " + FlightTuning.StickFields[i], FlightTuning.GetStick(i), v => FlightTuning.SetStick(idx, v), StickSteps[i]);
        }
    }

    private void AddRow(string name, float value, System.Action<float> set, float step)
    {
        var row = Ui.Node(_list, name);
        row.sizeDelta = new Vector2(0f, 48f);
        var bg = row.gameObject.AddComponent<Image>();
        bg.sprite = UiSprites.RoundedRect;
        bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(10f);
        bg.color = UiTheme.GlassLight;
        bg.raycastTarget = false;

        var label = Ui.Text(row, "Name", name, UiTheme.BodyStrong, 15, Color.white);
        Ui.Inset(label.rectTransform, 12f, 0f, 240f, 0f);
        var val = Ui.Text(row, "Value", value.ToString("0.###"), UiTheme.Label, 20, UiTheme.Highlight, TextAnchor.MiddleCenter);
        Ui.Place(val.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-64f, 0f), new Vector2(100f, 40f));

        Button(row, "-", new Vector2(-170f, 0f), () => { set(value - step); Rebuild(); });
        Button(row, "+", new Vector2(-6f, 0f), () => { set(value + step); Rebuild(); });
    }

    private static void Button(RectTransform row, string text, Vector2 pos, System.Action onClick)
    {
        var card = Ui.Card(row, "B" + text, UiTheme.Paper, 10f, 3f);
        Ui.Place(card.Root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), pos, new Vector2(52f, 44f));
        Ui.Text(card.Face.transform, "L", text, UiTheme.Display, 26, UiTheme.Ink, TextAnchor.MiddleCenter);
        Ui.Press(card.Face, onClick, false);
    }

    private void Copy()
    {
        var p = FlightTuning.Lab ?? FlightTuning.Compiled;
        var json = FlightTuning.ToJson(p);
        json = json.TrimEnd('}');
        for (int i = 0; i < FlightTuning.StickFields.Length; i++)
            json += $",\"{FlightTuning.StickFields[i]}\":{FlightTuning.GetStick(i).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
        json += "}";
        GUIUtility.systemCopyBuffer = json;
        _status.text = "Copied to the clipboard.";
        SfxManager.PlayConfirm();
    }
}
