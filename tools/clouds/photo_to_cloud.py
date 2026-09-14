#!/usr/bin/env python3
"""Turns photographs of the sky into cloud sprites with a real alpha channel.

Blue sky is keyed out by how blue-dominant a pixel is (sky has a big b-r gap,
cloud is near-neutral). The gap is measured against the sky *around* the
cloud, not one global threshold: a phone sky runs from deep blue at the top
to pale blue near the horizon, and a fixed cut-off turns the pale half into
a washed-out, see-through cloud (and a plane "hiding" in it stays visible).

Each photo yields its biggest cloud(s), and only them:

- the cloud is isolated as a connected region of a blurred mask, and every
  pixel outside that region is dropped - cropping a bounding box used to
  drag in slices of the neighbouring clouds with a hard straight edge;
- where the cloud runs off the frame it fades out over FEATHER_PX instead
  of ending in the photo's cut line. That cut line is what made the first
  photo clouds look broken in game.

Usage:
  python3 tools/clouds/photo_to_cloud.py tools/clouds/raw/sky_*.jpg [--apply]

The collider is baked here too. Clouds are cover (Bullet.HandleContact stops
a shot on one), and Unity's own fallback physics shape traces the alpha at a
threshold of its choosing, with no filter: a dozen speck islands per sprite
that stop a bullet in what looks like empty air, and on a soft cloud almost
the whole translucent blob. Instead the outline of the solid cloud (alpha >=
COLLIDER_ALPHA, eroded, small islands dropped, simplified) goes into the
.meta as the sprite's physics shape, so Sprite.GetPhysicsShape - and with it
CloudManager.FitColliderToSprite - returns exactly that polygon. The same
outline is written into Cloud.prefab so the editor shows what the game uses.

Without --apply the sprites land in tools/clouds/out/ for review, together
with preview.png (every sprite on a dark and a light ground). With --apply
they replace Assets/.../Resources/Sprites/Clouds/: the old cloud PNGs are
removed, new ones are written as cloud_<photo>_<n>.png with a .meta each
(an existing .meta keeps its guid and only gets a new physicsShape).
Either way collider_overlay.png in tools/clouds/out/ shows the baked
collider over every sprite - that is where the threshold gets judged.
"""
import argparse
import re
import sys
import uuid
from collections import deque
from pathlib import Path

from PIL import Image, ImageChops, ImageFilter

ROOT = Path(__file__).parents[2]
ASSETS = ROOT / "Assets/Doodlebugs/Resources/Sprites/Clouds"
OUT = Path(__file__).parent / "out"

WORK_MAX = 1400      # long edge the keying runs at
SPRITE_MAX = 512     # long edge of an exported sprite (CloudManager scales assume it)
CLOUD_GAP = 10       # b-r of a sunlit cloud; shaded undersides sit around 20-30
SOLID_LO, SOLID_HI = 0.10, 0.80   # alpha contrast: below LO gone, above HI solid
MIN_BLOB_FRAC = 0.03  # a cloud smaller than this share of the frame is a scrap
FEATHER_PX = 90       # fade where a cloud leaves the frame (work resolution)
GRID = 4              # components are traced at 1/GRID resolution

PPU = 100                 # Cloud.prefab collider is in world units
COLLIDER_ALPHA = 153      # 60 %: where the cloud reads solid, not wisp
COLLIDER_ERODE = 3        # px pulled in from that edge - cover starts inside it
COLLIDER_MIN_AREA = 0.02  # islands under this share of the sprite are dropped
COLLIDER_MAX_POINTS = 48  # per path, after simplification
COLLIDER_MAX_PATHS = 6
PREFAB = ROOT / "Assets/Doodlebugs/Prefabs/Cloud.prefab"


def smooth(t):
    t = 0.0 if t < 0 else 1.0 if t > 1 else t
    return t * t * (3 - 2 * t)


