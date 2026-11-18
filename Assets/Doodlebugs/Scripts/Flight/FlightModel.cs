using System.Text;
using UnityEngine;

/// <summary>
/// Flight model v2 maths (Prompts/28 §3) as pure functions, so the numbers
/// can be checked without a scene. PlayerController owns the state and calls
/// these every FixedUpdate.
/// </summary>
public static class FlightModel
{
    /// <summary>Fraction of the peak turn rate available at a speed: 1 at
    /// corner speed, falling toward EnvAtMin / EnvAtMax at the extremes.</summary>
    public static float Envelope(FlightPreset p, float v)
    {
        if (v <= p.CornerSpeed)
            return Mathf.Lerp(p.EnvAtMin, 1f, Mathf.InverseLerp(p.MinSpeed, p.CornerSpeed, v));
        return Mathf.Lerp(1f, p.EnvAtMax, Mathf.InverseLerp(p.CornerSpeed, p.MaxSpeed, v));
    }

    /// <summary>Peak turn rate at a speed, deg/s.</summary>
    public static float TurnRateAt(FlightPreset p, float v, float handling = 1f) =>
        p.TurnRate * Envelope(p, v) * handling;

    /// <summary>Turn radius at full stick, world units.</summary>
    public static float TurnRadius(FlightPreset p, float v, float handling = 1f)
    {
        float omegaRad = Mathf.Max(1f, TurnRateAt(p, v, handling)) * Mathf.Deg2Rad;
        return v / omegaRad;
    }

    /// <summary>First-order response of the turn rate toward its target.</summary>
    public static float StepOmega(FlightPreset p, float omega, float target, float dt)
    {
        float tau = Mathf.Abs(target) > Mathf.Abs(omega) ? p.TurnAttack : p.TurnRelease;
        if (tau <= 0.0001f) return target;
        return Mathf.Lerp(omega, target, 1f - Mathf.Exp(-dt / tau));
    }

    /// <summary>Speed bled by a hard turn above corner speed (never below it).</summary>
    public static float TurnBleed(FlightPreset p, float speed, float omega, float handling, float dt)
    {
        float over = speed - p.CornerSpeed;
        if (over <= 0f) return speed;
        float k = Mathf.Clamp01(Mathf.Abs(omega) / Mathf.Max(1f, TurnRateAt(p, speed, handling)));
        return Mathf.Max(p.CornerSpeed, speed - p.TurnDrag * over * k * k * dt);
    }

    /// <summary>The check table of §3 for a preset - logged once at boot.</summary>
    public static string Describe(FlightPreset p)
    {
        var sb = new StringBuilder($"[Flight] preset {p.Name}: v | deg/s | radius | 180 | loop");
        foreach (float v in new[] { p.MinSpeed, p.CornerSpeed, p.CruiseSpeed, p.MaxSpeed })
        {
            float w = TurnRateAt(p, v);
            sb.Append($"\n  {v,5:0.0} | {w,5:0} | {TurnRadius(p, v),5:0.00} | {180f / w,4:0.00}s | {360f / w,4:0.00}s");
        }
        return sb.ToString();
    }
}
