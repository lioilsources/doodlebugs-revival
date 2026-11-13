using UnityEngine;

/// <summary>
/// Combat half of the bot pilot (Prompts/27 WP4). Priority each tick:
/// ground/space override → Recover → Evade → Disengage → Pursue → Cruise.
/// Overrides and Recover live in BotBrain.cs and run first; this file picks
/// between the rest and fires.
///
/// Fairness, non-negotiable: the input ramp is the same for every tier and
/// the same as a keyboard's, nothing reads a human's input, nothing writes
/// velocity or position. A tier is reaction time, aim error, lead and
/// trigger discipline (BotTier).
/// </summary>
public partial class BotBrain
{
    // Threat = an enemy this close behind with this bot inside its nose cone.
    private const float ThreatRange = 12f;
    private const float ThreatConeDeg = 25f;
    private const float EvadeCooldownSeconds = 3f;
    private const float EvadeTurnDegrees = 150f;

    private const float RetargetSeconds = 1f;
    private const float CrateRefreshSeconds = 0.5f;
    private const float BreakAwayDistance = 3f;      // a midair kills both planes
    private const float AimFloorAboveGround = 3f;    // never aim the dive into the dirt
    private const float AimCeilingBelowSpace = 3f;
    private const float ExplosiveSafetyMargin = 1.5f;
    private const float LowSpeedMaxClimbDeg = 30f;   // climb cap near stall speed
    private const float DisengageWeaveDeg = 25f;

    private Shooting _shooting;
    private PlayerController _target;
    private float _targetAcquiredAt;
    private float _nextRetargetAt;
    private PlayerController _threat;
    private float _threatSeenAt;
    private bool _threatRolled;
    private float _evadeCooldownUntil;
    private PowerUp _crate;
    private float _nextCrateAt;
    private float _aimError;

    /// <summary>Recover gave up (the 6 s warning) - the soak gate counts these.</summary>
    public static int RecoverTimeouts;

    // Soak/profiler: wall time spent in combat brains, per physics tick.
    public static double TickMillisTotal;
    public static int TickSamples;

    private BotTier Tier => BotTier.Get(_player.NetBotTier.Value);

    private int HitPoints
    {
        get
        {
            var stats = _player.PlaneStats;
            return stats != null ? stats.Health + stats.Shield : 3;
        }
    }

    private void ResetCombatSenses()
    {
        _target = null;
        _threat = null;
        _threatRolled = false;
        _crate = null;
        _nextRetargetAt = 0f;
        _nextCrateAt = 0f;
        _aimError = 0f;
    }

    private static void RecordTickCost(long started)
    {
        long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
        TickMillisTotal += ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        TickSamples++;
    }

    // --- sensing + state choice -------------------------------------------

    /// <summary>Keeps target and threat current; runs even while an edge
    /// override flies the plane, so the bot does not come out of a pull-out blind.</summary>
    private void SenseCombat(bool engineOff)
    {
        float t = Time.time;
        float width = BotSenses.ArenaWidth;

        bool targetGone = _target != null &&
                          (!BotSenses.IsFlying(_target) || BotSenses.IsInvulnerable(_target));
        if (targetGone || t >= _nextRetargetAt)
        {
            var next = BotSenses.PickTarget(_player, targetGone ? null : _target, width);
            if (next != _target)
            {
                _targetAcquiredAt = t;
                RollAimError(Tier);
            }
            _target = next;
            _nextRetargetAt = t + RetargetSeconds;
        }

        var threat = BotSenses.FindThreat(_player, width, ThreatRange, ThreatConeDeg);
        if (threat != _threat)
        {
            _threat = threat;
            _threatSeenAt = t;
            _threatRolled = false;
        }
    }

