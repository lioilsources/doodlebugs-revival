#!/usr/bin/env python3
"""Turns a photograph of the sky into cloud sprites with a real alpha channel.

Blue sky is keyed out by how blue-dominant a pixel is (sky has a big b-r gap,
cloud is near-neutral), then the cloud colour is un-matted from the sky it was
blended with — without that, every soft edge keeps a blue halo that reads as
dirt once the sprite sits on a different background.

Each cloud in the frame is exported separately: the mask is flood-filled into
blobs and the biggest ones are cropped out, so one photo yields several usable
sprites instead of one busy sheet.

Usage:
  python3 tools/clouds/photo_to_cloud.py tools/clouds/raw/sky_01.jpg [--apply]

Without --apply the results land in tools/clouds/out/ for review; with it they
are written to Assets/.../Resources/Sprites/Clouds/ as cloud_photo_<n>.png.
"""
import argparse
import sys
from collections import deque
from pathlib import Path

from PIL import Image, ImageFilter

ROOT = Path(__file__).parents[2]
ASSETS = ROOT / "Assets/Doodlebugs/Resources/Sprites/Clouds"
OUT = Path(__file__).parent / "out"

WORK_MAX = 1600      # long edge the keying runs at
SPRITE_MAX = 512     # long edge of an exported sprite
# b-r gap: below LO is solid cloud, above HI is pure sky, between is the edge.
KEY_LO, KEY_HI = 14, 52
MIN_BLOB_FRAC = 0.004   # ignore specks smaller than this share of the frame


def key_alpha(img):
    """RGBA with sky removed and edge pixels un-matted from the sky colour."""
    px = img.load()
    w, h = img.size

    # Sky reference: the bluest decile of the frame, averaged.
    samples = sorted((px[x, y][2] - px[x, y][0], px[x, y])
                     for y in range(0, h, 7) for x in range(0, w, 7))
    sky_pool = [p for _, p in samples[int(len(samples) * 0.9):]]
    sky = tuple(sum(c[i] for c in sky_pool) // len(sky_pool) for i in range(3))

    out = Image.new("RGBA", (w, h))
    op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y][:3]
            d = b - r
            if d <= KEY_LO:
                a = 255
            elif d >= KEY_HI:
                a = 0
            else:
                a = int(255 * (KEY_HI - d) / (KEY_HI - KEY_LO))
            if a == 0:
                op[x, y] = (0, 0, 0, 0)
                continue
            if a == 255:
                op[x, y] = (r, g, b, 255)
                continue
            # Un-matting by dividing out the sky (pixel = a*cloud + (1-a)*sky)
            # is correct on paper and awful in practice: at low alpha the
            # division amplifies sensor noise into orange rims. Cloud edges are
            # neutral grey in reality, so instead of inventing colour we simply
            # drain the sky's blue cast — pull the pixel toward its own
            # luminance, hardest where the sky contributed most.
            f = a / 255
            lum = int(0.299 * r + 0.587 * g + 0.114 * b)
            k = 1 - f                       # 0 at solid cloud, 1 at the wisp
            cloud = tuple(int(c + (lum - c) * k) for c in (r, g, b))
            op[x, y] = cloud + (a,)
    return out


def blobs(alpha, w, h, min_px):
    """Bounding boxes of connected regions of solid-ish alpha, largest first."""
    seen = bytearray(w * h)
    found = []
    for sy in range(h):
        for sx in range(w):
            i = sy * w + sx
            if seen[i] or alpha[i] < 185:
                continue
            q = deque([(sx, sy)])
            seen[i] = 1
            x0 = x1 = sx
            y0 = y1 = sy
            n = 0
            while q:
                x, y = q.popleft()
                n += 1
                x0, x1 = min(x0, x), max(x1, x)
                y0, y1 = min(y0, y), max(y1, y)
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h:
                        j = ny * w + nx
                        if not seen[j] and alpha[j] >= 185:
                            seen[j] = 1
                            q.append((nx, ny))
            if n >= min_px:
                found.append((n, (x0, y0, x1 + 1, y1 + 1)))
    found.sort(reverse=True, key=lambda t: t[0])
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("photo")
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--count", type=int, default=3, help="sprites to export")
    args = ap.parse_args()

    src = Image.open(args.photo).convert("RGB")
    src.thumbnail((WORK_MAX, WORK_MAX), Image.LANCZOS)
    keyed = key_alpha(src)
    w, h = keyed.size

    a = keyed.getchannel("A")
    # Blob detection runs on a blurred mask so wisps of the same cloud join up
    # instead of splintering into a dozen scraps.
    mask = list(a.filter(ImageFilter.GaussianBlur(3)).get_flattened_data())
    regions = blobs(mask, w, h, int(w * h * MIN_BLOB_FRAC))
    if not regions:
        sys.exit("no cloud found — adjust KEY_LO/KEY_HI for this photo")

    dest = ASSETS if args.apply else OUT
    dest.mkdir(parents=True, exist_ok=True)
    n = 0
    for _, box in regions:
        if n >= args.count:
            break
        pad = 12
        crop = keyed.crop((max(0, box[0] - pad), max(0, box[1] - pad),
                           min(w, box[2] + pad), min(h, box[3] + pad)))
        # A phone frame is portrait and a formation photographed in it comes
        # out as a tall streak; in game the clouds drift sideways, so anything
        # markedly taller than wide is split into two and laid on its side.
        # Clouds have no canonical orientation — nobody can tell.
        parts = [crop]
        cw, ch = crop.size
        if ch > cw * 1.6:
            mid = ch // 2
            parts = [crop.crop((0, 0, cw, mid)), crop.crop((0, mid, cw, ch))]
        for part in parts:
            if n >= args.count:
                break
            if part.height > part.width:
                part = part.transpose(Image.ROTATE_90)
            bbox = part.getbbox()
            if bbox is None:
                continue
            part = part.crop(bbox)
            part.thumbnail((SPRITE_MAX, SPRITE_MAX), Image.LANCZOS)
            n += 1
            out = dest / f"cloud_photo_{n}.png"
            part.save(out)
            print(f"{out.name}  {part.size[0]}x{part.size[1]}")
    print(f"-> {dest}")


if __name__ == "__main__":
    main()
