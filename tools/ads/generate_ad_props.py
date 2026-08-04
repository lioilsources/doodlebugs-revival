#!/usr/bin/env python3
"""Generates giant advertising props — cans, bottles, jars — from brands.json.

Roadside-attraction scale objects in the same flat pixel style as the signs:
a soda can the size of a shed, standing in the terrain, destructible like
everything else in the foreground. Output: Assets/.../Sprites/Ads/prop_*.png.

Usage: python3 tools/ads/generate_ad_props.py
"""
import json
import random
from pathlib import Path

from PIL import Image, ImageDraw

import generate_ad_signs as gas

ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = Path(__file__).parent / "sprites"
BRANDS = json.loads((Path(__file__).parent / "brands.json").read_text())

SIZES = {"can": (300, 420), "bottle": (260, 540), "jar": (320, 380),
         "phone": (240, 500), "watch": (300, 430), "diamond": (340, 300),
         "box": (300, 380), "car": (560, 330)}


def shade(hex_color, f):
    hex_color = hex_color.lstrip("#")
    r, g, b = (int(hex_color[i:i + 2], 16) for i in (0, 2, 4))
    return tuple(min(255, max(0, int(c * f))) for c in (r, g, b)) + (255,)


def label_band(d, x0, y0, x1, y1, pal, text):
    """Brand label wrapped around the body: bg band, border edges, big text."""
    d.rectangle([x0, y0, x1, y1], fill=pal["bg"])
    d.rectangle([x0, y0, x1, y0 + 8], fill=pal["border"])
    d.rectangle([x0, y1 - 8, x1, y1], fill=pal["border"])
    font = gas.fit_font(d, text, (x1 - x0) - 30, start=48, floor=8)
    d.text(((x0 + x1 - d.textlength(text, font=font)) // 2,
            (y0 + y1 - font.size) // 2), text, font=font, fill=pal["text"])


def draw_can(pal, text):
    w, h = SIZES["can"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    body = shade(pal["accent"], 1.0)
    d.rounded_rectangle([10, 26, w - 11, h - 13], radius=26, fill=body)
    d.ellipse([10, 8, w - 11, 54], fill=shade(pal["border"], 0.9))       # lid
    d.ellipse([22, 14, w - 23, 44], fill=shade(pal["border"], 1.15))
    d.rectangle([w // 2 - 26, 20, w // 2 + 26, 32], fill=shade(pal["border"], 0.7))  # tab
    d.ellipse([10, h - 46, w - 11, h - 4], fill=shade(pal["accent"], 0.7))
    d.rectangle([34, 60, 52, h - 50], fill=(255, 255, 255, 46))          # highlight
    label_band(d, 10, h // 3, w - 11, h // 3 * 2 + 20, pal, text)
    return img


def draw_bottle(pal, text):
    w, h = SIZES["bottle"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    glass = shade(pal["bg"], 0.55)
    neck_w = w // 3
    d.rectangle([(w - neck_w) // 2, 46, (w + neck_w) // 2, h // 4], fill=glass)      # neck
    d.rectangle([(w - neck_w) // 2 - 8, 12, (w + neck_w) // 2 + 8, 52], fill=shade(pal["border"], 1.0))  # cap
    d.polygon([((w - neck_w) // 2, h // 4), ((w + neck_w) // 2, h // 4),
               (w - 13, h // 3 + 30), (13, h // 3 + 30)], fill=glass)                # shoulders
    d.rounded_rectangle([13, h // 3 + 20, w - 14, h - 9], radius=22, fill=glass)     # body
    d.rectangle([30, h // 3 + 40, 44, h - 40], fill=(255, 255, 255, 52))
    label_band(d, 13, h // 2 - 20, w - 14, h // 4 * 3, pal, text)
    return img


def draw_jar(pal, text):
    w, h = SIZES["jar"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([16, 10, w - 17, 66], fill=shade(pal["border"], 0.85))               # lid
    for lx in range(16, w - 17, 22):
        d.rectangle([lx, 14, lx + 8, 60], fill=shade(pal["border"], 0.65))           # lid ridges
    d.rounded_rectangle([6, 60, w - 7, h - 7], radius=34, fill=shade(pal["bg"], 0.5))
    d.rectangle([26, 80, 42, h - 40], fill=(255, 255, 255, 52))
    label_band(d, 6, h // 3 + 10, w - 7, h - 60, pal, text)
    return img


def draw_phone(pal, text):
    """Candlestick telephone, the 1910s upright kind."""
    w, h = SIZES["phone"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    body = shade("#2A2420", 1.0)
    lite = shade("#2A2420", 2.2)
    cx = w // 2 + 30
    d.polygon([(cx - 34, 96), (cx + 34, 96), (cx + 56, 30), (cx - 56, 30)], fill=body)   # mouthpiece horn
    d.ellipse([cx - 60, 12, cx + 60, 46], fill=lite)
    d.rectangle([cx - 12, 90, cx + 12, h - 110], fill=body)                              # stem
    d.ellipse([cx - 80, h - 130, cx + 80, h - 60], fill=body)                            # base
    d.rectangle([cx - 80, h - 95, cx + 80, h - 12], fill=body)
    d.ellipse([cx - 90, h - 42, cx + 90, h - 2], fill=lite)
    d.rectangle([18, 130, 40, 220], fill=body)                                           # hook arm
    d.rectangle([12, 150, 84, 170], fill=body)
    d.ellipse([16, 168, 68, 300], fill=lite)                                             # earpiece
    d.ellipse([26, 180, 58, 288], fill=body)
    band_y = h - 190
    d.rectangle([cx - 70, band_y, cx + 70, band_y + 44], fill=pal["bg"])                 # little brand plate
    font = gas.fit_font(d, text, 130, start=24, floor=8)
    d.text((cx - d.textlength(text, font=font) // 2, band_y + (44 - font.size) // 2),
           text, font=font, fill=pal["text"])
    return img


def draw_watch(pal, text):
    """Pocket watch on a stub of chain."""
    w, h = SIZES["watch"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    gold = shade(pal["border"], 1.0)
    cx, cy, r = w // 2, h // 2 + 40, w // 2 - 14
    d.rectangle([cx - 16, 24, cx + 16, 70], fill=gold)                                   # crown
    d.ellipse([cx - 30, 0, cx + 30, 34], outline=gold, width=10)                         # bow ring
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=gold)                               # case
    d.ellipse([cx - r + 16, cy - r + 16, cx + r - 16, cy + r - 16], fill="#F4EFE2")      # face
    import math as m
    for i in range(12):                                                                  # hour ticks
        a = i * m.pi / 6
        x1, y1 = cx + (r - 30) * m.sin(a), cy - (r - 30) * m.cos(a)
        x2, y2 = cx + (r - 48) * m.sin(a), cy - (r - 48) * m.cos(a)
        d.line([(x1, y1), (x2, y2)], fill="#2A2420", width=8)
    d.line([(cx, cy), (cx + int(r * 0.45), cy - int(r * 0.28))], fill="#2A2420", width=10)  # hands
    d.line([(cx, cy), (cx - int(r * 0.2), cy - int(r * 0.55))], fill="#2A2420", width=10)
    d.ellipse([cx - 10, cy - 10, cx + 10, cy + 10], fill=gold)
    font = gas.fit_font(d, text, int(r * 1.1), start=16, floor=8)
    d.text((cx - d.textlength(text, font=font) // 2, cy + r // 2 - 6),
           text, font=font, fill=pal["bg"])
    return img


def draw_diamond(pal, text):
    """Faceted gem, point down — the point buries itself in the terrain."""
    w, h = SIZES["diamond"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    girdle = h // 3
    hue = shade(pal["accent"], 1.0)
    hue_d = shade(pal["accent"], 0.62)
    hue_l = shade(pal["accent"], 1.6)
    d.polygon([(10, girdle), (w - 11, girdle), (w // 2, h - 4)], fill=hue)               # pavilion
    d.polygon([(10, girdle), (w // 2, h - 4), (w // 3, girdle)], fill=hue_d)
    d.polygon([(w // 4, 10), (w - w // 4, 10), (w - 11, girdle), (10, girdle)], fill=hue_l)  # crown
    d.polygon([(w // 4, 10), (w // 2, girdle), (10, girdle)], fill=hue)
    d.polygon([(w - w // 4, 10), (w - 11, girdle), (w // 2, girdle)], fill=hue)
    for sx, sy in [(w // 5, h // 5), (w - w // 6, h // 2)]:                              # sparkles
        d.line([(sx - 16, sy), (sx + 16, sy)], fill="white", width=6)
        d.line([(sx, sy - 16), (sx, sy + 16)], fill="white", width=6)
    font = gas.fit_font(d, text, w // 2, start=16, floor=8)
    d.text(((w - d.textlength(text, font=font)) // 2, girdle - font.size // 2 - 4),
           text, font=font, fill=pal["bg"])
    return img


def draw_box(pal, text):
    """Soap-flake box with a starburst, giant detergent-aisle energy."""
    w, h = SIZES["box"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([6, 6, w - 7, h - 7], fill=pal["bg"])
    d.rectangle([6, 6, w - 7, 40], fill=shade(pal["bg"], 0.7))                           # lid flap
    d.rectangle([6, 6, w - 7, h - 7], outline=pal["border"], width=6)
    import math as m
    cx, cy, rad = w // 2, h // 2 - 10, w // 2 - 30                                       # starburst
    pts = []
    for i in range(16):
        a = i * m.pi / 8
        r = rad if i % 2 == 0 else rad // 2
        pts.append((cx + r * m.cos(a), cy + r * m.sin(a)))
    d.polygon(pts, fill=pal["accent"])
    font = gas.fit_font(d, text, w - 70, start=32, floor=8)
    d.text((cx - d.textlength(text, font=font) // 2, cy - font.size // 2),
           text, font=font, fill=pal["text"])
    sub = "SOAP FLAKES"
    f2 = gas.fit_font(d, sub, w - 80, start=16, floor=8)
    d.text((cx - d.textlength(sub, font=f2) // 2, h - 58), sub, font=f2, fill=pal["border"])
    return img


def draw_car(pal, text):
    """Vintage motorcar, side view, brand on the door."""
    w, h = SIZES["car"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    body = shade(pal["bg"], 1.0)
    dark = shade(pal["bg"], 0.6)
    wheel_y = h - 74
    d.rounded_rectangle([16, h - 170, w - 17, wheel_y + 24], radius=26, fill=body)       # chassis
    d.rounded_rectangle([w // 2 - 30, h - 260, w - 60, h - 150], radius=20, fill=body)   # cabin
    d.rectangle([w // 2 - 6, h - 238, w - 84, h - 172], fill="#B9D8E8")                  # window
    d.rectangle([16, h - 150, w // 4, h - 120], fill=dark)                               # hood vent
    for wx in (110, w - 130):                                                            # wheels
        d.ellipse([wx - 62, wheel_y - 62, wx + 62, wheel_y + 62], fill="#2A2420")
        d.ellipse([wx - 34, wheel_y - 34, wx + 34, wheel_y + 34], fill=shade(pal["border"], 1.1))
        d.ellipse([wx - 10, wheel_y - 10, wx + 10, wheel_y + 10], fill="#2A2420")
    d.ellipse([w - 46, h - 208, w - 10, h - 172], fill="#FFE9B0")                        # headlamp
    font = gas.fit_font(d, text, w // 2 - 60, start=24, floor=8)
    d.text((w // 4 + 10, h - 140), text, font=font, fill=pal["text"])
    return img


DRAW = {"can": draw_can, "bottle": draw_bottle, "jar": draw_jar,
        "phone": draw_phone, "watch": draw_watch, "diamond": draw_diamond,
        "box": draw_box, "car": draw_car}


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    rng = random.Random(500)
    by_id = {s["id"]: s for s in BRANDS["signs"]}
    for prop in BRANDS["props"]:
        spec = by_id[prop["brand"]]
        text = spec["name"].split()[0]           # first word reads best on a curved label
        img = DRAW[prop["shape"]](spec["palette"], text)
        gas.weather(img, rng)
        out = OUT_DIR / f"prop_{prop['shape']}_{prop['brand']}.png"
        img.save(out)
        print(f"{out.name}  {img.size[0]}x{img.size[1]}")


if __name__ == "__main__":
    main()
