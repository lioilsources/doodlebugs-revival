#!/usr/bin/env python3
"""Turns SPARK Matchbox-car renders into print creatives (ad_<brand>.png).

Layout mirrors the existing SDXL creatives: the art fills the sheet, the
headline sits along the bottom edge in the print-shop slab face, so
draw_panel's contain-fit shows everything.

Usage: python3 tools/ads/typeset_car_creatives.py <render_dir>
Expects <render_dir>/{griffon_1,griffon_2,tinbox_1,tinbox_2}.png; picks are
hardcoded below once candidates are reviewed.
"""
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).parent
BRANDS = {s["id"]: s for s in json.loads((HERE / "brands.json").read_text())["signs"]}
FONT = HERE / "fonts/AlfaSlabOne-Regular.ttf"
OUT = HERE / "sprites"

# brand id -> chosen render file (edit after reviewing candidates)
PICKS = {"griffon_motors": "griffon_1.png", "tinbox_toys": "tinbox_2.png"}

def typeset(brand_id, render_path):
    spec = BRANDS[brand_id]
    art = Image.open(render_path).convert("RGB")
    W, H = 600, 380
    sheet = Image.new("RGB", (W, H), "#F2EFE6")
    # Art: cover the top band
    s = max(W / art.width, (H - 84) / art.height)
    art = art.resize((int(art.width * s), int(art.height * s)), Image.LANCZOS)
    sheet.paste(art, ((W - art.width) // 2, min(0, (H - 84 - art.height) // 2)))
    d = ImageDraw.Draw(sheet)
    # Headline band along the bottom, brand accent underline
    band_top = H - 84
    d.rectangle([0, band_top, W, H], fill="#F2EFE6")
    d.rectangle([0, band_top, W, band_top + 6], fill=spec["palette"]["accent"])
    f1 = ImageFont.truetype(str(FONT), 40)
    while d.textlength(spec["name"], font=f1) > W - 40 and f1.size > 16:
        f1 = ImageFont.truetype(str(FONT), f1.size - 2)
    d.text(((W - d.textlength(spec["name"], font=f1)) // 2, band_top + 12),
           spec["name"], font=f1, fill="#241D16")
    f2 = ImageFont.truetype(str(FONT), 17)
    d.text(((W - d.textlength(spec["slogan"], font=f2)) // 2, band_top + 58),
           spec["slogan"], font=f2, fill="#6E5F4B")
    return sheet.resize((300, 190), Image.LANCZOS)

def main():
    render_dir = Path(sys.argv[1])
    for brand, fname in PICKS.items():
        img = typeset(brand, render_dir / fname)
        out = OUT / f"ad_{brand}.png"
        img.convert("RGBA").save(out)
        print(f"{out.name}  {img.size[0]}x{img.size[1]}")

if __name__ == "__main__":
    main()
