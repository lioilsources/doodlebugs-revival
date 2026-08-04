#!/usr/bin/env python3
"""Photoreal / print-grade ad creative: SDXL art + programmatic typography.

Diffusion models cannot spell, so the pipeline splits the job the way a real
print shop does: Juggernaut-XL-Lightning (local ComfyUI checkpoint) paints the
poster ART with no lettering, then Pillow typesets the headline and slogan in
Alfa Slab One (OFL). Painted brands get a full-bleed poster panel; neon brands
get a moody dark artwork with glowing channel letters on top.

Art is cached in tools/ads/art/<id>.png — delete a file to force a repaint.
Output overwrites tools/ads/sprites/ad_<id>.png, so the normal compose step
picks the print creative up unchanged.

Usage:
  /Volumes/YOTTA/Documents/ComfyUI/.venv/bin/python3 tools/ads/generate_print_ads.py [--only id]
"""
import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).parent
BRANDS = json.loads((HERE / "brands.json").read_text())
ART_DIR = HERE / "art"
OUT_DIR = HERE / "sprites"
FONT = HERE / "fonts/AlfaSlabOne-Regular.ttf"
CHECKPOINT = "/Volumes/YOTTA/Ai/ComfyUI/models/checkpoints/Juggernaut-XL-Lightning_4Steps.safetensors"

NEG = ("text, letters, words, typography, watermark, signature, logo, "
       "frame, border, people, photo of a sign")
STYLE = ("vintage 1930s advertising poster illustration, gouache painting, "
         "rich warm colours, soft studio light, aged paper grain, "
         "centred hero composition, no text")
NEON_STYLE = ("moody night scene, near-black backdrop, cinematic rim light, "
              "dark art deco atmosphere, subtle bokeh, no text")

ART_PROMPTS = {
    "doodle_cola":        f"{STYLE}, glass cola bottle with condensation droplets, radiant sunburst rays behind it",
    "cloud_nine_gum":     f"{STYLE}, sticks of chewing gum floating among fluffy pastel clouds",
    "piston_pete":        f"{STYLE}, gleaming spark plug hero shot, art deco engine parts, electric sparks",
    "ace_academy":        f"{STYLE}, yellow biplane looping through a bright sky, smoke trail spiral",
    "turbulence_mutual":  f"{STYLE}, small biplane flying under a dramatic storm cloud, umbrella motif",
    "goggles_sons":       f"{STYLE}, brass aviator goggles with leather strap, product hero shot",
    "dirigible_express":  f"{STYLE}, majestic silver airship sailing over cloud tops at dawn",
    "hangar_hotel":       f"{STYLE}, cosy aircraft hangar interior with warm lamplight and a parked biplane",
    "mountain_mule":      f"{STYLE}, sturdy leather hiking boot standing on a granite peak, alpine background",
    "sierra_sarsaparilla":f"{STYLE}, frosted soda bottle in front of snowy mountain peaks, cool blue palette",
    "desert_dew":         f"{STYLE}, soda bottle silhouetted against a saguaro desert sunset, orange sky",
    "el_sombrero":        f"{STYLE}, wide embroidered sombrero, radiating serape stripes background",
    "seagull_ice":        f"{STYLE}, ice cream cone with a seagull perched on top, seaside boardwalk",
    "punctual_watches":   f"{STYLE}, ornate pocket watch with roman numerals, product hero shot",
    "propwash_flakes":    f"{STYLE}, cardboard soap box spilling white soap flakes like snow",
    "griffon_motors":     f"{NEON_STYLE}, chrome grille of a vintage luxury motorcar emerging from shadow",
    "talkbox":            f"{NEON_STYLE}, antique 1920s upright pedestal telephone with tall brass stem, cone mouthpiece and side-hung earpiece receiver, on a desk, single warm spotlight",
    "glint_diamonds":     f"{NEON_STYLE}, brilliant cut diamond on black velvet, prismatic sparkle",
}


def load_pipe():
    import torch
    from diffusers import StableDiffusionXLPipeline, EulerDiscreteScheduler
    # float16 overflows to NaN in the UNet on MPS (black frames). bfloat16
    # keeps float32's exponent range at half the memory, which is what a
    # 16 GB M2 needs for SDXL.
    pipe = StableDiffusionXLPipeline.from_single_file(CHECKPOINT, torch_dtype=torch.bfloat16)
    # Lightning models want trailing-spacing Euler and near-1 guidance.
    pipe.scheduler = EulerDiscreteScheduler.from_config(
        pipe.scheduler.config, timestep_spacing="trailing")
    pipe.to("mps")
    pipe.vae = pipe.vae.to(torch.float32)
    # NO attention slicing: it is a notorious source of NaN frames on MPS,
    # and bf16 SDXL at ~1MP fits a 16 GB M2 without it.
    return pipe


def gen_art(pipe, spec_id, seed):
    out = ART_DIR / f"{spec_id}.png"
    if out.exists():
        return Image.open(out).convert("RGB")
    import torch
    g = torch.Generator("mps").manual_seed(seed)
    img = pipe(prompt=ART_PROMPTS[spec_id], negative_prompt=NEG,
               width=1024, height=640, num_inference_steps=5,
               guidance_scale=1.0, generator=g).images[0]
    ART_DIR.mkdir(exist_ok=True)
    img.save(out)
    return img


