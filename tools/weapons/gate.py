#!/usr/bin/env python3
"""Art contract for projectile sprites and effect flipbooks.

Two gate families, both pure Pillow (no numpy, no GPU) so the same rules can be
mirrored in Assets/Doodlebugs/Editor (Doodlebugs -> Validate Projectile Art)
and so `post` can gate without touching SPARK.

PROJECTILE (P1-P6). A projectile is tiny and moves fast; what makes it legible
is that it FILLS its canvas along its long axis and that nothing else is in the
frame. The canvas is the form's (forms.py), never the render's.

  P1 size      exactly the form canvas
  P2 extent    alpha bbox covers >= 60 % of the canvas along its LONG axis
  P3 ring      the outer 1 px ring is empty (Unity's sprite packer bleeds, and
               a bullet that touches the edge looks clipped when it rotates)
  P4 palette   <= 16 distinct opaque colours (plan D3: quantised retro look)
  P5 facing    right-facing forms only - the opaque mass centroid sits in the
               right half, i.e. the heavy end is the nose. A fail here is
               usually a MIRROR, not a bad render: metrics["flip"] says so and
               generate_projectiles.py acts on it instead of rejecting.
  P6 mass      at least MIN_OPAQUE_FRAC of the bbox is opaque - a wispy outline
               reads as nothing at 32 px

FLIPBOOK (F1-F6). An impact/explosion is a shape that grows and dies. What the
gate is really testing is that the frames form an ARC, because separately
rendered frames never do.

  F1 count     exactly the kind's frame count (impact 6, explosion 8)
  F2 size      every frame exactly the kind's canvas
  F3 arc       alpha bbox radius rises then falls, no dips on the way up and no
               bumps on the way down, and the peak is genuinely bigger than
               frame 0
  F4 start     frame 0 bbox area < 30 % of the canvas (it starts as a spark)
  F5 end       last frame < 5 % opaque coverage (it actually dies)
  F6 palette   <= 16 distinct opaque colours across the WHOLE sequence

    python3 tools/weapons/gate.py Assets/Doodlebugs/Resources/Sprites/Projectiles/fire/*.png
    python3 tools/weapons/gate.py Assets/Doodlebugs/Resources/Sprites/Effects/fire/impact_*.png

Files whose stem is "<kind>_<NN>" are grouped into one flipbook per directory;
anything else is gated as a projectile, with the form taken from the stem.
"""
import re
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import forms as F  # noqa: E402

ALPHA = 128                    # opaque threshold, same cut as tools/planes
PALETTE_MAX = 16               # P4 / F6
LONG_AXIS_MIN = 0.60           # P2
MIN_OPAQUE_FRAC = 0.28         # P6 - share of the bbox that must be solid
RING = 1                       # P3 - px of canvas edge that must stay empty
START_AREA_MAX = 0.30          # F4
END_COVERAGE_MAX = 0.05        # F5

# impact/explosion geometry, mirrored in EffectLibrary.cs.
KINDS = {
    "impact": dict(frames=6, canvas=(64, 64), fps=24),
    "explosion": dict(frames=8, canvas=(96, 96), fps=20),
}

P_GATES = ("P1size", "P2extent", "P3ring", "P4palette", "P5facing", "P6mass")
F_GATES = ("F1count", "F2size", "F3arc", "F4start", "F5end", "F6palette")


# --------------------------------------------------------------- measure --
def measure_image(im):
    """Per-image primitives shared by both families. Image space, y=0 on top."""
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()
    min_x, max_x, min_y, max_y = w, -1, h, -1
    sum_x = sum_y = opaque = 0
    colours = set()
    ring_hit = False
    for y in range(h):
        for x in range(w):
            p = px[x, y]
            if p[3] < ALPHA:
                continue
            opaque += 1
            sum_x += x
            sum_y += y
            if x < min_x: min_x = x
            if x > max_x: max_x = x
            if y < min_y: min_y = y
            if y > max_y: max_y = y
            colours.add(p[:3])
            if x < RING or x >= w - RING or y < RING or y >= h - RING:
                ring_hit = True
    if opaque == 0:
        return dict(size=(w, h), w=0, h=0, bbox=None, fill=0.0, cx=0.0, cy=0.0,
                    opaque=0, coverage=0.0, colours=0, ring_hit=ring_hit,
                    radius=0.0, area=0.0, palette=[])
    bw, bh = max_x - min_x + 1, max_y - min_y + 1
    return dict(
        size=(w, h), w=bw, h=bh, bbox=(min_x, min_y, max_x, max_y),
        fill=opaque / (bw * bh),
        cx=sum_x / opaque, cy=sum_y / opaque,
        opaque=opaque, coverage=opaque / (w * h),
        colours=len(colours), ring_hit=ring_hit,
        radius=max(bw, bh) / 2.0,
        area=(bw * bh) / (w * h),
        palette=sorted(colours)[:PALETTE_MAX + 1],
    )


