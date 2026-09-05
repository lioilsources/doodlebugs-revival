#!/usr/bin/env python3
"""Impact and explosion flipbooks - 6 x 64 px and 8 x 96 px per element.

Two ways to get them, one gate (gate.py, F1-F6) and one output shape:

  FLUX mode (default). One 2048x512 CONTACT SHEET per (element, kind): four
  phases left to right - spark, burst, bloom, fade. One prompt gives one
  palette across the whole sequence, which four separately rendered frames
  never do. Locally the sheet is sliced into four 512 px panels, keyed
  (RMBG union white-key), scaled by ONE common factor so the phases keep
  their relative sizes, and Pillow cross-fades them into the 6/8 frames.

  --procedural. Deterministic Pillow flipbooks drawn from primitives with the
  element's own ramp (elements.ramp): expanding rings, radial forks, droplet
  scatter, ember specks, shrapnel sparks, feather fans. Zero GPU, zero
  network, and it always passes the gate - the safety net of plan 24 D4, and
  probably the right answer for lightning, where a fork drawn by code reads
  better at 64 px than a painting shrunk to it.

Both modes share the timing: the radius of frame i is REACH[i] x the canvas
radius, an arc that rises to a peak at the middle and dies to a remnant. The
generator owns the arc, the renderer only supplies the LOOK - which is why a
FLUX sheet whose four phases came back all the same size still animates.

Outputs per (element, kind) job id "<element>__<kind>__proc" / "__s<seed>":
  out/raw/<jid>_rgba.png, _rgb.png    SPARK contact sheets (resume cache)
  out/frames/<jid>/<kind>_NN.png      the flipbook, 00..N-1
  out/frames/<jid>.json               metrics + gate verdict
  review/<jid>_grey.png, _white.png   filmstrips on both backdrops
  effects.html                        every sequence, gate verdict, best starred

Usage:
  python3 tools/weapons/generate_effects.py --procedural render      # no GPU, all 12 sequences
  python3 tools/weapons/generate_effects.py --procedural apply       # -> Assets
  python3 tools/weapons/generate_effects.py render --elements batch1 --seeds 2
  python3 tools/weapons/generate_effects.py post                     # re-slice out/raw, no GPU
  python3 tools/weapons/generate_effects.py sheet && open tools/weapons/effects.html
  python3 tools/weapons/generate_effects.py apply --pick fire.explosion=4401
  python3 tools/weapons/generate_effects.py render --dry-run         # prompts only

`apply` prefers a passing FLUX seed and falls back to the procedural
sequence; `--procedural apply` forces the procedural one even when a seed
passes. Frames land in Resources/Sprites/Effects/<element>/<kind>_NN.png,
which is what EffectLibrary.Frames() loads and caches.
"""
import argparse
import io
import json
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
ASSETS_DIR = ROOT / "Assets/Doodlebugs/Resources/Sprites/Effects"
OUT = HERE / "out"
RAW = OUT / "raw"
FRAMES = OUT / "frames"
REVIEW = HERE / "review"

sys.path.insert(0, str((ROOT / "tools/backgrounds").resolve()))
import spark_backgrounds as backend  # noqa: E402

sys.path.insert(0, str((ROOT / "tools/planes").resolve()))
import unity_meta as UM  # noqa: E402

sys.path.insert(0, str(HERE))
import elements as E  # noqa: E402
import gate as G  # noqa: E402

GEN = (2048, 512)              # contact sheet: four 512 px panels side by side
PANELS = 4
PALETTE = 16                   # gate F6 ceiling
PREVIEW_SCALE = 3
GREY = (128, 132, 138, 255)
CLOUD = (244, 247, 250, 255)

# The arc every sequence follows, as a fraction of the canvas radius. Rises to
# 1.0 at the middle, falls, and ends on a remnant small enough that F5 (< 5 %
# coverage) holds by construction rather than by luck.
REACH_START, REACH_END, REACH_TAIL = 0.20, 0.42, 0.14


def reach_profile(n):
    peak = (n - 1) // 2
    out = []
    for i in range(n):
        if i == n - 1:
            out.append(REACH_TAIL)
        elif i <= peak:
            f = i / peak if peak else 1.0
            out.append(REACH_START + (1.0 - REACH_START) * f)
        else:
            f = (i - peak) / (n - 1 - peak)
            out.append(1.0 - (1.0 - REACH_END) * f)
    return out


