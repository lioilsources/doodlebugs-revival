using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The hangar, phone-first (Prompts/30, direction A "Sky Poster"). One
/// skeleton for all five modes: sky of the next arena as the background, a
/// top bar of pills, your plane big on the left with its weapon and gadget,
/// tabs (WEAPON / GADGET / UPGRADES / PLANE) and their content on the right,
/// pilots and the big CTA along the bottom. Replaces the hangar, plane
/// picker and scene picker that lived in GameHUD.
///
/// Mockup with the exact sizes: Prompts/30-hangar-mockup.html.
/// </summary>
public class HangarScreen : MonoBehaviour
{
    public static HangarScreen Instance { get; private set; }

    public static bool IsOpen => Instance != null && Instance._open;

    private enum Tab { Weapon, Gadget, Upgrades, Plane }

    private RectTransform _bg, _layer;
    private Image _sky, _veilTop, _veilBottom, _veilFlat;

    // top bar
    private Text _title;
    private RectTransform _topRow;
    private (RectTransform root, Image bg, Text label) _rolePill, _roundPill, _nextPill, _readyPill, _statusPill;

    // hero
    private RectTransform _hero;
    private Vector2 _heroHome, _planeHome;
    private Image _heroPlane;
    private Text _heroName;
    private (RectTransform root, Image bg, Text label) _elementChip;
    private Image _slotWeaponIcon, _slotGadgetIcon;
    private Text _slotWeaponName, _slotGadgetName;
    private GameObject _slotGadget;

    // tabs + content
    private RectTransform _tabRow, _content;
    private readonly Dictionary<Tab, (Ui.Panel panel, Text label, Text badge)> _tabs = new();
    private Tab _tab = Tab.Weapon;
    private readonly List<(DraftCard card, int id)> _cards = new();

    // bottom
    private RectTransform _roster;
    private Ui.Cta _cta;
    private string _rosterSignature;

    // searching
    private RectTransform _boot;
    private Text _bootStatus;

    private GameObject _corner;
    private ArenaSheet _arena;

    private bool _open;
    private MatchManager.HangarMode _mode = MatchManager.HangarMode.Searching;
    private bool _localReady;
    private readonly HashSet<ulong> _readyClients = new();
    private readonly List<int> _weaponOffers = new();
    private readonly List<int> _gadgetOffers = new();
    private int _pickedWeapon = -1, _pickedGadget = -1;
    private bool _planeAutoShown;
    private float _openedAt;
    private float _nextPoll;
    private int _lastHeroKey = int.MinValue;

    private Doodlebugs.Network.ConnectionManager _connection;

    // --- lifecycle ------------------------------------------------------------