    private void SelectCombatState(float y, float r, Vector2 dir, bool engineOff)
    {
        SenseCombat(engineOff);
        if (_state == State.Recover || engineOff) return;

        float t = Time.time;
        var tier = Tier;

        // Evade: someone is on my tail. React after the tier's delay, once per
        // threat, with the tier's probability; then finish the break.
        if (_state == State.Evade) return;
        if (_threat != null && !_threatRolled && t - _threatSeenAt >= tier.ReactionDelay &&
            t >= _evadeCooldownUntil)
        {
            _threatRolled = true;
            if (Random.value < tier.EvadeProbability && TryPickTurnDirection(y, 2f * r, out _turnDir))
            {
                _evadeCooldownUntil = t + EvadeCooldownSeconds;
                Enter(State.Evade);
                return;
            }
        }

        // Disengage: one hit from death, go find a crate (Ace, Baron).
        if (tier.DisengageAtOneHp && HitPoints <= 1)
        {
            if (_state != State.Disengage && !IsFigure(_state)) Enter(State.Disengage);
            return;
        }
        if (_state == State.Disengage) Enter(State.Cruise);

        // Aerobatics run to completion.
        if (IsFigure(_state)) return;

        if (_target != null)
        {
            if (_state != State.Pursue) Enter(State.Pursue);
        }
        else if (_state == State.Pursue)
        {
            _baseHeading = LevelHeading(dir);
            Enter(State.Cruise);
        }
    }

    // --- states -------------------------------------------------------------

    private void TickPursue(float y, float v, float r, bool engineOff, Vector2 dir)
    {
        if (_target == null) return;
        var tier = Tier;
        float width = BotSenses.ArenaWidth;
        Vector2 me = _player.transform.position;
        Vector2 delta = BotSenses.WrapDelta(me, _target.transform.position, width);
        float dist = delta.magnitude;

        Vector2 aim = BotSenses.InterceptPoint(me, _target, BotSenses.MuzzleSpeed(_shooting, v), tier.LeadFactor, width);
        aim.y = Mathf.Clamp(aim.y, _groundTop + AimFloorAboveGround, _spaceBottom - AimCeilingBelowSpace);
        Vector2 toAim = aim - me;
        float desired = Mathf.Atan2(toAim.y, toAim.x) * Mathf.Rad2Deg + _aimError;

        // Too close and closing: break off rather than trade a midair.
        Vector2 closingVel = BotSenses.VelocityOf(_target) - (Vector2)dir * v;
        if (dist < BreakAwayDistance && Vector2.Dot(closingVel, delta) < 0f)
        {
            desired = Mathf.Atan2(-delta.y, -delta.x) * Mathf.Rad2Deg;
        }

        _hTarget = SteerTo(LimitClimb(desired, v, dir), SteerGain);

        // Close the gap at full power, then sit on the target's speed.
        _vTarget = dist > tier.EngageRange * 0.5f
            ? 1f
            : Mathf.Clamp((_target.Speed - v) / 2f, -1f, 1f);
        if (v < StallGuardSpeed) _vTarget = 1f;

        // Out of range: the tiers that show off may fly a figure meanwhile.
        if (dist > tier.EngageRange && tier.AerobaticsChance > 0f) TryAerobatics(y, v, r, tier.AerobaticsChance);
    }

    private void TickDisengage(float v, Vector2 dir)
    {
        float t = Time.time;
        float width = BotSenses.ArenaWidth;
        Vector2 me = _player.transform.position;

        if (t >= _nextCrateAt || (_crate != null && !_crate.IsAvailable))
        {
            _crate = BotSenses.NearestHealCrate(me, width);
            _nextCrateAt = t + CrateRefreshSeconds;
        }

        float desired;
        if (_crate != null)
        {
            Vector2 d = BotSenses.WrapDelta(me, _crate.transform.position, width);
            desired = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }
        else
        {
            // No crate: run from the nearest plane, weaving, inside the cruise band.
            var enemy = BotSenses.NearestEnemy(_player, width);
            Vector2 away = enemy != null
                ? -BotSenses.WrapDelta(me, enemy.transform.position, width)
                : (Vector2)dir;
            float floor = _groundTop + CruiseFloorAboveGround;
            float ceiling = _spaceBottom - CruiseCeilingBelowSpace;
            if (me.y < floor) away.y = Mathf.Abs(away.x) * 0.5f;
            else if (me.y > ceiling) away.y = -Mathf.Abs(away.x) * 0.5f;
            desired = Mathf.Atan2(away.y, away.x) * Mathf.Rad2Deg +
                      Mathf.Sin(t * 1.5f + _seed) * DisengageWeaveDeg;
        }

        _hTarget = SteerTo(LimitClimb(desired, v, dir), SteerGain);
        _vTarget = 1f;
    }