def job_id(element, kind, seed=None):
    return f"{element}__{kind}__{'proc' if seed is None else 's%d' % seed}"


def parse_job_id(jid):
    """'<element>__<kind>__proc|s<seed>' -> (element, kind, seed or None)."""
    element, kind, tail = jid.split("__")
    return element, kind, None if tail == "proc" else int(tail[1:])


def seed_for(element, kind, i):
    return E.seed_for(element, i) + 50 * list(G.KINDS).index(kind)


# ---------------------------------------------------------------- prompt --
def prompt_for(element, kind):
    """One sheet, four phases. The panel discipline in the prompt is what the
    slicer relies on - if FLUX paints one big blast across the sheet instead of
    four panels, the gate catches it (every phase comes out the same size, so
    the arc still works, but the look will be mush; re-roll or go procedural)."""
    scale = ("a small hit splash" if kind == "impact" else "a big explosion blast")
    return (f"a contact sheet of four equal square panels side by side in a "
            f"single row, showing four phases of {scale}: {E.prompt_burst(element)}. "
            f"Panel 1 a tiny bright spark just starting, panel 2 the burst "
            f"opening up, panel 3 the full bloom at its widest, panel 4 the "
            f"last faint remnant fading away. Each panel holds one centred "
            f"burst and nothing else, {E.STYLE_FX}")


def render_graph(element, kind, seed, steps, guidance):
    g = backend.flux_graph(prompt_for(element, kind), GEN, seed, steps, guidance,
                           negative=E.NEG)
    backend.rmbg_tail(g, ["9", 0], f"dbg/fx_{element}_{kind}_s{seed}")
    return g


# ------------------------------------------------------------ procedural --
def _rgba(c):
    return (c[0], c[1], c[2], 255)


def shades(element, shift):
    """The element's ramp as RGBA, rotated `shift` steps toward the dark end so
    the fade dims without introducing a single new colour (F6 counts distinct
    opaque colours across the WHOLE sequence)."""
    ramp = [_rgba(c) for c in E.ramp(element, 6)]
    shift = max(0, min(shift, len(ramp) - 1))
    return ramp[shift:] + [ramp[-1]] * shift


def particle_set(element, kind, n):
    """Fixed per (element, kind): a spark keeps its direction for the whole
    sequence, so the flipbook reads as one event expanding rather than as n
    unrelated drawings. Seeded from the element id - never from hash(), which
    Python salts per process."""
    rng = random.Random(seed_for(element, kind, 0))
    out = []
    for i in range(n):
        out.append(dict(
            a=(i + rng.random() * 0.75) * (2 * math.pi / n),
            d=0.55 + 0.45 * rng.random(),
            s=rng.random(),
            j=[rng.uniform(-1.0, 1.0) for _ in range(4)]))
    return out


def _disc(d, cx, cy, r, colour):
    if r < 0.5:
        return
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=colour)


def _ring(d, cx, cy, r, w, colour):
    if r < 1:
        return
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=colour, width=int(w))


def _m_sparks(d, cx, cy, R, pal, parts):
    """metal - a grey puff spitting shrapnel."""
    _disc(d, cx, cy, R * 0.58, pal[4])
    _disc(d, cx, cy, R * 0.36, pal[3])
    for p in parts:
        ux, uy = math.cos(p["a"]), math.sin(p["a"])
        r1 = R * p["d"]
        r0 = r1 * 0.66
        d.line((cx + ux * r0, cy + uy * r0, cx + ux * r1, cy + uy * r1),
               fill=pal[0], width=1)
    _disc(d, cx, cy, R * 0.16, pal[0])


def _m_embers(d, cx, cy, R, pal, parts):
    """fire - a hot core under rising embers."""
    _disc(d, cx, cy, R * 0.60, pal[3])
    _disc(d, cx, cy, R * 0.42, pal[2])
    _disc(d, cx, cy, R * 0.24, pal[1])
    _disc(d, cx, cy, R * 0.10, pal[0])
    for p in parts:
        r = R * p["d"]
        x = cx + math.cos(p["a"]) * r
        y = cy + math.sin(p["a"]) * r - R * 0.20 * p["d"]      # embers rise
        s = 0.5 if p["s"] < 0.5 else 1.0
        d.rectangle((x - s, y - s, x + s, y + s), fill=pal[0 if p["s"] > 0.7 else 2])


