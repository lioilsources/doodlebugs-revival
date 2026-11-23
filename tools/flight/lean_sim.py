#!/usr/bin/env python3
"""LEAN meter simulator (Prompts/32-CLAUDE-PLAN-lean-hybrid.md).

Offline mirror of `Scripts/Flight/LeanModel.cs`: the same three functions,
the same numbers as `FlightPreset.Lean*`. Unity does not run on the dev Mac,
so this is where a retune is checked before it reaches a device.

The meter integrates how far the phone was ROLLED (around the axis that
comes out of the screen) and leaks it away:

    swing += rate * dt          deg, + = clockwise as the player sees it
    swing *= exp(-dt / tau)     a pose that stops moving fades to nothing
    swing  = clamp(swing, -cap, +cap)
    lean   = curve(|swing|, dead, full)      0..1, keeps the sign of swing

and the plane turns up to `bonus` faster while the stick already turns the
same way. Motion is rewarded, a pose is not - that is the whole design.

  python3 tools/flight/lean_sim.py            # table for the compiled numbers
  python3 tools/flight/lean_sim.py --tau 1.5  # try a retune
  python3 tools/flight/lean_sim.py --check    # exit 1 if a design rule breaks
"""
import argparse
import math
import sys

DT = 1 / 60
DEFAULTS = dict(tau=2.0, dead=6.0, full=24.0, cap=36.0, bonus=0.15)   # ARCADE
ARCADE = dict(turn_rate=210.0, corner_speed=6.0)


def step(swing, rate, dt, tau, cap):
    swing = (swing + rate * dt) * math.exp(-dt / tau)
    return max(-cap, min(cap, swing))


def lean01(swing, dead, full):
    a = abs(swing)
    if a <= dead:
        return 0.0
    return math.copysign(min(1.0, (a - dead) / (full - dead)), swing)


def multiplier(lean, x, bonus):
    """Turn-rate multiplier for stick input x (-1..1, + = clockwise)."""
    if x == 0:
        return 1.0
    return 1.0 + bonus * max(0.0, lean * math.copysign(1.0, x)) * min(1.0, abs(x))


def run(rate_fn, stick_fn, seconds, p):
    """Average usable lean while the stick turns, and seconds spent at full lean."""
    swing, acc, n, full_t, t = 0.0, 0.0, 0, 0.0, 0.0
    while t < seconds:
        swing = step(swing, rate_fn(t), DT, p["tau"], p["cap"])
        x = stick_fn(t)
        if x:
            usable = max(0.0, lean01(swing, p["dead"], p["full"]) * x)
            acc += usable
            n += 1
            full_t += DT if usable >= 0.999 else 0.0
        t += DT
    return acc / max(1, n), full_t


# --- hand motions (deg/s, + = clockwise) ------------------------------------
def swing_once(theta, dur):
    return lambda t: theta / dur if t < dur else 0.0


def rock(theta, hz):
    return lambda t: theta * 2 * math.pi * hz * math.cos(2 * math.pi * hz * t)


def ratchet(theta, fast, slow):
    return lambda t: theta / fast if (t % (fast + slow)) < fast else -theta / slow


CW = lambda t: 1.0                                               # noqa: E731
SCISSORS = lambda hz: (lambda t: 1.0 if math.sin(2 * math.pi * hz * t) >= 0 else -1.0)   # noqa: E731

HALF_TURN = 180.0 / ARCADE["turn_rate"]                          # 0.86 s

CASES = [
    # name, phone motion, stick, seconds, what it stands for
    ("one 30 deg swing into a half-turn", swing_once(30, .2), CW, HALF_TURN, "the basic move"),
    ("one 40 deg swing into a half-turn", swing_once(40, .2), CW, HALF_TURN, "a big move"),
    ("rocking +-30 deg with every reversal", rock(30, .5), SCISSORS(.5), 20, "scissors: the dance"),
    ("phone held tilted 25 deg, turn 3 s later", lambda t: 25 / .3 if t < .3 else 0.0,
     lambda t: 1.0 if t >= 3 else 0.0, 8, "a pose must earn nothing"),
    ("ratchet: 30 deg in 0.15 s, back in 2 s", ratchet(30, .15, 2.0), CW, 20, "pumping in one long turn"),
    ("steady roll 20 deg/s", lambda t: 20.0, CW, 20, "winding the phone / a swivel chair"),
    ("gyro bias 1 deg/s", lambda t: 1.0, CW, 20, "sensor drift must earn nothing"),
    ("walking jitter +-4 deg at 2 Hz", rock(4, 2.0), CW, 20, "noise must earn nothing"),
]

# Design rules a retune has to keep (case index, comparison, bound).
RULES = [
    (0, ">=", 0.80, "one swing has to carry a whole half-turn"),
    (2, ">=", 0.50, "rocking with the reversals is the rewarded play"),
    (3, "<=", 0.05, "a static pose earns nothing"),
    (4, "<=", 0.20, "pumping in a one-way turn stays a small edge"),
    (6, "<=", 0.01, "gyro bias earns nothing"),
    (7, "<=", 0.01, "hand jitter earns nothing"),
]


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    for k, v in DEFAULTS.items():
        ap.add_argument(f"--{k}", type=float, default=v)
    ap.add_argument("--check", action="store_true")
    a = ap.parse_args()
    p = {k: getattr(a, k) for k in DEFAULTS}

    print("tau {tau} s, dead {dead} deg, full {full} deg, cap {cap} deg, bonus +{pct:.0f} %".format(pct=p["bonus"] * 100, **p))
    results = []
    for name, rate, stick, seconds, why in CASES:
        avg, full_t = run(rate, stick, seconds, p)
        results.append(avg)
        print(f"  {avg:4.2f} avg lean  {full_t:5.2f} s full   {name}  ({why})")

    w, v = ARCADE["turn_rate"], ARCADE["corner_speed"]
    w2 = w * (1 + p["bonus"])
    print(f"at full lean: {w:.0f} -> {w2:.0f} deg/s, radius {v / math.radians(w):.2f} -> {v / math.radians(w2):.2f} u, "
          f"half-turn {180 / w:.2f} -> {180 / w2:.2f} s, "
          f"a full-lean pilot gains 180 deg on an equal one in {180 / (w * p['bonus']):.1f} s")
    print(f"sustained lean needs a steady roll of {p['dead'] / p['tau']:.0f} deg/s (any) .. {p['full'] / p['tau']:.0f} deg/s (full)")

    failed = 0
    for i, op, bound, why in RULES:
        ok = results[i] >= bound if op == ">=" else results[i] <= bound
        failed += not ok
        if not ok or a.check:
            print(f"  {'ok  ' if ok else 'FAIL'} {results[i]:.2f} {op} {bound}: {why}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
