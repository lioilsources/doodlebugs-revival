using Doodlebugs.Network;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The boot screen (Prompts/27 S1, WP5): PLAY (one device, bots, offline),
/// PARTY (today's pairing flow) and SETTINGS. Nothing touches the network
/// until one of them is tapped, so a fresh install shows no permission
/// prompt here. PARTY turns the same screen into the search view - status
/// line, CANCEL, and PLAY still there to give up waiting.
///
/// Its own class over UiKit (GameHUD stops growing); GameHUD's
/// HangarMode.Searching delegates here. Opaque: the arena is never visible
/// behind it.
/// </summary>
public class HomeScreen : MonoBehaviour
{
    public static HomeScreen Instance { get; private set; }

    public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;

    private static readonly Color TitleColor = new Color(1f, 0.85f, 0.3f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.5f);
    private static readonly Color PlayColor = new Color(0.12f, 0.4f, 0.16f, 1f);
    private static readonly Color PlayPressed = new Color(0.2f, 0.6f, 0.26f, 1f);
    private static readonly Color PartyColor = new Color(0.12f, 0.3f, 0.45f, 1f);
    private static readonly Color CancelColor = new Color(0.3f, 0.3f, 0.34f, 1f);
    private static readonly Color SettingsColor = new Color(0.25f, 0.25f, 0.29f, 1f);

    private GameObject _root;
    private Button _play;
    private Image _playBg;
    private Text _playLabel;
    private GameObject _party;
    private GameObject _cancel;
    private Text _status;

    private ConnectionManager _connection;
    private MatchManager _match;
    private ScoreManager _score;

    public static HomeScreen Create(Canvas canvas)
    {
        var go = new GameObject("HomeScreen");
        go.transform.SetParent(canvas.transform, false);
        UiKit.Stretch(go);
        return go.AddComponent<HomeScreen>();
    }

    private void Awake()
    {
        Instance = this;
        Build();
        _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_connection != null)
        {
            _connection.OnStatusMessage -= OnStatus;
            _connection.OnSessionChanged -= OnSessionChanged;
        }
        if (_match != null) _match.OnHangarOpened -= OnHangarOpened;
        if (_score != null) _score.OnMatchStarted -= Hide;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // Singletons appear in their own Awake/Start; bind once each exists.
        if (_connection == null && ConnectionManager.Instance != null)
        {
            _connection = ConnectionManager.Instance;
            _connection.OnStatusMessage += OnStatus;
            _connection.OnSessionChanged += OnSessionChanged;
        }
        if (_match == null && MatchManager.Instance != null)
        {
            _match = MatchManager.Instance;
            _match.OnHangarOpened += OnHangarOpened;
        }
        if (_score == null && ScoreManager.Instance != null)
        {
            _score = ScoreManager.Instance;
            _score.OnMatchStarted += Hide;
        }
    }

    /// <summary>Show Home - the search view if a PARTY search is running.</summary>
    public void Show()
    {
        transform.SetAsLastSibling();
        _root.SetActive(true);
        bool searching = ConnectionManager.Instance != null &&
                         ConnectionManager.Instance.Session == SessionKind.Party;
        SetView(searching);
        _play.interactable = true;
        _playBg.color = PlayColor;
        _playLabel.text = Loc.T("home.play");
    }

    public void Hide()
    {
        if (_root != null) _root.SetActive(false);
    }

    // Any real hangar (Waiting on a party host, PreBattle/LateJoin on a client)
    // means a session is up: get out of the way.
    private void OnHangarOpened(MatchManager.HangarMode mode, int seconds)
    {
        if (mode != MatchManager.HangarMode.Searching) Hide();
    }

    private void OnSessionChanged(SessionKind kind)
    {
        if (IsOpen) SetView(kind == SessionKind.Party);
    }

    private void OnStatus(string message)
    {
        if (_status != null) _status.text = Loc.Upper(message);
    }

    private void SetView(bool searching)
    {
        _party.SetActive(!searching);
        _cancel.SetActive(searching);
        _status.gameObject.SetActive(searching);
        if (searching && string.IsNullOrEmpty(_status.text)) _status.text = Loc.T("hangar.searching");
    }

    // --- actions (four channels: press colour + label, tick, haptic, status) ---

    private void OnPlay()
    {
        if (ConnectionManager.Instance == null) return;
        _play.interactable = false;
        _playBg.color = PlayPressed;
        _playLabel.text = Loc.T("home.loading");
        SfxManager.PlayTick();
        SfxManager.Haptic();
        Telemetry.Log("home_play");
        ConnectionManager.Instance.BeginSolo();
    }

    private void OnParty()
    {
        if (ConnectionManager.Instance == null) return;
        _status.text = Loc.T("hangar.searching");
        SetView(true);
        SfxManager.PlayTick();
        SfxManager.Haptic();
        Telemetry.Log("home_party");
        ConnectionManager.Instance.BeginParty();
    }

    private void OnCancel()
    {
        SfxManager.PlayTick();
        Telemetry.Log("home_party_cancel");
        ConnectionManager.Instance?.CancelParty();
        _status.text = "";
        SetView(false);
    }

    // --- layout ----------------------------------------------------------------

    private void Build()
    {
        _root = new GameObject("Root");
        _root.transform.SetParent(transform, false);
        UiKit.Stretch(_root);
        UiKit.Panel(_root.transform, "Background", Color.black, true);

        var centre = new Vector2(0.5f, 0.5f);
        UiKit.Label(_root.transform, "Title", "DOODLEBUGS", 64, centre, new Vector2(0, 195), TitleColor,
            new Vector2(1400, 90));

        _status = UiKit.Label(_root.transform, "Status", "", 18, centre, new Vector2(0, 110), Color.white,
            new Vector2(1400, 40));

        Button play;
        (play, _playBg, _playLabel) = UiKit.FlatButton(_root.transform, "Play", Loc.T("home.play"), 40,
            centre, centre, new Vector2(0, 0), new Vector2(440, 120), PlayColor, OnPlay);
        _play = play;

        _party = UiKit.FlatButton(_root.transform, "Party", Loc.T("home.party"), 20,
            centre, centre, new Vector2(0, -115), new Vector2(280, 70), PartyColor, OnParty).button.gameObject;
        _cancel = UiKit.FlatButton(_root.transform, "Cancel", Loc.T("home.cancel"), 20,
            centre, centre, new Vector2(0, -115), new Vector2(280, 70), CancelColor, OnCancel).button.gameObject;

        // Same slot as the hangar's SETTINGS column (bottom-left).
        UiKit.FlatButton(_root.transform, "Settings", Loc.T("hangar.settings"), 16,
            Vector2.zero, Vector2.zero, new Vector2(60, 40), new Vector2(180, 68), SettingsColor,
            () => SettingsOverlay.Instance?.Open());

        UiKit.Label(_root.transform, "Version", Doodlebugs.UI.ConnectionUI.GameVersion, 16,
            centre, new Vector2(0, -230), Dim);
    }
}