def _m_forks(d, cx, cy, R, pal, parts):
    """lightning - jagged radial forks off a white core."""
    for p in parts:
        pts = []
        for k in range(5):
            f = k / 4
            r = R * (0.12 + (p["d"] - 0.12) * f)
            a = p["a"] + p["j"][min(k, 3)] * 0.24 * f
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
        d.line(pts, fill=pal[1], width=1)
        d.line(pts[:3], fill=pal[0], width=1)
    _disc(d, cx, cy, R * 0.18, pal[0])


def _m_droplets(d, cx, cy, R, pal, parts):
    """venom - a splat throwing sagging droplets."""
    _disc(d, cx, cy, R * 0.46, pal[3])
    _disc(d, cx, cy, R * 0.30, pal[2])
    _disc(d, cx, cy, R * 0.14, pal[1])
    for p in parts:
        r = R * p["d"]
        x = cx + math.cos(p["a"]) * r
        y = cy + math.sin(p["a"]) * r + R * 0.20 * p["d"]      # drips sag
        s = max(0.6, R * (0.06 + 0.06 * p["s"]))
        _disc(d, x, y, s, pal[2])
        _disc(d, x, y - s, s * 0.55, pal[1])


def _m_ring(d, cx, cy, R, pal, parts):
    """plasma - a clean energy ring, no smoke."""
    w = max(1, round(R * 0.12))
    _ring(d, cx, cy, R * 0.90, w, pal[2])
    _ring(d, cx, cy, R * 0.62, max(1, w - 1), pal[1])
    _disc(d, cx, cy, R * 0.22, pal[0])
    for p in parts[:6]:
        r = R * (0.86 + 0.12 * p["s"])
        _disc(d, cx + math.cos(p["a"]) * r, cy + math.sin(p["a"]) * r,
              max(0.6, R * 0.05), pal[0])


def _m_feathers(d, cx, cy, R, pal, parts):
    """air - a gust ring shedding feather darts. Biased to the DARK half of
    the ramp: air's tint is 225,240,255, so a burst drawn in its bright shades
    is invisible against the cloud-white half of the review sheet (and against
    the actual clouds it spawns in front of)."""
    _ring(d, cx, cy, R * 0.92, 1, pal[4])
    _ring(d, cx, cy, R * 0.60, 1, pal[3])
    for p in parts:
        r = R * p["d"] * 0.86
        ax, ay = math.cos(p["a"]), math.sin(p["a"])
        px, py = cx + ax * r, cy + ay * r
        ln = max(1.5, R * (0.13 + 0.07 * p["s"]))
        wd = max(1.0, ln * 0.42)
        d.polygon([(px + ax * ln, py + ay * ln),
                   (px - ay * wd, py + ax * wd),
                   (px - ax * ln * 0.6, py - ay * ln * 0.6),
                   (px + ay * wd, py - ax * wd)], fill=pal[3])
    _disc(d, cx, cy, R * 0.20, pal[4])
    _disc(d, cx, cy, R * 0.12, pal[0])


MOTIFS = {"sparks": _m_sparks, "embers": _m_embers, "forks": _m_forks,
          "droplets": _m_droplets, "ring": _m_ring, "feathers": _m_feathers}


def procedural_frames(element, kind):
    """The whole flipbook, drawn. Every primitive is aliased and every colour
    comes out of the element's 6-shade ramp, so the frames are hard-alpha
    <= 6-colour pixel art with no quantiser in the loop."""
    spec = G.KINDS[kind]
    n = spec["frames"]
    size = spec["canvas"][0]
    motif = MOTIFS[E.get(element)["motif"]]
    parts = particle_set(element, kind, 9 if kind == "impact" else 14)
    reach = reach_profile(n)
    peak = (n - 1) // 2
    # -2, not -1: motifs whose particles are drawn AT the reach (feather darts,
    # venom droplets) overshoot it by their own size, and a burst silently
    # clipped by the canvas edge reads as a rectangle at 3x zoom.
    rmax = size / 2.0 - 2.0
    c = (size - 1) / 2.0
    out = []
    for i in range(n):
        im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        pal = shades(element, 0 if i <= peak else (2 if i == n - 1 else 1))
        motif(ImageDraw.Draw(im), c, c, reach[i] * rmax, pal, parts)
        out.append(im)
    return out


