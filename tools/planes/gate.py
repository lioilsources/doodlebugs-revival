#!/usr/bin/env python3
"""Envelope contract for plane models - the fairness gate.

PlaneHolder's hitbox is one shared BoxCollider2D: a 50x50 px square in the
middle of the 128x128 sprite (px box (39,39)-(88,88)), not derived from the
sprite. So a new silhouette is fair as long as (a) that box always sits on
solid body and (b) the model neither looks like a much bigger nor a much
smaller target than the original. Those two ideas are the seven gates below;
Prompts/23-CLAUDE-PLAN-plane-shapes.md section 1 is the rationale.

Reference = BiPlane1.png: bbox 110x55, fill 0.54, core coverage 0.66, mass
centroid (64.4, 59.3), nose at x=116, tail at x=7. The plan's first draft
asked for 95 % core coverage - the reference itself only manages 66 % (the
box spans the full height of the plane, wings and gaps included), so the
bar is "at least about as solid under the box as the original", 0.55.

Mirrored in Assets/Doodlebugs/Editor/PlaneModelValidator.cs (Doodlebugs ->
Validate Plane Models) - change both or neither. Pure Pillow, no numpy.

    python3 tools/planes/gate.py Assets/Doodlebugs/Sprites/BiPlane/BiPlane1.png out/models/*.png
"""
import sys
from pathlib import Path

from PIL import Image

SIZE = 128
# The hitbox is 50x30 px of the sprite (PlaneHolder's BoxCollider2D, size
# 0.5 x 0.3 world units against a 1.28 wu sprite), centred. It was square
# until 2026-09-04; flattening it kept the box identical for everyone - so
# fairness is untouched - while admitting the whole family of slim aircraft
# (bombers, monoplane fighters, manta, dragonfly) whose honest side view is
# 110x17..41 and could never fill a square.
CORE_X = (39, 88)             # inclusive px range of the hitbox footprint
CORE_Y = (49, 78)
# 0.70, not the 0.75 the shipped set would allow: those are all Kontext
# redesigns of one biplane and share its solid fuselage (0.83-1.00). Designs
# built around floats, struts and rotors sit lower by nature. Below 0.70 more
# than a third of the box is gap and a bullet through the hole still scores,
# which is the "invisible hitbox" the gate exists to prevent.
CORE_MIN_COVERAGE = 0.70      # G1 (BiPlane1: 0.89)
WIDTH = (96, 118)             # G2 - alpha bbox width band
HEIGHT = (26, 72)             #      height band - slim monoplanes in, blimps out
# Upper bound relaxed 0.66 -> 0.72: a chunky solid hull (gunship, 0.69) is not
# unfair, it just looks stocky. The bound that matters is the lower one, which
# keeps wispy outlines from reading as smaller targets than they are.
FILL = (0.42, 0.72)           # G3 - opaque / bbox area
CENTROID_TOL = 8              # G4 - mass centroid within +-8 px of (64,64) (BiPlane1 sits 4.7 px high)
NOSE = (108, 122)             # G5 - rightmost opaque column
TAIL = (4, 18)                #      leftmost opaque column
MARGIN = 3                    # G6 - outer ring that must stay empty (rotation clipping)
LIVERY_MIN = 0.35             # G7 - red livery share of the body (skins need paint to land on)

GATES = ("G1core", "G2extent", "G3mass", "G4centre", "G5nose", "G6margin", "G7livery")


def is_livery_red(p):
    """A red pixel, tolerant of the tinted and DARK reds a quantised AI
    render produces - BiPlane1's own (v,0,0) shading (v >= 141) passes
    trivially. The floor sits at 70 so the render's dark-red panel shading
    lands in the paint mask (it was showing through every skin as thin red
    lines at the first cut of 100) while near-black outlines stay fixed."""
    r, g, b = p[:3]
    gb = max(g, b)
    return r >= 70 and r > 1.8 * gb and r - gb >= 35


