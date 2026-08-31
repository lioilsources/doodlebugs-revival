#!/usr/bin/env python3
"""Generates standalone advertising foreground strips — towers of billboards.

Not tied to any background: one pool of these rotates independently of the
map, so a new background needs no foreground authored for it.

Each strip is a skyline of advertising towers of varying height and width,
stacked from panels of mixed formats — painted boards, lit neon signs and the
SDXL print posters. The gaps between towers are the point: planes render
behind the foreground, so a tall irregular skyline gives them somewhere to
hide, and since bullets DO collide with it the towers double as cover.

Seam-safe by construction: the layout always opens and closes on a gap, the
ground plinth is a constant height, so the last column meets the first with
nothing to notice while the strip scrolls.

Profiles differ in density and height, so the rotation changes how the arena
plays, not just how it looks:
  city      dense tall towers, narrow canyons — the concrete jungle
  broadway  tall and lit, wide-ish gaps
  suburb    low and open, plenty of sky
  strip     mostly roadside boards with the occasional tower

Output: Assets/Doodlebugs/Sprites/Foreground/AdStrip_<profile>_<n>.png

Usage:
  python3 tools/ads/generate_ad_strips.py            # preview into out/
  python3 tools/ads/generate_ad_strips.py --apply    # write into Assets/
"""
import argparse
import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw

import generate_ad_signs as gas

HERE = Path(__file__).parent
ROOT = HERE.parents[1]
FG_DIR = ROOT / "Assets/Doodlebugs/Sprites/Foreground"
OUT_DIR = HERE / "out"
BRANDS = json.loads((HERE / "brands.json").read_text())

STRIP_W = 4096
GROUND_H = 150
ASPHALT, ASPHALT_DARK, KERB = "#3A3630", "#2A2721", "#6E675C"
FRAME, FRAME_LIT = "#2A2721", "#3E392F"

# tower_w   candidate tower widths
# floors    (min, max) panels stacked per tower
# gap       (min, max) px of sky between towers
# styles    weighted panel styles
PROFILES = {
    "city":     dict(tower_w=(320, 448, 576), floors=(1, 4), gap=(70, 420),
                     styles=["board", "poster", "poster", "neon"], seeds=(101, 202)),
    "broadway": dict(tower_w=(448, 576), floors=(1, 3), gap=(110, 480),
                     styles=["neon", "neon", "poster"], seeds=(303,)),
    "suburb":   dict(tower_w=(384, 512), floors=(1, 2), gap=(220, 640),
                     styles=["board", "poster"], seeds=(404,)),
    "strip":    dict(tower_w=(512, 640), floors=(1, 2), gap=(180, 520),
                     styles=["board", "board", "poster"], seeds=(505, 606)),
}
FLOOR_H = (150, 300)
POSTER_ASPECT = 280 / 170        # the print creative's shape


def floor_height(tower_w, rng):
    """Floor heights cluster near the poster aspect so letterboxing stays
    slim, with enough spread that towers do not look like a regular grid."""
    ideal = tower_w / POSTER_ASPECT
    h = int(rng.gauss(ideal, ideal * 0.16))
    return max(FLOOR_H[0], min(FLOOR_H[1], h))