# -------------------------------------------------------- FLUX -> frames --
def slice_sheet(rgba_bytes, rgb_bytes):
    """The contact sheet -> four keyed, cropped RGBA panels in phase order."""
    rgba = Image.open(io.BytesIO(rgba_bytes)).convert("RGBA")
    rgb = Image.open(io.BytesIO(rgb_bytes)).convert("RGB")
    if rgb.size != rgba.size:
        rgb = rgb.resize(rgba.size, Image.LANCZOS)
    keyed = backend.white_key(rgb)
    alpha = ImageChops.lighter(rgba.getchannel("A"), keyed)
    src = backend.unmatte_white(rgb, alpha).convert("RGBA")
    src.putalpha(alpha)

    w, h = src.size
    pw = w // PANELS
    panels = []
    for i in range(PANELS):
        panel = src.crop((i * pw, 0, (i + 1) * pw, h))
        bbox = panel.getchannel("A").point(lambda v: 255 if v > 127 else 0).getbbox()
        panels.append(panel.crop(bbox) if bbox else None)
    return panels


def key_radius(panel):
    return max(panel.size) / 2.0 if panel else 0.0


def scale_about_centre(panel, target_r, size):
    """Uniformly scale a cropped panel so its longest side is 2*target_r, then
    centre it on a size x size canvas. Premultiplied resize - straight-alpha
    LANCZOS bleeds transparent colour into the edge."""
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    if panel is None or target_r < 0.5:
        return canvas
    f = (2.0 * target_r) / max(panel.size)
    nw, nh = max(1, round(panel.width * f)), max(1, round(panel.height * f))
    small = panel.convert("RGBa").resize((nw, nh), Image.LANCZOS).convert("RGBA")
    canvas.paste(small, (round(size / 2 - nw / 2), round(size / 2 - nh / 2)))
    return canvas


def quantise_sequence(frames):
    """<= PALETTE colours across the WHOLE flipbook, hard alpha.

    Quantising frame by frame is the obvious version and it is wrong: six
    independent median cuts of the same fireball produce six slightly
    different oranges, the union blows past 16 (F6 fails at 17 on a clean
    synthetic sheet) and the animation shimmers. So the frames are laid out as
    one filmstrip, cut ONCE, and sliced back apart - which also means colour
    n in frame 0 is the same colour n in frame 5.

    Transparent pixels are painted the opaque mean first so they don't eat a
    palette slot; ImageStat with a mask gives that mean without a Python loop."""
    from PIL import ImageStat
    w, h = frames[0].size
    n = len(frames)
    alphas = [f.getchannel("A").point(lambda v: 255 if v > 127 else 0) for f in frames]
    strip = Image.new("RGB", (w * n, h))
    mask = Image.new("L", (w * n, h), 0)
    for i, f in enumerate(frames):
        strip.paste(f.convert("RGB"), (i * w, 0))
        mask.paste(alphas[i], (i * w, 0))
    if mask.getbbox() is None:
        return [f.copy() for f in frames]
    mean = tuple(int(v) for v in ImageStat.Stat(strip, mask).mean)
    filled = Image.composite(strip, Image.new("RGB", strip.size, mean), mask)
    q = filled.quantize(colors=PALETTE, method=Image.Quantize.MEDIANCUT,
                        dither=Image.Dither.NONE).convert("RGB")
    out = []
    for i in range(n):
        frame = q.crop((i * w, 0, (i + 1) * w, h)).convert("RGBA")
        frame.putalpha(alphas[i])
        out.append(frame)
    return out


