using UnityEngine;

/// <summary>
/// The warm-up bot's pilot: an IInputProvider that PlayerController reads
/// exactly as it would a keyboard, producing rotation, throttle and trigger
/// from a small state machine. Lives only on the host, on the bot's own
/// GameObject, added by BotManager after spawn.
///
/// It flies the real flight model and nothing else - no teleports, no direct
/// velocity writes - so everything it does a human could do with the same
/// inputs, and its inputs ramp at the keyboard provider's rate so it can
/// never out-turn one. It never reads a human's position to steer (that is
/// what "not aggressive" means here); the only thing it knows about humans
/// is whether one sits in its line of fire.
///
/// Sign conventions, derived from PlayerController.rotatePlane: horizontal
/// input +1 spins the plane clockwise, i.e. heading DEcreases - nose down
/// when flying right, nose up when flying left. Heading 0 = flying right,
/// 90 = straight up, -90 = straight down, +-180 = flying left.
///
/// Design notes and the numbers behind every constant:
/// Prompts/25-CLAUDE-PLAN-warmup-bot.md.
/// </summary>
[DefaultExecutionOrder(-50)]   // decide before PlayerController.FixedUpdate reads us
public class BotBrain : MonoBehaviour, IInputProvider
{
    private enum State { Cruise, Loop, Reverse, ZoomClimb, Recover }

    // --- tunables --------------------------------------------------------

    // DesktopInputProvider.smoothingSpeed: 0.5 s to full deflection. The bot
    // gets the same ramp, which is what keeps it honest.
    private const float InputRampPerSecond = 2f;

    // Altitude band the bot cruises in, as offsets from the live Ground and
    // Space border lines (read at Init - they depend on the host's aspect).
    private const float CruiseFloorAboveGround = 6f;
    private const float CruiseCeilingBelowSpace = 5f;

    private const float CruiseSpeed = 7f;              // +-CruiseSpeedWander over time
    private const float CruiseSpeedWander = 1.5f;
    private const float CruisePitchPerUnit = 6f;       // degrees of pitch per unit of altitude error
    private const float CruiseMaxPitch = 30f;
    private const float CruiseHeadingWander = 15f;     // degrees, Perlin
    private const float StallGuardSpeed = 4f;          // throttle up below this
    private const float StallGuardPitch = 20f;         // ...or when climbing steeper than this

    private const float LoopIntervalMin = 10f, LoopIntervalMax = 20f;
    private const float LoopMinSpeed = 7f;
    private const float LoopMarginUp = 2f, LoopMarginDown = 2.5f;

    private const float ReverseIntervalMin = 8f, ReverseIntervalMax = 16f;

    // The deliberate stall that puts the recovery on show.
    private const float ZoomIntervalMin = 30f, ZoomIntervalMax = 50f;
    private const float ZoomFloorAboveGround = 12f;    // room to fall, relight and pull out
    private const float ZoomCeilingBelowSpace = 8f;
    private const float ZoomPitch = 75f;
    private const float ZoomMaxSeconds = 4f;

    private const float RecoverDiveHeading = -90f;     // dead centre of the relight window
    private const float RecoverSteerGain = 20f;        // degrees of error per full deflection
    private const float RecoverBuildSpeedSeconds = 0.4f;
    private const float RecoverMaxSeconds = 6f;
    private const float RecoverLevelTolerance = 0.25f; // |right.y| below this counts as level
    private const float RecoverMinSpeed = 4f;

    private const float GroundPullOutMargin = 2.5f;
    private const float SpaceAvoidMargin = 4f;
    private const float SteerGain = 30f;               // cruise/pull-out steering gain

    private const float BurstMin = 0.4f, BurstMax = 1.2f;
    private const float BurstGapMin = 2f, BurstGapMax = 6f;
    private const float NoFireAfterSpawnSeconds = 1f;

    /// <summary>A human inside this cone ahead mutes the trigger. 0 disables.</summary>
    public static float FriendlyFireConeDegrees = 20f;
    public static float FriendlyFireRange = 15f;

    // --- state -----------------------------------------------------------

    private PlayerController _player;
    private Rigidbody2D _rb;
    private float _groundTop;
    private float _spaceBottom;

    private State _state = State.Cruise;
    private float _stateEnteredAt;
    private float _baseHeading;         // 0 or 180: which way cruise faces

    private float _h, _v;               // ramped outputs
    private float _hTarget, _vTarget;   // what the state asked for this tick
    private bool _bursting;
    private bool _suppressed;

    private float _nextLoopAt, _nextReverseAt, _nextZoomAt;
    private float _burstEndsAt, _nextBurstAt;
    private float _spawnedAt;
    private float _seed;

