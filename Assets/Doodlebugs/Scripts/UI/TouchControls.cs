using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// On-screen stick (bottom-right) and fire triggers (bottom-left) for phones
/// (Prompts/26). Presentation and pointer bookkeeping only: the stick's axes
/// and each trigger's held flag go into <see cref="TouchInputProvider"/>, the
/// charge ring reads <see cref="Shooting.GetCharge01"/>. Built at runtime
/// under a safe-area container - nothing in the scene, nothing on desktop
/// (the object exists but never shows).
///
/// Multi-touch comes through uGUI pointer ids (the scene's EventSystem runs
/// InputSystemUIInputModule): the stick keeps the id that pressed it, each
/// trigger keeps its own, and a finger sliding from one onto another does
/// nothing - triggers act on pointer-down only. A pointer-up the module
/// never delivers (cancelled touch) must not wedge a control: each one also
/// watches its own touch id on the Touchscreen and lets go when it ends.
/// </summary>
public class TouchControls : MonoBehaviour
{
    public static TouchControls Instance { get; private set; }

    // Canvas units (reference 1920x1080, match 0.5 - an iPhone 12 mini is
    // 2119x978 of them).
    private const float StickTravel = 90f;
    private const float StickBaseSize = 260f;
    private const float StickKnobSize = 110f;
    private const float StickDeadZone = 0.12f;
    private const float StickExpo = 1.3f;
    // Right half up to 80 % height: the top strip stays free for the corner
    // HANGAR button (TouchControls sits above GameHUD and would swallow it).
    private const float ZoneWidthFrac = 0.50f;
    private const float ZoneHeightFrac = 0.80f;
    private const float BaseFadeSeconds = 0.15f;
    private const float StickIdleAlpha = 0.35f;   // resting ghost: shows where the stick lives

    private const float PrimarySize = 200f;
    private const float SecondarySize = 170f;
    private const float RingWidthFrac = 0.07f;
    private const float IconFrac = 0.55f;
    private const float PunchScale = 1.12f;
    private const float PunchSeconds = 0.12f;
    private static readonly Vector2 PrimaryPos = new Vector2(150f, 150f);
    private static readonly Vector2 SecondaryPos = new Vector2(150f, 380f);

