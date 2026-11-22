using UnityEngine;

/// <summary>
/// LEAN meter maths (Prompts/32 §4.1) as pure functions - a 1:1 port of
/// <c>step</c> / <c>lean01</c> / <c>multiplier</c> in
/// <c>tools/flight/lean_sim.py</c>. Any change here goes through
/// <c>python3 tools/flight/lean_sim.py --check</c> first.
///
/// The meter integrates how far the phone was ROLLED (around the axis that
/// comes out of the screen) and leaks it away, so motion is rewarded and a
/// static pose is not. Sign convention everywhere: + = clockwise as the
/// player sees the screen = the same sign as the rotation input x and
/// PlayerController's _omega.
/// </summary>
public static class LeanModel
{
    /// <summary>Combined snap x lean multiplier cap (Prompts/32 D3).</summary>
    public const float MaxTurnMultiplier = 1.8f;

    /// <summary>One meter step. swing in degrees, rateDeg in deg/s.</summary>
    public static float Step(float swing, float rateDeg, float dt, FlightPreset p)
    {
        float tau = Mathf.Max(0.01f, p.LeanTau);
        swing = (swing + rateDeg * dt) * Mathf.Exp(-dt / tau);
        return Mathf.Clamp(swing, -p.LeanCapDeg, p.LeanCapDeg);
    }

    /// <summary>Meter value, -1..1, keeps the sign of the swing.</summary>
    public static float Lean01(float swing, FlightPreset p)
    {
        float a = Mathf.Abs(swing);
        if (a <= p.LeanDeadDeg) return 0f;
        float span = Mathf.Max(0.01f, p.LeanFullDeg - p.LeanDeadDeg);
        return Mathf.Sign(swing) * Mathf.Min(1f, (a - p.LeanDeadDeg) / span);
    }

    /// <summary>The part of the meter that counts for stick input x: only in
    /// the stick's direction, scaled by how far it is pushed. 0..1.</summary>
    public static float Usable(float lean, float x)
    {
        if (x == 0f) return 0f;
        return Mathf.Max(0f, lean * Mathf.Sign(x)) * Mathf.Min(1f, Mathf.Abs(x));
    }

    /// <summary>Turn-rate multiplier for stick input x (-1..1, + = clockwise).
    /// 1 without stick - the phone alone never turns the plane.</summary>
    public static float Multiplier(FlightPreset p, float lean, float x)
    {
        if (x == 0f) return 1f;
        return 1f + p.LeanBonus * Usable(lean, x);
    }
}
