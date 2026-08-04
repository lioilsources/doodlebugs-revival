#!/usr/bin/env python3
"""Bakes generated ad signs into the foreground strips.

Scans each strip's alpha for flat terrain plateaus, plants billboards there on
wooden posts, and writes the composed strip. Because ads become part of the
strip they inherit the whole foreground stack for free: infinite scroll,
planes render behind them, and ForegroundTile makes them destructible per
100x100 tile — billboards can be shot to pieces.

Deterministic per map (seed in brands.json), idempotent: the pristine strip is
archived in tools/ads/base/ on first run and every composition starts from it.

Usage:
  python3 tools/ads/compose_foreground_ads.py            # preview into --out (no Assets touched)
  python3 tools/ads/compose_foreground_ads.py --apply    # write into Assets/.../Foreground/
Then in Unity run  Doodlebugs -> Sync Background Profiles.
"""
import argparse
import json
import random
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
FG_DIR = ROOT / "Assets/Doodlebugs/Sprites/Foreground"
ADS_DIR = Path(__file__).parent / "sprites"
BASE_DIR = Path(__file__).parent / "base"
BRANDS = json.loads((Path(__file__).parent / "brands.json").read_text())

ALPHA_MIN = 64        # what counts as solid terrain
FLATNESS = 120        # max height wobble (px) across a sign's footprint —
                      # generous, because each post reaches its own ground
EDGE_MARGIN = 150     # keep clear of the wrap seam at x=0/width
MIN_GAP = 700         # min distance between two signs
POST_W = 14
POST_EMBED = 40       # posts sink this deep into terrain
POST_MIN = 30         # min visible post height


def terrain_top(img):
    """Per-column y of the first solid pixel, or None where the column is empty."""
    w, h = img.size
    alpha = img.getchannel("A").load()
    tops = []
    for x in range(w):
        top = None
        for y in range(h):
            if alpha[x, y] >= ALPHA_MIN:
                top = y
                break
        tops.append(top)
    return tops


def ground_profile(img):
    """Per-column y of the top of the solid mass that touches the strip bottom.
    Unlike terrain_top this ignores anything floating above the ground —
    billboards, overhangs — so perimeter boards measure the actual soil."""
    w, h = img.size
    alpha = img.getchannel("A").load()
    tops = []
    for x in range(w):
        if alpha[x, h - 1] < ALPHA_MIN:
            tops.append(None)
            continue
        y = h - 1
        while y > 0 and alpha[x, y - 1] >= ALPHA_MIN:
            y -= 1
        tops.append(y)
    return tops


def plateaus(tops, width, need_w, strip_h, sign_h, flat=FLATNESS):
    """Candidate x positions where a sign of need_w fits on flat solid ground
    with headroom for the sign + posts above it."""
    out = []
    x = EDGE_MARGIN
    while x < width - EDGE_MARGIN - need_w:
        window = tops[x:x + need_w]
        if (all(t is not None for t in window)
                and max(window) - min(window) <= flat
                and min(window) - POST_MIN - sign_h >= 10):
            out.append(x)
            x += 50
        else:
            x += 25
    return out


def wall_spots(tops, width, strip_h, sign_w, sign_h):
    """Candidate (x, y) positions painted flush on a solid face (ghost signs):
    every footprint column must be solid from above the sign's top down."""
    out = []
    margin = 16
    need_w = sign_w + 2 * margin
    x = EDGE_MARGIN
    while x < width - EDGE_MARGIN - need_w:
        window = tops[x:x + need_w]
        if all(t is not None for t in window):
            face_top = max(window)               # lowest roofline in the footprint
            y = face_top + 40                    # breathing room under the roofline
            if y + sign_h <= strip_h - 20:
                out.append((x + margin, y))
                x += 50
                continue
        x += 25
    return out