    private float _turned;              // accumulated rotation in a loop/reverse
    private float _prevHeading;
    private int _turnDir;               // +1 = nose-up input, -1 = nose-down
    private bool _prevEngineOff;
    private bool _relit;
    private float _relitAt;

    public void Init(PlayerController player)
    {
        _player = player;
        _rb = player.GetComponent<Rigidbody2D>();
        _seed = Random.Range(0f, 1000f);
        _spawnedAt = Time.time;

        // The borders are runtime objects from ScreenSetup; their lines depend
        // on the host's aspect ratio, so they are read, never assumed.
        var ground = GameObject.Find("Ground");
        var space = GameObject.Find("Space");
        var groundCol = ground != null ? ground.GetComponent<Collider2D>() : null;
        var spaceCol = space != null ? space.GetComponent<Collider2D>() : null;
        _groundTop = groundCol != null ? groundCol.bounds.max.y : -9f;
        _spaceBottom = spaceCol != null ? spaceCol.bounds.min.y : 17f;

        _baseHeading = _player.transform.right.x >= 0f ? 0f : 180f;
        _prevHeading = _player.HeadingDegrees;
        _prevEngineOff = _player.IsEngineOff;

        float now = Time.time;
        _nextLoopAt = now + Random.Range(LoopIntervalMin, LoopIntervalMax);
        _nextReverseAt = now + Random.Range(ReverseIntervalMin, ReverseIntervalMax);
        _nextZoomAt = now + Random.Range(ZoomIntervalMin, ZoomIntervalMax);
        _nextBurstAt = now + NoFireAfterSpawnSeconds + Random.Range(BurstGapMin, BurstGapMax);

        Enter(State.Cruise);
        Debug.Log($"[BotBrain] Init: ground {_groundTop:0.0}, space {_spaceBottom:0.0}, turn radius {_player.EngineOnTurnRadius:0.00}");
    }

    // --- IInputProvider --------------------------------------------------

    public float GetHorizontalInput() => _h;
    public float GetVerticalInput() => _v;
    public bool GetShootInput() => _bursting && !_suppressed;
    public void UpdateInput() { }   // decisions run in FixedUpdate

    // --- per-tick ----------------------------------------------------------

    private void FixedUpdate()
    {
        if (_player == null) return;

        if (_player.InHangar)
        {
            _hTarget = 0f; _vTarget = 0f; _bursting = false;
            Ramp();
            return;
        }

        float heading = _player.HeadingDegrees;
        float y = _player.transform.position.y;
        float v = _rb != null ? _rb.linearVelocity.magnitude : _player.Speed;
        bool engineOff = _player.IsEngineOff;
        float r = _player.EngineOnTurnRadius;
        Vector2 dir = _player.transform.right;

        // Engine just cut, for whatever reason: recovery pre-empts everything.
        if (engineOff && !_prevEngineOff && _state != State.Recover) Enter(State.Recover);
        _prevEngineOff = engineOff;

        _hTarget = 0f;
        _vTarget = 0f;

        // Overrides - only meaningful with a running engine; Recover handles
        // its own pull-out with the same maths.
        bool overridden = false;
        if (!engineOff && _state != State.Recover)
        {
            if (dir.y < 0f)
            {
                // A pull-out from dive angle phi bottoms out r(1 - cos phi)
                // lower; for a unit vector cos phi = |right.x|.
                float dip = r * (1f - Mathf.Abs(dir.x));
                if (y < _groundTop + dip + GroundPullOutMargin)
                {
                    _hTarget = SteerTo(LevelHeading(dir), SteerGain);
                    _vTarget = 1f;
                    overridden = true;
                }
            }
            else if (dir.y > 0f && y > _spaceBottom - SpaceAvoidMargin)
            {
                _hTarget = SteerTo(LevelHeading(dir), SteerGain);
                _vTarget = 1f;
                overridden = true;
            }
        }

        if (overridden)
        {
            // An aerobatic figure that ran into the edge is abandoned; a cruise
            // just picks up where the override leaves it.
            if (_state == State.Loop || _state == State.Reverse || _state == State.ZoomClimb)
            {
                _baseHeading = LevelHeading(dir);
                Enter(State.Cruise);
            }
        }
        else
        {
            switch (_state)
            {
                case State.Cruise:    TickCruise(heading, y, v, r, engineOff); break;
                case State.Loop:      TickTurn(heading, 360f); break;
                case State.Reverse:   TickTurn(heading, 180f); break;
                case State.ZoomClimb: TickZoom(heading); break;
                case State.Recover:   TickRecover(heading, v, dir, engineOff); break;
            }
        }

        _prevHeading = heading;
        Ramp();
        TickShooting(engineOff, dir);
    }

