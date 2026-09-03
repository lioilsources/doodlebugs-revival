#!/usr/bin/env python3
"""Plane skin generator - 50 texture swaps on one locked silhouette.

Every skin shares BiPlane1's exact 128x128 alpha mask and hitbox: no gameplay
code (collider, ForegroundTile-style alpha reads, sprite bounds) needs to
know skins exist. What changes per skin is only the fill inside the
fuselage/wing region ("paint mask"); the tail-fin ("accent mask") stays pure
red in every skin so PlayerController's existing ColorReplace shader keeps
tinting it per-player - skin choice and player-colour identity stay
independent, per the 2026-09-03 design decision.

    ACCENT (tail fin, x<=26 of the red-keyed area) -> forced pure red always
    PAINT  (rest of the red-keyed area)             -> skin's pattern x luminance
    FIXED  (grey engine/prop/pilot/wheel, non-red)  -> copied unchanged
    outside ALPHA                                   -> transparent

The luminance layer is extracted from the ORIGINAL sprite's red channel
value before any skin exists, so panel-line shading/highlights are identical
and consistent across all 50 skins with zero manual shading work per skin -
classic colourise-a-photo technique, applied to pixel art.

Pattern swatches render on SPARK's ComfyUI the same way as
tools/backgrounds/spark_backgrounds.py (imported directly - same API client,
same FLUX+NAG graph). Swatches are flat materials, not scenes, so there is no
upscale pass: a modest render resolution is downsampled straight into the
128x128 sprite.

Swatches: every render also keeps the 128x128 pattern it painted from
(out/swatch_<key>.png). Those ship to Resources/Sprites/PlaneSkins/Swatches/
and are what PlaneModelCatalog composites onto the other plane SHAPES at
runtime (tools/planes) - the original biplane keeps these baked skins.
`swatches` renders only the swatch for skins whose swatch is missing (the
first batch predates swatches; same seed + prompt = the same pattern).

Usage:
  python3 tools/skins/generate_skins.py masks              # (re)build masks/*.png once, review them
  python3 tools/skins/generate_skins.py render                     # all 50 (skips existing + the no-prompt starter)
  python3 tools/skins/generate_skins.py render --keys jungle_camo,gold_leaf
  python3 tools/skins/generate_skins.py swatches           # re-render just the missing out/swatch_*.png
  python3 tools/skins/generate_skins.py sheet
  python3 tools/skins/generate_skins.py apply               # copy skins + swatches into Assets/.../Resources
  python3 tools/skins/generate_skins.py render --dry-run
"""
import argparse
import io
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
BASE_SPRITE = ROOT / "Assets/Doodlebugs/Sprites/BiPlane/BiPlane1.png"
ASSETS_DIR = ROOT / "Assets/Doodlebugs/Resources/Sprites/PlaneSkins"
MASKS_DIR = HERE / "masks"
OUT_DIR = HERE / "out"
REVIEW_DIR = HERE / "review"

sys.path.insert(0, str((ROOT / "tools/backgrounds").resolve()))
import spark_backgrounds as backend  # noqa: E402  (api/enqueue/wait/fetch/flux_graph/free_models)

sys.path.insert(0, str(HERE))
import skins as S  # noqa: E402

sys.path.insert(0, str((ROOT / "tools/planes").resolve()))
import unity_meta as UM  # noqa: E402  (.meta writer shared with the plane models)

SWATCH_ASSETS_DIR = ASSETS_DIR / "Swatches"
SIZE = 128
ACCENT_MAX_X = 26          # red-keyed pixels at x<=26 are the tail fin accent
SOURCE_RED = (255, 0, 0)   # matches PlayerController's ColorReplace._SourceColor


def is_red(p):
    """The sprite's livery red is not flat - shading uses (255,0,0) down to
    a shadow (141,0,0), always g==b and r well above either. Matching only
    near-pure (255,0,0) (an earlier version of this check) missed the
    darker shading pixels entirely, silently dropping them into the
    "fixed, never repainted" bucket - g==b screens out the brownish wheel
    strut (105,57,36) and the pilot's skin tone (251,173,139), which have
    g != b, without needing a second exclusion list."""
    r, g, b, a = p
    return a > 10 and g == b and g <= 60 and r >= 130