def draw_ground(draw, rng, top_y):
    """Continuous paved plinth: constant height, so the wrap seam cannot show."""
    draw.rectangle([0, top_y, STRIP_W, top_y + GROUND_H], fill=ASPHALT)
    draw.rectangle([0, top_y, STRIP_W, top_y + 12], fill=KERB)
    draw.rectangle([0, top_y + 12, STRIP_W, top_y + 19], fill=ASPHALT_DARK)
    for _ in range(STRIP_W * GROUND_H // 900):
        draw.point((rng.randrange(STRIP_W), rng.randrange(top_y + 22, top_y + GROUND_H)),
                   fill=ASPHALT_DARK if rng.random() < 0.6 else KERB)
    # Shading only — the silhouette stays flat so terrain collisions behave
    # identically on every strip.
    for x in range(STRIP_W):
        t = x / STRIP_W * 2 * math.pi
        draw.line([(x, top_y + 19),
                   (x, top_y + 19 + int(5 + 4 * math.sin(t * 8) + 3 * math.sin(t * 21 + 1.1)))],
                  fill=ASPHALT_DARK)


def plan(cfg, rng):
    """Lay towers out left to right. Always opens and closes on a gap so the
    strip wraps cleanly; the final gap absorbs the rounding."""
    towers, x = [], rng.randint(*cfg["gap"])
    while True:
        w = rng.choice(cfg["tower_w"])
        # A skyline breathes when the rhythm breaks: every few towers either
        # a huddled pair (almost touching) or a wide-open block of sky.
        r = rng.random()
        if r < 0.22:
            gap = rng.randint(24, 60)                       # huddle
        elif r < 0.40:
            gap = int(rng.randint(*cfg["gap"]) * 1.8)       # open sky
        else:
            gap = rng.randint(*cfg["gap"])
        if x + w + gap > STRIP_W:
            break
        floors = [floor_height(w, rng) for _ in range(rng.randint(*cfg["floors"]))]
        towers.append((x, w, floors))
        x += w
        # An open block of sky still gets an ad: a knee-high roadside board
        # in the middle of the gap. Billboards everywhere — just not tall ones.
        if gap > 420:
            mw = rng.choice((256, 288, 320))
            towers.append((x + (gap - mw) // 2, mw, [rng.randint(140, 180)]))
        x += gap
    return towers


def draw_tower(img, d, x, w, floors, ground_y, pool, cfg, rng, idx):
    """Stack of panels on a steel frame, standing on the plinth."""
    total = sum(floors) + 14 * (len(floors) - 1)
    top = ground_y - total

    # Frame first so panels sit inside it; legs run into the plinth.
    d.rectangle([x - 12, top - 14, x + w + 12, ground_y + 24], fill=FRAME)
    for lx in (x - 6, x + w // 2 - 5, x + w - 4):
        d.rectangle([lx, top, lx + 10, ground_y + 30], fill=FRAME_LIT)
    # Cross bracing in the gap below the lowest panel, if there is one.
    if top > ground_y - 420:
        for a, b in ((x - 6, x + w), (x + w, x - 6)):
            d.line([(a, ground_y + 20), (b, ground_y - 40)], fill=FRAME_LIT, width=7)

    y = top
    for i, fh in enumerate(floors):
        spec = pool[(idx + i) % len(pool)]
        style = rng.choice(cfg["styles"])
        variant = "w" if (idx + i) % 2 == 0 else "c"
        panel = gas.draw_panel(spec, variant, w, fh, style)
        img.alpha_composite(panel, (x, y))
        y += fh
        if i < len(floors) - 1:
            d.rectangle([x - 8, y, x + w + 8, y + 14], fill=FRAME)   # floor beam
            y += 14
    # Roof cap and a beacon, so the skyline reads as built structure.
    d.rectangle([x - 16, top - 20, x + w + 16, top - 8], fill=FRAME_LIT)
    d.ellipse([x + w // 2 - 6, top - 32, x + w // 2 + 6, top - 20], fill="#C4452F")


FLOWER_PALETTES = [("#D8433C", "#F2B23A"), ("#E8B4C8", "#F2E23A"),
                   ("#F2F2F2", "#F2B23A"), ("#B44CC8", "#F2E23A")]


def draw_flower(d, x, base_y, rng):
    """One chunky 8-bit flower: stem, two leaves, cross of petals, centre."""
    h = rng.randint(48, 92)
    petal, centre = rng.choice(FLOWER_PALETTES)
    d.rectangle([x - 3, base_y - h, x + 3, base_y], fill="#3E7A34")
    d.rectangle([x - 14, base_y - h // 2, x - 3, base_y - h // 2 + 8], fill="#4E9440")
    d.rectangle([x + 3, base_y - h // 3, x + 14, base_y - h // 3 + 8], fill="#4E9440")
    r = rng.randint(12, 19)
    cx, cy = x, base_y - h
    for dx, dy in ((-r, 0), (r, 0), (0, -r), (0, r)):
        d.rectangle([cx + dx - r + 2, cy + dy - r + 2,
                     cx + dx + r - 2, cy + dy + r - 2], fill=petal)
    d.rectangle([cx - r + 3, cy - r + 3, cx + r - 3, cy + r - 3], fill=centre)


def dress_street(img, d, towers, ground_y, rng):
    """Flower beds along the plinth. Motorcars now live INSIDE the billboards
    as Matchbox-style print creatives, not parked on the kerb."""
    spans = []
    occupied = sorted((x, x + w) for x, w, _ in towers)
    cur = 0
    for a, b in occupied:
        if a - cur > 140:
            spans.append((cur, a))
        cur = b
    if STRIP_W - cur > 140:
        spans.append((cur, STRIP_W))

    for a, b in spans:
        width = b - a
        # Flower bed: a loose row filling part of the gap.
        if rng.random() < 0.9:
            n = max(3, min(10, width // 70))
            for i in range(n):
                fx = a + 30 + int((width - 60) * (i + rng.uniform(0.1, 0.9)) / n)
                draw_flower(d, fx, ground_y + rng.randint(6, 14), rng)


def build(profile, seed):
    cfg = PROFILES[profile]
    rng = random.Random(seed)
    towers = plan(cfg, rng)
    tallest = max(sum(f) + 14 * (len(f) - 1) for _, _, f in towers)
    strip_h = tallest + 46 + GROUND_H          # 46 = frame cap + beacon headroom

    img = Image.new("RGBA", (STRIP_W, strip_h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ground_y = strip_h - GROUND_H
    draw_ground(d, rng, ground_y)

    pool = list(BRANDS["signs"])
    rng.shuffle(pool)
    idx = 0
    for x, w, floors in towers:
        draw_tower(img, d, x, w, floors, ground_y, pool, cfg, rng, idx)
        idx += len(floors)
    dress_street(img, d, towers, ground_y, rng)
    return img, len(towers), tallest


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="write into Assets/")
    args = ap.parse_args()
    OUT_DIR.mkdir(exist_ok=True)

    for profile, cfg in PROFILES.items():
        for n, seed in enumerate(cfg["seeds"], start=1):
            name = f"AdStrip_{profile}_{n}"
            img, towers, tallest = build(profile, seed)
            img.save((FG_DIR if args.apply else OUT_DIR) / f"{name}.png")
            print(f"{name}  {img.size[0]}x{img.size[1]}  "
                  f"{towers} towers, tallest {tallest}px ({tallest/100:.1f} units)")
            prev = Image.new("RGBA", (1900, int(1900 / STRIP_W * img.height)),
                             (140, 190, 225, 255))
            prev.alpha_composite(img.resize(prev.size, Image.LANCZOS))
            prev.save(OUT_DIR / f"preview_{name}.png")

    if args.apply:
        print("\nIn Unity: assign these to BackgroundManager.adStrips, then play.")


if __name__ == "__main__":
    main()
