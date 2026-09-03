#!/usr/bin/env python3
"""Arena background + parallax foreground generator on SPARK's ComfyUI.

Three render branches, one review step, one apply step:

  bg       FLUX.1-dev txt2img per (theme x style x seed) from themes.py,
           4x-UltraSharp on SPARK, delivered as 4096x2732 PNG (drop-in size).
  restyle  FLUX Kontext repaints the photographs already in Assets (Sky,
           Rainbow, Sunny_beach...) in a style block - same output format.
  fg       Two passes. (A) FLUX.1-dev renders a terrain cutout on white at
           2048x640. (B) the render is rolled by half its width so the wrap
           seam sits in the middle, FLUX Fill repaints a band across it, and
           the result is upscaled and keyed to alpha with RMBG-2.0 - all on
           SPARK. A rolled loop is still a loop, so nothing rolls back.
           Locally: solid ground fill, crop to content, cap at FG_MAX_HEIGHT
           -> 4096 px wide strip with bottom-anchored terrain, exactly what
           ForegroundScroller tiles (Sprites/Foreground/<Name>_fg.png).
  sheet    Thumbnails + out/index.html to pick from.
  apply    Copy a pick (bg and optionally fg) into Assets under a map name;
           finish with Unity -> Doodlebugs -> Sync Background Profiles.

Talks to ComfyUI the same way tools/ads/spark_generate.py does (POST /prompt,
poll /history, GET /view). One job in flight (see INFLIGHT for why not
two). Existing outputs are skipped, so a killed batch resumes.

Usage:
  python3 tools/backgrounds/spark_backgrounds.py bg                    # all themes x DEFAULT_STYLES
  python3 tools/backgrounds/spark_backgrounds.py bg --themes jungle,space --styles artdeco --seeds 2
  python3 tools/backgrounds/spark_backgrounds.py restyle --styles artdeco,ukiyoe
  python3 tools/backgrounds/spark_backgrounds.py fg --themes jungle --styles papercut
  python3 tools/backgrounds/spark_backgrounds.py sheet
  python3 tools/backgrounds/spark_backgrounds.py apply jungle__artdeco__s1101 --as Jungle --fg jungle__papercut__s1101
  python3 tools/backgrounds/spark_backgrounds.py bg --dry-run           # print the prompts only
"""
import argparse
import io
import json
import mimetypes
import os
import shutil
import sys
import time
import urllib.parse
import urllib.request
import uuid
from collections import deque
from pathlib import Path

from PIL import Image, ImageChops

sys.path.insert(0, str(Path(__file__).resolve().parent))
import themes as T  # noqa: E402

API = "http://192.168.88.66:8188"
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OUT = HERE / "out"
ASSETS_BG = ROOT / "Assets/Doodlebugs/Sprites/Background"
ASSETS_FG = ROOT / "Assets/Doodlebugs/Sprites/Foreground"

BG_W, BG_H = 4096, 2732
FG_W = 4096
FG_RENDER = (FG_W, 1280)       # strip size delivered by SPARK (2x GEN_FG)
GEN_BG = (1536, 1024)           # FLUX native 3:2, same ratio as 4096x2732
GEN_FG = (2048, 640)            # FLUX native for the terrain strip
SEAM_BAND = 320                 # px repainted across the wrap by FLUX Fill
SEAM = 256                      # fallback: px cross-faded when --seam blend

FLUX = "flux1-dev.safetensors"
KONTEXT = "flux1-dev-kontext_fp8_scaled.safetensors"
FILL = "flux1-fill-dev-fp8.safetensors"

# SDXL family for the bg/fg branches (restyle stays Flux Kontext - no SDXL
# equivalent in the checkpoint list). Illustrious is an illustration/anime
# checkpoint: strong flat-shaded landscape art, but drifts toward figures
# unless steered off with "no humans" + a same-family negative. Juggernaut
# is RunDiffusion's photoreal merge; included per request to see whether it
# still earns its keep outside portraits - landscapes are in its training
# mix, just not its main draw. Lightning is the same Juggernaut lineage
# distilled to 4 steps: same aesthetic, ~5x faster, noisier at our step count.
CHECKPOINTS = {
    "illustrious": dict(ckpt="Illustrious-XL-v2.0.safetensors", steps=28, cfg=6.0,
                        sampler="euler_ancestral", scheduler="normal",
                        prefix="scenery, background art, no humans, ",
                        neg_extra="humans, people, figures, worst quality, low quality, "),
    "juggernaut": dict(ckpt="Juggernaut-XL_v9_RunDiffusionPhoto_v2.safetensors",
                       steps=30, cfg=5.5, sampler="dpmpp_2m", scheduler="karras",
                       prefix="", neg_extra="blurry, "),
    "juggernaut-lightning": dict(ckpt="Juggernaut-XL-Lightning_4Steps.safetensors",
                                 steps=8, cfg=2.0, sampler="dpmpp_sde",
                                 scheduler="sgm_uniform", prefix="", neg_extra="blurry, "),
}
UPSCALER = "4x-UltraSharp.pth"