    private static readonly Color DiscColor = new Color(0.05f, 0.05f, 0.08f, 0.55f);
    private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.22f);
    private static readonly Color ChargingColor = new Color(1f, 0.8f, 0.35f, 0.85f);
    private static readonly Color ChargedColor = Color.white;
    private static readonly Color StickBaseColor = new Color(1f, 1f, 1f, 0.18f);
    private static readonly Color StickKnobColor = new Color(1f, 1f, 1f, 0.55f);

    private const float PlaneLookupInterval = 0.5f;

    private Canvas _canvas;
    private RectTransform _root;
    private Rect _appliedSafeArea = new Rect(-1, -1, -1, -1);
    private Vector2Int _appliedScreen;

    private StickZone _stick;
    private readonly Trigger[] _triggers = new Trigger[TouchInputProvider.Slots];

    private PlayerController _plane;
    private Shooting _shooting;
    private float _nextPlaneLookup;
    private bool _visible;

    private static TouchInputProvider Provider => InputManager.Instance?.TouchProvider;

    public static TouchControls Create(Canvas canvas)
    {
        var go = new GameObject("TouchControls");
        go.transform.SetParent(canvas.transform, false);
        UiKit.Stretch(go);
        return go.AddComponent<TouchControls>();
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        _canvas = GetComponentInParent<Canvas>();

        var rootGo = new GameObject("SafeArea");
        rootGo.transform.SetParent(transform, false);
        _root = UiKit.Stretch(rootGo);

        BuildStick();
        BuildTriggers();

        ControlSettings.Changed += OnSchemeChanged;
        ApplyScheme(ControlSettings.Scheme);
        SetVisible(false);
    }

    private void OnDestroy()
    {
        ControlSettings.Changed -= OnSchemeChanged;
        if (Instance == this) Instance = null;
    }

    private void OnApplicationFocus(bool focus)
    {
        if (!focus) ReleaseEverything();
    }

    private void Update()
    {
        ApplySafeArea();

        bool show = ShouldShow();
        if (show != _visible) SetVisible(show);
        if (!show) return;

        RefreshTriggers();
    }

    // --- visibility ------------------------------------------------------------

    private bool ShouldShow()
    {
        var input = InputManager.Instance;
        if (input == null || !input.IsMobile() || input.IsUsingGamepad) return false;

        var plane = LocalPlane();
        if (plane == null || plane.InHangar) return false;

        var hud = GameHUD.Instance;
        if (hud != null && hud.IsAnyOverlayOpen) return false;

        return true;
    }

    private PlayerController LocalPlane()
    {
        if (_plane != null && _plane.IsOwner) return _plane;
        if (Time.unscaledTime < _nextPlaneLookup) return null;
        _nextPlaneLookup = Time.unscaledTime + PlaneLookupInterval;

        _plane = null;
        _shooting = null;
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p.IsOwner && !p.IsBot)   // the host owns the warm-up bot too
            {
                _plane = p;
                _shooting = p.GetComponent<Shooting>();
                break;
            }
        }
        return _plane;
    }

    private void SetVisible(bool show)
    {
        _visible = show;
        if (_root != null) _root.gameObject.SetActive(show);
        if (!show) ReleaseEverything();
    }

    private void ReleaseEverything()
    {
        _stick?.Release();
        foreach (var t in _triggers) t?.Release();
        Provider?.ReleaseAll();
    }

    private void OnSchemeChanged(ControlScheme scheme) => ApplyScheme(scheme);

    private void ApplyScheme(ControlScheme scheme)
    {
        if (_stick != null) _stick.gameObject.SetActive(scheme == ControlScheme.Joystick);
        _stick?.Release();
        Provider?.ReleaseAll();
    }

    // --- safe area ---------------------------------------------------------------

    private void ApplySafeArea()
    {
        var safe = Screen.safeArea;
        var screen = new Vector2Int(Screen.width, Screen.height);
        if (safe == _appliedSafeArea && screen == _appliedScreen) return;
        _appliedSafeArea = safe;
        _appliedScreen = screen;

        // Overlay canvas: pixels / scaleFactor = canvas units.
        float s = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
        _root.offsetMin = new Vector2(safe.xMin, safe.yMin) / s;
        _root.offsetMax = new Vector2(safe.xMax - screen.x, safe.yMax - screen.y) / s;
    }

    // --- stick -------------------------------------------------------------------

    private void BuildStick()
    {
        var zoneGo = new GameObject("StickZone");
        zoneGo.transform.SetParent(_root, false);
        var zone = zoneGo.AddComponent<RectTransform>();
        zone.anchorMin = new Vector2(1f - ZoneWidthFrac, 0f);
        zone.anchorMax = new Vector2(1f, ZoneHeightFrac);
        zone.offsetMin = Vector2.zero;
        zone.offsetMax = Vector2.zero;
        zone.pivot = new Vector2(0.5f, 0.5f);

        // Invisible but raycastable: the whole zone is the touch target.
        var catcher = zoneGo.AddComponent<Image>();
        catcher.color = Color.clear;
        catcher.raycastTarget = true;

        var baseGo = new GameObject("Base");
        baseGo.transform.SetParent(zoneGo.transform, false);
        var baseRect = UiKit.Rect(baseGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(StickBaseSize, StickBaseSize));
        var baseImage = baseGo.AddComponent<Image>();
        baseImage.sprite = UiSprites.Ring(0.06f);
        baseImage.color = StickBaseColor;
        baseImage.raycastTarget = false;
        var baseGroup = baseGo.AddComponent<CanvasGroup>();
        baseGroup.blocksRaycasts = false;

        var knobGo = new GameObject("Knob");
        knobGo.transform.SetParent(baseGo.transform, false);
        var knobRect = UiKit.Rect(knobGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(StickKnobSize, StickKnobSize));
        var knobImage = knobGo.AddComponent<Image>();
        knobImage.sprite = UiSprites.Disc;
        knobImage.color = StickKnobColor;
        knobImage.raycastTarget = false;

        _stick = zoneGo.AddComponent<StickZone>();
        _stick.Base = baseRect;
        _stick.Knob = knobRect;
        _stick.BaseGroup = baseGroup;
        _stick.Travel = StickTravel;
        _stick.OnAxes = OnStickAxes;
        _stick.IdleAlpha = StickIdleAlpha;
        _stick.Rest();
    }

    private void OnStickAxes(Vector2 raw)
    {
        var provider = Provider;
        if (provider == null) return;

        // Stick up = turn left (rotation -1), down = turn right, right = more
        // throttle, left = less. Per-axis curve, no circular normalisation: a
        // thumb in the corner means full throttle AND full turn (the knob is
        // clamped to the ring, the axes are not).
        provider.SetStick(new Vector2(
            -MobileInputProvider.Curve(raw.y, StickDeadZone, 1f, StickExpo),
            MobileInputProvider.Curve(raw.x, StickDeadZone, 1f, StickExpo)));
    }

    /// <summary>Floating stick (D7): the base jumps to where the thumb lands,
    /// the knob follows within <see cref="Travel"/>, release fades the base
    /// back to a dim ghost in the middle of the zone.</summary>
    private class StickZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform Base;
        public RectTransform Knob;
        public CanvasGroup BaseGroup;
        public float Travel;
        public System.Action<Vector2> OnAxes;
        public float IdleAlpha;

        private RectTransform _rect;
        private int _pointer = int.MinValue;
        private int _touchId = NoTouch;
        private int _pressFrame;
        private Vector2 _origin;
        private Coroutine _fade;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        private void OnDisable()
        {
            Release();
        }

        private void Update()
        {
            if (_pointer != int.MinValue && !TouchStillDown(_touchId, _pressFrame)) Release();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer != int.MinValue) return;   // one finger owns the stick
            if (!ToLocal(e, out var local)) return;

            _pointer = e.pointerId;
            _touchId = TouchIdOf(e);
            _pressFrame = Time.frameCount;
            _origin = local;
            if (_fade != null) StopCoroutine(_fade);
            _fade = null;
            BaseGroup.alpha = 1f;
            Base.anchoredPosition = local;
            Knob.anchoredPosition = Vector2.zero;
            OnAxes?.Invoke(Vector2.zero);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            if (!ToLocal(e, out var local)) return;

            var delta = local - _origin;
            Knob.anchoredPosition = Vector2.ClampMagnitude(delta, Travel);
            OnAxes?.Invoke(new Vector2(
                Mathf.Clamp(delta.x / Travel, -1f, 1f),
                Mathf.Clamp(delta.y / Travel, -1f, 1f)));
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            Release();
        }

        public void Release()
        {
            bool wasHeld = _pointer != int.MinValue;
            _pointer = int.MinValue;
            _touchId = NoTouch;
            OnAxes?.Invoke(Vector2.zero);
            if (!wasHeld || Base == null) return;

            Knob.anchoredPosition = Vector2.zero;
            if (isActiveAndEnabled) _fade = StartCoroutine(FadeBase());
            else Rest();
        }

        /// <summary>Dim ghost at the zone centre - the stick is always findable.</summary>
        public void Rest()
        {
            Base.anchoredPosition = Vector2.zero;
            Knob.anchoredPosition = Vector2.zero;
            BaseGroup.alpha = IdleAlpha;
        }

        private IEnumerator FadeBase()
        {
            float t = 0f;
            while (t < BaseFadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                BaseGroup.alpha = 1f - t / BaseFadeSeconds;
                yield return null;
            }
            Rest();
            _fade = null;
        }

        private bool ToLocal(PointerEventData e, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, e.position, e.pressEventCamera, out local);
    }

    // --- triggers ------------------------------------------------------------------

    private void BuildTriggers()
    {
        _triggers[0] = BuildTrigger(0, PrimaryPos, PrimarySize);
        _triggers[1] = BuildTrigger(1, SecondaryPos, SecondarySize);
        _triggers[1].gameObject.SetActive(false);   // until the plane carries a second weapon
    }

    private Trigger BuildTrigger(int slot, Vector2 pos, float size)
    {
        var go = new GameObject($"Trigger{slot}");
        go.transform.SetParent(_root, false);
        UiKit.Rect(go, Vector2.zero, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));

        var disc = go.AddComponent<Image>();
        disc.sprite = UiSprites.Disc;
        disc.color = DiscColor;
        disc.raycastTarget = true;   // the whole disc is the button

        var ringSprite = UiSprites.Ring(RingWidthFrac);

        var trackGo = new GameObject("Track");
        trackGo.transform.SetParent(go.transform, false);
        UiKit.Stretch(trackGo);
        var track = trackGo.AddComponent<Image>();
        track.sprite = ringSprite;
        track.color = TrackColor;
        track.raycastTarget = false;

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(go.transform, false);
        UiKit.Stretch(fillGo);
        var fill = fillGo.AddComponent<Image>();
        fill.sprite = ringSprite;
        fill.color = ChargedColor;
        fill.raycastTarget = false;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = true;
        fill.fillAmount = 1f;

        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(go.transform, false);
        float iconSize = size * IconFrac;
        UiKit.Rect(iconGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(iconSize, iconSize));
        var icon = iconGo.AddComponent<Image>();
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        icon.enabled = false;

        var fallback = UiKit.Label(go.transform, "Name", "", 14,
            new Vector2(0.5f, 0.5f), Vector2.zero, Color.white, new Vector2(size, 30));
        fallback.enabled = false;

        var trigger = go.AddComponent<Trigger>();
        trigger.Slot = slot;
        trigger.Fill = fill;
        trigger.Icon = icon;
        trigger.Fallback = fallback;
        trigger.WasCharged = true;
        trigger.WeaponId = -1;
        trigger.OnHeld = OnTriggerHeld;
        return trigger;
    }

    private void OnTriggerHeld(int slot, bool held) => Provider?.SetHeld(slot, held);

    private void RefreshTriggers()
    {
        int slots = _shooting != null ? _shooting.SlotCount : 1;

        for (int slot = 0; slot < _triggers.Length; slot++)
        {
            var t = _triggers[slot];
            bool active = slot < slots;
            if (t.gameObject.activeSelf != active)
            {
                t.gameObject.SetActive(active);
                if (!active) t.Release();
            }
            if (!active) continue;

            float charge = _shooting != null ? _shooting.GetCharge01(slot) : 1f;
            bool charged = charge >= 1f;
            t.Fill.fillAmount = charge;
            t.Fill.color = charged ? ChargedColor : ChargingColor;
            if (charged && !t.WasCharged) StartCoroutine(Punch(t.transform));
            t.WasCharged = charged;

            int weaponId = _shooting != null ? _shooting.GetWeaponId(slot) : (int)WeaponType.MG;
            if (weaponId != t.WeaponId)
            {
                t.WeaponId = weaponId;
                SetIcon(t, WeaponProfile.Get(weaponId));
            }
        }
    }

    private static void SetIcon(Trigger t, WeaponProfile weapon)
    {
        var sprite = weapon.LoadIcon(out var tint);
        if (sprite != null)
        {
            t.Icon.sprite = sprite;
            t.Icon.color = tint;
            t.Icon.enabled = true;
            t.Fallback.enabled = false;
        }
        else
        {
            t.Icon.enabled = false;
            t.Fallback.text = weapon.DisplayName;
            t.Fallback.enabled = true;
        }
    }

    /// <summary>The ring just filled: one small squash so the eye catches it.</summary>
    private static IEnumerator Punch(Transform target)
    {
        float t = 0f;
        while (t < PunchSeconds)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / PunchSeconds) * Mathf.PI);
            target.localScale = Vector3.one * (1f + (PunchScale - 1f) * k);
            yield return null;
        }
        target.localScale = Vector3.one;
    }

    private const int NoTouch = -1;

    /// <summary>Touchscreen touch id behind a uGUI pointer, or <see cref="NoTouch"/>
    /// for mouse/pen (editor, Device Simulator) - those have no watchdog.</summary>
    private static int TouchIdOf(PointerEventData e) =>
        e is ExtendedPointerEventData x && x.pointerType == UIPointerType.Touch ? x.touchId : NoTouch;

    /// <summary>Is the finger that pressed a control still on the glass? The
    /// press frame always counts as down, so a one-frame tap still fires.</summary>
    private static bool TouchStillDown(int touchId, int pressFrame)
    {
        if (touchId == NoTouch || Time.frameCount == pressFrame) return true;
        var screen = Touchscreen.current;
        if (screen == null) return false;
        foreach (var touch in screen.touches)
        {
            if (touch.isInProgress && touch.touchId.ReadValue() == touchId) return true;
        }
        return false;
    }

    /// <summary>Hold-to-fire button. Pointer-down starts holding, the matching
    /// pointer-up (delivered even after the finger slid off) ends it.</summary>
    private class Trigger : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public int Slot;
        public Image Fill;
        public Image Icon;
        public Text Fallback;
        public bool WasCharged;
        public int WeaponId;
        public System.Action<int, bool> OnHeld;

        private int _pointer = int.MinValue;
        private int _touchId = NoTouch;
        private int _pressFrame;

        private void OnDisable()
        {
            Release();
        }

        private void Update()
        {
            if (_pointer != int.MinValue && !TouchStillDown(_touchId, _pressFrame)) Release();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer != int.MinValue) return;
            _pointer = e.pointerId;
            _touchId = TouchIdOf(e);
            _pressFrame = Time.frameCount;
            OnHeld?.Invoke(Slot, true);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            Release();
        }

        public void Release()
        {
            if (_pointer == int.MinValue) return;
            _pointer = int.MinValue;
            _touchId = NoTouch;
            OnHeld?.Invoke(Slot, false);
        }
    }
}
