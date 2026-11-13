using UnityEngine;

/// <summary>
/// What a combat bot knows about the sky (Prompts/27 WP4): targets, threats,
/// intercept points and crates, read from <see cref="PlayerController.All"/>
/// and <see cref="PowerUp.Active"/> - no scene scans, no allocations per tick.
///
/// Planes wrap left/right (PlayerController.OnTriggerEnter2D), bullets do
/// not. So there are two distances: <see cref="WrapDelta"/> is how far a
/// plane really is (the bot may chase through the wrap), the raw delta is
/// what a bullet would have to cross (<see cref="IsDirect"/> - the bot only
/// fires on that geometry).
/// </summary>
public static class BotSenses
{
    /// <summary>Distance between the two wrap edges; 0 when the arena has no edges yet.</summary>
    public static float ArenaWidth
    {
        get
        {
            var left = ArenaBounds.Left;
            var right = ArenaBounds.Right;
            if (left == null || right == null) return 0f;
            return Mathf.Max(0f, right.bounds.min.x - left.bounds.max.x);
        }
    }

    /// <summary>Shortest vector from `from` to `to`, counting the horizontal wrap.</summary>
    public static Vector2 WrapDelta(Vector2 from, Vector2 to, float width)
    {
        Vector2 d = to - from;
        if (width > 0f && Mathf.Abs(d.x) > width * 0.5f) d.x -= Mathf.Sign(d.x) * width;
        return d;
    }

    /// <summary>True when the straight line is also the shortest way - a bullet can get there.</summary>
    public static bool IsDirect(Vector2 from, Vector2 to, float width) =>
        width <= 0f || Mathf.Abs(to.x - from.x) <= width * 0.5f;

    /// <summary>Can be shot at or chased: in the sky, not in the hangar, not the warm-up bot.</summary>
    public static bool IsFlying(PlayerController p) =>
        p != null && p.IsSpawned && !p.InHangar && !p.IsWarmupBot;

    public static bool IsInvulnerable(PlayerController p) =>
        p.PlaneStats != null && p.PlaneStats.IsInvulnerable;

    /// <summary>World velocity from synced state (remote planes' rigidbodies
    /// are kinematic on the host, so their linearVelocity reads zero).</summary>
    public static Vector2 VelocityOf(PlayerController p) => (Vector2)p.transform.right * p.Speed;

    /// <summary>Nearest flying, non-invulnerable plane that is not `self`;
    /// keeps `current` unless the new one is at least `switchRatio` closer.</summary>
    public static PlayerController PickTarget(PlayerController self, PlayerController current,
        float width, float switchRatio = 0.7f)
    {
        Vector2 me = self.transform.position;
        PlayerController best = null;
        float bestSq = float.MaxValue;
        foreach (var p in PlayerController.All)
        {
            if (p == self || !IsFlying(p) || IsInvulnerable(p)) continue;
            float sq = WrapDelta(me, p.transform.position, width).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = p; }
        }

        if (current != null && current != best && IsFlying(current) && !IsInvulnerable(current) && best != null)
        {
            float curSq = WrapDelta(me, current.transform.position, width).sqrMagnitude;
            // Hysteresis on distance (squared ratio = ratio^2).
            if (bestSq > curSq * switchRatio * switchRatio) return current;
        }
        return best;
    }

    /// <summary>An enemy within `range` behind `self` with `self` inside its
    /// forward cone - someone lining up a shot on this bot's tail.</summary>
    public static PlayerController FindThreat(PlayerController self, float width, float range, float coneDeg)
    {
        Vector2 me = self.transform.position;
        Vector2 myDir = self.transform.right;
        float rangeSq = range * range;
        foreach (var p in PlayerController.All)
        {
            if (p == self || !IsFlying(p)) continue;
            Vector2 fromThem = WrapDelta(p.transform.position, me, width);
            if (fromThem.sqrMagnitude > rangeSq) continue;
            // Behind me: the threat sits on the far side of my nose.
            if (Vector2.Dot(myDir, -fromThem) > 0f) continue;
            if (Vector2.Angle(p.transform.right, fromThem) <= coneDeg) return p;
        }
        return null;
    }

    /// <summary>One-iteration lead: where the target will be when a bullet
    /// fired now arrives, scaled by `lead` (0 = aim at the plane itself).</summary>
    public static Vector2 InterceptPoint(Vector2 from, PlayerController target, float muzzleSpeed,
        float lead, float width)
    {
        Vector2 delta = WrapDelta(from, target.transform.position, width);
        Vector2 aim = from + delta;
        if (lead <= 0f || muzzleSpeed <= 0.01f) return aim;
        float time = delta.magnitude / muzzleSpeed;
        return aim + VelocityOf(target) * time * lead;
    }

    /// <summary>Units per second a shot leaves the gun at: impulse
    /// baseBulletForce x weapon x maturity + plane speed on a mass-1 bullet
    /// (Shooting.SpawnBullet). Bots skip maturity (the tier owns skill).</summary>
    public static float MuzzleSpeed(Shooting shooting, float planeSpeed)
    {
        if (shooting == null) return 20f + planeSpeed;
        return shooting.baseBulletForce * shooting.CurrentWeapon.ForceMultiplier + planeSpeed;
    }

    /// <summary>Nearest health, shield or repair crate, or null.</summary>
    public static PowerUp NearestHealCrate(Vector2 from, float width)
    {
        PowerUp best = null;
        float bestSq = float.MaxValue;
        foreach (var crate in PowerUp.Active)
        {
            if (crate == null || !crate.IsAvailable) continue;
            var type = crate.Type;
            if (type != PowerUpType.Health && type != PowerUpType.Shield && type != PowerUpType.Repair) continue;
            float sq = WrapDelta(from, crate.transform.position, width).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = crate; }
        }
        return best;
    }

    /// <summary>Nearest flying plane that is not `self` (for disengage).</summary>
    public static PlayerController NearestEnemy(PlayerController self, float width)
    {
        Vector2 me = self.transform.position;
        PlayerController best = null;
        float bestSq = float.MaxValue;
        foreach (var p in PlayerController.All)
        {
            if (p == self || !IsFlying(p)) continue;
            float sq = WrapDelta(me, p.transform.position, width).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = p; }
        }
        return best;
    }
}