def interpolate(panels, kind):
    """Four keyframes -> the kind's frame count. The generator owns the timing
    (reach_profile): each output frame scales the two bracketing keyframes to
    the SAME target radius, cross-fades them, then quantises. So the arc F3
    wants is guaranteed even when FLUX painted all four phases the same size,
    and all the render has to supply is the look."""
    spec = G.KINDS[kind]
    n, size = spec["frames"], spec["canvas"][0]
    reach = reach_profile(n)
    rmax = size / 2.0 - 2.0
    out = []
    for i in range(n):
        t = i / (n - 1) * (PANELS - 1)
        lo = min(int(t), PANELS - 2)
        u = t - lo
        target = reach[i] * rmax
        a = scale_about_centre(panels[lo], target, size)
        b = scale_about_centre(panels[lo + 1], target, size)
        out.append(Image.blend(a, b, u) if u > 0 else a)
    return quantise_sequence(out)


# ------------------------------------------------------------------ post --
def filmstrip(frames, backdrop, out_path, scale=PREVIEW_SCALE):
    size = frames[0].width
    strip = Image.new("RGBA", (size * len(frames) * scale, size * scale), backdrop)
    for i, f in enumerate(frames):
        big = f.resize((size * scale, size * scale), Image.NEAREST)
        strip.alpha_composite(big, (i * size * scale, 0))
    out_path.parent.mkdir(parents=True, exist_ok=True)
    strip.convert("RGB").save(out_path)


def finish(jid, frames):
    _element, kind, _seed = parse_job_id(jid)
    d = FRAMES / jid
    d.mkdir(parents=True, exist_ok=True)
    for i, f in enumerate(frames):
        f.save(d / f"{kind}_{i:02d}.png")
    ok, m = G.gate_flipbook(frames, kind)
    (FRAMES / f"{jid}.json").write_text(json.dumps(
        dict(metrics={k: v for k, v in m.items() if k != "frames"}, ok=ok), indent=1))
    filmstrip(frames, GREY, REVIEW / f"{jid}_grey.png")
    filmstrip(frames, CLOUD, REVIEW / f"{jid}_white.png")
    return m, ok


# --------------------------------------------------------------- driver --
def pairs(a):
    kinds = a.kinds.split(",") if a.kinds else list(G.KINDS)
    unknown = [k for k in kinds if k not in G.KINDS]
    if unknown:
        sys.exit(f"unknown kind(s) {unknown}; have {list(G.KINDS)}")
    return [(e, k) for e in E.keys(a.elements) for k in kinds]


def cmd_render(a):
    for d in (RAW, FRAMES, REVIEW):
        d.mkdir(parents=True, exist_ok=True)
    if a.procedural:
        for element, kind in pairs(a):
            jid = job_id(element, kind)
            m, _ok = finish(jid, procedural_frames(element, kind))
            print(G.fmt_flipbook(jid, m))
        return

    wanted = []
    for element, kind in pairs(a):
        for i in range(a.seeds):
            seed = seed_for(element, kind, i)
            jid = job_id(element, kind, seed)
            if (RAW / f"{jid}_rgba.png").exists() and not a.force:
                continue
            if a.dry_run:
                print(f"## {jid}\n{prompt_for(element, kind)}\n")
                continue
            wanted.append((element, kind, seed, jid))
    if a.dry_run or not wanted:
        if not a.dry_run:
            print("nothing to render (all cached) - use `post` to redo the local steps, "
                  "or --procedural for the GPU-free flipbooks")
        return

    if not a.no_free:
        backend.free_models()
    jobs = [(jid, render_graph(element, kind, seed, a.steps, a.guidance))
            for element, kind, seed, jid in wanted]

    def handler(jid, outputs):
        rgba = backend.fetch(outputs["31"]["images"][0])
        rgb = backend.fetch(outputs["34"]["images"][0])
        (RAW / f"{jid}_rgba.png").write_bytes(rgba)
        (RAW / f"{jid}_rgb.png").write_bytes(rgb)
        _element, kind, _seed = parse_job_id(jid)
        m, _ok = finish(jid, interpolate(slice_sheet(rgba, rgb), kind))
        print("   " + G.fmt_flipbook(jid, m))

    backend.run_jobs(jobs, handler, "effect")


