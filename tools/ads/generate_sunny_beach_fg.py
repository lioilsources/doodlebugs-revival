#!/usr/bin/env python3
"""Builds a foreground strip for the Sunny_beach map from advertising props.

The beach background has no authored foreground, so it never had a
destructible layer — and therefore no ads. This generates one: rolling sand
dunes with a SEAGULL ICE CREAM billboard and giant props (cola can, ice-cream
tub) planted in the sand. Naming follows the map pipeline convention
(Sprites/Foreground/<Background>_fg.png), so Doodlebugs -> Sync Background
Profiles wires it to Profile_Sunny_beach automatically.

Usage:
  python3 tools/ads/generate_sunny_beach_fg.py           # preview to out/
  python3 tools/ads/generate_sunny_beach_fg.py --apply   # write Assets strip + base/
"""
import argparse
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw

import generate_ad_signs as gas

ROOT = Path(__file__).resolve().parents[2]
FG_PATH = ROOT / "Assets/Doodlebugs/Sprites/Foreground/Sunny_beach_fg.png"
ADS_DIR = Path(__file__).parent / "sprites"
BASE_PATH = Path(__file__).parent / "base/Sunny_beach_fg.png"
OUT_DIR = Path(__file__).parent / "out"

STRIP_W, STRIP_H = 4096, 700
SAND, SAND_DARK, SAND_LIGHT = "#D9BC85", "#B99C63", "#E8D3A4"


PROM_X0, PROM_X1, PROM_H = 2400, 3950, 190   # flat promenade for the rink boards


def dune_height(x):
    """Rolling dune profile, seam-safe: built from whole sine periods so the
    height at x=0 equals the height at x=STRIP_W and the wrap never shows.
    The right side blends into a flat promenade — real beaches have one, and
    the perimeter boards need level ground to stand on."""
    t = x / STRIP_W * 2 * math.pi
    base = (170
            + 55 * math.sin(t * 3)
            + 28 * math.sin(t * 7 + 1.3)
            + 8 * math.sin(t * 13 + 0.4))
    if PROM_X0 <= x <= PROM_X1:
        k = min((x - PROM_X0) / 120, (PROM_X1 - x) / 120, 1.0)
        s = k * k * (3 - 2 * k)                 # smoothstep edge ramps
        return base * (1 - s) + PROM_H * s
    return base


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    args = ap.parse_args()

    rng = random.Random(700)
    strip = Image.new("RGBA", (STRIP_W, STRIP_H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(strip)

    tops = []
    for x in range(STRIP_W):
        h = dune_height(x)
        y = STRIP_H - int(h)
        tops.append(y)
        draw.line([(x, STRIP_H), (x, y)], fill=SAND)
        draw.point((x, y), fill=SAND_DARK)
        if x % 7 == 0 and rng.random() < 0.5:          # sand sparkle
            draw.point((x, min(STRIP_H - 1, y + rng.randint(4, 60))), fill=SAND_LIGHT)

    def ground(x0, w):
        return min(tops[x0:x0 + w])

    # SEAGULL ICE CREAM billboard on posts, planted on a dune crest.
    spec = next(s for s in gas.BRANDS["signs"] if s["id"] == "seagull_ice")
    sign = gas.draw_sign(spec, rng)
    sx = 1650
    g = ground(sx, sign.width + 40)
    sy = g - 70 - sign.height
    for px in (sx + 10, sx + sign.width - 24):
        draw.rectangle([px, sy + sign.height - 10, px + 14, ground(px, 14) + 40], fill="#4A3826")
    strip.alpha_composite(sign.rotate(rng.uniform(-2, 2), expand=True, resample=Image.BICUBIC), (sx, sy))

    # Giant props in the sand.
    # Props keep to the dune half; the promenade belongs to the boards.
    for prop_file, px in [("prop_can_doodle_cola.png", 420),
                          ("prop_jar_seagull_ice.png", 1080),
                          ("prop_bottle_sierra_sarsaparilla.png", 2150)]:
        img = Image.open(ADS_DIR / prop_file).convert("RGBA")
        tilted = img.rotate(rng.uniform(-4, 4), expand=True, resample=Image.BICUBIC)
        g = ground(px, tilted.width)
        strip.alpha_composite(tilted, (px, g - tilted.height + 18))

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    preview = Image.new("RGBA", (STRIP_W // 2, STRIP_H // 2), (140, 190, 225, 255))
    small = strip.resize((STRIP_W // 2, STRIP_H // 2), Image.LANCZOS)
    preview.alpha_composite(small)
    preview.save(OUT_DIR / "preview_sunny_beach_fg.png")
    print(f"preview -> {OUT_DIR / 'preview_sunny_beach_fg.png'}")

    if args.apply:
        strip.save(FG_PATH)
        BASE_PATH.parent.mkdir(parents=True, exist_ok=True)
        strip.save(BASE_PATH)
        print(f"applied -> {FG_PATH}")
        print("In Unity run: Doodlebugs -> Sync Background Profiles")
    else:
        print("(preview only — run with --apply to write the Assets strip)")


if __name__ == "__main__":
    main()
