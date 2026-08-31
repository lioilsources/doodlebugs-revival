#!/usr/bin/env python3
"""App icon from the game's own art: BiPlane2 nearest-neighbour-upscaled onto
a flat 8-bit sky. Deliberately a pixel-art icon — honest about what the game
is, crisp at every size, and reproducible without an art department.

Usage: python3 tools/icon/generate_icon.py [out.png]
Writes an opaque 1024x1024 PNG (App Store icons must carry no alpha).
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).parents[2]
PLANE = ROOT / "Assets/Doodlebugs/Sprites/BiPlane/BiPlane2.png"
SIZE = 1024

# 8-bit sky: three horizontal bands, light at the top.
BANDS = ["#8ecff2", "#79c3ee", "#63b5e8"]

def pixel_cloud(d, x, y, s, color="#ffffff"):
    """Chunky stepped cloud from three overlapping rectangles."""
    d.rectangle([x, y + s, x + 7 * s, y + 3 * s], fill=color)
    d.rectangle([x + s, y, x + 4 * s, y + 2 * s], fill=color)
    d.rectangle([x + 4 * s, y + s // 2, x + 6 * s, y + 2 * s], fill=color)

def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "tools/icon/out/AppIcon_1024.png"
    out.parent.mkdir(parents=True, exist_ok=True)

    img = Image.new("RGB", (SIZE, SIZE))
    d = ImageDraw.Draw(img)
    band_h = SIZE // len(BANDS)
    for i, c in enumerate(BANDS):
        d.rectangle([0, i * band_h, SIZE, (i + 1) * band_h + 1], fill=c)

    # Clouds behind the plane: one large upper-left, one small lower-right,
    # slightly translucent-looking via a lighter tint on the lower one.
    pixel_cloud(d, 60, 120, 34)
    pixel_cloud(d, 690, 700, 22, color="#eaf6fd")

    plane = Image.open(PLANE).convert("RGBA")
    plane = plane.crop(plane.getbbox())  # sprite sits in a 128px canvas with margins
    scale = 7
    plane = plane.resize((plane.width * scale, plane.height * scale), Image.NEAREST)
    # Slightly right of centre and above the midline — motion headroom ahead
    # of the nose reads as flight even in a static icon.
    px = (SIZE - plane.width) // 2 + 30
    py = (SIZE - plane.height) // 2 - 40
    img.paste(plane, (px, py), plane)

    img.save(out)
    print(f"icon -> {out}  ({img.size[0]}x{img.size[1]}, mode {img.mode})")

if __name__ == "__main__":
    main()
