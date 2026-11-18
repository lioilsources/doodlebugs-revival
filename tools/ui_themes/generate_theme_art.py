#!/usr/bin/env python3
"""UI theme art generator (Prompts/31 §7): render / post / sheet / apply.

Jobs come from prompts.py. `render` is the only step that touches the
network: it queues every missing (job, seed) on a ComfyUI server at once
(txt2img only, nothing is uploaded) and downloads the PNGs into out/raw/.
Everything after it is local Pillow work and can be redone without a GPU.

  COMFY=http://127.0.0.1:18188 python3 tools/ui_themes/generate_theme_art.py render
  python3 tools/ui_themes/generate_theme_art.py post
  python3 tools/ui_themes/generate_theme_art.py sheet      # out/sheets/<theme>.jpg
  python3 tools/ui_themes/generate_theme_art.py apply      # picks.json or seed 0 -> Assets
  python3 tools/ui_themes/generate_theme_art.py render --model schnell --theme canopy

Matte (prompts.KINDS):
  None    opaque; tiles are made seamless (half roll + cross-fade)
  cutout  white key: alpha from distance to the white ground, colours
          un-matted, biggest component kept, trimmed
  luma    glow on black: alpha = max(r, g, b), colour un-premultiplied
"""
import argparse
import io
import json
import os
import random
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OUT = HERE / "out"
RAW, POST, SHEETS = OUT / "raw", OUT / "post", OUT / "sheets"
PICKS = HERE / "picks.json"
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(ROOT / "tools/planes"))
import prompts as P  # noqa: E402

COMFY = os.environ.get("COMFY", "http://127.0.0.1:18188")


def jid(j, seed):
    return f"{j['theme']}__{j['asset']}__s{seed}"