# ------------------------------------------------------------ projectile --
def measure_projectile(im, form):
    """measure_image + the form's contract (canvas, facing)."""
    spec = F.get(form)
    cw, ch = spec["canvas"]
    m = measure_image(im)
    m["form"] = form
    m["canvas"] = (cw, ch)
    m["facing"] = spec["facing"]
    # Long axis of the CANVAS, not of the sprite: a tracer that came back
    # square would pass a "fills its own bbox" test and still look wrong.
    m["long"] = "x" if cw >= ch else "y"
    m["long_frac"] = (m["w"] / cw) if m["long"] == "x" else (m["h"] / ch)
    # Right-facing forms want the mass behind the nose, i.e. centroid right of
    # centre. The margin is half a pixel of slack for genuinely symmetric art.
    m["mass_right"] = m["cx"] > (cw - 1) / 2.0 if m["opaque"] else False
    m["flip"] = bool(spec["facing"] == "right" and m["opaque"] and not m["mass_right"])
    return m


def check_projectile(m):
    cw, ch = m["canvas"]
    return [
        ("P1size", m["size"] == (cw, ch), f"{m['size'][0]}x{m['size'][1]} == {cw}x{ch}"),
        ("P2extent", m["long_frac"] >= LONG_AXIS_MIN,
         f"{m['long']} {m['long_frac']:.2f} >= {LONG_AXIS_MIN}"),
        ("P3ring", not m["ring_hit"], f"outer {RING} px ring empty"),
        ("P4palette", 0 < m["colours"] <= PALETTE_MAX, f"{m['colours']} <= {PALETTE_MAX}"),
        ("P5facing", not m["flip"],
         "any" if m["facing"] != "right" else f"centroid x {m['cx']:.1f} in right half"),
        ("P6mass", m["fill"] >= MIN_OPAQUE_FRAC, f"fill {m['fill']:.2f} >= {MIN_OPAQUE_FRAC}"),
    ]


def gate_projectile(src, form):
    """(ok, metrics) for a projectile sprite. `src` is a path or an Image;
    metrics carries "checks" and "flip" (P5 is fixable by a mirror)."""
    im = src if isinstance(src, Image.Image) else Image.open(src)
    m = measure_projectile(im, form)
    checks = check_projectile(m)
    m["checks"] = [(g, bool(ok), d) for g, ok, d in checks]
    ok = all(c[1] for c in checks)
    m["ok"] = ok
    return ok, m


# -------------------------------------------------------------- flipbook --
def measure_flipbook(frames, kind):
    """frames: list of paths or Images, in order. One metrics dict for the
    sequence, with the per-frame rows under "frames"."""
    spec = KINDS[kind]
    cw, ch = spec["canvas"]
    ims = [f if isinstance(f, Image.Image) else Image.open(f) for f in frames]
    rows = [measure_image(im) for im in ims]
    colours = set()
    for im in ims:
        im = im.convert("RGBA")
        px = im.load()
        for y in range(im.height):
            for x in range(im.width):
                p = px[x, y]
                if p[3] >= ALPHA:
                    colours.add(p[:3])
                    if len(colours) > PALETTE_MAX:
                        break
            if len(colours) > PALETTE_MAX:
                break

    radii = [r["radius"] for r in rows]
    peak = radii.index(max(radii)) if radii else 0
    rising = all(radii[i] <= radii[i + 1] + 1e-9 for i in range(peak))
    falling = all(radii[i] >= radii[i + 1] - 1e-9 for i in range(peak, len(radii) - 1))
    grew = bool(radii) and radii[peak] > radii[0]
    return dict(
        kind=kind, canvas=(cw, ch), want_frames=spec["frames"], fps=spec["fps"],
        count=len(rows),
        sizes_ok=all(r["size"] == (cw, ch) for r in rows),
        radii=[round(r, 2) for r in radii], peak=peak,
        rising=rising, falling=falling, grew=grew,
        start_area=rows[0]["area"] if rows else 1.0,
        end_coverage=rows[-1]["coverage"] if rows else 1.0,
        colours=len(colours),
        frames=rows,
    )