def fit(d, text, max_w, start, floor=14):
    size = start
    while size > floor:
        f = ImageFont.truetype(str(FONT), size)
        if d.textlength(text, font=f) <= max_w:
            return f
        size -= 2
    return ImageFont.truetype(str(FONT), floor)


def shadow_text(img, xy, text, font, fill, blur=4, offset=3):
    """Print-style drop shadow keeps type readable over painted art."""
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).text((xy[0] + offset, xy[1] + offset), text, font=font, fill=(20, 12, 6, 200))
    img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(blur)))
    ImageDraw.Draw(img).text(xy, text, font=font, fill=fill)


def make_painted(spec, art):
    """Full-bleed poster: art fills the panel, typography set over it."""
    w, h = spec["size"]
    pal = spec["palette"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 255))
    scale = max(w / art.width, h / art.height)
    a = art.resize((int(art.width * scale) + 1, int(art.height * scale) + 1), Image.LANCZOS)
    img.paste(a, ((w - a.width) // 2, (h - a.height) // 2))
    d = ImageDraw.Draw(img)

    # Darkened band at the bottom carries the type, like a printed poster strap.
    band_h = int(h * 0.40)
    grad = Image.new("L", (1, band_h))
    for y in range(band_h):
        grad.putpixel((0, y), int(200 * (y / band_h) ** 1.4))
    img.alpha_composite(Image.merge("RGBA", [Image.new("L", (w, band_h), 8)] * 3
                                    + [grad.resize((w, band_h))]), (0, h - band_h))

    name_font = fit(d, spec["name"], w - 44, start=int(h * 0.19))
    slog_font = fit(d, spec["slogan"], w - 60, start=max(15, int(h * 0.085)))
    ny = h - band_h + int(band_h * 0.16)
    shadow_text(img, ((w - d.textlength(spec["name"], font=name_font)) // 2, ny),
                spec["name"], name_font, "#F4E9CF")
    sy = ny + name_font.size + int(h * 0.035)
    shadow_text(img, ((w - d.textlength(spec["slogan"], font=slog_font)) // 2, sy),
                spec["slogan"], slog_font, pal.get("accent", "#C9A227"), blur=3, offset=2)

    # Printed frame.
    d.rectangle([0, 0, w - 1, h - 1], outline="#1C150F", width=6)
    d.rectangle([8, 8, w - 9, h - 9], outline="#E8DCC0", width=2)
    return img


def make_neon(spec, art):
    """Dark artwork with glowing channel letters — a photoreal Times Square sign."""
    w, h = spec["size"]
    pal = spec["palette"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 255))
    scale = max(w / art.width, h / art.height)
    a = art.resize((int(art.width * scale) + 1, int(art.height * scale) + 1), Image.LANCZOS)
    img.paste(a, ((w - a.width) // 2, (h - a.height) // 2))
    # Dim the art so the tubes carry the sign.
    img.alpha_composite(Image.new("RGBA", (w, h), (0, 0, 4, 120)))
    d = ImageDraw.Draw(img)

    def tube(text, font, color, y):
        x = (w - d.textlength(text, font=font)) // 2
        glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        ImageDraw.Draw(glow).text((x, y), text, font=font, fill=color)
        for r in (14, 7):
            img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(r)))
        core = tuple(min(255, c + 100) for c in color[:3]) + (255,)
        d.text((x, y), text, font=font, fill=core)

    def rgb(hx):
        hx = hx.lstrip("#")
        return tuple(int(hx[i:i + 2], 16) for i in (0, 2, 4))

    name_font = fit(d, spec["name"], w - 56, start=int(h * 0.21))
    slog_font = fit(d, spec["slogan"], w - 70, start=max(15, int(h * 0.09)))
    total = name_font.size + int(h * 0.07) + slog_font.size
    ny = (h - total) // 2
    tube(spec["name"], name_font, rgb(pal["text"]), ny)
    tube(spec["slogan"], slog_font, rgb(pal["accent"]), ny + name_font.size + int(h * 0.07))

    # Steel frame + marquee bulbs.
    d.rectangle([0, 0, w - 1, h - 1], outline="#2A2833", width=8)
    for i, x in enumerate(range(20, w - 16, 30)):
        c = "#FFE9B0" if i % 2 == 0 else "#5A4C2C"
        d.ellipse([x - 4, 2, x + 4, 10], fill=c)
        d.ellipse([x - 4, h - 11, x + 4, h - 3], fill=c)
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default=None, help="regenerate a single brand id")
    args = ap.parse_args()

    specs = [s for s in BRANDS["signs"] if not args.only or s["id"] == args.only]
    pipe = None
    for i, spec in enumerate(specs):
        if not (ART_DIR / f"{spec['id']}.png").exists() and pipe is None:
            print("loading Juggernaut-XL-Lightning...", flush=True)
            pipe = load_pipe()
        # Seed derives from the id via a stable digest — hash() is salted per
        # process and would repaint everything on every run.
        seed = 1000 + int.from_bytes(spec["id"].encode(), "little") % 9000
        art = gen_art(pipe, spec["id"], seed)
        img = make_neon(spec, art) if spec.get("style") == "neon" else make_painted(spec, art)
        img.save(OUT_DIR / f"ad_{spec['id']}.png")
        print(f"[{i + 1}/{len(specs)}] ad_{spec['id']}.png", flush=True)


if __name__ == "__main__":
    main()
