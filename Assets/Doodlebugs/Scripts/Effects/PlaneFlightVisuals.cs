using UnityEngine;

/// <summary>
/// The plane's flight body language (Prompts/28 §5), on every client, from
/// what is already synced: heading (transform), engine state, speed. No new
/// network state. Added at runtime by PlaneVisualEffects - no prefab change.
///
/// - bank: the sprite squashes vertically with the turn rate (fake roll)
/// - half-roll: flying wheels-up for a moment rolls the sprite upright
///   (scale.y through zero, flipY at the midpoint)
/// - wingtip trails in a hard turn
/// - LEAN (Prompts/32 §4.10): turning faster than the envelope allows for
///   longer than a moment brightens and stretches the wingtip trails - from
///   the observed heading, so opponents see why that plane out-turned them
/// - stall shudder near the stall speed while climbing
/// - engine cut / relight puffs; sounds and haptics for the owner only
///
/// The visual child's ROTATION is never touched: PlayerController reads it
/// for the engine-relight window.
/// </summary>
public class PlaneFlightVisuals : MonoBehaviour
{
    private const float BankMin = 0.55f;          // scale.y at full bank
    private const float BankRateForFull = 220f;   // deg/s
    private const float BankSmoothing = 0.08f;    // s
    private const float RollSeconds = 0.22f;
    private const float InvertedHoldSeconds = 0.25f;
    private const float TrailLoad = 0.7f;         // fraction of the preset turn rate
    private const float WingSpan = 0.55f;         // local units of PlaneHolder (scale 2)
    private const float OverEnvelope = 1.04f;     // measured rate vs TurnRateAt
    private const float OverHoldSeconds = 0.25f;
    private const float LeanTrailLifetime = 2.2f; // x the normal trail lifetime
    private const float LeanTrailSize = 1.4f;
    private static readonly Color LeanTrailTint = new Color(1f, 0.93f, 0.55f);

    private PlayerController _pc;
    private Transform _plane;
    private SpriteRenderer _sprite;
    private SpriteRenderer _glow;
    private ParticleSystem _trailA, _trailB;

    private float _prevHeading;
    private float _omega;            // measured, deg/s, smoothed
    private float _invertedFor;
    private bool _flipped;
    private float _rollT = -1f;      // -1 = not rolling, else 0..1
    private bool _rollFlipDone;
    private float _shake;
    private float _nextStallTick;
    private float _flashUntil;
    private float _overFor;
    private bool _leanLook;
    private TrailPreset _trailPreset;

    // Arsenal (Prompts/29 §6)
    private Shooting _shooting;
    private PlaneStats _stats;
    private LineRenderer _beam;
    private float _beamStart;
    private SpriteRenderer[] _aimDots;
    private float _dodgeRollT = -1f;

    public void Init(PlayerController pc)
    {
        _pc = pc;
        _plane = pc.plane;
        if (_plane == null) return;
        _sprite = _plane.GetComponent<SpriteRenderer>();
        var glow = _plane.Find("GlowOutline");
        _glow = glow != null ? glow.GetComponent<SpriteRenderer>() : null;
        _prevHeading = pc.HeadingDegrees;

        var preset = new TrailPreset
        {
            Rate = 0f, LifetimeMin = 0.25f, LifetimeMax = 0.35f, SizeMin = 0.06f, SizeMax = 0.09f,
            Speed = 0f, Jitter = 0.01f, StartSizeCurveEnd = 0.4f,
            ColorStart = Color.white, ColorEnd = new Color(0.8f, 0.9f, 1f), AlphaPeak = 0.7f,
        };
        _trailPreset = preset;
        int order = _sprite != null ? _sprite.sortingOrder - 1 : 0;
        _trailA = EffectAssets.CreateTrailSystem(transform, preset, order);
        _trailB = EffectAssets.CreateTrailSystem(transform, preset, order);

        pc.OnEngineStateChanged += OnEngineState;
        pc.OnSnapTurn += OnSnap;

        _shooting = pc.GetComponent<Shooting>();
        _stats = pc.GetComponent<PlaneStats>();
        if (_shooting != null) _shooting.NetCharging.OnValueChanged += OnCharging;
        if (_stats != null) _stats.NetDodging.OnValueChanged += OnDodging;
    }

    private void OnCharging(bool prev, bool now)
    {
        if (now) _beamStart = Time.time;
        if (_beam == null && now)
        {
            var go = new GameObject("LanceBeam");
            go.transform.SetParent(transform, false);
            _beam = go.AddComponent<LineRenderer>();
            _beam.positionCount = 2;
            _beam.useWorldSpace = true;
            _beam.material = EffectAssets.ParticleMaterial;
            _beam.sortingOrder = WorldFx.Order - 4;
            _beam.numCapVertices = 2;
        }
        if (_beam != null) _beam.enabled = now;
    }

    private void OnDodging(bool prev, bool now)
    {
        if (now) _dodgeRollT = 0f;
    }