def check_flipbook(m):
    cw, ch = m["canvas"]
    return [
        ("F1count", m["count"] == m["want_frames"], f"{m['count']} == {m['want_frames']}"),
        ("F2size", m["sizes_ok"], f"every frame {cw}x{ch}"),
        ("F3arc", m["rising"] and m["falling"] and m["grew"],
         f"radii {m['radii']} peak@{m['peak']}"
         f"{'' if m['rising'] else ' (dip on the way up)'}"
         f"{'' if m['falling'] else ' (bump on the way down)'}"
         f"{'' if m['grew'] else ' (never grew)'}"),
        ("F4start", m["start_area"] < START_AREA_MAX,
         f"{m['start_area']:.2f} < {START_AREA_MAX}"),
        ("F5end", m["end_coverage"] < END_COVERAGE_MAX,
         f"{m['end_coverage']:.3f} < {END_COVERAGE_MAX}"),
        ("F6palette", 0 < m["colours"] <= PALETTE_MAX, f"{m['colours']} <= {PALETTE_MAX}"),
    ]


def gate_flipbook(frames, kind):
    """(ok, metrics) for a whole flipbook."""
    m = measure_flipbook(frames, kind)
    checks = check_flipbook(m)
    m["checks"] = [(g, bool(ok), d) for g, ok, d in checks]
    ok = all(c[1] for c in checks)
    m["ok"] = ok
    return ok, m


# ------------------------------------------------------------- reporting --
def fmt_projectile(name, m):
    fails = " ".join(g for g, ok, _ in m["checks"] if not ok)
    return (f"{name:30s} {m['size'][0]:3d}x{m['size'][1]:<3d} bbox {m['w']:3d}x{m['h']:<3d} "
            f"long {m['long_frac']:.2f} fill {m['fill']:.2f} cx {m['cx']:5.1f} "
            f"col {m['colours']:3d}  "
            f"{'PASS' if m['ok'] else 'FAIL ' + fails}{'  [flip]' if m['flip'] else ''}")


def fmt_flipbook(name, m):
    fails = " ".join(g for g, ok, _ in m["checks"] if not ok)
    return (f"{name:30s} {m['count']}f r{m['radii']} start {m['start_area']:.2f} "
            f"end {m['end_coverage']:.3f} col {m['colours']:3d}  "
            f"{'PASS' if m['ok'] else 'FAIL ' + fails}")


# ------------------------------------------------------------------ main --
_FRAME_RE = re.compile(r"^(?P<kind>impact|explosion)_(?P<idx>\d+)$")


def main(argv):
    if not argv:
        sys.exit(__doc__)
    books, singles = {}, []
    for arg in argv:
        p = Path(arg)
        mt = _FRAME_RE.match(p.stem)
        if mt:
            books.setdefault((p.parent, mt.group("kind")), []).append((int(mt.group("idx")), p))
        else:
            singles.append(p)

    bad = 0
    for p in singles:
        form = p.stem if p.stem in F.FORMS else None
        if form is None:
            print(f"{p.stem:30s} SKIP  not a form name ({list(F.FORMS)}) nor a "
                  f"<kind>_NN flipbook frame")
            continue
        ok, m = gate_projectile(p, form)
        print(fmt_projectile(f"{p.parent.name}/{p.stem}", m))
        bad += not ok
    for (parent, kind), items in sorted(books.items(), key=lambda kv: str(kv[0])):
        frames = [p for _i, p in sorted(items)]
        ok, m = gate_flipbook(frames, kind)
        print(fmt_flipbook(f"{parent.name}/{kind}", m))
        bad += not ok
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main(sys.argv[1:])
