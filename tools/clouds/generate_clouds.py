#!/usr/bin/env python3
"""Three cloud shapes in the flat two-tone style of the original Cloud.png,
sized to the same ~270px canvas so colliders and spawn maths stay honest.
Output: Assets/Doodlebugs/Resources/Sprites/Clouds/cloud_{0,1,2}.png
"""
import random
from pathlib import Path
from PIL import Image, ImageDraw

OUT = Path(__file__).parents[2] / "Assets/Doodlebugs/Resources/Sprites/Clouds"
W = H = 270
BODY, SHADE = (245, 245, 245, 255), (155, 155, 155, 255)

def puffs(d, blobs, color):
    for x, y, r in blobs:
        d.ellipse([x - r, y - r, x + r, y + r], fill=color)

def cumulus(rng):
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    base = [(70 + i * 34, 160 + rng.randint(-6, 6), 34 + rng.randint(-4, 8)) for i in range(4)]
    tops = [(95 + i * 40, 125 + rng.randint(-8, 4), 30 + rng.randint(-2, 10)) for i in range(3)]
    puffs(d, [(x, y + 12, r) for x, y, r in base], SHADE)
    puffs(d, base + tops, BODY)
    return img

def stratus(rng):
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    rows = [(50 + i * 28, 150 + (i % 2) * 14, 22 + rng.randint(-3, 6)) for i in range(7)]
    puffs(d, [(x + 6, y + 10, r) for x, y, r in rows], SHADE)
    puffs(d, rows, BODY)
    return img

def puffball(rng):
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    blobs = [(135, 140, 52), (100, 155, 38), (172, 152, 40), (135, 112, 40)]
    puffs(d, [(x, y + 10, r) for x, y, r in blobs], SHADE)
    puffs(d, blobs, BODY)
    return img

OUT.mkdir(parents=True, exist_ok=True)
rng = random.Random(7)
for i, fn in enumerate((cumulus, stratus, puffball)):
    img = fn(rng)
    img.save(OUT / f"cloud_{i}.png")
    print(f"cloud_{i}.png {img.size}")