def cmd_post(a):
    want = set(pairs(a))
    n = 0
    if a.procedural:
        for element, kind in sorted(want):
            jid = job_id(element, kind)
            m, _ok = finish(jid, procedural_frames(element, kind))
            print(G.fmt_flipbook(jid, m))
            n += 1
        print(f"post: {n} procedural sequence(s)")
        return
    for rgba_path in sorted(RAW.glob("*_rgba.png")):
        jid = rgba_path.name[:-len("_rgba.png")]
        element, kind, _seed = parse_job_id(jid)
        if (element, kind) not in want:
            continue
        rgb_path = RAW / f"{jid}_rgb.png"
        if not rgb_path.exists():
            print(f"[SKIP] {jid}: no _rgb.png")
            continue
        m, _ok = finish(jid, interpolate(
            slice_sheet(rgba_path.read_bytes(), rgb_path.read_bytes()), kind))
        print(G.fmt_flipbook(jid, m))
        n += 1
    print(f"post: {n} sheet(s)")


def load_results():
    """{(element, kind): [(jid, metrics, ok)]} from out/frames/*.json."""
    res = {}
    for j in sorted(FRAMES.glob("*.json")):
        element, kind, _seed = parse_job_id(j.stem)
        d = json.loads(j.read_text())
        res.setdefault((element, kind), []).append((j.stem, d["metrics"], d["ok"]))
    return res


def best_jid(entries, procedural_only=False):
    """A passing FLUX seed wins; the procedural sequence is the fallback (and
    the forced pick under --procedural)."""
    passing = [e for e in entries if e[2]]
    if procedural_only:
        passing = [e for e in passing if e[0].endswith("__proc")]
    if not passing:
        return None
    flux = [e for e in passing if not e[0].endswith("__proc")]
    return (flux or passing)[0][0]


def cmd_sheet(a):
    res = load_results()
    sections = []
    for element in E.ELEMENTS:
        rows = []
        for kind in G.KINDS:
            entries = sorted(res.get((element, kind), []))
            best = best_jid(entries, a.procedural)
            figs = []
            for jid, m, ok in entries:
                fails = " ".join(g for g, good, _ in m["checks"] if not good)
                star = " &#9733;" if jid == best else ""
                tag = jid.rsplit("__", 1)[1]
                figs.append(
                    f'<figure class="{"pass" if ok else "fail"}">'
                    f'<img src="review/{jid}_grey.png"><br>'
                    f'<img src="review/{jid}_white.png">'
                    f'<figcaption>{tag}{star} - {m["count"]}f, {m["colours"]} colours<br>'
                    f'r {m["radii"]} peak@{m["peak"]}<br>'
                    f'start {m["start_area"]:.2f} end {m["end_coverage"]:.3f}<br>'
                    f'{"PASS" if ok else fails}</figcaption></figure>')
            if not figs:
                figs = ['<figure class="none"><figcaption>no sequence yet</figcaption></figure>']
            spec = G.KINDS[kind]
            rows.append(f'<h4>{kind} <small>{spec["frames"]} frames, '
                        f'{spec["canvas"][0]}x{spec["canvas"][1]}, {spec["fps"]} fps</small></h4>'
                        f'<div class=grid>{"".join(figs)}</div>')
        sections.append(f'<h3 style="color:{E.hex_tint(element)}">{E.get(element)["name"]} '
                        f'<small>{element} - {E.get(element)["motif"]}</small></h3>'
                        f'{"".join(rows)}')
    html = ("<!doctype html><meta charset=utf-8><title>Doodlebugs effects</title>"
            "<style>body{background:#1b1e24;color:#ddd;font:14px system-ui;margin:24px}"
            ".grid{display:grid;grid-template-columns:1fr;gap:12px}"
            "figure{margin:0;border:2px solid #345;border-radius:8px;padding:6px;overflow-x:auto}"
            "figure.pass{border-color:#3a6}figure.fail{border-color:#a33;opacity:.75}"
            "figure.none{border-style:dashed;opacity:.4}"
            "img{image-rendering:pixelated;border-radius:3px;max-width:100%}"
            "figcaption{font:12px monospace;padding:4px 0;color:#9ab}"
            "h3{margin:32px 0 4px}h4{color:#8cf;margin:14px 0 6px;font:13px monospace}"
            "small{color:#789;font-weight:normal}</style>"
            "<h2>Effect flipbooks</h2><p>Top strip = mid-grey, bottom = cloud white. "
            "The frames grow then die; the radii row is what gate F3 reads.</p>"
            + "".join(sections))
    (HERE / "effects.html").write_text(html)
    print(f"gallery -> {HERE / 'effects.html'} ({sum(len(v) for v in res.values())} sequence(s))")