    private void OnDestroy()
    {
        if (_pc == null) return;
        _pc.OnEngineStateChanged -= OnEngineState;
        _pc.OnSnapTurn -= OnSnap;
        if (_shooting != null) _shooting.NetCharging.OnValueChanged -= OnCharging;
        if (_stats != null) _stats.NetDodging.OnValueChanged -= OnDodging;
        if (_aimDots != null) foreach (var d in _aimDots) if (d != null) Destroy(d.gameObject);
    }

    private bool Mine => _pc != null && _pc.IsOwner && !_pc.IsBot;

    private void OnEngineState(bool off)
    {
        if (_pc.InHangar) return;
        var puff = new BurstPreset
        {
            Count = off ? 10 : 6, SpeedMin = 0.5f, SpeedMax = 1.5f, LifetimeMin = 0.3f, LifetimeMax = 0.6f,
            SizeMin = 0.15f, SizeMax = 0.3f,
            ColorStart = off ? new Color(0.5f, 0.5f, 0.5f) : new Color(1f, 0.85f, 0.5f),
            ColorEnd = new Color(0.3f, 0.3f, 0.3f),
        };
        Vector3 tail = transform.position - transform.right * 0.6f;
        EffectAssets.CreateBurst(tail, puff, 1f, _sprite != null ? _sprite.sortingOrder + 1 : 50);
        if (!Mine) return;
        if (off) { SfxManager.PlayStall(); SfxManager.Haptic(HapticKind.Medium); }
        else { SfxManager.PlayRelight(); SfxManager.Haptic(HapticKind.Light); }
    }

    private void OnSnap()
    {
        _flashUntil = Time.time + 0.2f;
        _shake = Mathf.Max(_shake, 0.4f);
        if (!Mine) return;
        SfxManager.PlayWhoosh();
        SfxManager.Haptic(HapticKind.Light);
    }

    private void LateUpdate()
    {
        if (_pc == null || _plane == null) return;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        float heading = _pc.HeadingDegrees;
        float raw = Mathf.DeltaAngle(_prevHeading, heading) / dt;
        _prevHeading = heading;
        if (Mathf.Abs(raw) > 2000f) raw = 0f;   // respawn / wrap teleport
        _omega = Mathf.Lerp(_omega, raw, 1f - Mathf.Exp(-dt / BankSmoothing));
        float absW = Mathf.Abs(_omega);

        bool parked = _pc.InHangar;
        var preset = FlightTuning.Active;

        // --- bank -------------------------------------------------------------
        float bank = Mathf.Lerp(1f, BankMin, Mathf.Clamp01(absW / BankRateForFull));

        // --- half-roll to wheels-down -----------------------------------------
        bool wheelsUp = transform.right.x < -0.15f ? !_flipped
                      : transform.right.x > 0.15f ? _flipped
                      : false;
        bool hardTurn = absW > TrailLoad * preset.TurnRate;
        if (_rollT < 0f)
        {
            _invertedFor = wheelsUp && !hardTurn ? _invertedFor + dt : 0f;
            if (_invertedFor >= InvertedHoldSeconds)
            {
                _rollT = 0f;
                _rollFlipDone = false;
                _invertedFor = 0f;
                if (Mine) SfxManager.PlayWhoosh(0.3f);
            }
        }
        float roll = 1f;
        if (_rollT >= 0f)
        {
            _rollT += dt / RollSeconds;
            if (_rollT >= 0.5f && !_rollFlipDone)
            {
                _rollFlipDone = true;
                _flipped = !_flipped;
            }
            roll = Mathf.Abs(Mathf.Cos(Mathf.Clamp01(_rollT) * Mathf.PI));
            if (_rollT >= 1f) _rollT = -1f;
        }
        // Barrel roll gadget: one full fast roll, ghosted
        if (_dodgeRollT >= 0f)
        {
            _dodgeRollT += dt / 0.45f;
            roll *= Mathf.Abs(Mathf.Cos(Mathf.Clamp01(_dodgeRollT) * Mathf.PI * 2f));
            if (_dodgeRollT >= 1f) _dodgeRollT = -1f;
        }
        if (_sprite != null && _stats != null)
        {
            var c = _sprite.color;
            float want = _stats.IsDodging ? 0.55f : 1f;
            if (!Mathf.Approximately(c.a, want)) _sprite.color = new Color(c.r, c.g, c.b, want);
        }

        if (parked) { _flipped = false; _rollT = -1f; roll = 1f; }

        if (_sprite != null) _sprite.flipY = _flipped;
        if (_glow != null) _glow.flipY = _flipped;
        _plane.localScale = new Vector3(1f, Mathf.Max(0.04f, bank * roll), 1f);

        // --- stall shudder ----------------------------------------------------
        bool nearStall = !_pc.IsEngineOff && _pc.Speed < preset.MinSpeed + 1f && transform.right.y > 0.2f;
        if (nearStall)
        {
            _shake = Mathf.Max(_shake, 1f);
            if (Mine && Time.time >= _nextStallTick)
            {
                _nextStallTick = Time.time + 0.25f;
                SfxManager.PlayNamed("beep", 0.25f, 0f);
                SfxManager.Haptic(HapticKind.Light);
            }
        }
        _shake = Mathf.MoveTowards(_shake, 0f, dt * 4f);
        if (_sprite != null)
        {
            // Position jitter, not rotation: the child's rotation is the
            // heading PlayerController tests for the relight window.
            float jitter = _shake > 0f ? Mathf.Sin(Time.time * 70f) * 0.04f * _shake : 0f;
            _plane.localPosition = new Vector3(0f, jitter, 0f);
        }

        UpdateBeam(parked);
        UpdateAimGuide(parked);

        // --- over-envelope turn (LEAN) -----------------------------------------
        // A snap also beats the envelope for ~0.2 s; the owner knows when it
        // snapped and skips that window, remote clients only have the hold time.
        float handling = _stats != null ? _stats.Handling : 1f;
        float envelope = FlightModel.TurnRateAt(preset, _pc.Speed, handling);
        bool over = !parked && !_pc.IsEngineOff && absW > envelope * OverEnvelope
                    && Time.time >= _flashUntil + 0.1f;
        _overFor = over ? _overFor + dt : 0f;
        SetLeanLook(_overFor >= OverHoldSeconds);

        // --- wingtip trails ---------------------------------------------------
        bool trails = !parked && (hardTurn || _leanLook || Time.time < _flashUntil) && !_pc.IsEngineOff;
        SetTrail(_trailA, trails, +1f, bank * roll);
        SetTrail(_trailB, trails, -1f, bank * roll);
    }

