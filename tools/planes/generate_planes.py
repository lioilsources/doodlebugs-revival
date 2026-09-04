#!/usr/bin/env python3
"""Plane model generator - new silhouettes on the shared hitbox.

FLUX Kontext redesigns BiPlane1.png per concept (planes.py) on SPARK's
ComfyUI, RMBG-2.0 keys it out in the same job, and the local post-process
turns the render into a game sprite that satisfies the envelope contract
(gate.py) the shared BoxCollider2D relies on:

  1. alpha = RMBG mask UNION white-key of the render (same trick as the
     foreground strips - RMBG alone drops thin struts), colours un-matted
     against the white ground
  2. crop to the silhouette, uniform-scale so the bbox is TARGET_W wide,
     centre on (64,64) of a 128x128 canvas, hard alpha - the plan's
     "normalise" step; G2/G4/G5 mostly hold by construction, G1/G3/G7 don't
  3. palette-quantise to ~20 colours so the AI render sits next to the
     hand-drawn base without reading as a photo
  4. split the body the way tools/skins split BiPlane1: red pixels are the
     livery (paint, stored as (value,0,0) with the value band normalised to
     BiPlane1's 141..255), the leftmost ACCENT_FRAC of the bbox is the tail
     accent (forced red, ColorReplace tints it per player), everything else
     is a fixed part (engine, pilot, wheels) copied verbatim into every skin
  5. gate it (gate.py); every seed keeps its metrics in out/models/*.json

Two render modes: `kontext` (default - BiPlane1 as the reference, best for
concepts with a strong silhouette) and `txt2img` (no reference, FLUX.1-dev
from the prompt alone - for concepts Kontext keeps turning back into a
biplane: wing count, canard, twin boom...). txt2img job ids carry a `__txt`
tag so both modes coexist per concept.

Outputs per (concept, seed) job id "<key>__s<seed>" / "<key>__txt__s<seed>":
  out/raw/<jid>_rgba.png, _rgb.png    SPARK results (resume cache)
  out/models/<jid>.png, _mask.png     the sprite + R=paint G=accent A=alpha
  out/models/<jid>.json               metrics + gate verdict
  review/<jid>.jpg                    4x preview with hitbox + bbox drawn
  index.html                          all seeds per concept, best pick marked

Usage:
  python3 tools/planes/generate_planes.py render                    # every concept x --seeds
  python3 tools/planes/generate_planes.py render --keys triplane,racer --seeds 4
  python3 tools/planes/generate_planes.py render --mode txt2img --keys canard,twin_boom --seeds 3
  python3 tools/planes/generate_planes.py post                      # re-run steps 1-5 from out/raw, no GPU
  python3 tools/planes/generate_planes.py sheet && open tools/planes/index.html
  python3 tools/planes/generate_planes.py apply                     # best passing seed per concept -> Assets
  python3 tools/planes/generate_planes.py apply --pick triplane=7011,canard=txt__s7043
  python3 tools/planes/generate_planes.py render --dry-run          # prompts only

Reuses tools/backgrounds/spark_backgrounds.py's ComfyUI client and
tools/skins' mask conventions; unity_meta.py writes the .meta files the
runtime compositing needs (Read/Write enabled). Keys/ids live in planes.py
and must match PlaneModelCatalog.cs.
"""
import argparse
import io
import json
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
BASE_SPRITE = ROOT / "Assets/Doodlebugs/Sprites/BiPlane/BiPlane1.png"
ASSETS_DIR = ROOT / "Assets/Doodlebugs/Resources/Sprites/PlaneModels"
OUT = HERE / "out"
RAW = OUT / "raw"
MODELS = OUT / "models"
REVIEW = HERE / "review"

sys.path.insert(0, str((ROOT / "tools/backgrounds").resolve()))
import spark_backgrounds as backend  # noqa: E402

sys.path.insert(0, str(HERE))
import gate as G  # noqa: E402
import planes as P  # noqa: E402
import unity_meta as UM  # noqa: E402