def local_sky_gap(img):
    """Greyscale image of the sky's b-r gap around every pixel.

    The max over a wide window picks the sky next to a cloud; deep inside a
    big cloud there is no sky in reach, so the value is floored at most of
    the global sky gap rather than collapsing to the cloud's own."""
    r, _, b = img.split()
    gap = ImageChops.subtract(b, r)
    small = gap.resize((max(1, gap.width // 8), max(1, gap.height // 8)), Image.BILINEAR)
    vals = sorted(small.get_flattened_data())
    global_sky = vals[int(len(vals) * 0.9)]
    floor = int(global_sky * 0.75)
    small = small.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(6))
    small = small.point(lambda v: max(v, floor))
    return small.resize(gap.size, Image.BILINEAR), global_sky


def key(img):
    """RGBA with the sky removed and the soft edges drained of its blue."""
    sky_gap, global_sky = local_sky_gap(img)
    px = img.load()
    sg = sky_gap.load()
    w, h = img.size

    # Sky luminance for un-matting edge brightness: the bluest decile.
    samples = sorted(((px[x, y][2] - px[x, y][0]), px[x, y])
                     for y in range(0, h, 9) for x in range(0, w, 9))
    pool = [p for _, p in samples[int(len(samples) * 0.9):]]
    sky_lum = sum(0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2] for p in pool) / len(pool)

    out = Image.new("RGBA", (w, h))
    op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            s = max(sg[x, y], CLOUD_GAP + 30)
            raw = (s - (b - r)) / (s - CLOUD_GAP)
            a = smooth((raw - SOLID_LO) / (SOLID_HI - SOLID_LO))
            if a <= 0.02:
                op[x, y] = (0, 0, 0, 0)
                continue
            if raw >= 1.0:
                op[x, y] = (r, g, b, 255)
                continue
            # pixel = raw*cloud + (1-raw)*sky. Dividing the sky out per channel
            # turns sensor noise into orange rims; clouds are neutral anyway,
            # so only the brightness is un-matted and the blue cast drained.
            # Driven by raw, not by the contrast-boosted alpha: a pixel that is
            # opaque after the boost can still be a fifth sky, and kept as shot
            # it draws a dark rim round the cloud on a light map.
            f = max(raw, 0.25)
            lum = 0.299 * r + 0.587 * g + 0.114 * b
            cloud_lum = min(255.0, max(lum, (lum - (1 - f) * sky_lum) / f))
            k = 1 - raw
            rgb = tuple(int(min(255, max(0, c + (cloud_lum - c) * k))) for c in (r, g, b))
            op[x, y] = rgb + (int(a * 255),)
    return bleed_body_colour(out)


def bleed_body_colour(out):
    """Paint soft edges with the colour of the cloud body next to them.

    The un-matted edge is still a mix with the sky, and on a deep blue photo
    that mix reads as a dark grey rim once the sprite sits on a light map.
    Real cloud edges are the body's colour, only thinner - so edge pixels
    take the blurred colour of the nearby solid pixels, weighted toward it
    the more transparent they are."""
    alpha = out.getchannel("A")
    solid = alpha.point(lambda v: 255 if v >= 230 else 0)
    radius = 10
    body = [ImageChops.multiply(c, solid).filter(ImageFilter.GaussianBlur(radius))
            for c in out.split()[:3]]
    weight = solid.filter(ImageFilter.GaussianBlur(radius))
    bp = [c.load() for c in body]
    wp = weight.load()
    op = out.load()
    w, h = out.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = op[x, y]
            if a == 0 or a == 255:
                continue
            cover = wp[x, y]
            if cover < 8:
                continue
            body_rgb = [bp[i][x, y] * 255 / cover for i in range(3)]
            t = (1 - a / 255) * min(1.0, cover / 64)
            op[x, y] = tuple(int(min(255, c + (bc - c) * t))
                             for c, bc in zip((r, g, b), body_rgb)) + (a,)
    return out


def components(mask, w, h, min_px):
    """Connected regions of a binary mask (list of bytes), largest first."""
    seen = bytearray(w * h)
    found = []
    for i0 in range(w * h):
        if seen[i0] or not mask[i0]:
            continue
        q = deque([i0])
        seen[i0] = 1
        pixels = []
        while q:
            i = q.popleft()
            pixels.append(i)
            x, y = i % w, i // w
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if 0 <= nx < w and 0 <= ny < h:
                    j = ny * w + nx
                    if not seen[j] and mask[j]:
                        seen[j] = 1
                        q.append(j)
        if len(pixels) >= min_px:
            found.append(pixels)
    found.sort(key=len, reverse=True)
    return found


def edge_fade(w, h, touches):
    """Alpha multiplier fading to 0 at the frame sides the cloud runs off."""
    fade = Image.new("L", (w, h), 255)
    fp = fade.load()
    for y in range(h):
        for x in range(w):
            d = 1.0
            if touches["l"]: d = min(d, x / FEATHER_PX)
            if touches["r"]: d = min(d, (w - 1 - x) / FEATHER_PX)
            if touches["t"]: d = min(d, y / FEATHER_PX)
            if touches["b"]: d = min(d, (h - 1 - y) / FEATHER_PX)
            if d < 1.0:
                fp[x, y] = int(255 * smooth(d))
    return fade


def extract(path, count):
    src = Image.open(path).convert("RGB")
    src.thumbnail((WORK_MAX, WORK_MAX), Image.LANCZOS)
    keyed = key(src)
    w, h = keyed.size

    # Trace clouds on a small, blurred mask so a cloud's own wisps join it.
    sw, sh = w // GRID, h // GRID
    alpha = keyed.getchannel("A")
    small = alpha.resize((sw, sh), Image.BILINEAR).filter(ImageFilter.GaussianBlur(2))
    mask = bytes(1 if v > 90 else 0 for v in small.get_flattened_data())
    regions = components(mask, sw, sh, int(sw * sh * MIN_BLOB_FRAC))
    if not regions:
        sys.exit(f"{path}: no cloud found - adjust CLOUD_GAP/SOLID_* for this photo")

    sprites = []
    for pixels in regions[:count]:
        region = Image.new("L", (sw, sh), 0)
        rp = region.load()
        xs, ys = [], []
        for i in pixels:
            rp[i % sw, i // sw] = 255
            xs.append(i % sw)
            ys.append(i // sw)
        # Grow the region a little so its wisps survive, then soften the cut.
        region = region.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(3))
        region = region.resize((w, h), Image.BILINEAR)

        margin = 2
        touches = {"l": min(xs) <= margin, "r": max(xs) >= sw - 1 - margin,
                   "t": min(ys) <= margin, "b": max(ys) >= sh - 1 - margin}
        a = ImageChops.multiply(alpha, region)
        if any(touches.values()):
            a = ImageChops.multiply(a, edge_fade(w, h, touches))
        # Specks under ~3 % alpha only add slivers to the physics outline.
        a = a.point(lambda v: 0 if v < 8 else v)

        cloud = keyed.copy()
        cloud.putalpha(a)
        bbox = a.getbbox()
        if bbox is None:
            continue
        cloud = cloud.crop(bbox)
        if cloud.height > cloud.width:
            cloud = cloud.transpose(Image.ROTATE_90)  # clouds drift sideways
        cloud.thumbnail((SPRITE_MAX, SPRITE_MAX), Image.LANCZOS)
        cut = "".join(k for k, v in touches.items() if v) or "-"
        print(f"  {path.name}: cloud {len(sprites) + 1} {cloud.size[0]}x{cloud.size[1]} "
              f"({len(pixels) / (sw * sh):.0%} of frame, fades off: {cut})")
        sprites.append(cloud)
    return sprites


# --- collider -----------------------------------------------------------------

def trace_outlines(filled, w):
    """Closed loops of pixel corners around a set of pixel indices.

    Every filled pixel contributes its exposed edges, directed clockwise in
    image space (y down) so the region is always on the right; the edges are
    then chained. Where two pixels only touch at a corner the chain prefers
    the right turn, which keeps them as two loops touching at a point rather
    than one self-crossing figure of eight."""
    out = {}

    def edge(a, b):
        out.setdefault(a, []).append(b)

    for i in filled:
        x, y = i % w, i // w
        if (i - w) not in filled: edge((x, y), (x + 1, y))
        if x == w - 1 or (i + 1) not in filled: edge((x + 1, y), (x + 1, y + 1))
        if (i + w) not in filled: edge((x + 1, y + 1), (x, y + 1))
        if x == 0 or (i - 1) not in filled: edge((x, y + 1), (x, y))

    loops = []
    while out:
        start = next((p for p, ends in out.items() if len(ends) == 1), next(iter(out)))
        p, d, loop = start, None, []
        while True:
            ends = out[p]
            if len(ends) == 1 or d is None:
                q = ends[0]
            else:
                right = (-d[1], d[0])
                q = min(ends, key=lambda e: {right: 0, d: 1}.get((e[0] - p[0], e[1] - p[1]), 2))
            ends.remove(q)
            if not ends: del out[p]
            loop.append(p)
            d = (q[0] - p[0], q[1] - p[1])
            p = q
            if p == start: break
        loops.append(loop)
    return loops


def polygon_area(pts):
    return abs(sum(pts[i][0] * pts[i - 1][1] - pts[i - 1][0] * pts[i][1]
                   for i in range(len(pts)))) / 2


def douglas_peucker(pts, tol):
    keep = [False] * len(pts)
    keep[0] = keep[-1] = True
    stack = [(0, len(pts) - 1)]
    while stack:
        a, b = stack.pop()
        if b - a < 2: continue
        (ax, ay), (bx, by) = pts[a], pts[b]
        dx, dy, length = bx - ax, by - ay, ((bx - ax) ** 2 + (by - ay) ** 2) ** 0.5
        best, best_d = a, -1.0
        for i in range(a + 1, b):
            px, py = pts[i]
            d = (abs(dx * (ay - py) - (ax - px) * dy) / length if length
                 else ((px - ax) ** 2 + (py - ay) ** 2) ** 0.5)
            if d > best_d: best, best_d = i, d
        if best_d > tol:
            keep[best] = True
            stack.append((a, best))
            stack.append((best, b))
    return [p for p, k in zip(pts, keep) if k]


def simplify_loop(loop, tol):
    """Douglas-Peucker on a closed loop: split at the point farthest from
    the first, simplify both halves."""
    far = max(range(len(loop)), key=lambda k: (loop[k][0] - loop[0][0]) ** 2 + (loop[k][1] - loop[0][1]) ** 2)
    a = douglas_peucker(loop[:far + 1], tol)
    b = douglas_peucker(loop[far:] + [loop[0]], tol)
    return a[:-1] + b[:-1]


def collider_paths(sprite):
    """Outlines of the solid cloud as pixel corners in image space (y down)."""
    w, h = sprite.size
    a = sprite.getchannel("A").filter(ImageFilter.GaussianBlur(1.5))
    solid = a.point(lambda v: 255 if v >= COLLIDER_ALPHA else 0)
    solid = solid.filter(ImageFilter.MinFilter(2 * COLLIDER_ERODE + 1))
    mask = bytes(1 if v else 0 for v in solid.get_flattened_data())
    min_area = w * h * COLLIDER_MIN_AREA
    paths = []
    for pixels in components(mask, w, h, int(min_area))[:COLLIDER_MAX_PATHS]:
        # Largest loop only: the others are holes, and a hole in cloud cover
        # is not worth a path.
        loop = max(trace_outlines(set(pixels), w), key=polygon_area)
        tol = 2.0
        pts = simplify_loop(loop, tol)
        while len(pts) > COLLIDER_MAX_POINTS:
            tol *= 1.25
            pts = simplify_loop(loop, tol)
        if polygon_area(pts) >= min_area:
            paths.append(pts)
    return paths


def to_pivot_space(paths, size):
    """Image-space corners -> pixels relative to the centred pivot, y up:
    what the .meta physicsShape holds (Cloud.prefab wants the same / PPU)."""
    w, h = size
    return [[(x - w / 2, h / 2 - y) for x, y in path] for path in paths]


def yaml_paths(paths, indent, scale=1.0):
    lines = []
    for path in paths:
        for i, (x, y) in enumerate(path):
            lead = " " * indent + ("- - " if i == 0 else "  - ")
            lines.append(f"{lead}{{x: {x * scale:g}, y: {y * scale:g}}}")
    return "\n".join(lines)


def set_meta_physics_shape(meta, paths):
    """Replace the sprite's physicsShape block; everything else (the guid
    above all) stays as Unity serialised it."""
    text = meta.read_text()
    block = "    physicsShape:\n" + yaml_paths(paths, 4) + "\n"
    new, n = re.subn(r"^ {4}physicsShape:.*?(?=^ {4}\w)", block, text, count=1,
                     flags=re.MULTILINE | re.DOTALL)
    if n != 1:
        sys.exit(f"{meta.name}: no spriteSheet.physicsShape to replace")
    meta.write_text(new)


def set_prefab_collider(paths_by_guid):
    """Cloud.prefab's authored collider = the outline of the sprite it wears,
    so the editor shows the shape the game refits at runtime anyway."""
    text = PREFAB.read_text()
    m = re.search(r"m_Sprite: \{fileID: \d+, guid: ([0-9a-f]{32}), type: 3\}", text)
    if not m or m.group(1) not in paths_by_guid:
        print("Cloud.prefab: sprite not among the generated clouds - collider left alone")
        return
    block = "    m_Paths:\n" + yaml_paths(paths_by_guid[m.group(1)], 4, 1 / PPU) + "\n"
    new, n = re.subn(r"^ {4}m_Paths:.*?(?=^ {2}\w)", block, text, count=1,
                     flags=re.MULTILINE | re.DOTALL)
    if n != 1:
        sys.exit("Cloud.prefab: no PolygonCollider2D m_Paths to replace")
    PREFAB.write_text(new)


def collider_overlay(sprites, dest):
    from PIL import ImageDraw
    pad = 8
    width = max(s.width for _, s, _ in sprites) + pad * 2
    height = sum(s.height + pad for _, s, _ in sprites) + pad
    sheet = Image.new("RGBA", (width, height), (120, 170, 225, 255))
    y = pad
    for _, s, paths in sprites:
        sheet.alpha_composite(s, (pad, y))
        draw = ImageDraw.Draw(sheet)
        for path in paths:
            draw.polygon([(pad + x, y + py) for x, py in path], outline=(255, 0, 0, 255))
        y += s.height + pad
    sheet.save(dest / "collider_overlay.png")


# --- unity ----------------------------------------------------------------------

META = """fileFormatVersion: 2
guid: {guid}
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
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
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
    spriteID:
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
"""


def write_meta(png, paths):
    """A .meta that already exists keeps its guid - Cloud.prefab references
    it - and only its physicsShape is rewritten. Returns the guid."""
    meta = png.with_name(png.name + ".meta")
    if not meta.exists():
        meta.write_text(META.format(guid=uuid.uuid4().hex))
    set_meta_physics_shape(meta, paths)
    return re.search(r"^guid: ([0-9a-f]{32})", meta.read_text(), re.MULTILINE).group(1)


def preview(sprites, dest):
    pad = 16
    width = max(s.width for _, s, _ in sprites) * 2 + pad * 3
    height = sum(s.height + pad for _, s, _ in sprites) + pad
    sheet = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    half = width // 2
    sheet.paste((34, 44, 60, 255), (0, 0, half, height))
    sheet.paste((120, 170, 225, 255), (half, 0, width, height))
    y = pad
    for _, s, _ in sprites:
        sheet.alpha_composite(s, (pad, y))
        sheet.alpha_composite(s, (half + pad // 2, y))
        y += s.height + pad
    sheet.save(dest / "preview.png")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("photos", nargs="+", type=Path)
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--count", type=int, default=1, help="clouds to take per photo")
    args = ap.parse_args()

    sprites = []
    for photo in args.photos:
        for n, cloud in enumerate(extract(photo, args.count), 1):
            paths = collider_paths(cloud)
            print(f"    collider: {len(paths)} path(s), "
                  f"{sum(len(p) for p in paths)} points")
            sprites.append((f"cloud_{photo.stem}_{n}", cloud, paths))

    dest = ASSETS if args.apply else OUT
    dest.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)
    if args.apply:
        keep = {name for name, _, _ in sprites}
        for old in dest.glob("*.png"):
            if old.stem not in keep:
                old.unlink()
                old.with_name(old.name + ".meta").unlink(missing_ok=True)
                print(f"removed {old.name}")
    paths_by_guid = {}
    for name, cloud, paths in sprites:
        png = dest / f"{name}.png"
        cloud.save(png)
        if args.apply:
            guid = write_meta(png, to_pivot_space(paths, cloud.size))
            paths_by_guid[guid] = to_pivot_space(paths, cloud.size)
    if args.apply:
        set_prefab_collider(paths_by_guid)
    else:
        preview(sprites, dest)
    collider_overlay(sprites, OUT)
    print(f"-> {dest}")


if __name__ == "__main__":
    main()