# --------------------------------------------------------------------- API --
def api(path, payload=None, timeout=60):
    req = urllib.request.Request(
        API + path,
        data=json.dumps(payload).encode() if payload is not None else None,
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        body = r.read()
        return json.loads(body) if body else {}


def upload(path):
    """POST /upload/image (multipart) -> filename usable by LoadImage."""
    boundary = uuid.uuid4().hex
    data = Path(path).read_bytes()
    ctype = mimetypes.guess_type(str(path))[0] or "application/octet-stream"
    body = io.BytesIO()
    for name, value in (("overwrite", "true"), ("type", "input")):
        body.write(f"--{boundary}\r\nContent-Disposition: form-data; "
                   f"name=\"{name}\"\r\n\r\n{value}\r\n".encode())
    body.write(f"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"; "
               f"filename=\"{Path(path).name}\"\r\nContent-Type: {ctype}\r\n\r\n".encode())
    body.write(data)
    body.write(f"\r\n--{boundary}--\r\n".encode())
    req = urllib.request.Request(
        API + "/upload/image", data=body.getvalue(),
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.loads(r.read())["name"]


def enqueue(graph):
    return api("/prompt", {"prompt": graph, "client_id": "doodlebugs-bg"})["prompt_id"]


def wait(pid, timeout=1800):
    t0 = time.time()
    while time.time() - t0 < timeout:
        time.sleep(2)
        hist = api(f"/history/{pid}")
        if pid not in hist:
            continue
        entry = hist[pid]
        status = entry.get("status", {})
        if status.get("status_str") == "error":
            msgs = [m for m in status.get("messages", []) if m[0] == "execution_error"]
            detail = msgs[0][1].get("exception_message", "") if msgs else ""
            raise RuntimeError(f"SPARK execution error: {detail[:600]}")
        if entry.get("outputs"):
            return entry["outputs"]
    raise TimeoutError(f"SPARK job {pid} did not finish in {timeout}s")


def fetch(info):
    q = urllib.parse.urlencode({"filename": info["filename"],
                                "subfolder": info.get("subfolder", ""),
                                "type": info.get("type", "output")})
    with urllib.request.urlopen(f"{API}/view?{q}", timeout=300) as r:
        return r.read()


def free_models():
    """Drop whatever the LRU cache still holds (video models from other jobs)."""
    try:
        api("/free", {"unload_models": True, "free_memory": True})
    except Exception as e:  # noqa: BLE001 - best effort
        print(f"(free failed: {e})")


# ------------------------------------------------------------------ graphs --
# FLUX runs at cfg 1, where a negative prompt is a no-op and "no text" reads
# as text. NAG (Normalized Attention Guidance) restores negatives on distilled
# models at ~1.3x the step cost - that is what keeps signatures, seals and
# titles off the prints and stray aircraft out of the sky.
NEG_BG = ("text, letters, lettering, words, title, caption, signature, artist "
          "name, seal, stamp, watermark, logo, frame, border, aircraft, "
          "airplane, birds, people")
NEG_FG = ("text, letters, signature, watermark, logo, sky, clouds, sun, frame, "
          "border, white outline, sticker outline")
NEG_RESTYLE = "text, letters, signature, seal, watermark, logo, frame, border, people"


def sdxl_graph(prompt, negative, size, seed, model_key):
    """Plain SDXL txt2img - one checkpoint, real negative prompt, no NAG
    needed (SDXL is not distilled, cfg > 1 already gives negatives teeth).
    Node 9 mirrors flux_graph's decoded-image slot so the same tails work."""
    spec = CHECKPOINTS[model_key]
    w, h = size
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": spec["ckpt"]}},
        "4": {"class_type": "CLIPTextEncode",
              "inputs": {"clip": ["1", 1], "text": spec["prefix"] + prompt}},
        "5": {"class_type": "CLIPTextEncode",
              "inputs": {"clip": ["1", 1], "text": spec["neg_extra"] + negative}},
        "7": {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}},
        "8": {"class_type": "KSampler",
              "inputs": {"model": ["1", 0], "positive": ["4", 0], "negative": ["5", 0],
                         "latent_image": ["7", 0], "seed": seed, "steps": spec["steps"],
                         "cfg": spec["cfg"], "sampler_name": spec["sampler"],
                         "scheduler": spec["scheduler"], "denoise": 1.0}},
        "9": {"class_type": "VAEDecode", "inputs": {"samples": ["8", 0], "vae": ["1", 2]}},
    }


