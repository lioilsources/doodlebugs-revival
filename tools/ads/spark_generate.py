#!/usr/bin/env python3
"""Renders on SPARK's ComfyUI (DGX GB10) over the native API.

Follows the Ol1nLLM playbook (backend/comfyui/README.md): POST /prompt to
enqueue, poll GET /history/{id}, download via GET /view. On the LAN the API
at 192.168.88.66:8188 is reachable directly; the public comfyui.ol1n.com
sits behind Cloudflare Access and is not needed from here.

Default job renders the DOODLE AIR airliner background — the rights-clean
replacement for the Smartwings photo — as a two-pass hi-res Juggernaut v9
render (txt2img 1536x1024 -> latent 2x -> 0.35 denoise refine = 3072x2048),
then typesets the fictional airline titles and scales to the 4096x2732
background format.

Usage:
  python3 tools/ads/spark_generate.py            # render + typeset, preview only
  python3 tools/ads/spark_generate.py --apply    # also overwrite Assets Smart_wings.png
"""
import argparse
import json
import time
import urllib.parse
import urllib.request
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

API = "http://192.168.88.66:8188"
HERE = Path(__file__).parent
ROOT = HERE.parents[1]
FONT = HERE / "fonts/AlfaSlabOne-Regular.ttf"
BG_PATH = ROOT / "Assets/Doodlebugs/Sprites/Background/Smart_wings.png"

PROMPT = ("professional aviation photograph, close-up side view of a clean white "
          "passenger airliner fuselage parked on sunny airport tarmac, long row of "
          "passenger windows, boarding stairs, clear blue summer sky, bright "
          "daylight, crisp photorealistic detail")
NEG = ("text, letters, lettering, words, logo, livery titles, watermark, "
       "people, faces, blurry, painting, illustration")


def workflow(seed):
    return {
        "1": {"class_type": "CheckpointLoaderSimple",
              "inputs": {"ckpt_name": "Juggernaut-XL_v9_RunDiffusionPhoto_v2.safetensors"}},
        "2": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["1", 1], "text": PROMPT}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["1", 1], "text": NEG}},
        "4": {"class_type": "EmptyLatentImage",
              "inputs": {"width": 1536, "height": 1024, "batch_size": 1}},
        "5": {"class_type": "KSampler",
              "inputs": {"model": ["1", 0], "positive": ["2", 0], "negative": ["3", 0],
                         "latent_image": ["4", 0], "seed": seed, "steps": 32, "cfg": 5.5,
                         "sampler_name": "dpmpp_2m", "scheduler": "karras", "denoise": 1.0}},
        "6": {"class_type": "LatentUpscaleBy",
              "inputs": {"samples": ["5", 0], "upscale_method": "bislerp", "scale_by": 2.0}},
        "7": {"class_type": "KSampler",
              "inputs": {"model": ["1", 0], "positive": ["2", 0], "negative": ["3", 0],
                         "latent_image": ["6", 0], "seed": seed + 1, "steps": 18, "cfg": 5.0,
                         "sampler_name": "dpmpp_2m", "scheduler": "karras", "denoise": 0.35}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["7", 0], "vae": ["1", 2]}},
        "9": {"class_type": "SaveImage",
              "inputs": {"images": ["8", 0], "filename_prefix": "doodle_air"}},
    }


def api(path, payload=None):
    req = urllib.request.Request(API + path,
                                 data=json.dumps(payload).encode() if payload else None,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read())


def render(seed):
    pid = api("/prompt", {"prompt": workflow(seed), "client_id": "doodlebugs"})["prompt_id"]
    print(f"queued on SPARK: {pid}")
    while True:
        time.sleep(3)
        hist = api(f"/history/{pid}")
        if pid not in hist:
            continue
        entry = hist[pid]
        status = entry.get("status", {})
        if status.get("status_str") == "error":
            raise SystemExit(f"SPARK execution error: {json.dumps(status)[:500]}")
        outputs = entry.get("outputs", {})
        if outputs:
            img_info = next(iter(outputs.values()))["images"][0]
            q = urllib.parse.urlencode({"filename": img_info["filename"],
                                        "subfolder": img_info.get("subfolder", ""),
                                        "type": img_info.get("type", "output")})
            with urllib.request.urlopen(f"{API}/view?{q}", timeout=120) as r:
                raw = HERE / "art" / "doodle_air_raw.png"
                raw.write_bytes(r.read())
            print(f"downloaded {raw} ")
            return Image.open(raw).convert("RGB")


def typeset(img):
    """Fictional airline titles along the fuselage, then scale to 4096x2732."""
    img = img.resize((4096, 2732), Image.LANCZOS).convert("RGBA")
    d = ImageDraw.Draw(img)
    text = "DOODLE AIR"
    font = ImageFont.truetype(str(FONT), 340)
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    x = (img.width - ld.textlength(text, font=font)) // 2
    # Airline titles ride the upper fuselage; soft shadow seats them on the hull.
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).text((x + 8, 908), text, font=font, fill=(10, 20, 40, 110))
    layer.alpha_composite(sh.filter(ImageFilter.GaussianBlur(8)))
    ld.text((x, 900), text, font=font, fill="#1B4E9B")
    layer = layer.rotate(-2.5, resample=Image.BICUBIC, center=(img.width // 2, 1070))
    img.alpha_composite(layer)
    return img.convert("RGB")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="overwrite the Assets background")
    ap.add_argument("--seed", type=int, default=2732)
    args = ap.parse_args()

    (HERE / "art").mkdir(exist_ok=True)
    img = typeset(render(args.seed))
    out = HERE / "out" / "doodle_air_background.png"
    out.parent.mkdir(exist_ok=True)
    img.save(out)
    print(f"preview -> {out}")
    if args.apply:
        img.save(BG_PATH)
        print(f"applied -> {BG_PATH}  (Smartwings photo replaced)")


if __name__ == "__main__":
    main()