# ------------------------------------------------------------------ comfy --
def call(path, body=None, timeout=60):
    req = urllib.request.Request(COMFY + path, data=json.dumps(body).encode() if body else None,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        data = r.read()
    return json.loads(data) if data[:1] in (b"{", b"[") else data


def graph(j, seed):
    w, h = j["size"]
    m = P.MODELS[j["model"]]["comfy"]
    if j["model"] == "qwen":
        load = {"u": {"class_type": "UNETLoader", "inputs": {"unet_name": m["unet"], "weight_dtype": "default"}},
                "c": {"class_type": "CLIPLoader", "inputs": {"clip_name": m["clip"], "type": m["clip_type"]}},
                "v": {"class_type": "VAELoader", "inputs": {"vae_name": m["vae"]}},
                "ms": {"class_type": "ModelSamplingAuraFlow", "inputs": {"model": ["u", 0], "shift": m["shift"]}}}
        model, clip, vae, neg = ["ms", 0], ["c", 0], ["v", 0], j["negative"]
    else:  # schnell runs at cfg 1: a negative prompt is a no-op, the prompts are positive-only
        load = {"ck": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": m["ckpt"]}}}
        model, clip, vae, neg = ["ck", 0], ["ck", 1], ["ck", 2], ""
    return {**load,
            "p": {"class_type": "CLIPTextEncode", "inputs": {"clip": clip, "text": j["prompt"]}},
            "n": {"class_type": "CLIPTextEncode", "inputs": {"clip": clip, "text": neg}},
            "l": {"class_type": "EmptySD3LatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}},
            "k": {"class_type": "KSampler", "inputs": {"model": model, "positive": ["p", 0], "negative": ["n", 0],
                                                        "latent_image": ["l", 0], "seed": seed, "steps": m["steps"],
                                                        "cfg": m["cfg"], "sampler_name": m["sampler"],
                                                        "scheduler": m["scheduler"], "denoise": 1.0}},
            "d": {"class_type": "VAEDecode", "inputs": {"samples": ["k", 0], "vae": vae}},
            "s": {"class_type": "SaveImage", "inputs": {"images": ["d", 0],
                                                        "filename_prefix": f"doodle_themes/{jid(j, seed)}"}}}


def cmd_render(a):
    RAW.mkdir(parents=True, exist_ok=True)
    todo = []
    # schnell first (minutes), then the slow Qwen batch: one model swap
    for j in sorted(P.jobs(a.theme, a.model), key=lambda j: j["model"] != "schnell"):
        for seed in j["seeds"]:
            if a.force or not (RAW / f"{jid(j, seed)}.png").exists():
                todo.append((j, seed))
    print(f"{len(todo)} images to render on {COMFY}")
    if a.dry_run or not todo:
        return
    call("/system_stats")
    pending = {}
    for j, seed in todo:   # queue everything at once: the GPU never waits for a download
        r = call("/prompt", {"prompt": graph(j, seed), "client_id": "doodle-themes"})
        if r.get("node_errors"):
            sys.exit(f"node errors for {jid(j, seed)}: {r['node_errors']}")
        pending[r["prompt_id"]] = jid(j, seed)
    ok = failed = 0
    t0 = time.time()
    while pending:
        time.sleep(3)
        for pid in list(pending):
            hist = call(f"/history/{pid}").get(pid)
            if not hist:
                continue
            name = pending.pop(pid)
            imgs = [i for o in hist.get("outputs", {}).values() for i in o.get("images", [])]
            if not imgs:
                failed += 1
                print(f"  FAIL {name}: {hist.get('status', {}).get('messages', '')}"[:300])
                continue
            i = imgs[0]
            q = urllib.parse.urlencode({"filename": i["filename"], "subfolder": i["subfolder"], "type": i["type"]})
            (RAW / f"{name}.png").write_bytes(call(f"/view?{q}", timeout=120))
            ok += 1
            print(f"  {ok + failed}/{len(todo)} {name}  ({time.time() - t0:.0f} s)")
    print(json.dumps({"images": {"ok": ok, "failed": failed}}))


# ------------------------------------------------------------------- post --
def white_key(im):
    """Alpha from the distance to white; colours un-matted against white."""
    rgb = im.convert("RGB")
    inv = ImageChops.invert(rgb)
    r, g, b = inv.split()
    dist = ImageChops.lighter(ImageChops.lighter(r, g), b)            # 0 on pure white
    alpha = dist.point(lambda v: 0 if v < 18 else min(255, (v - 18) * 4))
    alpha = alpha.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.MinFilter(3))
    px, al = rgb.load(), alpha.load()
    out = Image.new("RGBA", rgb.size)
    op = out.load()
    for y in range(rgb.height):
        for x in range(rgb.width):
            a = al[x, y]
            if a == 0:
                op[x, y] = (0, 0, 0, 0)
                continue
            f = a / 255
            c = px[x, y]
            op[x, y] = tuple(max(0, min(255, round((c[k] - 255 * (1 - f)) / f))) for k in range(3)) + (a,)
    return keep_biggest(out)


def keep_biggest(im, min_frac=0.02):
    """Drop detached specks: keep every component at least min_frac of the largest."""
    small = im.split()[3].resize((im.width // 4, im.height // 4), Image.BOX).point(lambda v: 255 if v > 40 else 0)
    w, h = small.size
    px = small.load()
    seen, comps = set(), []
    for y in range(h):
        for x in range(w):
            if px[x, y] and (x, y) not in seen:
                stack, comp = [(x, y)], []
                seen.add((x, y))
                while stack:
                    cx, cy = stack.pop()
                    comp.append((cx, cy))
                    for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                        if 0 <= nx < w and 0 <= ny < h and px[nx, ny] and (nx, ny) not in seen:
                            seen.add((nx, ny))
                            stack.append((nx, ny))
                comps.append(comp)
    if len(comps) <= 1:
        return im
    big = max(len(c) for c in comps)
    keep = Image.new("L", small.size, 0)
    kp = keep.load()
    for c in comps:
        if len(c) >= big * min_frac:
            for x, y in c:
                kp[x, y] = 255
    keep = keep.resize(im.size, Image.NEAREST).filter(ImageFilter.MaxFilter(9))
    r, g, b, a = im.split()
    return Image.merge("RGBA", (r, g, b, ImageChops.multiply(a, keep)))


def luma_key(im):
    rgb = im.convert("RGB")
    r, g, b = rgb.split()
    a = ImageChops.lighter(ImageChops.lighter(r, g), b)
    a = a.point(lambda v: 0 if v < 10 else min(255, int((v - 10) * 1.15)))
    out = Image.new("RGBA", rgb.size)
    px, al, op = rgb.load(), a.load(), out.load()
    for y in range(rgb.height):
        for x in range(rgb.width):
            av = al[x, y]
            if av:
                c = px[x, y]
                op[x, y] = tuple(min(255, round(c[k] * 255 / max(av, 1))) for k in range(3)) + (av,)
    return out


def seamless(im, band=0.18):
    """Roll by half both ways and cross-fade the seams (same idea as --seam blend)."""
    w, h = im.size
    rolled = ImageChops.offset(im, w // 2, h // 2)
    mask = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(mask)
    bw, bh = int(w * band), int(h * band)
    for i in range(bw):
        v = int(255 * (1 - abs(i - bw / 2) / (bw / 2)))
        d.line([(w // 2 - bw // 2 + i, 0), (w // 2 - bw // 2 + i, h)], fill=v)
    for i in range(bh):
        v = int(255 * (1 - abs(i - bh / 2) / (bh / 2)))
        d.line([(0, h // 2 - bh // 2 + i), (w, h // 2 - bh // 2 + i)], fill=v)
    # Inside the band around the rolled seams take the original image (its
    # centre has no seam); the edges of the result are the original centre,
    # so the tile wraps without a step.
    return Image.composite(im, rolled, mask)


def fit(im, box):
    bb = im.getbbox() if im.mode == "RGBA" else None
    if bb:
        im = im.crop(bb)
    im.thumbnail(box, Image.LANCZOS)
    return im


def cmd_post(a):
    POST.mkdir(parents=True, exist_ok=True)
    n = 0
    for j in P.jobs(a.theme, a.model):
        for seed in j["seeds"]:
            name = jid(j, seed)
            src, dst = RAW / f"{name}.png", POST / f"{name}.png"
            if not src.exists() or (dst.exists() and not a.force):
                continue
            im = Image.open(src)
            if j["kind"] == "backdrop":
                im = im.convert("RGB").resize(j["deliver"], Image.LANCZOS)
            elif j["kind"] == "tile":
                im = seamless(im.convert("RGB")).resize(j["deliver"], Image.LANCZOS)
            else:
                work = im.resize((im.width // 2, im.height // 2), Image.LANCZOS)   # keying at half size is plenty
                im = luma_key(work) if j["matte"] == "luma" else white_key(work)
                im = fit(im, j["deliver"])
            im.save(dst)
            n += 1
    print(f"post: {n} images")


# ------------------------------------------------------------------ sheet --
def cmd_sheet(a):
    SHEETS.mkdir(parents=True, exist_ok=True)
    for theme in P.THEMES:
        if a.theme and theme != a.theme:
            continue
        rows = []
        for j in P.jobs(theme):
            cells = []
            for i, seed in enumerate(j["seeds"]):
                f = POST / f"{jid(j, seed)}.png"
                if not f.exists():
                    continue
                im = Image.open(f).convert("RGBA")
                im.thumbnail((360, 200))
                cell = Image.new("RGB", (370, 230), (128, 134, 146) if j["matte"] != "luma" else (24, 26, 34))
                cell.paste(im, ((370 - im.width) // 2, 4 + (200 - im.height) // 2), im)
                ImageDraw.Draw(cell).text((6, 210), f"{j['asset']} #{i}", fill=(255, 255, 255))
                cells.append(cell)
            if cells:
                row = Image.new("RGB", (370 * 4, 230), (40, 40, 48))
                for k, c in enumerate(cells):
                    row.paste(c, (370 * k, 0))
                rows.append(row)
        if rows:
            sheet = Image.new("RGB", (370 * 4, 230 * len(rows)))
            for k, r in enumerate(rows):
                sheet.paste(r, (0, 230 * k))
            sheet.save(SHEETS / f"{theme}.jpg", quality=82)
            print("sheet", SHEETS / f"{theme}.jpg")


# ------------------------------------------------------------------ apply --
def cmd_apply(a):
    import unity_meta  # noqa: F401  (tools/planes)
    picks = json.loads(PICKS.read_text()) if PICKS.exists() else {}
    n = 0
    for j in P.jobs(a.theme, a.model):
        key = f"{j['theme']}.{j['asset']}"
        idx = picks.get(key, 0)
        if idx is None or idx < 0:
            continue
        src = POST / f"{jid(j, j['seeds'][idx])}.png"
        if not src.exists():
            print("missing", src)
            continue
        dst = ROOT / j["out"]
        dst.parent.mkdir(parents=True, exist_ok=True)
        im = Image.open(src)
        if dst.suffix == ".jpg":
            im.convert("RGB").save(dst, quality=88, optimize=True)
        else:
            im.save(dst, optimize=True)
        write_meta(dst, tiled=j["kind"] == "tile")
        n += 1
    for d in sorted({(ROOT / j["out"]).parent for j in P.jobs()}):
        if d.exists():
            write_folder_meta(d)
    write_folder_meta(ROOT / P.ASSET_ROOT)
    print(f"apply: {n} files")


def guid(path):
    import hashlib
    return hashlib.md5(("doodle-ui-theme:" + str(path.relative_to(ROOT))).encode()).hexdigest()


def write_folder_meta(d):
    m = d.with_name(d.name + ".meta")
    if not m.exists():
        m.write_text(f"fileFormatVersion: 2\nguid: {guid(d)}\nfolderAsset: yes\nDefaultImporter:\n"
                     "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def write_meta(path, tiled=False):
    """Sprite import, bilinear, Read/Write off, 2048 cap; tiles repeat."""
    m = path.with_name(path.name + ".meta")
    if m.exists():
        return
    wrap = 0 if tiled else 1
    m.write_text(f"""fileFormatVersion: 2
guid: {guid(path)}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: {wrap}
    wrapV: {wrap}
    wrapW: {wrap}
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 0
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
""")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("cmd", choices=["render", "post", "sheet", "apply"])
    ap.add_argument("--theme")
    ap.add_argument("--model", choices=sorted(P.MODELS))
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    {"render": cmd_render, "post": cmd_post, "sheet": cmd_sheet, "apply": cmd_apply}[a.cmd](a)


if __name__ == "__main__":
    main()
