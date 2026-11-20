#!/usr/bin/env python3
"""App icon from the game's own art: BiPlane2 nearest-neighbour-upscaled onto
a flat 8-bit sky. Deliberately a pixel-art icon — honest about what the game
is, crisp at every size, and reproducible without an art department.

Usage: python3 tools/icon/generate_icon.py [out.png]
Writes an opaque 1024x1024 PNG (App Store icons must carry no alpha).

    python3 tools/icon/generate_icon.py --adaptive
writes the Android adaptive-icon layers into Assets/Doodlebugs/Sprites/Icon/
(referenced from ProjectSettings, Android > Adaptive icons):
  AdaptiveIcon_Background.png  the sky + clouds, full bleed, opaque
  AdaptiveIcon_Foreground.png  the plane alone on transparency
Both are 1536x1536 = the 108 dp adaptive canvas, laid out so its visible
72 dp centre (1024 px) is exactly the square icon above: the plane keeps
its integer x7 pixel scale and lands inside the 66 dp safe zone, and the
sky bands simply run on into the bleed the launcher mask crops away.
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).parents[2]
PLANE = ROOT / "Assets/Doodlebugs/Sprites/BiPlane/BiPlane2.png"
ICON_DIR = ROOT / "Assets/Doodlebugs/Sprites/Icon"
SIZE = 1024
ADAPTIVE = SIZE * 108 // 72  # 1536: 108 dp canvas whose 72 dp viewport is SIZE

# 8-bit sky: three horizontal bands, light at the top.
BANDS = ["#8ecff2", "#79c3ee", "#63b5e8"]

def pixel_cloud(d, x, y, s, color="#ffffff"):
    """Chunky stepped cloud from three overlapping rectangles."""
    d.rectangle([x, y + s, x + 7 * s, y + 3 * s], fill=color)
    d.rectangle([x + s, y, x + 4 * s, y + 2 * s], fill=color)
    d.rectangle([x + 4 * s, y + s // 2, x + 6 * s, y + 2 * s], fill=color)

def draw_sky(img, o=0):
    """Bands + clouds for a SIZE square placed at offset (o, o) in img; the
    outer bands extend to the edges of img."""
    d = ImageDraw.Draw(img)
    band_h = SIZE // len(BANDS)
    for i, c in enumerate(BANDS):
        top = 0 if i == 0 else o + i * band_h
        bottom = img.height if i == len(BANDS) - 1 else o + (i + 1) * band_h + 1
        d.rectangle([0, top, img.width, bottom], fill=c)

    # Clouds behind the plane: one large upper-left, one small lower-right,
    # slightly translucent-looking via a lighter tint on the lower one.
    pixel_cloud(d, o + 60, o + 120, 34)
    pixel_cloud(d, o + 690, o + 700, 22, color="#eaf6fd")

def draw_plane(img, o=0):
    plane = Image.open(PLANE).convert("RGBA")
    plane = plane.crop(plane.getbbox())  # sprite sits in a 128px canvas with margins
    scale = 7
    plane = plane.resize((plane.width * scale, plane.height * scale), Image.NEAREST)
    # Slightly right of centre and above the midline — motion headroom ahead
    # of the nose reads as flight even in a static icon.
    px = (SIZE - plane.width) // 2 + 30
    py = (SIZE - plane.height) // 2 - 40
    img.paste(plane, (o + px, o + py), plane)

def adaptive():
    o = (ADAPTIVE - SIZE) // 2
    bg = Image.new("RGB", (ADAPTIVE, ADAPTIVE))
    draw_sky(bg, o)
    fg = Image.new("RGBA", (ADAPTIVE, ADAPTIVE), (0, 0, 0, 0))
    draw_plane(fg, o)
    ICON_DIR.mkdir(parents=True, exist_ok=True)
    for name, img in (("Background", bg), ("Foreground", fg)):
        out = ICON_DIR / f"AdaptiveIcon_{name}.png"
        img.save(out)
        print(f"adaptive {name.lower()} -> {out}  ({img.size[0]}x{img.size[1]}, mode {img.mode})")

def main():
    if sys.argv[1:] == ["--adaptive"]:
        adaptive()
        return
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "tools/icon/out/AppIcon_1024.png"
    out.parent.mkdir(parents=True, exist_ok=True)

    img = Image.new("RGB", (SIZE, SIZE))
    draw_sky(img)
    draw_plane(img)

    img.save(out)
    print(f"icon -> {out}  ({img.size[0]}x{img.size[1]}, mode {img.mode})")

if __name__ == "__main__":
    main()
