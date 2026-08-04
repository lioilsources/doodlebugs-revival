#!/usr/bin/env python3
"""Generates pixel-art advertising sign PNGs from brands.json.

Signs are drawn with the game's own Press Start 2P (OFL) so the result is
rights-clean by construction — no external art, no real logos. Output goes to
Assets/Doodlebugs/Sprites/Ads/ (plain sprites; the composer bakes them into
the foreground strips, they are not referenced by Unity directly).

Usage: python3 tools/ads/generate_ad_signs.py
"""
import json
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
# Print-era typography when the Alfa Slab One (OFL) font is present — the
# whole ad set (posters, boards, prop labels) then reads as one print shop.
# Falls back to the game's Press Start 2P for the original pixel look.
_PRINT_FONT = Path(__file__).parent / "fonts/AlfaSlabOne-Regular.ttf"
FONT_PATH = _PRINT_FONT if _PRINT_FONT.exists() else ROOT / "Assets/Doodlebugs/Resources/Fonts/PressStart2P.ttf"
OUT_DIR = Path(__file__).parent / "sprites"
BRANDS = json.loads((Path(__file__).parent / "brands.json").read_text())


def fit_font(draw, text, max_w, start=32, floor=8):
    """Largest font size whose text fits max_w. Steps of 4 work for both the
    proportional print font and the 8px-grid pixel font (8/16/24... remain
    reachable)."""
    size = start
    while size > floor:
        font = ImageFont.truetype(str(FONT_PATH), size)
        if draw.textlength(text, font=font) <= max_w:
            return font
        size -= 4
    return ImageFont.truetype(str(FONT_PATH), floor)


