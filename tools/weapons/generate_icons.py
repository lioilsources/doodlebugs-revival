#!/usr/bin/env python3
"""Weapon icons for the on-screen fire triggers (Prompts/26 D13).

One 112x112 pixel-art badge per weapon, gadget and pickup (Prompts/29 §7):
a category-coloured disc (weapon orange, gadget cyan, special magenta)
with an ink rim, the glyph drawn with Pillow primitives on a 24x24 grid on
top, all scaled x4 with no filtering - the same chunky look as the
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
        d.ellipse((cx - 1.5, cy - 1.5, cx + 1.5, cy + 1.5), fill=BODY, outline=OUTLINE)


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


def draw_stinger(d):
    # A round flying LEFT out of a tail fin
    d.polygon([(20, 6), (23, 6), (23, 17), (20, 17)], fill=STEEL, outline=OUTLINE)
    d.rectangle((7, 10, 17, 13), fill=BODY, outline=OUTLINE)
    d.polygon([(7, 10), (3, 11.5), (7, 13)], fill=SHADE, outline=OUTLINE)
    for dy in (-1, 0, 1):
        d.line((18, 11.5 + dy * 2, 20, 11.5 + dy), fill=YELLOW)


def draw_airburst(d):
    d.ellipse((2, 2, 21, 21), outline=YELLOW, width=1)
    for i in range(0, 360, 45):
        import math
        a = math.radians(i)
        d.point((11.5 + 10 * math.cos(a), 11.5 + 10 * math.sin(a)), fill=OUTLINE)
    d.ellipse((8, 8, 15, 15), fill=YELLOW, outline=OUTLINE)
    d.line((10, 10, 11, 10), fill=BODY)


def draw_seeker(d):
    # Curved dotted path into a small rocket
    for x, y in ((2, 19), (4, 16), (6, 14), (9, 12)):
        d.point((x, y), fill=BODY)
    d.polygon([(10, 9), (8, 6), (12, 9)], fill=CYAN, outline=OUTLINE)
    d.polygon([(10, 15), (8, 18), (12, 15)], fill=CYAN, outline=OUTLINE)
    d.rectangle((10, 9, 19, 15), fill=BODY, outline=OUTLINE)
    d.rectangle((13, 9, 14, 15), fill=CYAN)
    d.polygon([(19, 9), (23, 12), (19, 15)], fill=CYAN, outline=OUTLINE)


def draw_bigone(d):
    d.rectangle((10, 1, 14, 4), fill=STEEL, outline=OUTLINE)
    d.ellipse((4, 3, 20, 21), fill=DARK, outline=OUTLINE)
    d.rectangle((5, 11, 19, 13), fill=YELLOW)
    d.line((7, 6, 7, 16), fill=SHADE)
    d.polygon([(9, 20), (12, 23), (15, 20)], fill=DARK, outline=OUTLINE)


def draw_emp(d):
    d.ellipse((2, 2, 21, 21), outline=CYAN, width=1)
    d.polygon([(13, 3), (6, 13), (11, 13), (9, 21), (17, 10), (12, 10)], fill=YELLOW, outline=OUTLINE)


def draw_swarm(d):
    for y, x in ((4, 9), (10, 4), (16, 9)):
        d.rectangle((x, y, x + 8, y + 3), fill=BODY, outline=OUTLINE)
        d.polygon([(x + 8, y), (x + 11, y + 1.5), (x + 8, y + 3)], fill=CYAN, outline=OUTLINE)
        d.point((x - 2, y + 1.5), fill=ORANGE)


def draw_nitro(d):
    for x in (4, 10, 16):
        d.polygon([(x, 5), (x + 5, 11.5), (x, 18), (x + 2, 11.5)], fill=BODY, outline=OUTLINE)


def draw_roll(d):
    d.arc((3, 3, 20, 20), 200, 520, fill=BODY, width=3)
    d.polygon([(4, 7), (2, 13), (8, 11)], fill=BODY, outline=OUTLINE)


def draw_clap(d):
    d.ellipse((9, 9, 14, 14), fill=BODY, outline=OUTLINE)
    d.arc((5, 5, 18, 18), 0, 360, fill=BODY, width=1)
    d.arc((1, 1, 22, 22), 30, 150, fill=BODY, width=2)
    d.arc((1, 1, 22, 22), 210, 330, fill=BODY, width=2)


def draw_canister(d):
    d.rectangle((8, 3, 15, 5), fill=STEEL, outline=OUTLINE)
    d.rectangle((6, 5, 17, 21), fill=CYAN, outline=OUTLINE)
    d.polygon([(12, 8), (9, 14), (12, 14), (11, 19), (15, 12), (12, 12)], fill=BODY, outline=OUTLINE)


def draw_crate(d):
    d.rectangle((4, 6, 19, 20), fill=(196, 140, 72, 255), outline=OUTLINE)
    d.line((4, 6, 19, 20), fill=OUTLINE)
    d.line((19, 6, 4, 20), fill=OUTLINE)
    d.rectangle((10, 11, 13, 15), fill=YELLOW, outline=OUTLINE)
    d.line((6, 6, 11, 1), fill=BODY)
    d.line((17, 6, 12, 1), fill=BODY)


W_ORANGE = (255, 138, 31, 255)
G_CYAN = (34, 199, 219, 255)
S_MAGENTA = (224, 72, 154, 255)
P_GREEN = (61, 220, 104, 255)

# key -> (draw, disc colour, file prefix)
ICONS = {
    "mg": draw_mg,
    "twinmg": draw_twinmg,
    "flak": draw_flak,
    "heavyflak": draw_heavyflak,
    "bomb": draw_bomb,
    "sniper": draw_sniper,
    "rocket": draw_rocket,
    "mine": draw_mine,
    "stinger": draw_stinger,
    "airburst": draw_airburst,
    "seeker": draw_seeker,
    "bigone": (draw_bigone, S_MAGENTA),
    "emp": (draw_emp, S_MAGENTA),
    "swarm": (draw_swarm, S_MAGENTA),
    "gadget_nitro": (draw_nitro, G_CYAN),
    "gadget_roll": (draw_roll, G_CYAN),
    "gadget_clap": (draw_clap, G_CYAN),
    "powerup_nitro": (draw_canister, G_CYAN),
    "powerup_supply": (draw_crate, S_MAGENTA),
}

BADGE = 28   # grid of the badge: 24 glyph + 2 rim each side


def spec(key):
    v = ICONS[key]
    return v if isinstance(v, tuple) else (v, W_ORANGE)


def filename(key):
    return key if key.startswith(("gadget_", "powerup_")) else f"weapon_{key}"


def render(key):
    draw, disc = spec(key)
    glyph = Image.new("RGBA", (GRID, GRID), (0, 0, 0, 0))
    draw(ImageDraw.Draw(glyph))
    img = Image.new("RGBA", (BADGE, BADGE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((0, 0, BADGE - 1, BADGE - 1), fill=OUTLINE)
    d.ellipse((1, 1, BADGE - 2, BADGE - 2), fill=disc)
    d.arc((3, 3, BADGE - 4, BADGE - 4), 200, 290, fill=(255, 255, 255, 110))
    img.alpha_composite(glyph, (2, 2))
    return img.resize((BADGE * SCALE, BADGE * SCALE), Image.NEAREST)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    args = ap.parse_args()

    icons = {key: render(key) for key in ICONS}

    OUT.mkdir(parents=True, exist_ok=True)
    pad = 12
    size = BADGE * SCALE
    sheet = Image.new("RGBA", ((size + pad) * len(icons) + pad, size + 2 * pad), (40, 44, 56, 255))
    x = pad
    for icon in icons.values():
        sheet.alpha_composite(icon, (x, pad))
        x += size + pad
    sheet.save(OUT / "icons.png")

    if args.apply:
        ensure_folder(ASSETS)
        powerups = ROOT / "Assets/Doodlebugs/Resources/Sprites/PowerUps"
        for key, icon in icons.items():
            png = (powerups if key.startswith("powerup_") else ASSETS) / f"{filename(key)}.png"
            icon.save(png)
            write_meta(png, "sprite")
            print(f"wrote {png.relative_to(ROOT)}")
    print(f"-> {OUT / 'icons.png'}")


if __name__ == "__main__":
    main()
