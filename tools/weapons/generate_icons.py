#!/usr/bin/env python3
"""Weapon icons for the on-screen fire triggers (Prompts/26 D13).

One 96x96 pixel-art PNG per WeaponType, drawn with Pillow primitives on a
24x24 grid and scaled x4 with no filtering - the same chunky look as the
power-up icons in Resources/Sprites/PowerUps. No GPU, no model: run it
anywhere. The trigger falls back to the weapon's projectile sprite when an
icon is missing, so a half-finished set never breaks a build.

Usage:
  python3 tools/weapons/generate_icons.py            # review sheet in tools/weapons/out/
  python3 tools/weapons/generate_icons.py --apply    # write Resources/Sprites/Weapons/ + .meta

Keys must match WeaponProfile.IconSpriteName ("weapon_<key>").
"""
import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).parents[2]
ASSETS = ROOT / "Assets/Doodlebugs/Resources/Sprites/Weapons"
OUT = Path(__file__).parent / "out"
sys.path.insert(0, str(ROOT / "tools/planes"))
from unity_meta import ensure_folder, write_meta  # noqa: E402

GRID = 24
SCALE = 4

OUTLINE = (20, 20, 32, 255)
BODY = (230, 233, 240, 255)
SHADE = (154, 163, 181, 255)
YELLOW = (255, 210, 74, 255)
ORANGE = (255, 154, 60, 255)
RED = (235, 70, 70, 255)
CYAN = (126, 242, 255, 255)
STEEL = (124, 131, 148, 255)
DARK = (58, 60, 72, 255)


def bullet(d, x0, y0, length, height, tip, fill=BODY, flash=YELLOW):
    """A horizontal round pointing right, with a muzzle flash behind it."""
    y1 = y0 + height
    d.rectangle((x0, y0, x0 + length, y1), fill=fill, outline=OUTLINE)
    d.polygon([(x0 + length, y0), (x0 + length + tip, (y0 + y1) / 2), (x0 + length, y1)],
              fill=SHADE, outline=OUTLINE)
    d.line((x0 + 1, y0 + 1, x0 + length - 3, y0 + 1), fill=SHADE)
    if flash:
        for dy in (-1, 0, 1):
            d.line((x0 - 4, (y0 + y1) / 2 + dy * 2, x0 - 2, (y0 + y1) / 2 + dy), fill=flash)


def draw_mg(d):
    bullet(d, 6, 10, 10, 3, 4)


def draw_twinmg(d):
    bullet(d, 6, 6, 10, 3, 4)
    bullet(d, 6, 14, 10, 3, 4)


def pellets(d, count, spread):
    """A shell breaking into a fan of pellets to the right."""
    d.rectangle((3, 10, 8, 13), fill=STEEL, outline=OUTLINE)
    d.line((4, 11, 7, 11), fill=SHADE)
    import math
    for i in range(count):
        t = (i / (count - 1) - 0.5) if count > 1 else 0.0
        ang = t * spread
        r = 9 + (i % 2) * 2
        cx = 10 + r * math.cos(ang)
        cy = 11.5 + r * math.sin(ang)
        d.ellipse((cx - 1.5, cy - 1.5, cx + 1.5, cy + 1.5), fill=ORANGE, outline=OUTLINE)


def draw_flak(d):
    pellets(d, 5, 1.3)


def draw_heavyflak(d):
    pellets(d, 7, 1.9)


def draw_bomb(d):
    # Fins on top, fat body, nose down.
    d.polygon([(8, 2), (11, 6), (11, 2)], fill=STEEL, outline=OUTLINE)
    d.polygon([(16, 2), (13, 6), (13, 2)], fill=STEEL, outline=OUTLINE)
    d.rectangle((11, 2, 13, 6), fill=STEEL, outline=OUTLINE)
    d.ellipse((7, 5, 17, 19), fill=DARK, outline=OUTLINE)
    d.rectangle((8, 11, 16, 12), fill=RED)
    d.polygon([(10, 18), (12, 22), (14, 18)], fill=DARK, outline=OUTLINE)
    d.line((9, 8, 9, 14), fill=SHADE)


