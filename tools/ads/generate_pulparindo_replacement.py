#!/usr/bin/env python3
"""Replaces the Pulparindo foreground strip with a giant roadside billboard.

The original strip was a photo of a real Pulparindo candy wrapper — a genuine
trademark + trade dress liability that cannot ship through App Review. This
keeps the map's gameplay role (one big destructible obstacle mid-strip) but
builds it from our own fictional advertising: a huge DESERT DEW SODA billboard
on a timber scaffold rising out of a sand dune.

Usage:
  python3 tools/ads/generate_pulparindo_replacement.py           # preview to out/
  python3 tools/ads/generate_pulparindo_replacement.py --apply   # write Assets strip + refresh base/
Then in Unity run  Doodlebugs -> Sync Background Profiles.
"""
import argparse
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw

import generate_ad_signs as gas

ROOT = Path(__file__).resolve().parents[2]
ADS_DIR = Path(__file__).parent / "sprites"
FG_PATH = ROOT / "Assets/Doodlebugs/Sprites/Foreground/Pulparindo_fg.png"
BASE_PATH = Path(__file__).parent / "base/Pulparindo_fg.png"
OUT_DIR = Path(__file__).parent / "out"

STRIP_W, STRIP_H = 4096, 1250

# Same ground the original wrapper occupied (x ~500-2130), so the obstacle
# sits where players already expect it.
STRUCT_X = 520
STRUCT_W = 1640

WOOD_DARK, WOOD_LIGHT = "#4A3826", "#5C4632"
SAND, SAND_DARK = "#C8A96B", "#A9884F"


def draw_dune(draw, rng):
    """Flat-topped sand mound under the scaffold feet — wide and level enough
    for a run of stadium perimeter boards along its crest."""
    base_y = STRIP_H
    left, right = STRUCT_X - 500, STRUCT_X + STRUCT_W + 500
    crest = 210
    for x in range(left, right):
        t = (x - left) / (right - left)
        h = crest * min(1.0, math.sin(t * math.pi) ** 1.5 * 2.2)
        h += rng.uniform(-6, 6)
        if h > 4:
            draw.line([(x, base_y), (x, base_y - int(h))], fill=SAND)
            draw.point((x, base_y - int(h)), fill=SAND_DARK)


def draw_scaffold(draw, panel_bottom):
    """Three braced timber columns from the panel down into the dune."""
    cols = [STRUCT_X + 80, STRUCT_X + STRUCT_W // 2 - 30, STRUCT_X + STRUCT_W - 140]
    for cx in cols:
        draw.rectangle([cx, panel_bottom - 20, cx + 60, STRIP_H], fill=WOOD_DARK)
        draw.rectangle([cx, panel_bottom - 20, cx + 12, STRIP_H], fill=WOOD_LIGHT)
    # X-bracing between neighbouring columns.
    for a, b in zip(cols, cols[1:]):
        top, bot = panel_bottom + 60, STRIP_H - 120
        for (x1, y1, x2, y2) in [(a + 60, top, b, bot), (a + 60, bot, b, top)]:
            for off in range(14):
                draw.line([(x1, y1 + off), (x2, y2 + off)], fill=WOOD_DARK, width=3)
    # Catwalk plank under the panel.
    draw.rectangle([STRUCT_X - 30, panel_bottom - 16, STRUCT_X + STRUCT_W + 30, panel_bottom + 10],
                   fill=WOOD_LIGHT)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="write into Assets/ and refresh base/")
    args = ap.parse_args()

    rng = random.Random(1250)
    strip = Image.new("RGBA", (STRIP_W, STRIP_H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(strip)

    # Giant panel reuses the small-sign renderer at billboard scale.
    panel_spec = {
        "name": "DESERT DEW SODA",
        "slogan": "SWEET AS SUNSET",
        "size": [STRUCT_W, 560],
        "palette": {"bg": "#C9A227", "border": "#8C3B2E", "text": "#8C3B2E", "accent": "#E8DCC0"},
    }
    # Temporarily widen the renderer's font search so the headline scales up —
    # 4x keeps the name/slogan proportion of the small signs (128 vs 64).
    orig_fit = gas.fit_font
    gas.fit_font = lambda d, t, w, start=32, floor=8: orig_fit(d, t, w, start=start * 4, floor=floor)
    panel = gas.draw_sign(panel_spec, rng)
    gas.fit_font = orig_fit

    panel_y = 140
    draw_dune(draw, rng)
    draw_scaffold(draw, panel_y + panel.height)
    strip.alpha_composite(panel, (STRUCT_X, panel_y))

    # Perimeter boards along the dune crest, passing in FRONT of the scaffold
    # legs the way rink boards front a stadium stand. Hand-placed: the
    # composer's chain search can't thread the 650px gaps between the legs.
    crest_y = STRIP_H - 210
    board_pool = ["board_doodle_cola_w.png", "board_desert_dew_c.png", "board_el_sombrero_w.png"]
    bx = STRUCT_X + 140
    for i, fname in enumerate(board_pool):
        board = Image.open(ADS_DIR / fname).convert("RGBA")
        strip.alpha_composite(board, (bx, crest_y - board.height + 14))
        bx += board.width - 6

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    preview = Image.new("RGBA", (STRUCT_W + 600, STRIP_H), (140, 190, 225, 255))
    preview.alpha_composite(strip.crop((STRUCT_X - 300, 0, STRUCT_X + STRUCT_W + 300, STRIP_H)))
    preview.save(OUT_DIR / "preview_pulparindo_replacement.png")
    print(f"preview -> {OUT_DIR / 'preview_pulparindo_replacement.png'}")

    if args.apply:
        strip.save(FG_PATH)
        BASE_PATH.parent.mkdir(parents=True, exist_ok=True)
        strip.save(BASE_PATH)   # new pristine base — the wrapper never comes back
        print(f"applied -> {FG_PATH}")
        print("In Unity run: Doodlebugs -> Sync Background Profiles")
    else:
        print("(preview only — run with --apply to replace the Assets strip)")


if __name__ == "__main__":
    main()
