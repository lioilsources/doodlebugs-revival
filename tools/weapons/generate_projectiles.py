#!/usr/bin/env python3
"""Projectile sprite generator - one bullet per (element, form).

FLUX.1-dev paints the thing on a white ground at 1024x1024, RMBG-2.0 keys it
in the same job on SPARK, and the local post-process turns a painting into a
32x16 game sprite that satisfies gate.py:

  1. alpha = RMBG mask UNION white-key of the render (RMBG alone eats a
     tracer's thin tail), colours un-matted against the white ground
  2. drop every detached component but the biggest - FLUX likes to leave a
     speck of ground shadow or a second smaller bullet in the corner
  3. crop to the silhouette, uniform-scale into the form canvas leaving a 1 px
     clear ring, centre
  4. mirror if the mass ended up on the wrong side (gate P5 reports `flip`
     rather than failing outright - a mirror is the whole fix)
  5. palette-quantise to <= 16 colours AFTER the downscale, hard alpha
  6. gate; every seed keeps its metrics in out/sprites/*.json

Outputs per (element, form, seed) job id "<element>__<form>__s<seed>":
  out/raw/<jid>_rgba.png, _rgb.png   SPARK results (resume cache)
  out/sprites/<jid>.png              the sprite
  out/sprites/<jid>.json             metrics + gate verdict
  review/<jid>_grey.png, _white.png  8x previews on both backdrops
  index.html                         every seed, both backdrops, best starred

The two backdrops are the review that matters: a venom mine has to disappear
against a cloud and a sniper bolt must not (plan 24 section 4.1).

Usage:
  python3 tools/weapons/generate_projectiles.py render                     # 6 elements x 6 forms x --seeds
  python3 tools/weapons/generate_projectiles.py render --elements batch1 --seeds 3
  python3 tools/weapons/generate_projectiles.py render --elements fire --forms bomb,rocket
  python3 tools/weapons/generate_projectiles.py post                       # redo steps 1-6, no GPU
  python3 tools/weapons/generate_projectiles.py sheet && open tools/weapons/index.html
  python3 tools/weapons/generate_projectiles.py apply                      # best passing seed -> Assets
  python3 tools/weapons/generate_projectiles.py apply --pick fire.bomb=4301
  python3 tools/weapons/generate_projectiles.py render --dry-run           # prompts only

Reuses tools/backgrounds/spark_backgrounds.py (ComfyUI client, FLUX graph with
NAG negatives, RMBG tail, run_jobs, white_key) and tools/planes/unity_meta.py.
Keys/ids live in elements.py and forms.py and must match the C# side.
"""
import argparse
import io
import json
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageFilter

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
ASSETS_DIR = ROOT / "Assets/Doodlebugs/Resources/Sprites/Projectiles"
OUT = HERE / "out"
RAW = OUT / "raw"
SPRITES = OUT / "sprites"
REVIEW = HERE / "review"

sys.path.insert(0, str((ROOT / "tools/backgrounds").resolve()))
import spark_backgrounds as backend  # noqa: E402

sys.path.insert(0, str((ROOT / "tools/planes").resolve()))
import unity_meta as UM  # noqa: E402

sys.path.insert(0, str(HERE))
import elements as E  # noqa: E402
import forms as F  # noqa: E402
import gate as G  # noqa: E402

GEN = (1024, 1024)             # FLUX native square; the subject fills it
PALETTE = 16                   # gate P4 ceiling - quantise exactly to it
PREVIEW_SCALE = 8              # NEAREST upscale for the review sheet
GREY = (128, 132, 138, 255)    # mid-grey backdrop
CLOUD = (244, 247, 250, 255)   # cloud white - the backdrop a mine must vanish into


# ---------------------------------------------------------------- prompt --
def prompt_for(element, form):
    """shape (forms.py) + material (elements.py) + the shared style block.
    Never varied per element beyond the material - that is the whole answer to
    plan 24's 'style drift between elements' risk."""
    return f"{F.prompt_shape(form)}, {E.prompt_material(element)}, {E.STYLE}"


