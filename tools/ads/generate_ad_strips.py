#!/usr/bin/env python3
"""Generates standalone advertising foreground strips.

These are not tied to any background: one pool of ad walls rotates
independently of the map, so N backgrounds need zero foreground authoring.
Each strip is a continuous wall of ad panels on a solid plinth — the
hockey-rink / Broadway look — sized so the run tiles across the wrap seam:
4096 / 512 = 8 panels exactly, and the ground is a whole number of sine
periods, so the last column meets the first with no visible join.

Styles:
  rink      low boards, alternating white/brand
  broadway  tall lit signs, marquee bulbs and neon type
  mixed     rink wall with the occasional tall sign breaking the skyline

Output: Assets/Doodlebugs/Sprites/Foreground/AdStrip_<style>_<n>.png
Then in Unity assign them to BackgroundManager.adStrips.

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
ADS_DIR = HERE / "sprites"
OUT_DIR = HERE / "out"
BRANDS = json.loads((HERE / "brands.json").read_text())

STRIP_W = 4096
GROUND_H = {"rink": 190, "broadway": 210, "mixed": 210}
ASPHALT, ASPHALT_DARK, KERB = "#3A3630", "#2A2721", "#6E675C"

# Which strips to build: (style, seed). Rotation picks between them at random.
VARIANTS = [("rink", 101), ("rink", 202), ("broadway", 303),
            ("broadway", 404), ("mixed", 505), ("mixed", 606)]


def draw_ground(draw, rng, top_y, h):
    """Paved strip under the wall: kerb line, asphalt, sparse grit."""
    draw.rectangle([0, top_y, STRIP_W, top_y + h], fill=ASPHALT)
    draw.rectangle([0, top_y, STRIP_W, top_y + 14], fill=KERB)
    draw.rectangle([0, top_y + 14, STRIP_W, top_y + 22], fill=ASPHALT_DARK)
    for _ in range(STRIP_W * h // 900):
        x = rng.randrange(STRIP_W)
        y = rng.randrange(top_y + 26, top_y + h)
        draw.point((x, y), fill=ASPHALT_DARK if rng.random() < 0.6 else KERB)
    # Shallow undulation drawn as shading only — the silhouette stays flat, so
    # collisions are identical everywhere and the seam cannot show.
    for x in range(STRIP_W):
        t = x / STRIP_W * 2 * math.pi
        d = int(6 + 5 * math.sin(t * 8) + 3 * math.sin(t * 21 + 1.1))
        draw.line([(x, top_y + 22), (x, top_y + 22 + d)], fill=ASPHALT_DARK)


def build(style, seed):
    rng = random.Random(seed)
    band_h = gas.BAND_H["broadway" if style == "broadway" else "rink"]
    ground_h = GROUND_H[style]
    tall_h = gas.BAND_H["broadway"]
    # Mixed needs headroom for the tall panels that break the skyline.
    top_pad = (tall_h - band_h) if style == "mixed" else 0
    strip_h = top_pad + band_h + ground_h

    img = Image.new("RGBA", (STRIP_W, strip_h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ground_top = top_pad + band_h
    draw_ground(d, rng, ground_top, ground_h)

    pool = list(BRANDS["signs"])
    rng.shuffle(pool)
    n = STRIP_W // gas.BAND_W
    # Which columns get a tall sign — never two in a row, so the wall reads as
    # a wall with landmarks rather than a sawtooth.
    tall = set()
    if style == "mixed":
        for i in range(n):
            if i not in tall and (i - 1) not in tall and rng.random() < 0.34:
                tall.add(i)

    for i in range(n):
        spec = pool[i % len(pool)]
        variant = "w" if i % 2 == 0 else "c"
        panel_style = "broadway" if (style == "broadway" or i in tall) else "rink"
        panel = Image.open(
            ADS_DIR / f"band_{panel_style}_{spec['id']}_{variant}.png").convert("RGBA")
        img.alpha_composite(panel, (i * gas.BAND_W, ground_top - panel.height))
        # Support posts behind the taller panels, so they read as mounted.
        if panel_style == "broadway" and style == "mixed":
            for px in (i * gas.BAND_W + 60, (i + 1) * gas.BAND_W - 74):
                d.rectangle([px, ground_top - 40, px + 14, ground_top + 10], fill="#2E2A24")
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="write into Assets/")
    args = ap.parse_args()
    OUT_DIR.mkdir(exist_ok=True)

    counts = {}
    for style, seed in VARIANTS:
        counts[style] = counts.get(style, 0) + 1
        name = f"AdStrip_{style}_{counts[style]}"
        img = build(style, seed)
        target = (FG_DIR if args.apply else OUT_DIR) / f"{name}.png"
        img.save(target)
        print(f"{name}  {img.size[0]}x{img.size[1]}  -> {target.parent.name}/")
        prev = Image.new("RGBA", (1900, int(1900 / STRIP_W * img.height)), (140, 190, 225, 255))
        prev.alpha_composite(img.resize(prev.size, Image.LANCZOS))
        prev.save(OUT_DIR / f"preview_{name}.png")

    if args.apply:
        print("\nIn Unity: assign these to BackgroundManager.adStrips, then play.")


if __name__ == "__main__":
    main()