def wrap_two_lines(draw, text, max_w, start=32):
    """Try one line; if the font would drop below 16 px, split near the middle."""
    font = fit_font(draw, text, max_w, start=start)
    if font.size >= 16 or " " not in text:
        return [(text, font)]
    words = text.split()
    mid = min(range(1, len(words)), key=lambda i: abs(len(" ".join(words[:i])) - len(text) // 2))
    lines = [" ".join(words[:mid]), " ".join(words[mid:])]
    font = min((fit_font(draw, ln, max_w, start=start) for ln in lines), key=lambda f: f.size)
    return [(ln, font) for ln in lines]


def weather(img, rng):
    """Sparse dark speckle + darkened corners — enough to read as 'painted tin'."""
    px = img.load()
    w, h = img.size
    for _ in range(w * h // 160):
        x, y = rng.randrange(w), rng.randrange(h)
        r, g, b, a = px[x, y]
        if a:
            px[x, y] = (max(0, r - 28), max(0, g - 28), max(0, b - 26), a)


def draw_sign(spec, rng):
    w, h = spec["size"]
    pal = spec["palette"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Panel with a double border, pixel-art sharp.
    d.rectangle([0, 0, w - 1, h - 1], fill=pal["border"])
    d.rectangle([6, 6, w - 7, h - 7], fill=pal["bg"])
    d.rectangle([12, 12, w - 13, h - 13], outline=pal["accent"], width=2)

    # Corner rivets.
    for cx in (10, w - 11):
        for cy in (10, h - 11):
            d.rectangle([cx - 2, cy - 2, cx + 2, cy + 2], fill=pal["accent"])

    inner_w = w - 48
    name_lines = wrap_two_lines(d, spec["name"], inner_w, start=32)
    slogan_font = fit_font(d, spec["slogan"], inner_w, start=16, floor=8)

    line_gap = 10
    name_h = sum(f.size for _, f in name_lines) + line_gap * (len(name_lines) - 1)
    total_h = name_h + 16 + slogan_font.size
    y = (h - total_h) // 2
    for text, font in name_lines:
        d.text(((w - d.textlength(text, font=font)) // 2, y), text, font=font, fill=pal["text"])
        y += font.size + line_gap
    y += 16 - line_gap
    # Accent rule between name and slogan.
    d.rectangle([w // 2 - inner_w // 4, y - 9, w // 2 + inner_w // 4, y - 7], fill=pal["accent"])
    d.text(((w - d.textlength(spec["slogan"], font=slogan_font)) // 2, y),
           spec["slogan"], font=slogan_font, fill=pal["accent"])

    weather(img, rng)
    return img


def neon_text(img, xy, text, font, color):
    """Neon tube lettering: a blurred glow pass under a bright core."""
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).text(xy, text, font=font, fill=color)
    glow = glow.filter(ImageFilter.GaussianBlur(9))
    img.alpha_composite(glow)
    img.alpha_composite(glow)          # second pass = hotter halo
    core = tuple(min(255, c + 90) for c in color[:3]) + (255,)
    ImageDraw.Draw(img).text(xy, text, font=font, fill=core)


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    """Blend hex colour a toward b by t (0..1)."""
    ca, cb = hex_rgb(a), hex_rgb(b)
    return tuple(int(x + (y - x) * t) for x, y in zip(ca, cb))


def _lum(c):
    r, g, b = hex_rgb(c) if isinstance(c, str) else c[:3]
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def ink_for(bg, pal):
    """Highest-contrast palette colour to print on `bg`.

    Picking a fixed role (e.g. always pal['bg'] on white) fails for brands
    whose palette is pale all round — cream on white is invisible.
    """
    return max((pal[k] for k in ("bg", "text", "border", "accent")),
               key=lambda c: abs(_lum(c) - _lum(bg)))


def draw_neon_sign(spec, rng):
    """Times-Square rooftop sign: near-black panel, bulb border, tube text."""
    w, h = spec["size"]
    pal = spec["palette"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    d.rectangle([0, 0, w - 1, h - 1], fill=pal["border"])
    d.rectangle([8, 8, w - 9, h - 9], fill=pal["bg"])

    # Marquee bulbs around the frame, every other one lit.
    step = 30
    for i, x in enumerate(range(18, w - 14, step)):
        c = "#FFE9B0" if i % 2 == 0 else "#6B5A33"
        d.ellipse([x - 4, 0, x + 4, 8], fill=c)
        d.ellipse([x - 4, h - 9, x + 4, h - 1], fill=c)
    for i, y in enumerate(range(18, h - 14, step)):
        c = "#FFE9B0" if i % 2 == 0 else "#6B5A33"
        d.ellipse([0, y - 4, 8, y + 4], fill=c)
        d.ellipse([w - 9, y - 4, w - 1, y + 4], fill=c)

    inner_w = w - 60
    name_lines = wrap_two_lines(d, spec["name"], inner_w, start=32)
    slogan_font = fit_font(d, spec["slogan"], inner_w, start=16, floor=8)
    line_gap = 12
    name_h = sum(f.size for _, f in name_lines) + line_gap * (len(name_lines) - 1)
    total_h = name_h + 18 + slogan_font.size
    y = (h - total_h) // 2
    for text, font in name_lines:
        neon_text(img, ((w - d.textlength(text, font=font)) // 2, y),
                  text, font, hex_rgb(pal["text"]))
        y += font.size + line_gap
    y += 18 - line_gap
    neon_text(img, ((w - d.textlength(spec["slogan"], font=slogan_font)) // 2, y),
              spec["slogan"], slogan_font, hex_rgb(pal["accent"]))
    return img


BOARD_W, BOARD_H = 480, 130

# Band panels tile edge to edge across the whole 4096px strip. 512 divides
# 4096 exactly, which is what lets the run continue through the wrap seam —
# the last panel meets the first one and the wall never shows an end.
BAND_W = 512
BAND_H = {"rink": 150, "broadway": 320}


def draw_band_panel(spec, variant, style):
    """One segment of a continuous advertising wall.

    'rink'     — low hockey-boards panel, alternating white/brand.
    'broadway' — tall lit sign: dark panel, marquee bulbs, neon-ish type.
    """
    w, h = BAND_W, BAND_H[style]
    pal = spec["palette"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    if style == "rink":
        if variant == "w":
            bg, txt, accent = "#F2EFE6", pal["bg"], pal["border"]
        else:
            bg, txt, accent = pal["bg"], pal["text"], pal["accent"]
        d.rectangle([0, 0, w - 1, h - 1], fill=bg)
        d.rectangle([0, 0, w - 1, 10], fill=accent)              # top rail
        d.rectangle([0, h - 11, w - 1, h - 1], fill=accent)      # kick plate
        name = " ".join(spec["name"].split()[:2])
        font = fit_font(d, name, w - 150, start=44, floor=16)
        d.text((26, (h - font.size) // 2 - 6), name, font=font, fill=txt)
        sf = fit_font(d, spec["slogan"], w - 150, start=16, floor=10)
        # Slogan in a faded headline colour, not the accent: several accents
        # sit too close to their own panel colour to stay legible at this size.
        d.text((26, (h + font.size) // 2 + 2), spec["slogan"], font=sf,
               fill=mix(txt, bg, 0.30))
        d.rectangle([w - 108, 26, w - 22, h - 27], fill=accent)  # brand block
        initial = name[0]
        f2 = fit_font(d, initial, 70, start=56, floor=28)
        d.text((w - 65 - d.textlength(initial, font=f2) // 2,
                (h - f2.size) // 2), initial, font=f2, fill=bg)
        return img

    # broadway: a lit sign in the wall. The panel is always dark — brands with
    # a pale palette (gold, cream) would otherwise wash the neon out entirely —
    # so the brand colour survives only as a faint tint and in the tubes.
    d.rectangle([0, 0, w - 1, h - 1], fill="#1A1822")
    d.rectangle([10, 10, w - 11, h - 11],
                fill=mix(pal["bg"], "#0E0D14", 0.84) if variant == "c" else "#12111A")
    for i, x in enumerate(range(22, w - 16, 34)):                # marquee bulbs
        c = "#FFE9B0" if i % 2 == 0 else "#6B5A33"
        d.ellipse([x - 5, 2, x + 5, 12], fill=c)
        d.ellipse([x - 5, h - 13, x + 5, h - 3], fill=c)
    for i, y in enumerate(range(24, h - 18, 34)):
        c = "#FFE9B0" if i % 2 == 0 else "#6B5A33"
        d.ellipse([2, y - 5, 12, y + 5], fill=c)
        d.ellipse([w - 13, y - 5, w - 3, y + 5], fill=c)

    inner = w - 70
    lines = wrap_two_lines(d, spec["name"], inner, start=56)
    sf = fit_font(d, spec["slogan"], inner, start=22, floor=12)
    gap = 12
    total = sum(f.size for _, f in lines) + gap * (len(lines) - 1) + 20 + sf.size
    y = (h - total) // 2
    for text, font in lines:
        neon_text(img, ((w - d.textlength(text, font=font)) // 2, y),
                  text, font, hex_rgb(pal["text"]))
        y += font.size + gap
    y += 20 - gap
    neon_text(img, ((w - d.textlength(spec["slogan"], font=sf)) // 2, y),
              spec["slogan"], sf, hex_rgb(pal["accent"]))
    return img


def draw_board(spec, variant):
    """Stadium perimeter board — the low wide panels lining a hockey rink.
    variant 'w' = white panel with brand-coloured text (the classic look),
    variant 'c' = brand-coloured panel, so a run of boards alternates."""
    pal = spec["palette"]
    img = Image.new("RGBA", (BOARD_W, BOARD_H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if variant == "w":
        bg, txt, accent = "#F2EFE6", pal["bg"], pal["border"]
    else:
        bg, txt, accent = pal["bg"], pal["text"], pal["accent"]
    d.rectangle([0, 0, BOARD_W - 1, BOARD_H - 1], fill=bg)
    d.rectangle([0, 0, BOARD_W - 1, 8], fill=accent)                  # top rail
    d.rectangle([0, BOARD_H - 9, BOARD_W - 1, BOARD_H - 1], fill=accent)  # kick plate

    # Perimeter boards carry a compact name; the full one rarely fits the format.
    name = " ".join(spec["name"].split()[:2])
    font = fit_font(d, name, BOARD_W - 190, start=32, floor=16)
    d.text((28, (BOARD_H - font.size) // 2 - 8), name, font=font, fill=txt)
    slogan_font = fit_font(d, spec["slogan"], BOARD_W - 56, start=8, floor=8)
    d.text((28, (BOARD_H + font.size) // 2 + 2), spec["slogan"], font=slogan_font, fill=accent)
    # Right-side brand block keeps the panel from looking empty.
    d.rectangle([BOARD_W - 120, 22, BOARD_W - 24, BOARD_H - 23], fill=accent)
    initial = name[0]
    f2 = fit_font(d, initial, 80, start=64, floor=32)
    d.text((BOARD_W - 72 - d.textlength(initial, font=f2) // 2,
            (BOARD_H - f2.size) // 2), initial, font=f2, fill=bg)
    return img


def draw_panel(spec, variant, w, h, style):
    """One advertising panel at an arbitrary size.

    Rendered in memory rather than to a file: the tower builder needs many
    width/height/style combinations and caching them all as sprites would be a
    combinatorial mess.

      board   painted tin panel, headline + slogan + initial block
      neon    dark lit panel, marquee bulbs, glowing tube type
      poster  the SDXL print creative for this brand, fitted and framed
    """
    pal = spec["palette"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    if style == "poster":
        src = OUT_DIR / f"ad_{spec['id']}.png"
        if src.exists():
            art = Image.open(src).convert("RGBA")
            # Contain, not cover: the print creative carries its own headline
            # along the bottom edge, and cropping to fill slices it off. The
            # letterbox becomes a mat, which is how a poster is mounted anyway.
            mat = mix(pal["bg"], "#151219", 0.55)
            d.rectangle([0, 0, w - 1, h - 1], fill=mat)
            s = min((w - 16) / art.width, (h - 16) / art.height)
            art = art.resize((max(1, int(art.width * s)), max(1, int(art.height * s))),
                             Image.LANCZOS)
            img.alpha_composite(art, ((w - art.width) // 2, (h - art.height) // 2))
            d.rectangle([0, 0, w - 1, h - 1], outline="#1C150F", width=max(3, w // 90))
            return img
        style = "board"      # print creative not generated yet — fall back

    if style == "neon":
        d.rectangle([0, 0, w - 1, h - 1], fill="#1A1822")
        d.rectangle([8, 8, w - 9, h - 9],
                    fill=mix(pal["bg"], "#0E0D14", 0.84) if variant == "c" else "#12111A")
        step = max(26, w // 14)
        for i, x in enumerate(range(16, w - 12, step)):
            c = "#FFE9B0" if i % 2 == 0 else "#6B5A33"
            d.ellipse([x - 4, 1, x + 4, 9], fill=c)
            d.ellipse([x - 4, h - 10, x + 4, h - 2], fill=c)
        inner = w - 44
        lines = wrap_two_lines(d, spec["name"], inner, start=max(20, h // 4))
        sf = fit_font(d, spec["slogan"], inner, start=max(12, h // 11), floor=10)
        gap = 8
        total = sum(f.size for _, f in lines) + gap * (len(lines) - 1) + 14 + sf.size
        y = (h - total) // 2
        for text, font in lines:
            neon_text(img, ((w - d.textlength(text, font=font)) // 2, y),
                      text, font, hex_rgb(pal["text"]))
            y += font.size + gap
        y += 14 - gap
        neon_text(img, ((w - d.textlength(spec["slogan"], font=sf)) // 2, y),
                  spec["slogan"], sf, hex_rgb(pal["accent"]))
        return img

    # board
    if variant == "w":
        bg = "#F2EFE6"
        txt = ink_for(bg, pal)
        accent = pal["border"] if _lum(pal["border"]) < 200 else txt
    else:
        bg, txt, accent = pal["bg"], ink_for(pal["bg"], pal), pal["accent"]
    d.rectangle([0, 0, w - 1, h - 1], fill=bg)
    rail = max(6, h // 16)
    d.rectangle([0, 0, w - 1, rail], fill=accent)
    d.rectangle([0, h - rail - 1, w - 1, h - 1], fill=accent)
    inner = w - 32
    lines = wrap_two_lines(d, spec["name"], inner, start=max(20, h // 3))
    sf = fit_font(d, spec["slogan"], inner, start=max(11, h // 10), floor=9)
    gap = 6
    total = sum(f.size for _, f in lines) + gap * (len(lines) - 1) + 10 + sf.size
    y = (h - total) // 2
    for text, font in lines:
        d.text(((w - d.textlength(text, font=font)) // 2, y), text, font=font, fill=txt)
        y += font.size + gap
    y += 10 - gap
    d.text(((w - d.textlength(spec["slogan"], font=sf)) // 2, y),
           spec["slogan"], font=sf, fill=mix(txt, bg, 0.30))
    return img


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    rng = random.Random(4096)
    for spec in BRANDS["signs"]:
        if spec.get("style") == "neon":
            img = draw_neon_sign(spec, rng)
        else:
            img = draw_sign(spec, rng)
        out = OUT_DIR / f"ad_{spec['id']}.png"
        img.save(out)
        print(f"{out.name}  {img.size[0]}x{img.size[1]}")
        for variant in ("w", "c"):
            draw_board(spec, variant).save(OUT_DIR / f"board_{spec['id']}_{variant}.png")
            for style in BAND_H:
                draw_band_panel(spec, variant, style).save(
                    OUT_DIR / f"band_{style}_{spec['id']}_{variant}.png")


if __name__ == "__main__":
    main()