    private void UpdateBeam(bool parked)
    {
        if (_beam == null || !_beam.enabled) return;
        if (parked) { _beam.enabled = false; return; }
        var w = _shooting.GetWeapon(0);
        float k = w.ChargeSeconds > 0f ? Mathf.Clamp01((Time.time - _beamStart) / w.ChargeSeconds) : 1f;
        Vector3 from = _shooting.firePoint != null ? _shooting.firePoint.position : transform.position;
        _beam.SetPosition(0, from);
        _beam.SetPosition(1, from + transform.right * 14f);
        float width = Mathf.Lerp(0.03f, 0.12f, k);
        _beam.startWidth = width;
        _beam.endWidth = width * 0.3f;
        var c = new Color(0.7f, 0.95f, 1f, Mathf.Lerp(0.15f, 0.7f, k) * (k >= 1f ? 0.8f + 0.2f * Mathf.Sin(Time.time * 30f) : 1f));
        _beam.startColor = c;
        _beam.endColor = new Color(c.r, c.g, c.b, 0f);
    }

    /// <summary>Owner only: where a dropped bomb will fall (8 dots).</summary>
    private void UpdateAimGuide(bool parked)
    {
        bool show = false;
        WeaponProfile w = null;
        if (Mine && !parked && _shooting != null)
        {
            w = _shooting.GetWeapon(0);
            show = w.GravityScale > 0f && w.ForceMultiplier <= 0f;
        }
        if (!show)
        {
            if (_aimDots != null) foreach (var d in _aimDots) d.enabled = false;
            return;
        }
        if (_aimDots == null)
        {
            _aimDots = new SpriteRenderer[8];
            for (int i = 0; i < _aimDots.Length; i++)
            {
                var go = new GameObject("AimDot");
                _aimDots[i] = go.AddComponent<SpriteRenderer>();
                _aimDots[i].sprite = UiSprites.Disc;
                _aimDots[i].sortingOrder = WorldFx.Order - 8;
                go.transform.localScale = Vector3.one * 0.14f;
            }
        }
        Vector2 p0 = transform.position;
        Vector2 v0 = (Vector2)transform.right * _pc.Speed;
        float g = Physics2D.gravity.y * w.GravityScale;
        for (int i = 0; i < _aimDots.Length; i++)
        {
            float t = 0.18f * (i + 1);
            Vector2 p = p0 + v0 * t + 0.5f * g * t * t * Vector2.up;
            _aimDots[i].enabled = true;
            _aimDots[i].transform.position = p;
            _aimDots[i].color = new Color(1f, 1f, 1f, 0.55f * (1f - i / 9f));
        }
    }

    private void SetLeanLook(bool on)
    {
        if (on == _leanLook || _trailPreset == null) return;
        _leanLook = on;
        foreach (var ps in new[] { _trailA, _trailB })
        {
            if (ps == null) continue;
            var main = ps.main;
            float l = on ? LeanTrailLifetime : 1f;
            float z = on ? LeanTrailSize : 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(_trailPreset.LifetimeMin * l, _trailPreset.LifetimeMax * l);
            main.startSize = new ParticleSystem.MinMaxCurve(_trailPreset.SizeMin * z, _trailPreset.SizeMax * z);
            main.startColor = on ? LeanTrailTint : Color.white;
        }
    }

    private void SetTrail(ParticleSystem ps, bool on, float side, float bank)
    {
        if (ps == null) return;
        ps.transform.localPosition = new Vector3(-0.15f, side * WingSpan * bank, 0f);
        var emission = ps.emission;
        emission.rateOverTime = on ? (_leanLook ? 90f : 60f) : 0f;
    }
}