SIZE = 128
TARGET_W = 110                 # BiPlane1's bbox width - what every model is scaled to
MAX_H = SIZE - 2 * G.MARGIN    # taller than this and G6 would clip on rotation anyway
ACCENT_FRAC = 0.18             # leftmost share of the bbox that is tail accent (BiPlane1: 19/110)
ACCENT_MIN_VALUE = 90          # non-red pixels this bright in the tail zone become red too (dark = outline, stays)
PALETTE = 20                   # quantise colours - the base sprite uses ~12
LUM_BAND = (141, 255)          # BiPlane1's red value band; models are normalised into it
GEN = (1024, 1024)             # Kontext works at ~1 MP; 128 px reference goes up 8x NEAREST
REF_NAME = "plane_ref_biplane1_1024.png"


# ---------------------------------------------------------------- SPARK --
def reference_png():
    """BiPlane1 at 1024x1024 on white (Kontext wants RGB at ~1 MP; NEAREST
    keeps the pixel edges the prompt asks it to reproduce)."""
    p = OUT / REF_NAME
    if not p.exists():
        OUT.mkdir(parents=True, exist_ok=True)
        im = Image.open(BASE_SPRITE).convert("RGBA").resize(GEN, Image.NEAREST)
        bg = Image.new("RGBA", GEN, (255, 255, 255, 255))
        bg.alpha_composite(im)
        bg.convert("RGB").save(p)
    return p


def pony_graph(prompt, seed, steps, guidance, lora_strength):
    """Pony Diffusion V6 XL + the Spacecraft LoRA. Plain SDXL topology (real
    negative prompt at cfg > 1, no NAG needed), with a LoraLoader spliced
    between the checkpoint and everything downstream. Node 9 is the decoded
    image so the shared RMBG tail hangs off it unchanged."""
    w, h = GEN
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": P.PONY_CKPT}},
        "2": {"class_type": "LoraLoader",
              "inputs": {"model": ["1", 0], "clip": ["1", 1], "lora_name": P.PONY_LORA,
                         "strength_model": lora_strength, "strength_clip": lora_strength}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 1], "text": prompt}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 1], "text": P.PONY_NEG}},
        "7": {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}},
        "8": {"class_type": "KSampler",
              "inputs": {"model": ["2", 0], "positive": ["4", 0], "negative": ["5", 0],
                         "latent_image": ["7", 0], "seed": seed, "steps": steps,
                         "cfg": guidance, "sampler_name": "dpmpp_2m", "scheduler": "karras",
                         "denoise": 1.0}},
        "9": {"class_type": "VAEDecode", "inputs": {"samples": ["8", 0], "vae": ["1", 2]}},
    }


def render_graph(key, seed, ref, steps, guidance, mode="kontext", lora_strength=0.8):
    """Kontext img2img off the reference, FLUX.1-dev txt2img, or Pony + the
    Spacecraft LoRA. RMBG keys the white ground in-job for all three."""
    prompt = P.prompt_for(key, mode)
    if mode == "pony":
        # Pony wants SDXL-ish cfg, not FLUX's 3.5 guidance.
        g = pony_graph(prompt, seed, max(steps, 26), 7.0 if guidance <= 4 else guidance,
                       lora_strength)
    else:
        g = backend.flux_graph(prompt, GEN, seed, steps, guidance,
                               ref=ref if mode == "kontext" else None,
                               negative=P.negative_for(mode))
    backend.rmbg_tail(g, ["9", 0], f"dbg/plane_{key}_{mode}_s{seed}")
    return g


MODE_TAG = {"kontext": "", "txt2img": "txt", "pony": "pony"}


def job_id(key, seed, mode):
    tag = MODE_TAG[mode]
    return f"{key}__s{seed}" if not tag else f"{key}__{tag}__s{seed}"


def parse_job_id(jid):
    """'<key>[__txt]__s<seed>' -> (key, seed)."""
    key = jid.split("__")[0]
    seed = int(jid.rsplit("__s", 1)[1])
    return key, seed