def build_masks():
    """One-time extraction from the original sprite. Idempotent - re-run any
    time BiPlane1.png changes (e.g. a repaint of the base silhouette)."""
    MASKS_DIR.mkdir(parents=True, exist_ok=True)
    im = Image.open(BASE_SPRITE).convert("RGBA")
    w, h = im.size
    assert (w, h) == (SIZE, SIZE), f"expected {SIZE}x{SIZE}, got {im.size}"
    px = im.load()

    alpha = Image.new("L", (w, h), 0)
    accent = Image.new("L", (w, h), 0)
    paint = Image.new("L", (w, h), 0)
    fixed = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    luminance = Image.new("L", (w, h), 128)

    ap, ac, pa, fx, lu = alpha.load(), accent.load(), paint.load(), fixed.load(), luminance.load()
    for y in range(h):
        for x in range(w):
            p = px[x, y]
            if p[3] <= 10:
                continue
            ap[x, y] = 255
            if is_red(p):
                # HSV value channel of the original red - the shading cue.
                lu[x, y] = max(p[0], p[1], p[2])
                if x <= ACCENT_MAX_X:
                    ac[x, y] = 255
                else:
                    pa[x, y] = 255
            else:
                fx[x, y] = p  # grey cowling/prop/pilot/wheel - untouched by any skin

    alpha.save(MASKS_DIR / "alpha.png")
    accent.save(MASKS_DIR / "accent.png")
    paint.save(MASKS_DIR / "paint.png")
    fixed.save(MASKS_DIR / "fixed.png")
    luminance.save(MASKS_DIR / "luminance.png")

    # Review composite: accent=cyan, paint=magenta, fixed=as-is, background grid.
    preview = Image.new("RGBA", (w, h), (30, 30, 34, 255))
    tint = Image.new("RGBA", (w, h), (0, 230, 230, 255))
    preview.paste(tint, (0, 0), accent)
    tint2 = Image.new("RGBA", (w, h), (230, 0, 200, 255))
    preview.paste(tint2, (0, 0), paint)
    preview.alpha_composite(fixed)
    preview.resize((w * 4, h * 4), Image.NEAREST).save(MASKS_DIR / "preview_4x.png")
    print(f"masks -> {MASKS_DIR} (accent={accent.getbbox()}, paint={paint.getbbox()})")


def load_masks():
    return {
        "alpha": Image.open(MASKS_DIR / "alpha.png").convert("L"),
        "accent": Image.open(MASKS_DIR / "accent.png").convert("L"),
        "paint": Image.open(MASKS_DIR / "paint.png").convert("L"),
        "fixed": Image.open(MASKS_DIR / "fixed.png").convert("RGBA"),
        "luminance": Image.open(MASKS_DIR / "luminance.png").convert("L"),
    }


def swatch_128(pattern_img):
    """Centre-crop the rendered swatch to square and resize to the sprite
    canvas - the exact pattern a skin samples, and what ships as
    swatch_<key>.png for runtime compositing onto other shapes."""
    pat = pattern_img.convert("RGB")
    side = min(pat.size)
    left = (pat.width - side) // 2
    top = (pat.height - side) // 2
    return pat.crop((left, top, left + side, top + side)).resize((SIZE, SIZE), Image.LANCZOS)