def measure(im):
    """Metrics of a 128x128 RGBA sprite in image space (y=0 at the top)."""
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()
    min_x, max_x, min_y, max_y = w, -1, h, -1
    sum_x = sum_y = 0
    opaque = core = red = 0
    margin_hit = False
    cx0, cx1 = CORE_X
    cy0, cy1 = CORE_Y
    for y in range(h):
        for x in range(w):
            p = px[x, y]
            if p[3] < 128:
                continue
            opaque += 1
            sum_x += x
            sum_y += y
            if x < min_x: min_x = x
            if x > max_x: max_x = x
            if y < min_y: min_y = y
            if y > max_y: max_y = y
            if cx0 <= x <= cx1 and cy0 <= y <= cy1:
                core += 1
            if x < MARGIN or x >= w - MARGIN or y < MARGIN or y >= h - MARGIN:
                margin_hit = True
            if is_livery_red(p):
                red += 1
    if opaque == 0:
        return dict(size=(w, h), w=0, h=0, fill=0.0, core=0.0, cx=0.0, cy=0.0,
                    nose=-1, tail=-1, margin_hit=margin_hit, red=0.0, opaque=0)
    bw, bh = max_x - min_x + 1, max_y - min_y + 1
    return dict(
        size=(w, h), w=bw, h=bh,
        fill=opaque / (bw * bh),
        core=core / ((cx1 - cx0 + 1) * (cy1 - cy0 + 1)),
        cx=sum_x / opaque, cy=sum_y / opaque,
        nose=max_x, tail=min_x,
        margin_hit=margin_hit,
        red=red / opaque,
        opaque=opaque,
    )


def check(m):
    """[(gate, ok, detail)] for a measure() dict."""
    return [
        ("G1core", m["core"] >= CORE_MIN_COVERAGE, f"{m['core']:.2f} >= {CORE_MIN_COVERAGE}"),
        ("G2extent", WIDTH[0] <= m["w"] <= WIDTH[1] and HEIGHT[0] <= m["h"] <= HEIGHT[1],
         f"{m['w']}x{m['h']} in {WIDTH}x{HEIGHT}"),
        ("G3mass", FILL[0] <= m["fill"] <= FILL[1], f"{m['fill']:.2f} in {FILL}"),
        ("G4centre", abs(m["cx"] - 64) <= CENTROID_TOL and abs(m["cy"] - 64) <= CENTROID_TOL,
         f"({m['cx']:.1f},{m['cy']:.1f}) +-{CENTROID_TOL} of (64,64)"),
        ("G5nose", NOSE[0] <= m["nose"] <= NOSE[1] and TAIL[0] <= m["tail"] <= TAIL[1],
         f"nose {m['nose']} in {NOSE}, tail {m['tail']} in {TAIL}"),
        ("G6margin", not m["margin_hit"], f"outer {MARGIN} px ring empty"),
        ("G7livery", m["red"] >= LIVERY_MIN, f"{m['red']:.2f} >= {LIVERY_MIN}"),
    ]


def run(path):
    m = measure(Image.open(path))
    if m["size"] != (SIZE, SIZE):
        m["size_ok"] = False
    checks = check(m)
    ok = m["size"] == (SIZE, SIZE) and all(c[1] for c in checks)
    return m, checks, ok


def fmt_row(name, m, checks, ok):
    fails = " ".join(g for g, good, _ in checks if not good)
    if m["size"] != (SIZE, SIZE):
        fails = f"size{m['size'][0]}x{m['size'][1]} " + fails
    return (f"{name:28s} {m['w']:3d}x{m['h']:<3d} fill {m['fill']:.2f} core {m['core']:.2f} "
            f"c ({m['cx']:5.1f},{m['cy']:5.1f}) nose {m['nose']:3d} tail {m['tail']:2d} "
            f"red {m['red']:.2f}  {'PASS' if ok else 'FAIL ' + fails}")


def main(argv):
    if not argv:
        sys.exit(__doc__)
    bad = 0
    for arg in argv:
        p = Path(arg)
        m, checks, ok = run(p)
        print(fmt_row(p.stem, m, checks, ok))
        bad += not ok
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main(sys.argv[1:])