def compose_map(name, cfg, out_dir, apply_to_assets):
    strip_path = FG_DIR / f"{name}.png"
    base_path = BASE_DIR / f"{name}.png"
    if not base_path.exists():
        base_path.write_bytes(strip_path.read_bytes())   # archive pristine original
    strip = Image.open(base_path).convert("RGBA")
    tops = terrain_top(strip)
    # Soil profile from the PRISTINE strip: the boards pass runs after signs
    # and props are pasted, and measuring alpha then would read their tops as
    # ground. From the base, ground_profile sees the soil under overhangs
    # (the mega billboard) while boards drawn later simply pass in front of
    # scaffold legs and props the way rink boards front a stadium.
    soil = ground_profile(strip)
    rng = random.Random(cfg["seed"])
    draw = ImageDraw.Draw(strip)

    signs = [s for s in BRANDS["signs"] if name in s["maps"]]
    rng.shuffle(signs)
    # Per-map favourites (e.g. neon belongs on Manhattan) go to the front of
    # the queue so density limits never squeeze them out.
    prefer = cfg.get("prefer", [])
    signs.sort(key=lambda s: prefer.index(s["id"]) if s["id"] in prefer else len(prefer))
    min_gap = cfg.get("min_gap", MIN_GAP)
    placed = []
    for spec in signs:
        if len(placed) >= cfg["count"]:
            break
        sign = Image.open(ADS_DIR / f"ad_{spec['id']}.png").convert("RGBA")
        need_w = sign.width + 2 * POST_W

        # Billboard on posts where flat ground + headroom exists; otherwise a
        # ghost sign painted flush on a solid face (city walls, mesa sides).
        spots = [x for x in plateaus(tops, strip.width, need_w, strip.height, sign.height)
                 if all(abs(x - p[0]) >= min_gap for p in placed)]
        if spots:
            x = rng.choice(spots)
            ground_hi = min(t for t in tops[x:x + need_w] if t is not None)
            post_h = rng.randint(POST_MIN, POST_MIN + 50)
            sign_y = ground_hi - post_h - sign.height

            # Wooden posts first, so the sign overlaps their tops. Each post
            # runs down to the terrain under ITS OWN feet — uneven ground
            # (tree canopy, rocky ridges) just means posts of different length.
            for px_x in (x + POST_W, x + need_w - 2 * POST_W):
                post_ground = max(t for t in tops[px_x:px_x + POST_W] if t is not None)
                draw.rectangle([px_x, sign_y + sign.height - 10,
                                px_x + POST_W - 1, post_ground + POST_EMBED], fill="#4A3826")
                draw.rectangle([px_x, sign_y + sign.height - 10,
                                px_x + 2, post_ground + POST_EMBED], fill="#5C4632")

            tilt = rng.uniform(-2.0, 2.0)
            rotated = sign.rotate(tilt, expand=True, resample=Image.BICUBIC)
            strip.alpha_composite(rotated, (x + POST_W, sign_y))
            placed.append((x, sign_y, sign.width, sign.height))
            print(f"  {name}: {spec['id']} billboard at x={x + POST_W} y={sign_y} tilt={tilt:+.1f}")
            continue

        walls = [(x, y) for x, y in wall_spots(tops, strip.width, strip.height,
                                               sign.width, sign.height)
                 if all(abs(x - p[0]) >= min_gap for p in placed)]
        if not walls:
            print(f"  {name}: no room for {spec['id']}, skipped")
            continue
        x, y = rng.choice(walls)
        # Ghost signs sit flat on the wall — slightly translucent, no tilt.
        faded = sign.copy()
        faded.putalpha(faded.getchannel("A").point(lambda a: int(a * 0.88)))
        strip.alpha_composite(faded, (x, y))
        placed.append((x, y, sign.width, sign.height))
        print(f"  {name}: {spec['id']} wall sign at x={x} y={y}")

    # Giant roadside props — cans, jars, bottles standing in the terrain.
    props = [p for p in BRANDS.get("props", []) if name in p["maps"]]
    rng.shuffle(props)
    for prop in props[:cfg.get("props", 0)]:
        full = Image.open(ADS_DIR / f"prop_{prop['shape']}_{prop['brand']}.png").convert("RGBA")
        # Props sit directly on the surface (no posts to absorb unevenness):
        # moderately flat ground, and the feet sink to the LOWEST point of the
        # footprint so a slope buries the prop's base instead of leaving a
        # corner hovering in the air. If nothing fits, try a smaller prop —
        # rooftop plateaus and mountain ledges are narrow.
        img = spots = None
        for scale in (1.0, 0.8, 0.65):
            img = full if scale == 1.0 else full.resize(
                (int(full.width * scale), int(full.height * scale)), Image.LANCZOS)
            need_w = img.width + 16
            spots = [x for x in plateaus(tops, strip.width, need_w, strip.height, img.height, flat=60)
                     if all(abs(x - p[0]) >= min(min_gap, 400) for p in placed)]
            if spots:
                break
        if not spots:
            print(f"  {name}: no room for prop {prop['shape']}/{prop['brand']}, skipped")
            continue
        need_w = img.width + 16
        x = rng.choice(spots)
        ground_low = max(t for t in tops[x:x + need_w] if t is not None)
        tilted = img.rotate(rng.uniform(-3.0, 3.0), expand=True, resample=Image.BICUBIC)
        y = ground_low - tilted.height + 10
        # Soft contact shadow anchors the prop to photographic terrain.
        shadow = Image.new("RGBA", strip.size, (0, 0, 0, 0))
        ImageDraw.Draw(shadow).ellipse(
            [x - img.width // 12, ground_low - 26, x + need_w + img.width // 12, ground_low + 30],
            fill=(20, 14, 8, 70))
        strip.alpha_composite(shadow)
        strip.alpha_composite(tilted, (x + 8, y))
        placed.append((x, y, tilted.width, tilted.height))
        print(f"  {name}: prop {prop['shape']}/{prop['brand']} at x={x + 8} y={y}")

    # Stadium perimeter boards — a continuous run of low ad panels standing on
    # the ground like hockey rink boards. Each 480px segment steps up or down
    # with its own stretch of terrain, so the run follows rolling ground the
    # way trackside hoarding does.
    boards_n = cfg.get("boards", 0)
    if boards_n >= 3:
        import generate_ad_signs as gas_mod
        seg_w, seg_h = gas_mod.BOARD_W, gas_mod.BOARD_H
        # Boards measure the soil itself, not billboards hovering above it.
        tops = soil
        pool = [s for s in BRANDS["signs"] if name in s["maps"]] or list(BRANDS["signs"])
        rng.shuffle(pool)
        import math
        starts = list(range(EDGE_MARGIN, strip.width - EDGE_MARGIN - seg_w * 3, 40))
        rng.shuffle(starts)
        chain = []
        for sx in starts:
            chain, x = [], sx
            # A run may skip past obstacles (scaffold legs, props) the way a
            # real fence breaks around a pylon — but it stays one visual band:
            # the whole run must fit inside 2.5x its ideal length.
            span_limit = sx + int(boards_n * seg_w * 2.5)
            while len(chain) < boards_n and x + seg_w < min(strip.width - EDGE_MARGIN, span_limit):
                win = tops[x:x + seg_w]
                # Ground-level only (lower half of the strip): perimeter boards
                # belong at pitch level, not on the flat roof of a billboard —
                # which is otherwise the flattest "terrain" on the whole map.
                if (all(t is not None for t in win) and max(win) - min(win) <= 140
                        and min(win) > strip.height * 0.5
                        # Never cover an already placed sign or prop; a run
                        # that can't fit in the open simply doesn't happen.
                        and not any(x < p[0] + p[2] + 30 and x + seg_w > p[0] - 30
                                    for p in placed)):
                    # Local slope: the segment leans with the hillside like real
                    # trackside hoarding does.
                    head = sum(win[:60]) / 60
                    tail = sum(win[-60:]) / 60
                    tilt = math.degrees(math.atan2(head - tail, seg_w))
                    chain.append((x, min(win), max(-8.0, min(8.0, tilt))))
                    x += seg_w - 6                    # slight overlap hides tilt joints
                else:
                    x += 40
            if len(chain) >= 3:
                break
            chain = []
        if chain:
            for i, (x, g, tilt) in enumerate(chain):
                spec = pool[i % len(pool)]
                board = Image.open(ADS_DIR / f"board_{spec['id']}_{'w' if i % 2 == 0 else 'c'}.png")
                draw.rectangle([x, g + 8, x + seg_w, g + 22], fill="#3A3128")   # ground shadow line
                rotated = board.rotate(tilt, expand=True, resample=Image.BICUBIC)
                strip.alpha_composite(rotated, (x, g - rotated.height + 16))
            print(f"  {name}: boards run x={chain[0][0]}..{chain[-1][0] + seg_w} ({len(chain)} segments)")
        else:
            print(f"  {name}: no stretch long enough for boards")

    out_path = (FG_DIR if apply_to_assets else out_dir) / f"{name}.png"
    out_dir.mkdir(parents=True, exist_ok=True)
    strip.save(out_path)
    print(f"  -> {out_path}")

    # Preview crops around each sign for a quick look without opening Unity —
    # composited over a sky colour so the silhouette reads like in game.
    for i, (x, y, sw, sh) in enumerate(placed):
        box = (max(0, x - 80), max(0, y - 80),
               min(strip.width, x + sw + 160), min(strip.height, y + sh + 260))
        crop = strip.crop(box)
        backdrop = Image.new("RGBA", crop.size, (140, 190, 225, 255))
        backdrop.alpha_composite(crop)
        backdrop.save(out_dir / f"preview_{name}_{i}.png")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="write composed strips into Assets/")
    ap.add_argument("--out", default=str(Path(__file__).parent / "out"), help="preview output dir")
    ap.add_argument("--map", default=None, help="compose a single map only")
    args = ap.parse_args()

    BASE_DIR.mkdir(parents=True, exist_ok=True)
    maps = BRANDS["maps"]
    for name, cfg in maps.items():
        if args.map and name != args.map:
            continue
        print(f"{name}:")
        compose_map(name, cfg, Path(args.out), args.apply)

    if args.apply:
        print("\nDone. In Unity run: Doodlebugs -> Sync Background Profiles")


if __name__ == "__main__":
    main()