def composite_skin(pattern_img, masks):
    """pattern_img: any RGB image (need not match sprite size) -> 128x128
    skin sprite. Pattern is centre-cropped to square then resized to fill
    the paint mask's bounding box, sampled, multiplied by luminance.
    PlaneModelCatalog.Composite() in C# is this same function for the other
    plane shapes - keep the two in step."""
    w, h = SIZE, SIZE
    pat = swatch_128(pattern_img)

    # Multiply blend: pixel * luminance/255 reproduces the original sprite's
    # panel-line shadows and highlights on top of the new pattern colour.
    # Blended down to 85% so deep shadow pixels don't crush the pattern to
    # near-black and lose readability at 128x128.
    lum = masks["luminance"]
    shaded = ImageChops.multiply(pat, lum.convert("RGB"))
    shaded = Image.blend(pat, shaded, 0.85)

    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.paste(shaded, (0, 0), masks["paint"])

    accent_layer = Image.new("RGBA", (w, h), (*SOURCE_RED, 255))
    out.paste(accent_layer, (0, 0), masks["accent"])

    out.alpha_composite(masks["fixed"])

    alpha = masks["alpha"]
    r, g, b, _ = out.split()
    out = Image.merge("RGBA", (r, g, b, alpha))
    return out


def render_pattern_on_spark(key, dry_run=False):
    prompt = S.prompt_for(key)
    if prompt is None:
        return None
    if dry_run:
        print(f"## {key}\n{prompt}\n")
        return None

    seed = S.DEFAULT_SEED + S.SKINS[key]["id"]
    graph = backend.flux_graph(prompt, (768, 768), seed, steps=20, guidance=3.5,
                               negative=backend.NEG_BG)
    graph["13"] = {"class_type": "SaveImage",
                   "inputs": {"images": ["9", 0], "filename_prefix": f"dbg/skin_{key}"}}
    pid = backend.enqueue(graph)
    outputs = backend.wait(pid)
    png_bytes = backend.fetch(outputs["13"]["images"][0])
    return Image.open(io.BytesIO(png_bytes))


def cmd_masks(_a):
    build_masks()


def cmd_render(a):
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    REVIEW_DIR.mkdir(parents=True, exist_ok=True)
    if not a.dry_run and not (MASKS_DIR / "alpha.png").exists():
        build_masks()
    masks = load_masks() if not a.dry_run else None

    keys = a.keys.split(",") if a.keys else list(S.SKINS)
    unknown = [k for k in keys if k not in S.SKINS]
    if unknown:
        sys.exit(f"unknown skin key(s): {unknown}")

    if not a.dry_run:
        backend.free_models()

    done = 0
    for key in keys:
        spec = S.SKINS[key]
        out_path = OUT_DIR / f"skin_{key}.png"
        if spec["prompt"] is None:
            # The starter skin IS the original sprite - no render needed.
            if not out_path.exists() and not a.dry_run:
                Image.open(BASE_SPRITE).convert("RGBA").save(out_path)
                print(f"[{key}] starter skin -> copied BiPlane1.png")
            continue
        if out_path.exists() and not a.force:
            continue

        pattern = render_pattern_on_spark(key, dry_run=a.dry_run)
        if pattern is None:
            continue

        swatch_128(pattern).save(OUT_DIR / f"swatch_{key}.png")
        sprite = composite_skin(pattern, masks)
        sprite.save(out_path)

        preview = Image.new("RGBA", (SIZE * 4, SIZE * 4), (140, 190, 225, 255))
        big = sprite.resize((SIZE * 4, SIZE * 4), Image.NEAREST)
        preview.alpha_composite(big)
        preview.convert("RGB").save(REVIEW_DIR / f"skin_{key}.jpg", quality=85)

        done += 1
        print(f"[{done}] {key} -> {out_path}")

    print(f"render: {done} skin(s) composited" + (" (dry run)" if a.dry_run else ""))


def cmd_swatches(a):
    """Render only the pattern swatch for skins that have none yet. The skin
    PNG itself is left alone - same seed and prompt reproduce the pattern
    the shipped skin was painted from."""
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    keys = a.keys.split(",") if a.keys else list(S.SKINS)
    todo = [k for k in keys if S.SKINS[k]["prompt"] is not None
            and (a.force or not (OUT_DIR / f"swatch_{k}.png").exists())]
    if not todo:
        print("all swatches present")
        return
    if a.dry_run:
        for k in todo:
            print(f"## {k}\n{S.prompt_for(k)}\n")
        return
    if not a.no_free:
        backend.free_models()
    done = 0
    for key in todo:
        pattern = render_pattern_on_spark(key)
        if pattern is None:
            continue
        swatch_128(pattern).save(OUT_DIR / f"swatch_{key}.png")
        done += 1
        print(f"[{done}/{len(todo)}] swatch {key}", flush=True)
    print(f"swatches: {done} rendered")