    public static HangarScreen Create()
    {
        var mc = MenuCanvas.Instance ?? MenuCanvas.Create();
        var go = new GameObject("HangarScreen");
        go.transform.SetParent(mc.transform, false);
        var hs = go.AddComponent<HangarScreen>();
        hs.Build(mc);
        return hs;
    }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Unhook();
    }

    private void Start()
    {
        StartCoroutine(Hook());
        Open(MatchManager.HangarMode.Searching, 0);
    }

    private IEnumerator Hook()
    {
        while (MatchManager.Instance == null) yield return new WaitForSeconds(0.1f);
        var mm = MatchManager.Instance;
        mm.OnHangarOpened += Open;
        mm.OnHangarTick += OnTick;
        mm.OnClientReadyChanged += OnClientReady;
        mm.OnRunStateChanged += OnRunState;
        mm.OnSessionReset += OnSessionReset;
        mm.OnMatchEnded += OnMatchEnded;

        while (ScoreManager.Instance == null) yield return new WaitForSeconds(0.1f);
        ScoreManager.Instance.OnMatchStarted += Close;

        while (Doodlebugs.Network.ConnectionManager.Instance == null) yield return new WaitForSeconds(0.1f);
        _connection = Doodlebugs.Network.ConnectionManager.Instance;
        _connection.OnStatusMessage += OnStatus;
        _connection.OnRoleChanged += OnRole;
        OnRole(_connection.Role);
    }

    private void Unhook()
    {
        var mm = MatchManager.Instance;
        if (mm != null)
        {
            mm.OnHangarOpened -= Open;
            mm.OnHangarTick -= OnTick;
            mm.OnClientReadyChanged -= OnClientReady;
            mm.OnRunStateChanged -= OnRunState;
            mm.OnSessionReset -= OnSessionReset;
            mm.OnMatchEnded -= OnMatchEnded;
        }
        if (ScoreManager.Instance != null) ScoreManager.Instance.OnMatchStarted -= Close;
        if (_connection != null)
        {
            _connection.OnStatusMessage -= OnStatus;
            _connection.OnRoleChanged -= OnRole;
        }
    }

    // --- build (once) -----------------------------------------------------------

    private void Build(MenuCanvas mc)
    {
        (_bg, _layer) = mc.CreateLayer("Hangar");

        // Sky of the arena + a gradient veil (text sits on dark at the bottom)
        _sky = _bg.gameObject.AddComponent<Image>();
        _sky.color = UiTheme.Ink;
        _sky.raycastTarget = true;   // the hangar swallows every touch
        var fit = _bg.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = 4096f / 2732f;
        var bgParent = Ui.Node(mc.Full, "HangarVeil");
        Ui.Inset(bgParent, 0, 0, 0, 0);
        _veilFlat = bgParent.gameObject.AddComponent<Image>();
        _veilFlat.color = UiTheme.WithAlpha(Color.black, 0.82f);
        _veilFlat.raycastTarget = true;   // late join: the live battle shows, touches do not reach it
        _veilBottom = Ui.Sprite(bgParent, "Bottom", UiSprites.VerticalGradient);
        _veilBottom.preserveAspect = false;
        _veilBottom.color = UiTheme.SkyBottom;
        _veilBottom.rectTransform.localScale = new Vector3(1f, -1f, 1f);
        Ui.Inset(_veilBottom.rectTransform, 0, 0, 0, 0);
        _veilTop = Ui.Sprite(bgParent, "Top", UiSprites.VerticalGradient);
        _veilTop.preserveAspect = false;
        _veilTop.color = UiTheme.SkyTop;
        Ui.Inset(_veilTop.rectTransform, 0, 0, 0, 0);
        bgParent.SetSiblingIndex(_bg.GetSiblingIndex() + 1);
        _veilRoot = bgParent;

        BuildTopBar();
        BuildHero();
        BuildTabs();
        BuildBottom();
        BuildBoot();

        SetVisible(false);
    }

    private RectTransform _veilRoot;

    private void BuildTopBar()
    {
        _topRow = Ui.Node(_layer, "TopBar");
        Ui.TopBand(_topRow, 6f, 48f, 16f, 16f);
        var h = _topRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = false;
        h.childForceExpandWidth = false;

        _title = Ui.AutoText(_topRow, "Title", "HANGAR", UiTheme.Display, UiTheme.SizeTitle, Color.white);
        var sh = _title.gameObject.AddComponent<Shadow>();
        sh.effectColor = UiTheme.Ink;
        sh.effectDistance = new Vector2(0f, -3f);
        _title.rectTransform.sizeDelta = new Vector2(120f, 40f);

        _rolePill = Ui.Pill(_topRow, "Role", "HOST", UiTheme.Glass, UiTheme.Highlight);
        _roundPill = Ui.Pill(_topRow, "Round", "", UiTheme.Glass, Color.white);
        _nextPill = Ui.Pill(_topRow, "Next", "", UiTheme.Glass, Color.white, 30f);
        _readyPill = Ui.Pill(_topRow, "Ready", "", UiTheme.Glass, UiTheme.Confirm);
        _statusPill = Ui.Pill(_topRow, "Status", "", UiTheme.Glass, Color.white);
        _nextPill.bg.raycastTarget = true;
        Ui.Press(_nextPill.bg, OpenArenas);

        var spacer = Ui.Node(_topRow, "Spacer");
        spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var gear = Ui.Card(_topRow, "Settings", UiTheme.Paper, 24f, 4f);
        gear.Root.sizeDelta = new Vector2(48f, 48f);
        var le = gear.Root.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 48f;
        le.preferredHeight = 48f;
        var icon = Ui.Sprite(gear.Face.transform, "Icon", UiSprites.Gear);
        icon.color = UiTheme.Ink;
        Ui.Inset(icon.rectTransform, 8, 8, 8, 8);
        Ui.Press(gear.Face, () => SettingsOverlay.Instance?.Open());
    }

    private void BuildHero()
    {
        _hero = Ui.Node(_layer, "Hero");
        _hero.anchorMin = new Vector2(0f, 0f);
        _hero.anchorMax = new Vector2(0f, 1f);
        _hero.pivot = new Vector2(0f, 0.5f);
        _hero.offsetMin = new Vector2(16f, 76f);
        _hero.offsetMax = new Vector2(16f + 254f, -60f);

        _heroPlane = Ui.Sprite(_hero, "Plane", null);
        var pr = _heroPlane.rectTransform;
        pr.anchorMin = new Vector2(0f, 0f);
        pr.anchorMax = new Vector2(1f, 1f);
        pr.offsetMin = new Vector2(16f, 86f);
        pr.offsetMax = new Vector2(-16f, 0f);
        pr.localRotation = Quaternion.Euler(0f, 0f, -7f);
        var sh = _heroPlane.gameObject.AddComponent<Shadow>();
        sh.effectColor = UiTheme.WithAlpha(UiTheme.Ink, 0.5f);
        sh.effectDistance = new Vector2(0f, -8f);

        var nameRow = Ui.Node(_hero, "NameRow");
        Ui.BottomBand(nameRow, 58f, 30f);
        var h = nameRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = false;
        h.childForceExpandWidth = false;
        _heroName = Ui.AutoText(nameRow, "Name", "", UiTheme.Display, 26, Color.white);
        _heroName.rectTransform.sizeDelta = new Vector2(100f, 30f);
        var nsh = _heroName.gameObject.AddComponent<Shadow>();
        nsh.effectColor = UiTheme.Ink;
        nsh.effectDistance = new Vector2(0f, -3f);
        _elementChip = Ui.Pill(nameRow, "Element", "", UiTheme.Weapon, UiTheme.Ink, 20f, 12, UiTheme.Label);

        var slots = Ui.Node(_hero, "Slots");
        Ui.BottomBand(slots, 0f, 50f);
        var sh2 = slots.gameObject.AddComponent<HorizontalLayoutGroup>();
        sh2.spacing = 10f;
        sh2.childControlWidth = true;
        sh2.childControlHeight = true;
        sh2.childForceExpandWidth = true;
        sh2.childForceExpandHeight = true;
        (_slotWeaponIcon, _slotWeaponName) = BuildSlot(slots, "Weapon", () => SwitchTab(Tab.Weapon));
        (_slotGadgetIcon, _slotGadgetName) = BuildSlot(slots, "Gadget", () => SwitchTab(Tab.Gadget));
        _slotGadget = _slotGadgetIcon.transform.parent.parent.gameObject;
        _heroHome = _hero.anchoredPosition;
        _planeHome = pr.anchoredPosition;
    }

    private (Image icon, Text name) BuildSlot(RectTransform parent, string label, System.Action onClick)
    {
        var card = Ui.Card(parent, "Slot" + label, UiTheme.Paper, UiTheme.RadiusButton, 4f);
        var face = card.Face.transform;
        var icon = Ui.Sprite(face, "Icon", null);
        Ui.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(36f, 36f));
        var small = Ui.Text(face, "Label", label, UiTheme.BodyStrong, UiTheme.SizeSmall, UiTheme.TextDim);
        Ui.Inset(small.rectTransform, 46f, 22f, 4f, 4f);
        var name = Ui.Text(face, "Name", "", UiTheme.Label, 17, UiTheme.Ink);
        name.horizontalOverflow = HorizontalWrapMode.Overflow;
        Ui.Inset(name.rectTransform, 46f, 4f, 4f, 20f);
        Ui.Press(card.Face, onClick);
        return (icon, name);
    }

    private void BuildTabs()
    {
        _tabRow = Ui.Node(_layer, "Tabs");
        _tabRow.anchorMin = new Vector2(0f, 1f);
        _tabRow.anchorMax = new Vector2(1f, 1f);
        _tabRow.pivot = new Vector2(0f, 1f);
        _tabRow.offsetMin = new Vector2(286f, -58f - 48f);
        _tabRow.offsetMax = new Vector2(-16f, -58f);
        var h = _tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
        {
            var p = Ui.Card(_tabRow, "Tab_" + t, UiTheme.Paper, UiTheme.RadiusButton, 4f);
            var le = p.Root.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 104f;
            le.preferredWidth = 120f;
            var label = Ui.Text(p.Face.transform, "Label", t.ToString().ToUpperInvariant(), UiTheme.Label,
                UiTheme.SizeTab, UiTheme.Ink, TextAnchor.MiddleCenter);
            var badge = Ui.Text(p.Face.transform, "Badge", "", UiTheme.BodyStrong, 12, UiTheme.Ink, TextAnchor.MiddleCenter);
            Ui.Place(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(20f, 20f));
            var bdisc = Ui.Node(p.Face.transform, "BadgeDisc");
            Ui.Place(bdisc, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(20f, 20f));
            var di = bdisc.gameObject.AddComponent<Image>();
            di.sprite = UiSprites.Disc;
            di.color = UiTheme.Highlight;
            di.raycastTarget = false;
            badge.transform.SetAsLastSibling();
            var captured = t;
            Ui.Press(p.Face, () => SwitchTab(captured));
            _tabs[t] = (p, label, badge);
        }

        _content = Ui.Node(_layer, "Content");
        Ui.Inset(_content, 286f, 80f, 16f, 122f);
    }

    private void BuildBottom()
    {
        var bar = Ui.Node(_layer, "Bottom");
        Ui.BottomBand(bar, 12f, 54f, 16f, 16f);

        _roster = Ui.Node(bar, "Roster");
        Ui.Inset(_roster, 0f, 0f, 220f, 0f);
        var h = _roster.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = false;
        h.childForceExpandWidth = false;

        _cta = Ui.BigButton(bar, "Cta", "READY", UiTheme.Confirm, PressCta);
        Ui.Place(_cta.Panel.Root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(204f, 54f));
    }

    private void BuildBoot()
    {
        _boot = Ui.Node(_layer, "Boot");
        Ui.Inset(_boot, 0, 0, 0, 0);
        var title = Ui.Text(_boot, "Title", "DOODLEBUGS", UiTheme.Display, 72, Color.white, TextAnchor.MiddleCenter);
        Ui.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(700f, 90f));
        var sh = title.gameObject.AddComponent<Shadow>();
        sh.effectColor = UiTheme.Ink;
        sh.effectDistance = new Vector2(0f, -6f);
        _bootStatus = Ui.Text(_boot, "Status", "SEARCHING FOR GAME...", UiTheme.Label, 20, UiTheme.Highlight, TextAnchor.MiddleCenter);
        Ui.Place(_bootStatus.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(700f, 30f));
        var ver = Ui.Text(_boot, "Version", Doodlebugs.UI.ConnectionUI.GameVersion, UiTheme.Body, 13,
            UiTheme.WithAlpha(Color.white, 0.6f), TextAnchor.MiddleCenter);
        Ui.Place(ver.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(400f, 20f));
    }

    // --- open / close -------------------------------------------------------------

    private void SetVisible(bool on)
    {
        _bg.gameObject.SetActive(on);
        _veilRoot.gameObject.SetActive(on);
        _layer.gameObject.SetActive(on);
        _open = on;
    }

    public void Open(MatchManager.HangarMode mode, int seconds)
    {
        bool fresh = !_open || mode != _mode;
        if (mode != _mode && _arena != null) { _arena.Close(); _arena = null; }
        _mode = mode;
        RemoveCorner();
        SetVisible(true);
        _openedAt = Time.unscaledTime;

        bool searching = mode == MatchManager.HangarMode.Searching;
        bool lateJoin = mode == MatchManager.HangarMode.LateJoin;
        bool readyCheck = mode == MatchManager.HangarMode.PreBattle || mode == MatchManager.HangarMode.Intermission;

        if (fresh)
        {
            _localReady = false;
            _readyClients.Clear();
        }

        // Background: the next arena's sky (live battle under a veil for a late joiner)
        var profile = BackgroundManager.Instance != null ? BackgroundManager.Instance.CurrentProfile : null;
        if (profile == null && BackgroundManager.Instance != null && BackgroundManager.Instance.BackgroundCount > 0)
            profile = BackgroundManager.Instance.GetProfile(0);
        _sky.sprite = profile != null ? profile.backgroundSprite : null;
        _sky.color = _sky.sprite != null ? Color.white : UiTheme.Ink;
        _bg.gameObject.SetActive(!lateJoin);
        _veilFlat.enabled = lateJoin;
        _veilTop.enabled = !lateJoin;
        _veilBottom.enabled = !lateJoin;

        _boot.gameObject.SetActive(searching);
        _hero.gameObject.SetActive(!searching);
        _tabRow.gameObject.SetActive(!searching);
        _content.gameObject.SetActive(!searching);
        _roster.parent.gameObject.SetActive(!searching);
        _topRow.gameObject.SetActive(!searching);

        _title.text = lateJoin ? "BATTLE IN PROGRESS" : "HANGAR";
        _readyPill.root.gameObject.SetActive(readyCheck);
        _statusPill.root.gameObject.SetActive(mode == MatchManager.HangarMode.Waiting);
        _statusPill.label.text = "WAITING FOR PLAYERS...";
        RefreshPills();

        // Tabs per mode
        bool intermission = mode == MatchManager.HangarMode.Intermission;
        _tabs[Tab.Gadget].panel.Root.gameObject.SetActive(!lateJoin);
        _tabs[Tab.Upgrades].panel.Root.gameObject.SetActive(intermission);

        // CTA per mode
        switch (mode)
        {
            case MatchManager.HangarMode.Waiting: SetCta("FLY", UiTheme.Gadget, 0, true); break;
            case MatchManager.HangarMode.LateJoin: SetCta("JOIN", UiTheme.Confirm, seconds, true); break;
            case MatchManager.HangarMode.PreBattle:
            case MatchManager.HangarMode.Intermission:
                SetCta("READY", UiTheme.Confirm, seconds > 0 ? seconds : MatchManager.HangarSeconds, true); break;
        }

        if (!searching)
        {
            if (fresh) RollOffers();
            var first = Tab.Weapon;
            if (mode == MatchManager.HangarMode.Waiting && !_planeAutoShown && NeedsLook())
            {
                _planeAutoShown = true;
                first = Tab.Plane;
            }
            if (!fresh && (_tab != Tab.Upgrades || intermission)) first = _tab;
            SwitchTab(first, false);
            RefreshHero(true);
            RebuildRoster(true);

            // Entrance
            if (fresh) UiTween.Move(_hero, _heroHome - new Vector2(30f, 0f), _heroHome, 0.3f, UiTween.Ease.OutBack);
        }

        Telemetry.Log("hangar_open", ("mode", mode));
        UiAudit.Check(_layer);
    }

    public void Close()
    {
        if (_arena != null) { _arena.Close(); _arena = null; }
        RemoveCorner();
        SetVisible(false);
        ClearContent();
    }

    private bool NeedsLook()
    {
        var owned = OwnedPlane();
        var mgr = PlaneSkinManager.Instance;
        return owned != null && mgr != null && !mgr.HasClaim(owned.OwnerClientId, Mathf.Max(owned.LocalPlayerIndex, 0));
    }

    // --- events -------------------------------------------------------------------

    private void OnTick(int secondsLeft)
    {
        if (!_open || MatchManager.Instance == null || MatchManager.Instance.IsRunOver) return;
        if (_mode == MatchManager.HangarMode.Waiting || _mode == MatchManager.HangarMode.Searching) return;
        Ui.SetCount(_cta, secondsLeft);
        if (secondsLeft <= 5 && secondsLeft > 0)
        {
            UiTween.Punch(_cta.CountChip, 0.2f, 0.2f);
            SfxManager.PlayTick();
        }
    }

    private void OnClientReady(ulong clientId)
    {
        _readyClients.Add(clientId);
        RefreshPills();
        RebuildRoster(true);
    }

    private void OnRunState()
    {
        RefreshPills();
        if (_open && _tab == Tab.Upgrades) SwitchTab(Tab.Upgrades, false);
    }

    private void OnSessionReset() => Open(MatchManager.HangarMode.Searching, 0);

    private void OnMatchEnded(MatchManager.MatchResult _) => Close();

    private void OnStatus(string message)
    {
        if (_mode == MatchManager.HangarMode.Searching && _bootStatus != null)
            _bootStatus.text = message.ToUpperInvariant();
    }

    private void OnRole(Doodlebugs.Network.LocalRole role)
    {
        if (_rolePill.root == null) return;
        _rolePill.root.gameObject.SetActive(role != Doodlebugs.Network.LocalRole.None);
        _rolePill.label.text = role == Doodlebugs.Network.LocalRole.Host ? "HOST" : "CLIENT";
        _rolePill.label.color = role == Doodlebugs.Network.LocalRole.Host ? UiTheme.Highlight : UiTheme.Gadget;
    }

    /// <summary>A late joiner's own plane deployed mid-battle: close the LateJoin hangar.</summary>
    public void OnLocalPlayerDeployed()
    {
        if (_mode == MatchManager.HangarMode.LateJoin && _open) Close();
    }

    // --- pills ----------------------------------------------------------------------

    private void RefreshPills()
    {
        var mm = MatchManager.Instance;
        int round = 1;
        if (mm != null) foreach (var kv in mm.RoundWins) round += kv.Value;
        _roundPill.label.text = $"ROUND {round} OF {MatchManager.RoundWinsTarget * 2 - 1}";
        _roundPill.root.gameObject.SetActive(_mode == MatchManager.HangarMode.Intermission);

        var profile = BackgroundManager.Instance != null ? BackgroundManager.Instance.CurrentProfile : null;
        string arena = profile != null && profile.backgroundSprite != null ? profile.backgroundSprite.name : "ARENA";
        bool host = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        _nextPill.label.text = (host ? "ARENAS · " : "NEXT · ") + arena.ToUpperInvariant();
        _nextPill.bg.raycastTarget = host;

        int devices = RosterDevices().Count;
        _readyPill.label.text = $"READY {_readyClients.Count}/{Mathf.Max(1, devices)}";

        if (mm != null && _tabs.TryGetValue(Tab.Upgrades, out var up))
        {
            up.badge.text = mm.LocalRunPoints > 0 ? mm.LocalRunPoints.ToString() : "";
            up.badge.transform.parent.Find("BadgeDisc").gameObject.SetActive(mm.LocalRunPoints > 0);
        }
        foreach (var t in _tabs)
        {
            if (t.Key == Tab.Upgrades) continue;
            t.Value.badge.text = "";
            t.Value.badge.transform.parent.Find("BadgeDisc").gameObject.SetActive(false);
        }
    }

    // --- tabs ------------------------------------------------------------------------

    private void SwitchTab(Tab tab) => SwitchTab(tab, true);

    private void SwitchTab(Tab tab, bool user)
    {
        if (!_open) return;
        if (!_tabs[tab].panel.Root.gameObject.activeSelf) tab = Tab.Weapon;
        _tab = tab;
        foreach (var kv in _tabs)
        {
            bool on = kv.Key == tab;
            kv.Value.panel.SetFace(on ? UiTheme.Paper : UiTheme.GlassLight);
            kv.Value.panel.SetShadow(on ? 4f : 0f);
            kv.Value.panel.Outline.enabled = on;
            kv.Value.label.color = on ? UiTheme.Ink : Color.white;
        }
        ClearContent();
        switch (tab)
        {
            case Tab.Weapon: BuildWeaponCards(); break;
            case Tab.Gadget: BuildGadgetCards(); break;
            case Tab.Upgrades: BuildUpgradeCards(); break;
            case Tab.Plane: PlaneTab.Build(_content); break;
        }
        if (user)
        {
            SfxManager.Haptic(HapticKind.Light);
            Telemetry.Log("hangar_tab", ("tab", tab));
        }
    }

    private void ClearContent()
    {
        _cards.Clear();
        if (_content == null) return;
        foreach (Transform c in _content) Destroy(c.gameObject);
    }

    private RectTransform CardRow()
    {
        var row = Ui.Node(_content, "Cards");
        Ui.Inset(row, 0, 0, 0, 0);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 12f;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;
        return row;
    }

    private void RollOffers()
    {
        var owned = OwnedPlane();
        var shooting = owned != null ? owned.GetComponent<Shooting>() : null;

        int current = shooting != null ? shooting.DraftedWeaponId : (int)WeaponType.MG;
        _pickedWeapon = current;
        _weaponOffers.Clear();
        _weaponOffers.Add(current);
        _weaponOffers.AddRange(DraftOffers.Roll(current, WeaponProfile.DraftPool, 2));

        int gadget = shooting != null && shooting.HasGadget ? shooting.NetGadgetId.Value : (int)GadgetProfile.Default;
        _pickedGadget = gadget;
        _gadgetOffers.Clear();
        _gadgetOffers.Add(gadget);
        foreach (var g in GadgetProfile.DraftPool) if ((int)g != gadget) _gadgetOffers.Add((int)g);
    }

    private void BuildWeaponCards()
    {
        var row = CardRow();
        for (int i = 0; i < _weaponOffers.Count; i++)
        {
            int id = _weaponOffers[i];
            var w = WeaponProfile.Get(id);
            var icon = w.LoadIcon(out var tint);
            var card = DraftCard.Build(row, new DraftCard.Data
            {
                Category = UiTheme.Weapon,
                Verb = w.Verb,
                Corner = i == 0 ? "KEEP" : "",
                Icon = icon,
                IconTint = tint,
                Title = w.DisplayName,
                Description = w.Description,
                Preview = w.Preview,
                Stats = new[] { ("DMG", w.StatDamage), ("RATE", w.StatRate), ("RANGE", w.StatRange) },
                OnClick = () => PickWeapon(id),
            }, i);
            card.SetSelected(id == _pickedWeapon);
            card.SetInteractable(!_localReady);
            _cards.Add((card, id));
        }
    }

    private void BuildGadgetCards()
    {
        var row = CardRow();
        for (int i = 0; i < _gadgetOffers.Count; i++)
        {
            int id = _gadgetOffers[i];
            var g = GadgetProfile.Get(id);
            var card = DraftCard.Build(row, new DraftCard.Data
            {
                Category = UiTheme.Gadget,
                Verb = g.Verb,
                Corner = i == 0 ? "KEEP" : "",
                Icon = g.LoadIcon(),
                Title = g.DisplayName,
                Description = g.Description,
                Stats = new[] { ("POWER", g.StatPower), ("RATE", g.StatRate), ("CD", Mathf.Clamp01(1f - g.Cooldown / 12f)) },
                OnClick = () => PickGadget(id),
            }, i);
            card.SetSelected(id == _pickedGadget);
            card.SetInteractable(!_localReady);
            _cards.Add((card, id));
        }
    }

    private static readonly string[] UpgradeIcons = { "powerup_shield", "powerup_health", "powerup_damage", "powerup_repair" };

    private void BuildUpgradeCards()
    {
        var mm = MatchManager.Instance;
        var row = CardRow();
        for (int i = 0; i < RunUpgrades.TypeCount; i++)
        {
            var type = (RunUpgradeType)i;
            int level = mm != null ? mm.GetLocalUpgradeLevel(type) : 0;
            bool maxed = level >= RunUpgrades.MaxLevel;
            bool affordable = mm != null && mm.LocalRunPoints >= RunUpgrades.CostPoints;
            string dots = $"{level} / {RunUpgrades.MaxLevel}";
            var captured = type;
            var card = DraftCard.Build(row, new DraftCard.Data
            {
                Category = UiTheme.Highlight,
                Verb = "UPGRADE",
                Corner = maxed ? "MAX" : $"{RunUpgrades.CostPoints} PT",
                Icon = Resources.Load<Sprite>("Sprites/PowerUps/" + UpgradeIcons[i]),
                Title = RunUpgrades.DisplayName(type),
                Description = RunUpgrades.Description(type),
                Footer = $"LEVEL {dots}",
                Disabled = maxed || !affordable,
                OnClick = () => BuyUpgrade(captured),
            }, i);
            _cards.Add((card, i));
        }
    }

    private void PickWeapon(int id)
    {
        if (_localReady) return;
        _pickedWeapon = id;
        foreach (var p in OwnedPlanes())
            p.GetComponent<Shooting>()?.RequestSelectWeaponServerRpc(id);
        foreach (var (card, cid) in _cards) card.SetSelected(cid == id);
        SfxManager.PlayConfirm();
        SfxManager.Haptic(HapticKind.Light);
        _lastHeroKey = int.MinValue;
    }

    private void PickGadget(int id)
    {
        if (_localReady) return;
        _pickedGadget = id;
        foreach (var p in OwnedPlanes())
            p.GetComponent<Shooting>()?.RequestSelectGadgetServerRpc(id);
        foreach (var (card, cid) in _cards) card.SetSelected(cid == id);
        SfxManager.PlayConfirm();
        SfxManager.Haptic(HapticKind.Light);
        _lastHeroKey = int.MinValue;
    }

    private void BuyUpgrade(RunUpgradeType type)
    {
        var mm = MatchManager.Instance;
        if (mm == null || mm.LocalRunPoints < RunUpgrades.CostPoints) return;
        if (mm.GetLocalUpgradeLevel(type) >= RunUpgrades.MaxLevel) return;
        OwnedPlane()?.RequestBuyUpgradeServerRpc((int)type);
        SfxManager.PlayConfirm();
        SfxManager.Haptic(HapticKind.Medium);
        // The tab rebuilds when the server confirms (OnRunStateChanged)
    }

    // --- arenas -----------------------------------------------------------------------

    private void OpenArenas()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;
        if (_arena != null) return;
        _arena = ArenaSheet.Open(_layer);
    }

    // --- CTA ------------------------------------------------------------------------------

    private void SetCta(string label, Color face, int seconds, bool interactable)
    {
        _cta.Label.text = label;
        _cta.Panel.SetFace(face);
        _cta.Count.color = face;
        _cta.Button.interactable = interactable;
        Ui.SetCount(_cta, seconds);
    }

    private void PressCta()
    {
        switch (_mode)
        {
            case MatchManager.HangarMode.Waiting:
                Fly();
                break;
            case MatchManager.HangarMode.LateJoin:
                if (_localReady) return;
                _localReady = true;
                SetCta("DEPLOYING", UiTheme.PaperDim, 0, false);
                OwnedPlane()?.RequestDeployServerRpc();
                break;
            default:
                if (_localReady) return;
                _localReady = true;
                SetCta("WAITING", UiTheme.PaperDim, 0, false);
                foreach (var (card, _) in _cards) card.SetInteractable(false);
                OwnedPlane()?.SetHangarReadyServerRpc();
                SfxManager.PlayConfirm();
                SfxManager.Haptic(HapticKind.Medium);
                Telemetry.Log("ready_ms", ("ms", Mathf.RoundToInt((Time.unscaledTime - _openedAt) * 1000f)));
                break;
        }
    }

    private void Fly()
    {
        // The arena changes here, on the way out of the opaque hangar, and
        // nowhere else during the warm-up (Prompts/27). Only the host waits.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            BackgroundManager.Instance?.ServerOnWarmUpFlyOut();
        if (_arena != null) { _arena.Close(); _arena = null; }
        SetVisible(false);
        ClearContent();
        ShowCorner();
        SfxManager.PlayWhoosh(0.7f);
    }

    private void ShowCorner()
    {
        if (_corner != null) return;
        var mc = MenuCanvas.Instance;
        var (r, bg, _) = Ui.Pill(mc.Root, "HangarCorner", "HANGAR", UiTheme.Glass, Color.white, 48f, 17, UiTheme.Label);
        r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(1f, 1f);
        r.anchoredPosition = new Vector2(-16f, -8f);
        var le = r.GetComponent<LayoutElement>();
        le.minWidth = 110f;
        bg.raycastTarget = true;
        Ui.Press(bg, () =>
        {
            RemoveCorner();
            Open(MatchManager.HangarMode.Waiting, 0);
        });
        _corner = r.gameObject;
    }

    private void RemoveCorner()
    {
        if (_corner != null) Destroy(_corner);
        _corner = null;
    }

    // --- hero + roster (polled while open) --------------------------------------------------

    private void Update()
    {
        if (!_open || _mode == MatchManager.HangarMode.Searching) return;
        // Gentle bob of the hero plane
        if (_heroPlane != null)
        {
            float s = Mathf.Sin(Time.unscaledTime * 1.6f);
            _heroPlane.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -7f + 1.5f * s);
            _heroPlane.rectTransform.anchoredPosition = _planeHome + new Vector2(0f, 4f * s);
        }
        if (Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + 0.25f;
        RefreshHero(false);
        RebuildRoster(false);
    }

    public void PunchHero()
    {
        _lastHeroKey = int.MinValue;
        if (_heroPlane != null) UiTween.Punch(_heroPlane.transform, 0.12f, 0.25f);
    }

    private void RefreshHero(bool force)
    {
        var owned = OwnedPlane();
        var a = owned != null ? owned.GetComponent<PlaneAppearance>() : null;
        var sh = owned != null ? owned.GetComponent<Shooting>() : null;
        int model = a != null ? a.NetModelId.Value : PlaneModelCatalog.BaseModelId;
        int skin = a != null ? a.NetSkinId.Value : PlaneSkinCatalog.StarterSkinId;
        int weapon = _pickedWeapon >= 0 ? _pickedWeapon : (sh != null ? sh.DraftedWeaponId : 0);
        int gadget = _pickedGadget >= 0 ? _pickedGadget : (sh != null ? sh.NetGadgetId.Value : 0);
        int key = ((model * 64 + skin) * 32 + weapon) * 8 + gadget;
        if (!force && key == _lastHeroKey) return;
        _lastHeroKey = key;

        _heroPlane.sprite = PlaneModelCatalog.LoadSprite(model, skin);
        var def = PlaneModelCatalog.Get(model);
        _heroName.text = def.DisplayName.ToUpperInvariant();
        var el = ElementProfile.Get(def.Element);
        _elementChip.label.text = el.DisplayName;
        _elementChip.bg.color = el.Tint;

        var w = WeaponProfile.Get(weapon);
        _slotWeaponIcon.sprite = w.LoadIcon(out _);
        _slotWeaponName.text = w.DisplayName;
        bool showGadget = _mode != MatchManager.HangarMode.LateJoin;
        _slotGadget.SetActive(showGadget);
        var g = GadgetProfile.Get(gadget);
        _slotGadgetIcon.sprite = g.LoadIcon();
        _slotGadgetName.text = g.DisplayName;
    }

    private struct Pilot { public ulong Client; public string Name; public int Model, Skin; }

    private static List<Pilot> RosterDevices()
    {
        var list = new List<Pilot>();
        var seen = new HashSet<ulong>();
        var planes = new List<PlayerController>(FindObjectsByType<PlayerController>(FindObjectsSortMode.None));
        planes.Sort((x, y) =>
        {
            int c = x.OwnerClientId.CompareTo(y.OwnerClientId);
            return c != 0 ? c : x.LocalPlayerIndex.CompareTo(y.LocalPlayerIndex);
        });
        foreach (var p in planes)
        {
            if (p.IsBot || !seen.Add(p.OwnerClientId)) continue;
            var a = p.GetComponent<PlaneAppearance>();
            string n = p.PlayerName;
            int hash = n.IndexOf('#');
            if (hash > 0) n = n.Substring(0, hash);
            list.Add(new Pilot
            {
                Client = p.OwnerClientId,
                Name = string.IsNullOrEmpty(n) ? $"P{p.OwnerClientId + 1}" : n,
                Model = a != null ? a.NetModelId.Value : 0,
                Skin = a != null ? a.NetSkinId.Value : 0,
            });
        }
        return list;
    }

    private void RebuildRoster(bool force)
    {
        var pilots = RosterDevices();
        var nm = NetworkManager.Singleton;
        ulong me = nm != null ? nm.LocalClientId : 0;
        bool readyCheck = _mode == MatchManager.HangarMode.PreBattle || _mode == MatchManager.HangarMode.Intermission;

        var sig = new System.Text.StringBuilder();
        foreach (var p in pilots) sig.Append(p.Client).Append(':').Append(p.Model).Append(':').Append(p.Skin)
            .Append(':').Append(_readyClients.Contains(p.Client)).Append(p.Name).Append('|');
        sig.Append(_mode);
        string s = sig.ToString();
        if (!force && s == _rosterSignature) return;
        _rosterSignature = s;
        RefreshPills();

        foreach (Transform c in _roster) Destroy(c.gameObject);
        int shown = 0;
        foreach (var p in pilots)
        {
            if (shown++ >= 4) break;
            var pill = Ui.Node(_roster, "Pilot_" + p.Client);
            pill.sizeDelta = new Vector2(120f, 40f);
            var bg = pill.gameObject.AddComponent<Image>();
            bg.sprite = UiSprites.RoundedRect;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = UiSprites.RoundMultiplier(20f);
            bg.color = UiTheme.Glass;
            bg.raycastTarget = false;
            var h = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(4, 12, 4, 4);
            h.spacing = 7f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = false;
            h.childForceExpandWidth = false;
            var le = pill.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;

            Color pc = PlayerColorManager.Instance != null
                ? PlayerColorManager.Instance.GetColor(p.Client, 0)
                : PlayerColorManager.GetColorByIndex((int)p.Client);
            var (thumb, _, icon) = Ui.Badge(pill, "Thumb", PlaneModelCatalog.LoadSprite(p.Model, p.Skin), UiTheme.Paper, 32f);
            thumb.GetChild(0).GetComponent<Image>().color = pc;
            var tle = thumb.gameObject.AddComponent<LayoutElement>();
            tle.preferredWidth = 32f;
            tle.preferredHeight = 32f;

            var col = Ui.Node(pill, "Text");
            col.sizeDelta = new Vector2(60f, 32f);
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childAlignment = TextAnchor.MiddleLeft;
            Ui.AutoText(col, "Name", p.Client == me ? "You" : p.Name, UiTheme.BodyStrong, 13, Color.white);
            bool ready = _readyClients.Contains(p.Client);
            string state = readyCheck ? (ready ? "READY" : "PICKING") : _mode == MatchManager.HangarMode.Waiting ? "IN LOBBY" : "";
            if (state.Length > 0)
                Ui.AutoText(col, "State", state, UiTheme.Label, 12, ready ? UiTheme.Confirm : UiTheme.Highlight);
        }
    }

    // --- helpers ------------------------------------------------------------------------------

    /// <summary>First plane this device owns (couch co-op: any of them), never the bot.</summary>
    public static PlayerController OwnedPlane()
    {
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            if (p.IsOwner && !p.IsBot) return p;
        return null;
    }

    private static IEnumerable<PlayerController> OwnedPlanes()
    {
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            if (p.IsOwner && !p.IsBot) yield return p;
    }
}

/// <summary>Draft offers: keep the current pick + n random others (Prompts/30 §4).</summary>
public static class DraftOffers
{
    public static List<int> Roll(int current, WeaponType[] pool, int n)
    {
        var list = new List<int>();
        foreach (var w in pool) if ((int)w != current) list.Add((int)w);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        if (list.Count > n) list.RemoveRange(n, list.Count - n);
        return list;
    }
}