# ----------------------------------------------------------------- post --
def components(mask, scale=4):
    """Connected components of a hard L mask, labelled on a /scale copy
    (pure Pillow floodfill is Python-speed). Returns (labels image at the
    small size, [(label, area, bbox)]) - label 0 is background."""
    small = mask.resize((mask.width // scale, mask.height // scale), Image.NEAREST)
    small = small.point(lambda v: 255 if v > 127 else 0)
    labels = Image.new("I", small.size, 0)
    lp, sp = labels.load(), small.load()
    w, h = small.size
    found = []
    label = 0
    for y in range(h):
        for x in range(w):
            if not sp[x, y] or lp[x, y]:
                continue
            label += 1
            # Iterative 4-neighbour flood fill on the label image.
            stack = [(x, y)]
            lp[x, y] = label
            area = 0
            x0 = x1 = x
            y0 = y1 = y
            while stack:
                cx, cy = stack.pop()
                area += 1
                x0, x1, y0, y1 = min(x0, cx), max(x1, cx), min(y0, cy), max(y1, cy)
                for nx, ny in ((cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1)):
                    if 0 <= nx < w and 0 <= ny < h and sp[nx, ny] and not lp[nx, ny]:
                        lp[nx, ny] = label
                        stack.append((nx, ny))
            found.append((label, area, (x0, y0, x1, y1)))
    return labels, found


def drop_ground_shadow(mask):
    """FLUX likes to put a ground shadow under the wheels even with
    'shadow' in the negative (twin_boom s7050, 2026-09-03). Nothing that is
    part of an aircraft sits ENTIRELY below the main body's bbox, so every
    detached component whose top edge is under the largest component's
    bottom edge is a shadow. Returns a keep mask (L, full size) or None."""
    labels, found = components(mask)
    if len(found) < 2:
        return None
    main = max(found, key=lambda f: f[1])
    main_bottom = main[2][3]
    shadows = [f[0] for f in found if f[0] != main[0] and f[2][1] > main_bottom]
    if not shadows:
        return None
    drop = set(shadows)
    keep_small = labels.point(lambda v: 0 if v in drop else 255, "L")
    return keep_small.resize(mask.size, Image.NEAREST).filter(ImageFilter.MaxFilter(3))


def normalise(src_rgba):
    """Crop to the silhouette, scale to TARGET_W wide (or MAX_H tall if that
    binds), centre on the 128 canvas. Returns the RGBA canvas."""
    alpha = src_rgba.getchannel("A")
    hard = alpha.point(lambda v: 255 if v > 127 else 0)
    # A 5 px opening drops the speck noise Kontext sometimes leaves on the
    # white ground - a stray dot 300 px from the plane would otherwise set
    # the bbox and shrink the whole model.
    blob = hard.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(5))
    keep = drop_ground_shadow(blob)
    if keep is not None:
        blob = ImageChops.multiply(blob, keep)
        src_rgba = src_rgba.copy()
        src_rgba.putalpha(ImageChops.multiply(alpha, keep))
    bbox = blob.getbbox()
    if not bbox:
        raise ValueError("empty render")
    crop = src_rgba.crop(bbox)
    bw, bh = crop.size
    scale = TARGET_W / bw
    if bh * scale > MAX_H:
        scale = MAX_H / bh
    nw, nh = max(1, round(bw * scale)), max(1, round(bh * scale))
    # Premultiplied resize: straight-alpha LANCZOS bleeds the transparent
    # pixels' colour into the edge.
    small = crop.convert("RGBa").resize((nw, nh), Image.LANCZOS).convert("RGBA")
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    canvas.paste(small, (round(SIZE / 2 - nw / 2), round(SIZE / 2 - nh / 2)))
    return canvas


def quantise(canvas):
    """Palette-quantise the opaque pixels. Transparent pixels are painted the
    mean body colour first so they don't eat palette slots."""
    alpha = canvas.getchannel("A").point(lambda v: 255 if v > 127 else 0)
    rgb = canvas.convert("RGB")
    stat = rgb.resize((1, 1), Image.BOX).getpixel((0, 0))
    filled = Image.composite(rgb, Image.new("RGB", rgb.size, stat), alpha)
    q = filled.quantize(colors=PALETTE, method=Image.Quantize.MEDIANCUT,
                        dither=Image.Dither.NONE).convert("RGB")
    return q, alpha


def split_body(q, alpha, outline=False):
    """The tools/skins mask split, generalised: returns (base RGBA, mask RGBA)."""
    w, h = q.size
    qp, ap = q.load(), alpha.load()
    bbox = alpha.getbbox()
    accent_x = bbox[0] + round(ACCENT_FRAC * (bbox[2] - bbox[0]))

    reds = [max(qp[x, y][:3]) for y in range(h) for x in range(w)
            if ap[x, y] and G.is_livery_red(qp[x, y])]
    lo, hi = (min(reds), max(reds)) if reds else LUM_BAND
    lb, ub = LUM_BAND

    def band(v):
        if hi == lo:
            return ub
        return int(round(lb + (min(max(v, lo), hi) - lo) * (ub - lb) / (hi - lo)))

    base = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    mask = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    bp, mp = base.load(), mask.load()
    for y in range(h):
        for x in range(w):
            if not ap[x, y]:
                continue
            p = qp[x, y]
            red = G.is_livery_red(p)
            v = max(p[:3])
            if x < accent_x and (red or v >= ACCENT_MIN_VALUE):
                bp[x, y] = (band(v), 0, 0, 255)
                mp[x, y] = (0, 255, 0, 255)
            elif red:
                bp[x, y] = (band(v), 0, 0, 255)
                mp[x, y] = (255, 0, 0, 255)
            else:
                r, g, b = p[:3]
                if v < 90 and r > 1.5 * max(g, b):
                    # Near-black outline pixels of the red livery (64,9,8)
                    # are too dark for the paint mask but read as thin red
                    # lines under a checkerboard - keep them as outline,
                    # drop the hue (luma grey) so they suit every skin.
                    luma = int(round(0.299 * r + 0.587 * g + 0.114 * b))
                    bp[x, y] = (luma, luma, luma, 255)
                else:
                    bp[x, y] = (r, g, b, 255)
                mp[x, y] = (0, 0, 0, 255)

    if outline:
        # 1 px darkening of every edge pixel (opaque with a transparent
        # 4-neighbour). Reds get a darker value, fixed parts a darker colour.
        edge = ImageChops.subtract(alpha, alpha.filter(ImageFilter.MinFilter(3)))
        ep = edge.load()
        for y in range(h):
            for x in range(w):
                if not ep[x, y]:
                    continue
                r, g, b, a = bp[x, y]
                if mp[x, y][0] or mp[x, y][1]:
                    bp[x, y] = (max(lb, int(r * 0.7)), 0, 0, 255)
                else:
                    bp[x, y] = (int(r * 0.55), int(g * 0.55), int(b * 0.55), 255)
    return base, mask


def post(rgba_bytes, rgb_bytes, outline=False, flip=False, white_key=True):
    rgba = Image.open(io.BytesIO(rgba_bytes)).convert("RGBA")
    rgb = Image.open(io.BytesIO(rgb_bytes)).convert("RGB")
    if rgb.size != rgba.size:
        rgb = rgb.resize(rgba.size, Image.LANCZOS)
    if flip:
        # The model drew it nose-left. No geometric test tells nose from tail
        # reliably across gliders, saucers and airships (measured: every
        # candidate feature scatters around zero on the known-good set), so
        # this is a manual call after looking at the sheet - and a mirror is
        # the whole fix, the sprite is otherwise fine.
        rgba = rgba.transpose(Image.FLIP_LEFT_RIGHT)
        rgb = rgb.transpose(Image.FLIP_LEFT_RIGHT)
    if white_key:
        # FLUX renders on a genuinely white ground, where RMBG alone drops
        # thin struts and the white key recovers them.
        keyed = backend.white_key(rgb)
        alpha = ImageChops.lighter(rgba.getchannel("A"), keyed)
        colour = backend.unmatte_white(rgb, alpha)
        src = colour.convert("RGBA")
        src.putalpha(alpha)
    else:
        # Pony ignores "white background" and lays the craft on warm grey
        # (223,207,197). white_key calls anything below 215 solid, so the
        # union marked the WHOLE backdrop as object - every seed came back a
        # filled 110x110 square. RMBG segments it fine on its own.
        src = rgba

    canvas = normalise(src)
    q, hard = quantise(canvas)
    base, mask = split_body(q, hard, outline=outline)
    m = G.measure(base)
    checks = G.check(m)
    ok = all(c[1] for c in checks)
    return base, mask, m, checks, ok


def review_jpg(base, m, ok, out_path):
    """4x NEAREST on sky blue with the hitbox (yellow) and bbox (white) drawn."""
    s = 4
    im = Image.new("RGBA", (SIZE * s, SIZE * s), (140, 190, 225, 255))
    im.alpha_composite(base.resize((SIZE * s, SIZE * s), Image.NEAREST))
    d = ImageDraw.Draw(im)
    cx0, cx1 = G.CORE_X
    cy0, cy1 = G.CORE_Y
    d.rectangle((cx0 * s, cy0 * s, (cx1 + 1) * s - 1, (cy1 + 1) * s - 1),
                outline=(255, 220, 0, 255) if ok else (255, 60, 60, 255), width=2)
    if m["opaque"]:
        d.rectangle((m["tail"] * s, 0, (m["nose"] + 1) * s - 1, SIZE * s - 1),
                    outline=(255, 255, 255, 110), width=1)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    im.convert("RGB").save(out_path, quality=88)


def finish(jid, rgba_bytes, rgb_bytes, outline, flip=False, white_key=True):
    base, mask, m, checks, ok = post(rgba_bytes, rgb_bytes, outline=outline, flip=flip,
                                     white_key=white_key)
    MODELS.mkdir(parents=True, exist_ok=True)
    base.save(MODELS / f"{jid}.png")
    mask.save(MODELS / f"{jid}_mask.png")
    (MODELS / f"{jid}.json").write_text(json.dumps(
        dict(metrics=m, checks=[(g, bool(o), d) for g, o, d in checks], ok=ok), indent=1))
    review_jpg(base, m, ok, REVIEW / f"{jid}.jpg")
    return m, checks, ok


# --------------------------------------------------------------- driver --
def pick_keys(arg):
    keys = arg.split(",") if arg else [k for k, v in P.CONCEPTS.items() if v["concept"]]
    unknown = [k for k in keys if k not in P.CONCEPTS]
    if unknown:
        sys.exit(f"unknown concept key(s): {unknown}; have {list(P.CONCEPTS)}")
    return [k for k in keys if P.CONCEPTS[k]["concept"]]


def cmd_render(a):
    for d in (RAW, MODELS, REVIEW):
        d.mkdir(parents=True, exist_ok=True)
    wanted = []
    for key in pick_keys(a.keys):
        for i in range(a.seeds):
            seed = P.seed_for(key, i)
            jid = job_id(key, seed, a.mode)
            if (RAW / f"{jid}_rgba.png").exists() and not a.force:
                continue
            if a.dry_run:
                print(f"## {jid}\n{P.prompt_for(key, a.mode)}\n")
                continue
            wanted.append((key, seed, jid))
    if a.dry_run or not wanted:
        if not a.dry_run:
            print("nothing to render (all cached) - use `post` to redo the local steps")
        return

    ref = backend.upload(reference_png()) if a.mode == "kontext" else None
    if not a.no_free:
        backend.free_models()
    jobs = [(jid, render_graph(key, seed, ref, a.steps, a.guidance, a.mode, a.lora))
            for key, seed, jid in wanted]

    def handler(jid, outputs):
        rgba = backend.fetch(outputs["31"]["images"][0])
        rgb = backend.fetch(outputs["34"]["images"][0])
        (RAW / f"{jid}_rgba.png").write_bytes(rgba)
        (RAW / f"{jid}_rgb.png").write_bytes(rgb)
        m, checks, ok = finish(jid, rgba, rgb, a.outline, a.flip, a.mode != "pony")
        print("   " + G.fmt_row(jid, m, checks, ok))

    backend.run_jobs(jobs, handler, "plane")


def cmd_post(a):
    n = 0
    keys = set(pick_keys(a.keys)) if a.keys else None
    for rgba_path in sorted(RAW.glob("*_rgba.png")):
        jid = rgba_path.name[:-len("_rgba.png")]
        if keys and jid.split("__")[0] not in keys:
            continue
        rgb_path = RAW / f"{jid}_rgb.png"
        if not rgb_path.exists():
            print(f"[SKIP] {jid}: no _rgb.png")
            continue
        m, checks, ok = finish(jid, rgba_path.read_bytes(), rgb_path.read_bytes(), a.outline,
                               a.flip, "__pony__" not in jid)
        print(G.fmt_row(jid, m, checks, ok))
        n += 1
    print(f"post: {n} render(s)")


def load_results():
    """{key: [(jid, metrics, checks, ok)]} from out/models/*.json."""
    res = {}
    for j in sorted(MODELS.glob("*.json")):
        jid = j.stem
        key, _seed = parse_job_id(jid)
        d = json.loads(j.read_text())
        res.setdefault(key, []).append((jid, d["metrics"], d["checks"], d["ok"]))
    return res


def score(m):
    """Lower is better among passing seeds: closest to the reference's mass
    and centring, most solid core."""
    return (abs(m["fill"] - 0.54) + 2 * (1 - m["core"])
            + abs(m["cx"] - 64) / 12 + abs(m["cy"] - 64) / 12)


def best_jid(entries):
    passing = [e for e in entries if e[3]]
    if not passing:
        return None
    return min(passing, key=lambda e: score(e[1]))[0]


def cmd_sheet(_a):
    res = load_results()
    ref_m = G.measure(Image.open(BASE_SPRITE))
    cards = []
    for key, spec in P.CONCEPTS.items():
        if not spec["concept"]:
            continue
        entries = res.get(key, [])
        best = best_jid(entries)
        figs = []
        for jid, m, checks, ok in sorted(entries):
            fails = " ".join(g for g, good, _ in checks if not good)
            cls = "pass" if ok else "fail"
            star = " &#9733; pick" if jid == best else ""
            label = jid[len(key) + 2:]  # "s7010" or "txt__s7010"
            figs.append(
                f'<figure class="{cls}"><a href="out/models/{jid}.png"><img src="review/{jid}.jpg"></a>'
                f'<figcaption>{label}{star}<br>{m["w"]}x{m["h"]} fill {m["fill"]:.2f} core {m["core"]:.2f} '
                f'red {m["red"]:.2f}<br>{"PASS" if ok else fails}</figcaption></figure>')
        cards.append(f'<h3>{spec["name"]} <small>{key} - {len(entries)} seed(s), '
                     f'{sum(1 for e in entries if e[3])} pass</small></h3><div class=grid>{"".join(figs)}</div>')
    html = ("<!doctype html><meta charset=utf-8><title>Doodlebugs plane models</title>"
            "<style>body{background:#1b1e24;color:#ddd;font:14px system-ui;margin:24px}"
            ".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px}"
            "figure{margin:0;border:2px solid #345;border-radius:8px;padding:6px}"
            "figure.pass{border-color:#3a6}figure.fail{border-color:#a33;opacity:.75}"
            "img{width:100%;image-rendering:pixelated;border-radius:4px;display:block}"
            "figcaption{font:12px monospace;padding:4px 0;color:#9ab}h3{color:#8cf;margin:28px 0 8px}"
            "small{color:#789;font-weight:normal}</style>"
            f"<h2>Plane models</h2><p>reference BiPlane1: {ref_m['w']}x{ref_m['h']} fill {ref_m['fill']:.2f} "
            f"core {ref_m['core']:.2f} centroid ({ref_m['cx']:.1f},{ref_m['cy']:.1f}). "
            "Yellow box = shared hitbox, white = alpha bbox.</p>" + "".join(cards))
    (HERE / "index.html").write_text(html)
    print(f"gallery -> {HERE / 'index.html'} ({sum(len(v) for v in res.values())} renders)")


def cmd_apply(a):
    res = load_results()
    picks = {}
    if a.pick:
        for item in a.pick.split(","):
            key, val = item.split("=")
            # bare seed = kontext render; anything else is the jid tail ("txt__s7043")
            picks[key] = f"{key}__s{val}" if val.isdigit() else f"{key}__{val}"
    skip = set(a.skip.split(",")) if a.skip else set()
    UM.ensure_folder(ASSETS_DIR)
    applied, skipped = [], []
    for key, spec in P.CONCEPTS.items():
        if not spec["concept"] or key in skip:
            continue
        jid = picks.get(key, best_jid(res.get(key, [])))
        if jid is None:
            skipped.append(key)
            continue
        entry = next((e for e in res.get(key, []) if e[0] == jid), None)
        if entry is None:
            sys.exit(f"{key}: {jid} has no render (out/models/{jid}.json)")
        if not entry[3] and not a.allow_fail:
            sys.exit(f"{key}: {jid} fails the gate - pick another or --allow-fail")
        for suffix, kind in (("", "sprite"), ("_mask", "mask")):
            dst = ASSETS_DIR / f"model_{key}{suffix}.png"
            dst.write_bytes((MODELS / f"{jid}{suffix}.png").read_bytes())
            UM.write_meta(dst, kind)
        applied.append((key, jid, entry[1]))
    (OUT / "applied.json").write_text(json.dumps({k: j for k, j, _ in applied}, indent=1))
    for key, jid, m in applied:
        print(f"{key:14s} {jid:26s} {m['w']}x{m['h']} fill {m['fill']:.2f} core {m['core']:.2f}")
    print(f"applied {len(applied)} model(s) -> {ASSETS_DIR}")
    if skipped:
        print(f"no passing seed yet (stay hidden in the picker): {', '.join(skipped)}")
    print("Unity: Doodlebugs -> Validate Plane Models, then play - the picker's shape row lists whatever is present.")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("render")
    p.add_argument("--keys", help="comma list (default: every concept)")
    p.add_argument("--mode", choices=["kontext", "txt2img", "pony"], default="kontext",
                   help="kontext = redesign the BiPlane1 reference; txt2img = FLUX from the "
                        "prompt alone; pony = Pony Diffusion V6 XL + the Spacecraft LoRA")
    p.add_argument("--lora", type=float, default=0.8, help="pony mode: LoRA strength")
    p.add_argument("--flip", action="store_true",
                   help="mirror the render horizontally - use when the model drew it nose-left")
    p.add_argument("--seeds", type=int, default=2, help="seeds per concept")
    p.add_argument("--steps", type=int, default=20)
    p.add_argument("--guidance", type=float, default=3.5)
    p.add_argument("--outline", action="store_true", help="1 px dark outline pass")
    p.add_argument("--force", action="store_true", help="re-render cached seeds")
    p.add_argument("--no-free", action="store_true",
                   help="skip ComfyUI /free (another batch is using the box)")
    p.add_argument("--dry-run", action="store_true")
    p.set_defaults(fn=cmd_render)

    p = sub.add_parser("post", help="re-run the local pipeline over out/raw (no GPU)")
    p.add_argument("--keys")
    p.add_argument("--outline", action="store_true")
    p.add_argument("--flip", action="store_true", help="mirror horizontally (drawn nose-left)")
    p.set_defaults(fn=cmd_post)

    p = sub.add_parser("sheet"); p.set_defaults(fn=cmd_sheet)

    p = sub.add_parser("apply")
    p.add_argument("--pick", help="key=seed or key=txt__s<seed> overrides for the best-passing default")
    p.add_argument("--skip", help="comma list of concepts NOT to ship even if a seed passes "
                                  "(gate-passing but off-concept renders, e.g. canard)")
    p.add_argument("--allow-fail", action="store_true", help="ship a --pick even if it fails the gate")
    p.set_defaults(fn=cmd_apply)

    a = ap.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