    private void Ramp()
    {
        float step = InputRampPerSecond * Time.fixedDeltaTime;
        _h = Mathf.MoveTowards(_h, Mathf.Clamp(_hTarget, -1f, 1f), step);
        _v = Mathf.MoveTowards(_v, Mathf.Clamp(_vTarget, -1f, 1f), step);
    }

    // --- states ----------------------------------------------------------

    private void Enter(State next)
    {
        _state = next;
        _stateEnteredAt = Time.time;
        _turned = 0f;
        _relit = false;
    }

    private void TickCruise(float heading, float y, float v, float r, bool engineOff)
    {
        float t = Time.time;

        float floor = _groundTop + CruiseFloorAboveGround;
        float ceiling = _spaceBottom - CruiseCeilingBelowSpace;
        float centre = (floor + ceiling) * 0.5f;
        float halfBand = (ceiling - floor) * 0.5f;

        // Slow Perlin wander: altitude target and a little heading noise, so
        // the plane meanders instead of holding a rail.
        float yTarget = centre + (Mathf.PerlinNoise(t * 0.1f, _seed) - 0.5f) * 2f * halfBand * 0.4f;
        float pitch = Mathf.Clamp((yTarget - y) * CruisePitchPerUnit, -CruiseMaxPitch, CruiseMaxPitch);
        float wander = (Mathf.PerlinNoise(t * 0.25f, _seed + 17f) - 0.5f) * 2f * CruiseHeadingWander;

        // Base 0 climbs by increasing the heading, base 180 by decreasing it.
        float sign = _baseHeading == 0f ? 1f : -1f;
        float target = _baseHeading + (pitch + wander) * sign;
        _hTarget = SteerTo(target, SteerGain);

        float vTarget = CruiseSpeed + (Mathf.PerlinNoise(t * 0.15f, _seed + 31f) - 0.5f) * 2f * CruiseSpeedWander;
        _vTarget = Mathf.Clamp((vTarget - v) / 2f, -1f, 1f);
        if (v < StallGuardSpeed || pitch > StallGuardPitch) _vTarget = 1f;

        if (engineOff) return;   // Recover is about to take over

        // Aerobatics, in priority order, each on its own clock.
        if (t >= _nextLoopAt)
        {
            _nextLoopAt = t + Random.Range(LoopIntervalMin, LoopIntervalMax);
            if (v >= LoopMinSpeed && TryPickTurnDirection(y, 2f * r, out _turnDir))
            {
                Enter(State.Loop);
                return;
            }
        }
        if (t >= _nextReverseAt)
        {
            _nextReverseAt = t + Random.Range(ReverseIntervalMin, ReverseIntervalMax);
            // A half-turn from level still spans a full 2r vertically.
            if (v >= LoopMinSpeed && TryPickTurnDirection(y, 2f * r, out _turnDir))
            {
                Enter(State.Reverse);
                return;
            }
        }
        if (t >= _nextZoomAt)
        {
            _nextZoomAt = t + Random.Range(ZoomIntervalMin, ZoomIntervalMax);
            if (y >= _groundTop + ZoomFloorAboveGround && y <= _spaceBottom - ZoomCeilingBelowSpace)
            {
                Enter(State.ZoomClimb);
            }
        }
    }

    /// <summary>Which way to pull a figure of vertical extent `extent`:
    /// nose-up if there is sky, nose-down if there is ground, the roomier
    /// side when both fit, neither when neither does.</summary>
    private bool TryPickTurnDirection(float y, float extent, out int dir)
    {
        float roomUp = _spaceBottom - (y + extent + LoopMarginUp);
        float roomDown = (y - extent - LoopMarginDown) - _groundTop;
        bool up = roomUp > 0f, down = roomDown > 0f;
        dir = 0;
        if (!up && !down) return false;
        dir = (up && down) ? (roomUp >= roomDown ? +1 : -1) : (up ? +1 : -1);
        return true;
    }

    private void TickTurn(float heading, float total)
    {
        // Full deflection the whole way round; the accumulated delta is
        // unambiguous because one tick never turns more than a few degrees.
        _hTarget = _turnDir * NoseUpInput(_player.transform.right);
        _vTarget = 1f;   // throttle swamps climb drag, so the circle stays 2r tall

        _turned += Mathf.DeltaAngle(_prevHeading, heading);
        if (Mathf.Abs(_turned) >= total)
        {
            if (_state == State.Reverse) _baseHeading = _baseHeading == 0f ? 180f : 0f;
            Debug.Log($"[BotBrain] {_state} done in {Time.time - _stateEnteredAt:0.0}s");
            Enter(State.Cruise);
        }
    }