def flux_graph(prompt, size, seed, steps, guidance, ref=None, negative=""):
    """FLUX.1-dev txt2img, or Kontext img2img when a reference is given.
    Node 9 is the decoded image; tails hang off it."""
    w, h = size
    g = {
        "1": {"class_type": "UNETLoader",
              "inputs": {"unet_name": KONTEXT if ref else FLUX,
                         # fp8 cast halves the 24 GB dev checkpoint; SPARK's
                         # unified memory is shared with the LLM containers.
                         "weight_dtype": "default" if ref else "fp8_e4m3fn"}},
        "2": {"class_type": "DualCLIPLoader",
              "inputs": {"clip_name1": "t5xxl_fp16.safetensors",
                         "clip_name2": "clip_l.safetensors", "type": "flux"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": "ae.safetensors"}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 0], "text": prompt}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 0], "text": negative}},
        "6": {"class_type": "FluxGuidance",
              "inputs": {"conditioning": ["4", 0], "guidance": guidance}},
        "7": {"class_type": "EmptySD3LatentImage",
              "inputs": {"width": w, "height": h, "batch_size": 1}},
        "8": {"class_type": "KSampler",
              "inputs": {"model": ["1", 0], "positive": ["6", 0], "negative": ["5", 0],
                         "latent_image": ["7", 0], "seed": seed, "steps": steps,
                         "cfg": 1.0, "sampler_name": "euler", "scheduler": "simple",
                         "denoise": 1.0}},
        "9": {"class_type": "VAEDecode", "inputs": {"samples": ["8", 0], "vae": ["3", 0]}},
    }
    if negative:
        g["15"] = {"class_type": "NAGuidance",
                   "inputs": {"model": ["1", 0], "nag_scale": 5.0, "nag_alpha": 0.5,
                              "nag_tau": 1.5}}
        g["8"]["inputs"]["model"] = ["15", 0]
    if ref:
        g["20"] = {"class_type": "LoadImage", "inputs": {"image": ref}}
        g["21"] = {"class_type": "FluxKontextImageScale", "inputs": {"image": ["20", 0]}}
        g["22"] = {"class_type": "VAEEncode", "inputs": {"pixels": ["21", 0], "vae": ["3", 0]}}
        g["23"] = {"class_type": "ReferenceLatent",
                   "inputs": {"conditioning": ["6", 0], "latent": ["22", 0]}}
        g["8"]["inputs"]["positive"] = ["23", 0]
    return g


def raw_tail(g, src, prefix):
    g["14"] = {"class_type": "SaveImage",
               "inputs": {"images": [src[0], src[1]], "filename_prefix": prefix}}
    return "14"


def seam_graph(prompt, ref, seed, steps):
    """FLUX Fill across the wrap of a GEN_FG strip already on SPARK (uploaded
    to input/). Roll by half the width (right half first), mask a centred
    band, inpaint, paste the band back onto the untouched pixels so the VAE
    round trip does not soften the rest. Node 28 is the seamless strip."""
    w, h = GEN_FG
    half = w // 2
    g = {
        "1": {"class_type": "UNETLoader",
              "inputs": {"unet_name": FILL, "weight_dtype": "default"}},
        "2": {"class_type": "DualCLIPLoader",
              "inputs": {"clip_name1": "t5xxl_fp16.safetensors",
                         "clip_name2": "clip_l.safetensors", "type": "flux"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": "ae.safetensors"}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 0], "text": prompt}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 0], "text": NEG_FG}},
        # Fill is trained for high guidance; 30 is the reference value.
        "6": {"class_type": "FluxGuidance",
              "inputs": {"conditioning": ["4", 0], "guidance": 30.0}},
        "20": {"class_type": "LoadImage", "inputs": {"image": ref}},
        "21": {"class_type": "ImageCrop",
               "inputs": {"image": ["20", 0], "width": half, "height": h, "x": half, "y": 0}},
        "22": {"class_type": "ImageCrop",
               "inputs": {"image": ["20", 0], "width": half, "height": h, "x": 0, "y": 0}},
        "23": {"class_type": "ImageStitch",
               "inputs": {"image1": ["21", 0], "image2": ["22", 0], "direction": "right",
                          "match_image_size": True, "spacing_width": 0,
                          "spacing_color": "white"}},
        "24": {"class_type": "SolidMask", "inputs": {"value": 0.0, "width": w, "height": h}},
        "25": {"class_type": "SolidMask",
               "inputs": {"value": 1.0, "width": SEAM_BAND, "height": h}},
        "26": {"class_type": "MaskComposite",
               "inputs": {"destination": ["24", 0], "source": ["25", 0],
                          "x": half - SEAM_BAND // 2, "y": 0, "operation": "add"}},
        "27": {"class_type": "InpaintModelConditioning",
               "inputs": {"positive": ["6", 0], "negative": ["5", 0], "vae": ["3", 0],
                          "pixels": ["23", 0], "mask": ["26", 0], "noise_mask": True}},
        "15": {"class_type": "NAGuidance",
               "inputs": {"model": ["1", 0], "nag_scale": 5.0, "nag_alpha": 0.5,
                          "nag_tau": 1.5}},
        "8": {"class_type": "KSampler",
              "inputs": {"model": ["15", 0], "positive": ["27", 0], "negative": ["27", 1],
                         "latent_image": ["27", 2], "seed": seed, "steps": steps,
                         "cfg": 1.0, "sampler_name": "euler", "scheduler": "simple",
                         "denoise": 1.0}},
        "9": {"class_type": "VAEDecode", "inputs": {"samples": ["8", 0], "vae": ["3", 0]}},
        "28": {"class_type": "ImageCompositeMasked",
               "inputs": {"destination": ["23", 0], "source": ["9", 0], "x": 0, "y": 0,
                          "resize_source": False, "mask": ["26", 0]}},
    }
    return g


def upscale_tail(g, src, size, prefix):
    """4x ESRGAN then lanczos to the exact delivery size. Returns save node id."""
    g["10"] = {"class_type": "UpscaleModelLoader", "inputs": {"model_name": UPSCALER}}
    g["11"] = {"class_type": "ImageUpscaleWithModel",
               "inputs": {"upscale_model": ["10", 0], "image": src}}
    g["12"] = {"class_type": "ImageScale",
               "inputs": {"image": ["11", 0], "upscale_method": "lanczos",
                          "width": size[0], "height": size[1], "crop": "disabled"}}
    g["13"] = {"class_type": "SaveImage",
               "inputs": {"images": ["12", 0], "filename_prefix": prefix}}
    return "13"


def rmbg_tail(g, src, prefix):
    """Key the white ground out on SPARK. Saves RGBA (unmatted colours) and
    the raw mask separately - the local finish uses both."""
    g["30"] = {"class_type": "RMBG",
               "inputs": {"image": src, "model": "RMBG-2.0", "sensitivity": 1.0,
                          "process_res": 2048, "mask_blur": 0, "mask_offset": 0,
                          "invert_output": False, "refine_foreground": True,
                          "background": "Alpha", "background_color": "#ffffff"}}
    g["31"] = {"class_type": "SaveImage",
               "inputs": {"images": ["30", 0], "filename_prefix": prefix + "_rgba"}}
    g["32"] = {"class_type": "MaskToImage", "inputs": {"mask": ["30", 1]}}
    g["33"] = {"class_type": "SaveImage",
               "inputs": {"images": ["32", 0], "filename_prefix": prefix + "_mask"}}
    # The RGB that went INTO RMBG: the local finish keys white from it and
    # unions that with RMBG's mask - RMBG alone drops low-contrast terrain.
    g["34"] = {"class_type": "SaveImage",
               "inputs": {"images": src, "filename_prefix": prefix + "_rgb"}}
    return {"rgba": "31", "mask": "33", "rgb": "34"}


# ------------------------------------------------------- foreground finish --
def cummax_down(mask):
    """Every pixel becomes the max of itself and everything above it, via
    log2(H) shift-and-lighten passes. That turns the keyed terrain outline
    into a solid ground: no see-through speckles where RMBG doubted a rock."""
    w, h = mask.size
    k = 1
    while k < h:
        shifted = Image.new("L", (w, h), 0)
        shifted.paste(mask.crop((0, 0, w, h - k)), (0, k))
        mask = ImageChops.lighter(mask, shifted)
        k *= 2
    return mask


def cummax_up(mask):
    """Mirror of cummax_down: every pixel becomes the max of itself and
    everything BELOW it ("is there any opaque pixel at or under me")."""
    w, h = mask.size
    k = 1
    while k < h:
        shifted = Image.new("L", (w, h), 0)
        shifted.paste(mask.crop((0, k, w, h)), (0, 0))
        mask = ImageChops.lighter(mask, shifted)
        k *= 2
    return mask


def blend_seam(img):
    """Cross-fade the last SEAM columns into the first SEAM so column W-SEAM-1
    meets column 0 without a step; width drops by SEAM (that is why the
    render is FG_W + SEAM wide)."""
    w, h = img.size
    left = img.crop((0, 0, SEAM, h))
    right = img.crop((w - SEAM, 0, w, h))
    # linear_gradient is black-top/white-bottom; a CCW quarter turn puts
    # black at x=0 -> take `right` there, white at x=SEAM -> take `left`.
    ramp = Image.linear_gradient("L").rotate(90, expand=True).resize((SEAM, h))
    head = Image.composite(left, right, ramp)
    out = Image.new(img.mode, (w - SEAM, h))
    out.paste(head, (0, 0))
    out.paste(img.crop((SEAM, 0, w - SEAM, h)), (SEAM, 0))
    return out


def row_coverage(mask):
    """Fraction of opaque pixels per row, as a list indexed by y."""
    col = mask.resize((1, mask.height), Image.BOX)
    return [v / 255 for v in col.get_flattened_data()]


def white_key(rgb):
    """Alpha from 'how far from the white ground': min(r,g,b) < 215 is
    solid terrain, > 245 is background, linear in between."""
    r, g, b = rgb.split()
    lum = ImageChops.darker(ImageChops.darker(r, g), b)
    return lum.point(lambda v: 255 if v < 215 else 0 if v > 245 else int(255 * (245 - v) / 30))


def unmatte_white(rgb, alpha):
    """colour = (matted - (1-a)*255) / a per channel, clamped. Solid pixels
    (a=255) pass through; fully transparent ones get a=1 in the divide so
    they stay finite (they get no alpha anyway).

    Pure ImageMath on F images: Pillow >= 11 hands `Image.point()` on I/F
    modes an ImagePointTransform instead of evaluating the lambda per value,
    so any comparison inside such a lambda raises - the per-pixel divide has
    to be expressed as image arithmetic, not a point LUT."""
    from PIL import ImageMath
    a = alpha.convert("F")
    out = []
    for ch in rgb.split():
        res = ImageMath.lambda_eval(
            lambda x: x["min"](x["max"]((x["c"] + x["a"] - 255.0) * 255.0
                                        / x["max"](x["a"], 1.0), 0.0), 255.0),
            c=ch.convert("F"), a=a)
        out.append(res.convert("L"))
    return Image.merge("RGB", out)


def finish_strip(rgba_bytes, mask_bytes, out_path, seam="fill", rgb_bytes=None):
    rgba = Image.open(io.BytesIO(rgba_bytes)).convert("RGBA")
    mask = Image.open(io.BytesIO(mask_bytes)).convert("L")
    if mask.size != rgba.size:
        mask = mask.resize(rgba.size, Image.LANCZOS)
    w, h = rgba.size

    # RMBG-2.0 is a salient-object model: on a low-contrast terrain strip it
    # keeps the hut and the darkest rocks and drops the grey ridge
    # (mountains__papercut, 2026-09-03). The render's ground IS white, so a
    # white key recovers everything RMBG dropped; RMBG still wins where the
    # terrain itself is white (arctic ice). Union of the two.
    if rgb_bytes is not None:
        rgb_src = Image.open(io.BytesIO(rgb_bytes)).convert("RGB")
        if rgb_src.size != rgba.size:
            rgb_src = rgb_src.resize(rgba.size, Image.LANCZOS)
        keyed = white_key(rgb_src)
        mask = ImageChops.lighter(mask, keyed)
        # Colour for pixels RMBG had thrown away (its RGB is black there),
        # un-matted against the white ground: an anti-aliased edge pixel is
        # (1-a)*white + a*colour in the render, so dividing that back out is
        # what stops the recovered outline from reading as a white halo.
        rmbg_kept = rgba.getchannel("A").point(lambda v: 255 if v > 8 else 0)
        unmatted = unmatte_white(rgb_src, keyed)
        rgb_fill = Image.composite(rgba.convert("RGB"), unmatted, rmbg_kept)
        rgba = rgb_fill.convert("RGBA")
        rgba.putalpha(mask)
    hard = mask.point(lambda v: 255 if v > 127 else 0)

    # The render leaves a white margin under the terrain and the keyed
    # colours are black wherever the mask is empty. Cut the strip where the
    # terrain body is continuous (last row >= 90 % opaque) so the floor is
    # real terrain, not a filled void.
    # Ground row from the SOFT mask: a render with a blurred, fading base
    # (city__papercut s1506) never reaches 90 % hard coverage except in a
    # thin roofline band, which cropped the strip to 55 px. Soft coverage
    # finds the real base; if even that leaves a sliver, keep everything and
    # let the floor band below carry it.
    cov = row_coverage(mask.point(lambda v: 255 if v > 32 else 0))
    ground = max((y for y, c in enumerate(cov) if c >= 0.9), default=h - 1)
    if ground + 1 < 0.3 * h:
        print(f"  (ground row {ground} of {h} too high - keeping full height)")
        ground = h - 1
    h = ground + 1
    rgba, mask, hard = (im.crop((0, 0, w, h)) for im in (rgba, mask, hard))

    # Solid ground WITHOUT pillars. The first version filled every column
    # from its topmost opaque pixel down, which turned the air under every
    # palm crown, roof and iceberg spike into a solid smear column (9 of 17
    # picks on 2026-09-03). Now: (a) fill only BELOW the lowest real pixel
    # of each column - the render's white margin under the terrain base -
    # (b) close small speckle holes inside the mass with a 9 px morphological
    # closing, never bridging real gaps, (c) force the floor band.
    from PIL import ImageFilter
    floor = Image.new("L", (w, h), 0)
    floor.paste(255, (0, int(h * 0.97), w, h))          # guaranteed floor band
    above = cummax_down(hard)                          # opaque somewhere at/above
    below = cummax_up(hard)                            # opaque somewhere at/below
    under_base = ImageChops.subtract(above, below)     # below the lowest real pixel
    # A column whose lowest opaque pixel sits in the upper half holds a
    # floater (cloud, sun, star, bird) and no terrain: filling under it made
    # the full-height grey columns in mountains/space/desert/dreamhouse.
    # Columns are judged by the height of their under-base run.
    col_run = under_base.resize((w, 1), Image.BOX)   # mean per column = run / h
    terrain_cols = col_run.point(lambda v: 255 if v < 0.5 * 255 else 0).resize((w, h), Image.NEAREST)
    under_base = ImageChops.multiply(under_base, terrain_cols)
    closed = hard.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.MinFilter(9))
    speckles = ImageChops.subtract(closed, hard)
    fill = ImageChops.lighter(ImageChops.lighter(under_base, speckles), floor)
    fill = ImageChops.lighter(fill, hard)
    alpha = ImageChops.lighter(mask, fill)
    rgb = rgba.convert("RGB")
    # Filled-in pixels have no colour of their own; propagate the nearest
    # terrain pixel above them all the way down (doubling shifts cover h).
    filled_only = ImageChops.subtract(fill, hard)
    if filled_only.getbbox():
        keep = ImageChops.invert(filled_only)   # 255 where the pixel is real
        # Colour source = solid AND not near-white: the render's fade-to-white
        # at the base must not be what the floor band inherits.
        r_, g_, b_ = rgb.split()
        dark_enough = ImageChops.darker(ImageChops.darker(r_, g_), b_).point(lambda v: 255 if v < 225 else 0)
        smear, known = rgb.copy(), ImageChops.multiply(hard, dark_enough)
        k = 1
        while k < h:
            sh_img = Image.new("RGB", (w, h))
            sh_img.paste(smear.crop((0, 0, w, h - k)), (0, k))
            sh_known = Image.new("L", (w, h), 0)
            sh_known.paste(known.crop((0, 0, w, h - k)), (0, k))
            take = ImageChops.subtract(sh_known, known)      # unknown here, known above
            smear = Image.composite(sh_img, smear, take)
            known = ImageChops.lighter(known, sh_known)
            k *= 2
        rgb = Image.composite(rgb, smear, keep)
    strip = rgb.copy()
    strip.putalpha(alpha)

    if seam == "blend":
        strip = blend_seam(strip)

    # Crop to the terrain top (16 px air), cap the height at FG_MAX_HEIGHT.
    bbox = strip.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox()
    top = max(0, (bbox[1] if bbox else 0) - 16)
    strip = strip.crop((0, top, strip.width, strip.height))
    if strip.height > T.FG_MAX_HEIGHT:
        strip = strip.resize((strip.width, T.FG_MAX_HEIGHT), Image.LANCZOS)
    if strip.width != FG_W:
        strip = strip.resize((FG_W, strip.height), Image.LANCZOS)
    strip.save(out_path, optimize=True)
    return strip


# ----------------------------------------------------------------- driver --
# Jobs kept queued on SPARK at once. 1, deliberately: 2 was meant to hide the
# download gap, but on the 2026-09-03 batch it halved throughput (fg-B 161 s
# per job with 2 in flight vs 84 s with 1 - memory pressure, not overlap) and
# then OOM-killed ComfyUI mid fg-B. Override with SPARK_INFLIGHT if a lighter
# graph ever benefits.
INFLIGHT = max(1, int(os.environ.get("SPARK_INFLIGHT", "1")))


def run_jobs(jobs, handler, label):
    """jobs: list of (id, graph). Keeps INFLIGHT queued; handler(id, outputs)."""
    todo = deque(jobs)
    inflight = deque()
    done = 0
    t_batch = time.time()
    while todo or inflight:
        while todo and len(inflight) < INFLIGHT:
            jid, graph = todo.popleft()
            inflight.append((jid, enqueue(graph), time.time()))
        jid, pid, t0 = inflight.popleft()
        try:
            outputs = wait(pid)
            handler(jid, outputs)
            done += 1
            print(f"[{done}/{len(jobs)}] {label} {jid}  {time.time() - t0:.0f}s", flush=True)
        except Exception as e:  # noqa: BLE001 - keep the batch going
            print(f"[FAIL] {label} {jid}: {e}", flush=True)
    print(f"{label}: {done}/{len(jobs)} ok in {(time.time() - t_batch) / 60:.1f} min", flush=True)


def thumb(img, out_path, width=640, backdrop=None):
    im = img.convert("RGBA")
    if backdrop:
        bg = Image.new("RGBA", im.size, backdrop)
        bg.alpha_composite(im)
        im = bg
    im = im.convert("RGB")
    im.thumbnail((width, width))
    out_path.parent.mkdir(parents=True, exist_ok=True)
    im.save(out_path, quality=78)


def style_seed(base, style, i):
    """One seed per (theme, style): the same seed across styles gives six
    near-identical compositions, and the point of the batch is choice."""
    return base + 101 * list(T.STYLES).index(style) + i


def pick_styles(arg):
    styles = arg.split(",") if arg else T.DEFAULT_STYLES
    unknown = [s for s in styles if s not in T.STYLES]
    if unknown:
        sys.exit(f"unknown style(s) {unknown}; have {list(T.STYLES)}")
    return styles


def pick_themes(arg):
    names = arg.split(",") if arg else list(T.THEMES)
    unknown = [n for n in names if n not in T.THEMES]
    if unknown:
        sys.exit(f"unknown theme(s) {unknown}; have {list(T.THEMES)}")
    return names


def gen_graph(model_key, a, prompt, size, seed, negative):
    """Dispatch to the resolved checkpoint (see themes.style_model)."""
    if model_key == "flux":
        return flux_graph(prompt, size, seed, a.steps, a.guidance, negative=negative)
    return sdxl_graph(prompt, negative, size, seed, model_key)


def cmd_bg(a):
    out_dir, rev = OUT / "bg", OUT / "review/bg"
    out_dir.mkdir(parents=True, exist_ok=True)
    jobs = []
    for theme in pick_themes(a.themes):
        base_seed = T.THEMES[theme]["seed"]
        for style in pick_styles(a.styles):
            for i in range(a.seeds):
                seed = style_seed(base_seed, style, i)
                model_key = T.style_model(style, a.model)
                tag = "" if model_key == "flux" else f"__{model_key}"
                jid = f"{theme}__{style}{tag}__s{seed}"
                if (out_dir / f"{jid}.png").exists():
                    continue
                prompt = T.bg_prompt(theme, style)
                if a.dry_run:
                    print(f"## {jid} [{model_key}]\n{prompt}\n")
                    continue
                g = gen_graph(model_key, a, prompt, GEN_BG, seed, NEG_BG)
                upscale_tail(g, ["9", 0], (BG_W, BG_H), f"dbg/bg_{jid}")
                jobs.append((jid, g))
    if a.dry_run:
        return
    free_models()

    def handler(jid, outputs):
        png = fetch(outputs["13"]["images"][0])
        (out_dir / f"{jid}.png").write_bytes(png)
        thumb(Image.open(io.BytesIO(png)), rev / f"{jid}.jpg")

    run_jobs(jobs, handler, "bg")


def cmd_restyle(a):
    out_dir, rev = OUT / "restyle", OUT / "review/restyle"
    out_dir.mkdir(parents=True, exist_ok=True)
    sources = a.sources.split(",") if a.sources else T.RESTYLE_SOURCES
    refs = {}
    for name in sources:
        src = ASSETS_BG / f"{name}.png"
        if not src.exists():
            sys.exit(f"missing source {src}")
        im = Image.open(src).convert("RGB")
        im.thumbnail((1536, 1536))                      # Kontext works at ~1MP anyway
        tmp = OUT / f"_ref_{name}.jpg"
        im.save(tmp, quality=92)
        refs[name] = upload(tmp)
    jobs = []
    for name in sources:
        for style in pick_styles(a.styles):
            jid = f"{name}__{style}"
            if (out_dir / f"{jid}.png").exists():
                continue
            prompt = T.RESTYLE_PROMPT.format(style=T.STYLES[style])
            if a.dry_run:
                print(f"## {jid}\n{prompt}\n")
                continue
            g = flux_graph(prompt, GEN_BG, a.seed, a.steps, a.guidance, ref=refs[name],
                           negative=NEG_RESTYLE)
            upscale_tail(g, ["9", 0], (BG_W, BG_H), f"dbg/re_{jid}")
            jobs.append((jid, g))
    if a.dry_run:
        return
    free_models()

    def handler(jid, outputs):
        png = fetch(outputs["13"]["images"][0])
        (out_dir / f"{jid}.png").write_bytes(png)
        thumb(Image.open(io.BytesIO(png)), rev / f"{jid}.jpg")

    run_jobs(jobs, handler, "restyle")


def parse_fg_jid(jid):
    """'<theme>__<style>[__<model>]__s<seed>' -> (theme, style, seed)."""
    theme, rest = jid.split("__", 1)
    return theme, rest.split("__")[0], int(rest.rsplit("__s", 1)[1])


def cmd_fg(a):
    out_dir, rev, raw = OUT / "fg", OUT / "review/fg", OUT / "fg_raw"
    for d in (out_dir, rev, raw):
        d.mkdir(parents=True, exist_ok=True)
    wanted = []
    if a.from_cache:
        # Re-run stage B over every cached stage-A render, whatever seed it
        # was made with. The theme x style x seed product cannot reproduce
        # job ids from an earlier batch that used --seeds 2 or a reseed
        # (city__papercut__s1507, jungle__papercut__s1101...), and this is
        # how a seam-mode change gets applied to ALL existing strips.
        for src in sorted(raw.glob("*_a.png")):
            jid = src.name[:-len("_a.png")]
            theme, style, seed = parse_fg_jid(jid)
            if a.themes and theme not in a.themes.split(","):
                continue
            if a.styles and style not in a.styles.split(","):
                continue
            prompt = T.fg_prompt(theme, style)
            if a.dry_run:
                print(f"## {jid} (cached stage A)")
                continue
            wanted.append((jid, theme, style, seed, prompt))
    else:
        for theme in pick_themes(a.themes):
            base_seed = T.THEMES[theme]["seed"]
            for style in pick_styles(a.styles):
                for i in range(a.seeds):
                    seed = style_seed(base_seed, style, i)
                    model_key = T.style_model(style, a.model)
                    tag = "" if model_key == "flux" else f"__{model_key}"
                    jid = f"{theme}__{style}{tag}__s{seed}"
                    if (out_dir / f"{jid}.png").exists() and not a.force:
                        continue
                    prompt = T.fg_prompt(theme, style)
                    if a.dry_run:
                        print(f"## {jid} [{model_key}]\n{prompt}\n")
                        continue
                    wanted.append((jid, theme, style, seed, prompt))
    if a.dry_run or not wanted:
        return
    free_models()

    # Stage A: the flat render, kept small - it is only the seed of stage B.
    stage_a = []
    for jid, _theme, style, seed, prompt in wanted:
        if (raw / f"{jid}_a.png").exists():
            continue
        model_key = T.style_model(style, a.model)
        g = gen_graph(model_key, a, prompt, GEN_FG, seed, NEG_FG)
        raw_tail(g, ["9", 0], f"dbg/fga_{jid}")
        stage_a.append((jid, g))

    def handler_a(jid, outputs):
        (raw / f"{jid}_a.png").write_bytes(fetch(outputs["14"]["images"][0]))

    if stage_a:
        run_jobs(stage_a, handler_a, "fg-A")

    # Stage B: seam repaint (or plain upscale when --seam blend), upscale, key.
    stage_b = []
    for jid, _theme, _style, seed, prompt in wanted:
        src = raw / f"{jid}_a.png"
        if not src.exists():
            print(f"[SKIP] fg-B {jid}: no stage A render")
            continue
        ref = upload(src)
        if a.seam == "fill":
            g = seam_graph(prompt, ref, seed + 7, a.steps)
            img = ["28", 0]
        else:
            g = {"20": {"class_type": "LoadImage", "inputs": {"image": ref}}}
            img = ["20", 0]
        upscale_tail(g, img, FG_RENDER if a.seam == "fill" else (FG_W + SEAM, 1280 + 80),
                     f"dbg/fgb_{jid}")
        rmbg_tail(g, ["12", 0], f"dbg/fgb_{jid}")
        stage_b.append((jid, g))

    def handler_b(jid, outputs):
        rgba = fetch(outputs["31"]["images"][0])
        mask = fetch(outputs["33"]["images"][0])
        rgb = fetch(outputs["34"]["images"][0])
        (raw / f"{jid}_rgba.png").write_bytes(rgba)
        (raw / f"{jid}_rgb.png").write_bytes(rgb)
        strip = finish_strip(rgba, mask, out_dir / f"{jid}.png", seam=a.seam,
                             rgb_bytes=rgb)
        thumb(strip, rev / f"{jid}.jpg", width=1024, backdrop=(140, 190, 225, 255))

    run_jobs(stage_b, handler_b, "fg-B")


def reconstruct_rgb(jid, raw_dir, size):
    """Pre-RMBG RGB for strips rendered before _rgb.png was saved: roll the
    stage-A render by half its width exactly like seam_graph does and scale
    it to the delivered size. Only the Fill band differs from what SPARK
    actually keyed - there RMBG's own mask still applies via the union."""
    a_png = raw_dir / f"{jid}_a.png"
    if not a_png.exists():
        return None
    im = Image.open(a_png).convert("RGB")
    half = im.width // 2
    rolled = Image.new("RGB", im.size)
    rolled.paste(im.crop((half, 0, im.width, im.height)), (0, 0))
    rolled.paste(im.crop((0, 0, half, im.height)), (half, 0))
    buf = io.BytesIO()
    rolled.resize(size, Image.LANCZOS).save(buf, "PNG")
    return buf.getvalue()


def cmd_refinish(a):
    """Re-run the local finish over every cached fg_raw/*_rgba.png - no GPU.
    Use after a finish_strip change."""
    out_dir, rev, raw = OUT / "fg", OUT / "review/fg", OUT / "fg_raw"
    n = 0
    for rgba_path in sorted(raw.glob("*_rgba.png")):
        jid = rgba_path.name[:-len("_rgba.png")]
        if a.themes and jid.split("__")[0] not in a.themes.split(","):
            continue
        rgba = rgba_path.read_bytes()
        im = Image.open(io.BytesIO(rgba)).convert("RGBA")
        mask_buf = io.BytesIO(); im.getchannel("A").save(mask_buf, "PNG")
        rgb_path = raw / f"{jid}_rgb.png"
        rgb = rgb_path.read_bytes() if rgb_path.exists() else reconstruct_rgb(jid, raw, im.size)
        strip = finish_strip(rgba, mask_buf.getvalue(), out_dir / f"{jid}.png", seam=a.seam, rgb_bytes=rgb)
        thumb(strip, rev / f"{jid}.jpg", width=1024, backdrop=(140, 190, 225, 255))
        n += 1
        print(f"{jid:48s} {strip.height:5d}px" + ("" if rgb_path.exists() else "  (rgb reconstructed from stage A)"))
    print(f"refinish: {n} strip(s)")


def cmd_sheet(_a):
    """Static gallery: out/index.html, grouped by branch and theme."""
    sections = []
    for branch, title in (("bg", "Backgrounds"), ("restyle", "Restyled photos"),
                          ("fg", "Foreground strips")):
        rev = OUT / "review" / branch
        if not rev.exists():
            continue
        groups = {}
        for p in sorted(rev.glob("*.jpg")):
            groups.setdefault(p.stem.split("__")[0], []).append(p)
        cards = []
        for group, files in groups.items():
            items = "".join(
                f'<figure><a href="{branch}/{p.stem}.png"><img loading="lazy" '
                f'src="review/{branch}/{p.name}"></a><figcaption>{p.stem}</figcaption></figure>'
                for p in files)
            cards.append(f"<h3>{group}</h3><div class=grid>{items}</div>")
        sections.append(f"<h2>{title}</h2>" + "".join(cards))
    html = ("<!doctype html><meta charset=utf-8><title>Doodlebugs arena picks</title>"
            "<style>body{font:14px system-ui;margin:24px;background:#1b1e24;color:#ddd}"
            ".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(320px,1fr));gap:12px}"
            "figure{margin:0}img{width:100%;border-radius:6px;display:block}"
            "figcaption{font-family:monospace;font-size:12px;padding:4px 0;color:#9ab}"
            "h2{margin-top:40px;border-bottom:1px solid #345}h3{color:#8cf;margin:24px 0 8px}</style>"
            + "".join(sections))
    (OUT / "index.html").write_text(html)
    print(f"gallery -> {OUT / 'index.html'}")


def cmd_apply(a):
    branch = "restyle" if "__s" not in a.pick and a.pick.split("__")[0] in T.RESTYLE_SOURCES else "bg"
    src = OUT / branch / f"{a.pick}.png"
    if not src.exists():
        sys.exit(f"no such render: {src}")
    im = Image.open(src)
    if im.size != (BG_W, BG_H):
        sys.exit(f"{src} is {im.size}, expected {(BG_W, BG_H)}")
    dst = ASSETS_BG / f"{a.name}.png"
    shutil.copyfile(src, dst)
    print(f"background -> {dst}")
    if a.fg:
        fsrc = OUT / "fg" / f"{a.fg}.png"
        if not fsrc.exists():
            sys.exit(f"no such strip: {fsrc}")
        fdst = ASSETS_FG / f"{a.name}_fg.png"
        shutil.copyfile(fsrc, fdst)
        print(f"foreground -> {fdst}")
    print("Unity: Doodlebugs -> Sync Background Profiles")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    def common(p, seeds=True):
        p.add_argument("--themes", help="comma list (default: all)")
        p.add_argument("--styles", help="comma list (default: DEFAULT_STYLES)")
        if seeds:
            p.add_argument("--seeds", type=int, default=1, help="variants per theme x style")
        p.add_argument("--steps", type=int, default=20, help="flux only - SDXL checkpoints use their own tuned steps")
        p.add_argument("--guidance", type=float, default=3.5, help="flux only")
        p.add_argument("--model", default="flux",
                       choices=["flux", *CHECKPOINTS],
                       help="checkpoint family for bg/fg (default: flux; restyle is always Flux Kontext)")
        p.add_argument("--dry-run", action="store_true", help="print prompts, render nothing")

    p = sub.add_parser("bg"); common(p); p.set_defaults(fn=cmd_bg)
    p = sub.add_parser("fg"); common(p)
    p.add_argument("--seam", choices=["fill", "blend"], default="fill",
                   help="fill = FLUX Fill repaints the wrap (default); blend = cross-fade")
    p.add_argument("--force", action="store_true",
                   help="re-run stage B even if out/fg/<jid>.png already exists")
    p.add_argument("--from-cache", action="store_true",
                   help="take the job list from cached fg_raw/*_a.png instead of theme x style x "
                        "seed - the way to re-run stage B for every existing strip (e.g. a seam "
                        "mode change), including ids from earlier --seeds 2 batches")
    p.set_defaults(fn=cmd_fg)
    p = sub.add_parser("restyle"); common(p, seeds=False)
    p.add_argument("--sources", help="comma list of Assets background names")
    p.add_argument("--seed", type=int, default=2732)
    p.set_defaults(fn=cmd_restyle)
    p = sub.add_parser("refinish", help="re-run the local strip finish over cached fg_raw (no GPU)")
    p.add_argument("--themes")
    p.add_argument("--seam", choices=["fill", "blend"], default="fill")
    p.set_defaults(fn=cmd_refinish)
    p = sub.add_parser("sheet"); p.set_defaults(fn=cmd_sheet)
    p = sub.add_parser("apply")
    p.add_argument("pick", help="render id, e.g. jungle__artdeco__s1101 or Sky__ukiyoe")
    p.add_argument("--as", dest="name", required=True, help="map name -> Sprites/Background/<Name>.png")
    p.add_argument("--fg", help="foreground render id to ship as <Name>_fg.png")
    p.set_defaults(fn=cmd_apply)

    a = ap.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