def cmd_sheet(_a):
    cards = []
    for key in S.SKINS:
        p = REVIEW_DIR / f"skin_{key}.jpg"
        starter = OUT_DIR / f"skin_{key}.png"
        if p.exists():
            cards.append(f'<figure><img src="review/skin_{key}.jpg"><figcaption>{key}</figcaption></figure>')
        elif starter.exists():
            cards.append(f'<figure><img src="out/skin_{key}.png"><figcaption>{key} (starter)</figcaption></figure>')
    html = ("<!doctype html><meta charset=utf-8><title>Plane skins</title>"
            "<style>body{background:#1b1e24;color:#ddd;font:14px monospace;margin:24px}"
            ".g{display:grid;grid-template-columns:repeat(auto-fill,minmax(140px,1fr));gap:10px}"
            "img{width:100%;image-rendering:pixelated;background:#8cb4e1;border-radius:6px}"
            "figcaption{text-align:center;padding:4px 0;color:#9ab}</style>"
            f"<div class=g>{''.join(cards)}</div>")
    (HERE / "index.html").write_text(html)
    print(f"gallery -> {HERE / 'index.html'} ({len(cards)} skins)")


def cmd_apply(_a):
    ASSETS_DIR.mkdir(parents=True, exist_ok=True)
    copied = 0
    for key in S.SKINS:
        src = OUT_DIR / f"skin_{key}.png"
        if not src.exists():
            print(f"[SKIP] {key}: not rendered yet")
            continue
        dst = ASSETS_DIR / f"skin_{key}.png"
        dst.write_bytes(src.read_bytes())
        copied += 1
    print(f"applied {copied}/{len(S.SKINS)} skins -> {ASSETS_DIR}")
    if copied < len(S.SKINS):
        print("Missing skins fall back to the starter skin in-game (PlaneSkinCatalog.LoadSprite) - safe, just re-run apply once more are rendered.")

    # Swatches: what the other plane shapes composite from at runtime.
    UM.ensure_folder(SWATCH_ASSETS_DIR)
    swatches = 0
    for key in S.SKINS:
        src = OUT_DIR / f"swatch_{key}.png"
        if not src.exists():
            continue
        dst = SWATCH_ASSETS_DIR / f"swatch_{key}.png"
        dst.write_bytes(src.read_bytes())
        UM.write_meta(dst, "texture")
        swatches += 1
    expected = sum(1 for k in S.SKINS if S.SKINS[k]["prompt"] is not None)
    print(f"applied {swatches}/{expected} swatches -> {SWATCH_ASSETS_DIR}")
    if swatches < expected:
        print("Skins without a swatch show the starter livery on non-base shapes - run `swatches` then apply again.")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("masks"); p.set_defaults(fn=cmd_masks)

    p = sub.add_parser("render")
    p.add_argument("--keys", help="comma list (default: all 50)")
    p.add_argument("--force", action="store_true", help="re-render even if the output already exists")
    p.add_argument("--dry-run", action="store_true")
    p.set_defaults(fn=cmd_render)

    p = sub.add_parser("swatches")
    p.add_argument("--keys", help="comma list (default: all)")
    p.add_argument("--force", action="store_true")
    p.add_argument("--no-free", action="store_true", help="skip ComfyUI /free (another batch is using the box)")
    p.add_argument("--dry-run", action="store_true")
    p.set_defaults(fn=cmd_swatches)

    p = sub.add_parser("sheet"); p.set_defaults(fn=cmd_sheet)
    p = sub.add_parser("apply"); p.set_defaults(fn=cmd_apply)

    a = ap.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