    /// <summary>Near stall speed a steep climb ends in an engine cut; cap it.</summary>
    private float LimitClimb(float desired, float v, Vector2 dir)
    {
        if (v >= StallGuardSpeed + 1.5f) return desired;
        float level = LevelHeading(dir);
        float sign = level == 0f ? 1f : -1f;   // base 0 climbs with +heading, 180 with -heading
        float climb = sign * Mathf.DeltaAngle(level, desired);
        return climb > LowSpeedMaxClimbDeg ? level + LowSpeedMaxClimbDeg * sign : desired;
    }

    // --- shooting -------------------------------------------------------------

    private void TickCombatShooting(bool engineOff, Vector2 dir)
    {
        float t = Time.time;
        var tier = Tier;
        bool canFire = CanFireAtTarget(engineOff, dir, tier, t);

        if (_bursting && (t >= _burstEndsAt || !canFire))
        {
            _bursting = false;
            _nextBurstAt = t + Random.Range(tier.GapMin, tier.GapMax);
            RollAimError(tier);
        }
        else if (!_bursting && canFire && t >= _nextBurstAt)
        {
            _bursting = true;
            _burstEndsAt = t + Random.Range(tier.BurstMin, tier.BurstMax);
        }
        _suppressed = false;
    }

    private void RollAimError(BotTier tier) => _aimError = Random.Range(-tier.AimErrorDeg, tier.AimErrorDeg);

    private bool CanFireAtTarget(bool engineOff, Vector2 dir, BotTier tier, float t)
    {
        if (engineOff || _target == null) return false;
        // Only while a round runs: a LAN bot added in the PreBattle lobby or
        // flying through the results screen holds fire.
        var mm = MatchManager.Instance;
        if (mm == null || mm.Phase != MatchManager.GamePhase.Battle || mm.IsIntermission) return false;
        if (t - _spawnedAt <= NoFireAfterSpawnSeconds) return false;
        if (t - _targetAcquiredAt < tier.ReactionDelay) return false;
        if (_state != State.Pursue && _state != State.Disengage && _state != State.Cruise) return false;
        if (!BotSenses.IsFlying(_target) || BotSenses.IsInvulnerable(_target)) return false;

        Vector2 me = _player.transform.position;
        Vector2 them = _target.transform.position;
        float width = BotSenses.ArenaWidth;
        // Bullets do not wrap: only direct geometry.
        if (!BotSenses.IsDirect(me, them, width)) return false;

        float dist = Vector2.Distance(me, them);
        if (dist > tier.EngageRange) return false;

        var weapon = _shooting != null ? _shooting.CurrentWeapon : null;
        if (weapon != null && weapon.ExplosionRadius > 0f && dist < weapon.ExplosionRadius + ExplosiveSafetyMargin)
            return false;

        float speed = _rb != null ? _rb.linearVelocity.magnitude : _player.Speed;
        Vector2 aim = BotSenses.InterceptPoint(me, _target, BotSenses.MuzzleSpeed(_shooting, speed), tier.LeadFactor, width);
        // The bot believes its aim is off by _aimError: it pulls the trigger
        // when the nose matches that belief, so the error becomes a miss.
        Vector2 believed = Quaternion.Euler(0f, 0f, _aimError) * (aim - me);
        return Vector2.Angle(dir, believed) <= tier.FireConeDeg;
    }
}