def job_id(element, form, seed):
    return f"{element}__{form}__s{seed}"


def parse_job_id(jid):
    """'<element>__<form>__s<seed>' -> (element, form, seed)."""
    element, form, tail = jid.split("__")
    return element, form, int(tail[1:])


def seed_for(element, form, i):
    """One seed lane per (element, form) so adding seeds to one pair never
    shifts another's cache."""
    return E.seed_for(element, i) + list(F.FORMS).index(form)


def render_graph(element, form, seed, steps, guidance):
    g = backend.flux_graph(prompt_for(element, form), GEN, seed, steps, guidance,
                           negative=E.NEG)
    backend.rmbg_tail(g, ["9", 0], f"dbg/proj_{element}_{form}_s{seed}")
    return g


# ------------------------------------------------------------------ post --
def largest_component(mask, scale=4):
    """Keep mask (L, full size) of the biggest connected blob, or None when
    there is only one. Labelled on a /scale copy - pure Pillow floodfill is
    Python-speed, and a 1024 px render is 1 M pixels.

    The component's pixels are collected as a list rather than painted into an
    "I" label image and thresholded afterwards: since Pillow 11, Image.point()
    on an I/F image hands back an ImagePointTransform instead of evaluating the
    lambda per value, so `labels.point(lambda v: 255 if v == keep else 0, "L")`
    silently returns the untouched I image and the next ImageChops call dies
    with "images do not match"."""
    small = mask.resize((max(1, mask.width // scale), max(1, mask.height // scale)),
                        Image.NEAREST)
    small = small.point(lambda v: 255 if v > 127 else 0)   # L mode: still a LUT
    seen = Image.new("L", small.size, 0)
    lp, sp = seen.load(), small.load()
    w, h = small.size
    best = None
    n = 0
    for y in range(h):
        for x in range(w):
            if not sp[x, y] or lp[x, y]:
                continue
            n += 1
            stack = [(x, y)]
            lp[x, y] = 255
            pixels = []
            while stack:
                cx, cy = stack.pop()
                pixels.append((cx, cy))
                for nx, ny in ((cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1)):
                    if 0 <= nx < w and 0 <= ny < h and sp[nx, ny] and not lp[nx, ny]:
                        lp[nx, ny] = 255
                        stack.append((nx, ny))
            if best is None or len(pixels) > len(best):
                best = pixels
    if n < 2:
        return None
    keep_small = Image.new("L", small.size, 0)
    kp = keep_small.load()
    for px, py in best:
        kp[px, py] = 255
    # MaxFilter recovers the pixel the /scale round trip shaved off the edge.
    return keep_small.resize(mask.size, Image.NEAREST).filter(ImageFilter.MaxFilter(3))


def normalise(src_rgba, form):
    """Crop to the silhouette and uniform-scale it into the form canvas with a
    RING px clear border, centred. Returns the RGBA canvas."""
    cw, ch = F.canvas(form)
    alpha = src_rgba.getchannel("A")
    hard = alpha.point(lambda v: 255 if v > 127 else 0)
    # A 5 px opening drops speck noise on the white ground; a stray dot 300 px
    # from the bullet would otherwise set the bbox and shrink the whole sprite.
    blob = hard.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(5))
    keep = largest_component(blob)
    if keep is not None:
        blob = ImageChops.multiply(blob, keep)
        src_rgba = src_rgba.copy()
        src_rgba.putalpha(ImageChops.multiply(alpha, keep))
    bbox = blob.getbbox()
    if not bbox:
        raise ValueError("empty render")
    crop = src_rgba.crop(bbox)
    bw, bh = crop.size
    inner_w, inner_h = cw - 2 * G.RING, ch - 2 * G.RING
    scale = min(inner_w / bw, inner_h / bh)
    nw, nh = max(1, round(bw * scale)), max(1, round(bh * scale))
    # Premultiplied resize: straight-alpha LANCZOS bleeds the transparent
    # pixels' colour into the edge, which at 16 px is the whole sprite.
    small = crop.convert("RGBa").resize((nw, nh), Image.LANCZOS).convert("RGBA")
    canvas = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    canvas.paste(small, (round(cw / 2 - nw / 2), round(ch / 2 - nh / 2)))
    return canvas


def quantise(canvas):
    """Palette-quantise the opaque pixels to <= PALETTE colours and harden the
    alpha. Transparent pixels are painted the mean body colour first so they
    don't eat palette slots (the tools/planes trick)."""
    alpha = canvas.getchannel("A").point(lambda v: 255 if v > 127 else 0)
    rgb = canvas.convert("RGB")
    mean = rgb.resize((1, 1), Image.BOX).getpixel((0, 0))
    filled = Image.composite(rgb, Image.new("RGB", rgb.size, mean), alpha)
    q = filled.quantize(colors=PALETTE, method=Image.Quantize.MEDIANCUT,
                        dither=Image.Dither.NONE).convert("RGB")
    out = q.convert("RGBA")
    out.putalpha(alpha)
    return out


def post(rgba_bytes, rgb_bytes, form, white_key=True):
    rgba = Image.open(io.BytesIO(rgba_bytes)).convert("RGBA")
    rgb = Image.open(io.BytesIO(rgb_bytes)).convert("RGB")
    if rgb.size != rgba.size:
        rgb = rgb.resize(rgba.size, Image.LANCZOS)
    if white_key:
        keyed = backend.white_key(rgb)
        alpha = ImageChops.lighter(rgba.getchannel("A"), keyed)
        src = backend.unmatte_white(rgb, alpha).convert("RGBA")
        src.putalpha(alpha)
    else:
        src = rgba

    sprite = quantise(normalise(src, form))
    ok, m = G.gate_projectile(sprite, form)
    if m["flip"]:
        # Mass on the wrong side: the model drew it nose-left. Mirroring is the
        # entire fix and the sprite is otherwise fine, so do it and re-gate.
        sprite = sprite.transpose(Image.FLIP_LEFT_RIGHT)
        ok, m = G.gate_projectile(sprite, form)
        m["flipped"] = True
    return sprite, m, ok


def preview(sprite, backdrop, out_path, scale=PREVIEW_SCALE):
    w, h = sprite.size
    im = Image.new("RGBA", (w * scale, h * scale), backdrop)
    im.alpha_composite(sprite.resize((w * scale, h * scale), Image.NEAREST))
    out_path.parent.mkdir(parents=True, exist_ok=True)
    im.convert("RGB").save(out_path)


def finish(jid, rgba_bytes, rgb_bytes):
    _element, form, _seed = parse_job_id(jid)
    sprite, m, ok = post(rgba_bytes, rgb_bytes, form)
    SPRITES.mkdir(parents=True, exist_ok=True)
    sprite.save(SPRITES / f"{jid}.png")
    (SPRITES / f"{jid}.json").write_text(json.dumps(
        dict(metrics={k: v for k, v in m.items() if k != "palette"}, ok=ok), indent=1))
    preview(sprite, GREY, REVIEW / f"{jid}_grey.png")
    preview(sprite, CLOUD, REVIEW / f"{jid}_white.png")
    return m, ok


# --------------------------------------------------------------- driver --
def pairs(a):
    return [(e, f) for e in E.keys(a.elements) for f in F.keys(a.forms)]


def cmd_render(a):
    for d in (RAW, SPRITES, REVIEW):
        d.mkdir(parents=True, exist_ok=True)
    wanted = []
    for element, form in pairs(a):
        for i in range(a.seeds):
            seed = seed_for(element, form, i)
            jid = job_id(element, form, seed)
            if (RAW / f"{jid}_rgba.png").exists() and not a.force:
                continue
            if a.dry_run:
                print(f"## {jid}\n{prompt_for(element, form)}\n")
                continue
            wanted.append((element, form, seed, jid))
    if a.dry_run or not wanted:
        if not a.dry_run:
            print("nothing to render (all cached) - use `post` to redo the local steps")
        return

    if not a.no_free:
        backend.free_models()
    jobs = [(jid, render_graph(element, form, seed, a.steps, a.guidance))
            for element, form, seed, jid in wanted]

    def handler(jid, outputs):
        rgba = backend.fetch(outputs["31"]["images"][0])
        rgb = backend.fetch(outputs["34"]["images"][0])
        (RAW / f"{jid}_rgba.png").write_bytes(rgba)
        (RAW / f"{jid}_rgb.png").write_bytes(rgb)
        m, _ok = finish(jid, rgba, rgb)
        print("   " + G.fmt_projectile(jid, m))

    backend.run_jobs(jobs, handler, "projectile")


def cmd_post(a):
    want = {(e, f) for e, f in pairs(a)}
    n = 0
    for rgba_path in sorted(RAW.glob("*_rgba.png")):
        jid = rgba_path.name[:-len("_rgba.png")]
        element, form, _seed = parse_job_id(jid)
        if (element, form) not in want:
            continue
        rgb_path = RAW / f"{jid}_rgb.png"
        if not rgb_path.exists():
            print(f"[SKIP] {jid}: no _rgb.png")
            continue
        m, _ok = finish(jid, rgba_path.read_bytes(), rgb_path.read_bytes())
        print(G.fmt_projectile(jid, m))
        n += 1
    print(f"post: {n} render(s)")


def load_results():
    """{(element, form): [(jid, metrics, ok)]} from out/sprites/*.json."""
    res = {}
    for j in sorted(SPRITES.glob("*.json")):
        element, form, _seed = parse_job_id(j.stem)
        d = json.loads(j.read_text())
        res.setdefault((element, form), []).append((j.stem, d["metrics"], d["ok"]))
    return res


def score(m):
    """Lower is better among passing seeds: fills its long axis without
    crowding the ring, solid rather than wispy, few colours rather than many."""
    return (2 * abs(m["long_frac"] - 0.88) + abs(m["fill"] - 0.55)
            + m["colours"] / 200.0)


def best_jid(entries):
    passing = [e for e in entries if e[2]]
    if not passing:
        return None
    return min(passing, key=lambda e: score(e[1]))[0]


def cmd_sheet(_a):
    res = load_results()
    sections = []
    for element in E.ELEMENTS:
        rows = []
        for form in F.FORMS:
            entries = sorted(res.get((element, form), []))
            best = best_jid(entries)
            figs = []
            for jid, m, ok in entries:
                fails = " ".join(g for g, good, _ in m["checks"] if not good)
                star = " &#9733;" if jid == best else ""
                figs.append(
                    f'<figure class="{"pass" if ok else "fail"}">'
                    f'<a href="out/sprites/{jid}.png">'
                    f'<img src="review/{jid}_grey.png"><img src="review/{jid}_white.png"></a>'
                    f'<figcaption>s{jid.rsplit("__s", 1)[1]}{star}<br>'
                    f'{m["w"]}x{m["h"]} long {m["long_frac"]:.2f} fill {m["fill"]:.2f} '
                    f'col {m["colours"]}<br>{"PASS" if ok else fails}</figcaption></figure>')
            if not figs:
                figs = ['<figure class="none"><figcaption>no render yet</figcaption></figure>']
            cw, ch = F.canvas(form)
            rows.append(f'<h4>{form} <small>{cw}x{ch} {F.facing(form)} - '
                        f'{", ".join(F.get(form)["weapons"])}</small></h4>'
                        f'<div class=grid>{"".join(figs)}</div>')
        sections.append(f'<h3 style="color:{E.hex_tint(element)}">{E.get(element)["name"]} '
                        f'<small>{element}</small></h3>{"".join(rows)}')
    html = ("<!doctype html><meta charset=utf-8><title>Doodlebugs projectiles</title>"
            "<style>body{background:#1b1e24;color:#ddd;font:14px system-ui;margin:24px}"
            ".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:12px}"
            "figure{margin:0;border:2px solid #345;border-radius:8px;padding:6px}"
            "figure.pass{border-color:#3a6}figure.fail{border-color:#a33;opacity:.75}"
            "figure.none{border-style:dashed;opacity:.4}"
            "img{image-rendering:pixelated;border-radius:3px;max-width:48%;vertical-align:middle}"
            "figcaption{font:12px monospace;padding:4px 0;color:#9ab}"
            "h3{margin:32px 0 4px}h4{color:#8cf;margin:14px 0 6px;font:13px monospace}"
            "small{color:#789;font-weight:normal}</style>"
            "<h2>Projectiles</h2><p>Left tile = mid-grey, right tile = cloud white. "
            "A mine has to disappear against the cloud; a bolt must not.</p>"
            + "".join(sections))
    (HERE / "index.html").write_text(html)
    print(f"gallery -> {HERE / 'index.html'} ({sum(len(v) for v in res.values())} renders)")


def cmd_apply(a):
    res = load_results()
    picks = {}
    if a.pick:
        for item in a.pick.split(","):
            key, val = item.split("=")
            element, form = key.split(".")
            picks[(element, form)] = job_id(element, form, int(val))
    applied, skipped = [], []
    for element, form in pairs(a):
        jid = picks.get((element, form), best_jid(res.get((element, form), [])))
        if jid is None:
            skipped.append(f"{element}.{form}")
            continue
        entry = next((e for e in res.get((element, form), []) if e[0] == jid), None)
        if entry is None:
            sys.exit(f"{element}.{form}: {jid} has no render (out/sprites/{jid}.json)")
        if not entry[2] and not a.allow_fail:
            sys.exit(f"{element}.{form}: {jid} fails the gate - pick another or --allow-fail")
        dst_dir = ASSETS_DIR / element
        UM.ensure_folder(dst_dir)
        dst = dst_dir / f"{form}.png"
        dst.write_bytes((SPRITES / f"{jid}.png").read_bytes())
        UM.write_meta(dst, "sprite", pivot=F.pivot(form))
        applied.append((element, form, jid, entry[1]))
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "applied_projectiles.json").write_text(json.dumps(
        {f"{e}.{f}": j for e, f, j, _ in applied}, indent=1))
    for element, form, jid, m in applied:
        pv = F.pivot(form)
        print(f"{element:10s} {form:8s} {jid:26s} {m['w']}x{m['h']} "
              f"long {m['long_frac']:.2f} fill {m['fill']:.2f}"
              f"{'  pivot ' + str(pv) if pv else ''}")
    print(f"applied {len(applied)} sprite(s) -> {ASSETS_DIR}")
    if skipped:
        print(f"no passing seed yet (Bullet falls back to metal, then to the "
              f"shared tracer): {', '.join(skipped)}")
    print("Unity: Doodlebugs -> Validate Projectile Art, then play.")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    def common(p):
        p.add_argument("--elements", help="comma list, or 'batch1' (default: all six)")
        p.add_argument("--forms", help="comma list (default: all six)")
        return p

    p = common(sub.add_parser("render"))
    p.add_argument("--seeds", type=int, default=2, help="seeds per (element, form)")
    p.add_argument("--steps", type=int, default=24)
    p.add_argument("--guidance", type=float, default=3.5)
    p.add_argument("--force", action="store_true", help="re-render cached seeds")
    p.add_argument("--no-free", action="store_true",
                   help="skip ComfyUI /free (another batch is using the box)")
    p.add_argument("--dry-run", action="store_true")
    p.set_defaults(fn=cmd_render)

    p = common(sub.add_parser("post", help="re-run the local pipeline over out/raw (no GPU)"))
    p.set_defaults(fn=cmd_post)

    p = sub.add_parser("sheet"); p.set_defaults(fn=cmd_sheet)

    p = common(sub.add_parser("apply"))
    p.add_argument("--pick", help="element.form=seed overrides, e.g. fire.bomb=4301,venom.mine=4502")
    p.add_argument("--allow-fail", action="store_true", help="ship a --pick even if it fails the gate")
    p.set_defaults(fn=cmd_apply)

    a = ap.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