    private void TickZoom(float heading)
    {
        // Nose high, throttle closed: speed bleeds ~6/s and the engine cuts
        // inside a second. The cut itself flips us into Recover.
        float sign = _baseHeading == 0f ? 1f : -1f;
        _hTarget = SteerTo(_baseHeading + ZoomPitch * sign, SteerGain);
        _vTarget = -1f;
        if (Time.time - _stateEnteredAt > ZoomMaxSeconds) Enter(State.Cruise);
    }

    private void TickRecover(float heading, float v, Vector2 dir, bool engineOff)
    {
        float elapsed = Time.time - _stateEnteredAt;
        if (elapsed > RecoverMaxSeconds)
        {
            Debug.LogWarning("[BotBrain] Recover gave up after 6 s");
            _baseHeading = LevelHeading(dir);
            Enter(State.Cruise);
            return;
        }

        if (engineOff)
        {
            // The reflex: nose to straight down, the centre of the relight
            // window. Above Space the engine cannot relight at all, and
            // pointing down is also the fastest way back below the line.
            // Throttle is pre-armed for the first engine-on tick.
            _hTarget = SteerTo(RecoverDiveHeading, RecoverSteerGain);
            _vTarget = 1f;
            _relit = false;
            return;
        }

        if (!_relit)
        {
            _relit = true;
            _relitAt = Time.time;
            Debug.Log($"[BotBrain] Recover: relit after {elapsed:0.00}s");
        }

        if (Time.time - _relitAt < RecoverBuildSpeedSeconds)
        {
            // Hold the dive briefly: dive boost + throttle is +8 u/s of speed,
            // which the pull-out will need.
            _hTarget = 0f;
            _vTarget = 1f;
            return;
        }

        _hTarget = SteerTo(LevelHeading(dir), SteerGain);
        _vTarget = 1f;
        if (Mathf.Abs(dir.y) < RecoverLevelTolerance && v >= RecoverMinSpeed)
        {
            _baseHeading = LevelHeading(dir);
            Enter(State.Cruise);
        }
    }

    // --- shooting --------------------------------------------------------

    private void TickShooting(bool engineOff, Vector2 dir)
    {
        float t = Time.time;
        bool mayFire = !engineOff && t - _spawnedAt > NoFireAfterSpawnSeconds &&
                       (_state == State.Cruise || _state == State.Loop || _state == State.Reverse);

        if (_bursting && (t >= _burstEndsAt || !mayFire))
        {
            _bursting = false;
            _nextBurstAt = t + Random.Range(BurstGapMin, BurstGapMax);
        }
        else if (!_bursting && mayFire && t >= _nextBurstAt)
        {
            _bursting = true;
            _burstEndsAt = t + Random.Range(BurstMin, BurstMax);
        }

        _suppressed = _bursting && HumanInLineOfFire(dir);
    }

    /// <summary>The one thing the bot knows about humans: whether one sits
    /// inside the cone ahead. Not aggression avoidance in the tactical sense
    /// - just not shooting a bystander in the back.</summary>
    private bool HumanInLineOfFire(Vector2 dir)
    {
        if (FriendlyFireConeDegrees <= 0f || FriendlyFireRange <= 0f) return false;

        Vector2 from = _player.transform.position;
        foreach (var other in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (other == _player || other.IsBot || other.InHangar) continue;
            Vector2 to = (Vector2)other.transform.position - from;
            if (to.sqrMagnitude > FriendlyFireRange * FriendlyFireRange) continue;
            if (Vector2.Angle(dir, to) < FriendlyFireConeDegrees) return true;
        }
        return false;
    }

    // --- steering primitives ---------------------------------------------

    /// <summary>Horizontal input that rotates the heading toward a target.
    /// DeltaAngle is positive when the heading must increase (counter-
    /// clockwise), and +1 input spins clockwise, hence the minus.</summary>
    private float SteerTo(float targetDeg, float gainDeg)
    {
        float delta = Mathf.DeltaAngle(_player.HeadingDegrees, targetDeg);
        return -Mathf.Clamp(delta / gainDeg, -1f, 1f);
    }

    /// <summary>Input that raises the nose toward the sky for the current
    /// direction of flight: -1 flying right, +1 flying left.</summary>
    private static float NoseUpInput(Vector2 dir) => dir.x >= 0f ? -1f : 1f;

    /// <summary>The nearer level heading, 0 or 180.</summary>
    private static float LevelHeading(Vector2 dir) => dir.x >= 0f ? 0f : 180f;
}