def draw_sniper(d):
    # Crosshair with a long bolt through it.
    d.ellipse((6, 6, 18, 18), outline=OUTLINE, width=1)
    d.ellipse((7, 7, 17, 17), outline=CYAN, width=1)
    for x0, y0, x1, y1 in ((12, 3, 12, 6), (12, 18, 12, 21), (3, 12, 6, 12), (18, 12, 21, 12)):
        d.line((x0, y0, x1, y1), fill=OUTLINE, width=2)
    d.rectangle((2, 11, 19, 12), fill=CYAN, outline=OUTLINE)
    d.polygon([(19, 10), (23, 11.5), (19, 13)], fill=BODY, outline=OUTLINE)


def draw_rocket(d):
    # Flame, fins, body, nose - flying right.
    d.polygon([(0, 11.5), (5, 9), (5, 14)], fill=ORANGE)
    d.polygon([(1, 11.5), (5, 10), (5, 13)], fill=YELLOW)
    d.polygon([(6, 9), (3, 5), (8, 9)], fill=RED, outline=OUTLINE)
    d.polygon([(6, 14), (3, 18), (8, 14)], fill=RED, outline=OUTLINE)
    d.rectangle((6, 9, 17, 14), fill=BODY, outline=OUTLINE)
    d.line((7, 10, 16, 10), fill=SHADE)
    d.rectangle((10, 9, 12, 14), fill=RED)
    d.polygon([(17, 9), (22, 11.5), (17, 14)], fill=RED, outline=OUTLINE)


def draw_mine(d):
    import math
    cx, cy = 12, 12
    for i in range(8):
        ang = i * math.pi / 4
        x0, y0 = cx + 5 * math.cos(ang), cy + 5 * math.sin(ang)
        x1, y1 = cx + 9 * math.cos(ang), cy + 9 * math.sin(ang)
        d.line((x0, y0, x1, y1), fill=OUTLINE, width=3)
        d.line((x0, y0, x1, y1), fill=DARK, width=1)
    d.ellipse((6, 6, 18, 18), fill=DARK, outline=OUTLINE)
    d.ellipse((8, 8, 12, 12), fill=SHADE)
    d.ellipse((10, 10, 14, 14), fill=RED, outline=OUTLINE)


ICONS = {
    "mg": draw_mg,
    "twinmg": draw_twinmg,
    "flak": draw_flak,
    "heavyflak": draw_heavyflak,
    "bomb": draw_bomb,
    "sniper": draw_sniper,
    "rocket": draw_rocket,
    "mine": draw_mine,
}


def render(key):
    img = Image.new("RGBA", (GRID, GRID), (0, 0, 0, 0))
    ICONS[key](ImageDraw.Draw(img))
    return img.resize((GRID * SCALE, GRID * SCALE), Image.NEAREST)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    args = ap.parse_args()

    icons = {key: render(key) for key in ICONS}

    OUT.mkdir(parents=True, exist_ok=True)
    pad = 12
    sheet = Image.new("RGBA", ((GRID * SCALE + pad) * len(icons) + pad, GRID * SCALE + 2 * pad), (40, 44, 56, 255))
    x = pad
    for icon in icons.values():
        sheet.alpha_composite(icon, (x, pad))
        x += GRID * SCALE + pad
    sheet.save(OUT / "icons.png")

    if args.apply:
        ensure_folder(ASSETS)
        for key, icon in icons.items():
            png = ASSETS / f"weapon_{key}.png"
            icon.save(png)
            write_meta(png, "sprite")
            print(f"wrote {png.relative_to(ROOT)}")
    print(f"-> {OUT / 'icons.png'}")


if __name__ == "__main__":
    main()