def cmd_apply(a):
    res = load_results()
    picks = {}
    if a.pick:
        for item in a.pick.split(","):
            key, val = item.split("=")
            element, kind = key.split(".")
            picks[(element, kind)] = job_id(element, kind,
                                            None if val == "proc" else int(val))
    applied, skipped = [], []
    for element, kind in pairs(a):
        jid = picks.get((element, kind), best_jid(res.get((element, kind), []), a.procedural))
        if jid is None:
            skipped.append(f"{element}.{kind}")
            continue
        entry = next((e for e in res.get((element, kind), []) if e[0] == jid), None)
        if entry is None:
            sys.exit(f"{element}.{kind}: {jid} has no sequence (out/frames/{jid}.json)")
        if not entry[2] and not a.allow_fail:
            sys.exit(f"{element}.{kind}: {jid} fails the gate - pick another or --allow-fail")
        dst_dir = ASSETS_DIR / element
        UM.ensure_folder(dst_dir)
        n = G.KINDS[kind]["frames"]
        for i in range(n):
            name = f"{kind}_{i:02d}.png"
            dst = dst_dir / name
            dst.write_bytes((FRAMES / jid / name).read_bytes())
            UM.write_meta(dst, "sprite")
        applied.append((element, kind, jid, entry[1]))
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "applied_effects.json").write_text(json.dumps(
        {f"{e}.{k}": j for e, k, j, _ in applied}, indent=1))
    for element, kind, jid, m in applied:
        print(f"{element:10s} {kind:10s} {jid:28s} {m['count']} frames, "
              f"{m['colours']} colours, peak@{m['peak']}")
    print(f"applied {len(applied)} sequence(s) -> {ASSETS_DIR}")
    if skipped:
        print(f"no passing sequence yet (EffectLibrary falls back to "
              f"explosion.prefab): {', '.join(skipped)}")
    print("Unity: Doodlebugs -> Validate Projectile Art, then play.")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--procedural", action="store_true",
                    help="deterministic Pillow flipbooks instead of FLUX - no GPU, "
                         "no network, always passes the gate")
    sub = ap.add_subparsers(dest="cmd", required=True)

    def common(p):
        p.add_argument("--elements", help="comma list, or 'batch1' (default: all six)")
        p.add_argument("--kinds", help="impact,explosion (default: both)")
        # Accepted after the subcommand too, so `... post --procedural` works.
        p.add_argument("--procedural", action="store_true", default=None,
                       dest="procedural_sub", help=argparse.SUPPRESS)
        return p

    p = common(sub.add_parser("render"))
    p.add_argument("--seeds", type=int, default=2, help="seeds per (element, kind)")
    p.add_argument("--steps", type=int, default=24)
    p.add_argument("--guidance", type=float, default=3.5)
    p.add_argument("--force", action="store_true", help="re-render cached seeds")
    p.add_argument("--no-free", action="store_true",
                   help="skip ComfyUI /free (another batch is using the box)")
    p.add_argument("--dry-run", action="store_true")
    p.set_defaults(fn=cmd_render)

    p = common(sub.add_parser("post", help="re-slice out/raw (no GPU)"))
    p.set_defaults(fn=cmd_post)

    p = common(sub.add_parser("sheet")); p.set_defaults(fn=cmd_sheet)

    p = common(sub.add_parser("apply"))
    p.add_argument("--pick", help="element.kind=seed|proc overrides, e.g. "
                                  "fire.explosion=4401,lightning.impact=proc")
    p.add_argument("--allow-fail", action="store_true",
                   help="ship a --pick even if it fails the gate")
    p.set_defaults(fn=cmd_apply)

    a = ap.parse_args()
    a.procedural = bool(a.procedural or getattr(a, "procedural_sub", None))
    a.fn(a)


if __name__ == "__main__":
    main()
