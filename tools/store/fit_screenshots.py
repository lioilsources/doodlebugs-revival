#!/usr/bin/env python3
"""Store screenshots and Play graphics for Doodlebugs Revival (plan 33 WP5).

Screenshots from a device go in, files with the exact pixel sizes the stores
accept come out. Nothing is stretched: an image is scaled to cover the target
and the overflow is cropped from both sides equally (--fit cover, default), or
it is scaled to fit and the bars are filled with a blurred copy of itself
(--fit pad) - use pad when cropping would cut off HUD or hangar buttons.

    python3 tools/store/fit_screenshots.py shots/iphone/*.png --target iphone69
    python3 tools/store/fit_screenshots.py shots/ipad/*.png   --target ipad13
    python3 tools/store/fit_screenshots.py shots/android/*.png --target play --fit pad
    python3 tools/store/fit_screenshots.py --play-art

Output: tools/store/out/<target>/<name>.png (gitignored). Pillow only.
"""
import argparse
import sys
from pathlib import Path

from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent / "out"

# Landscape sizes. App Store: 6.9" iPhone is the required set, 13" iPad only
# when the build supports iPad. Play: 16:9 phone screenshots.
TARGETS = {
    "iphone69": (2868, 1320),
    "ipad13": (2732, 2048),
    "play": (1920, 1080),
}

ICON = ROOT / "Assets/Doodlebugs/Sprites/Icon/AppIcon_1024.png"
POSTER = ROOT / "Assets/Doodlebugs/Resources/Sprites/UiThemes/skyposter"


def cover(img, size):
    w, h = size
    k = max(w / img.width, h / img.height)
    big = img.resize((round(img.width * k), round(img.height * k)), Image.LANCZOS)
    x = (big.width - w) // 2
    y = (big.height - h) // 2
    return big.crop((x, y, x + w, y + h))


def pad(img, size):
    w, h = size
    back = cover(img, size).filter(ImageFilter.GaussianBlur(radius=max(w, h) // 60))
    k = min(w / img.width, h / img.height)
    front = img.resize((round(img.width * k), round(img.height * k)), Image.LANCZOS)
    back.paste(front, ((w - front.width) // 2, (h - front.height) // 2))
    return back


def fit_shots(paths, target, mode):
    size = TARGETS[target]
    dest = OUT / target
    dest.mkdir(parents=True, exist_ok=True)
    for p in paths:
        img = Image.open(p).convert("RGB")
        if img.height > img.width:
            img = img.rotate(90, expand=True)   # portrait capture of a landscape game
        ratio_in = img.width / img.height
        ratio_out = size[0] / size[1]
        out = (cover if mode == "cover" else pad)(img, size)
        name = dest / (Path(p).stem + ".png")
        out.save(name, optimize=True)
        lost = max(0.0, 1 - min(ratio_in, ratio_out) / max(ratio_in, ratio_out))
        note = f", {lost:.0%} cropped" if mode == "cover" and lost > 0.005 else ""
        print(f"{name.relative_to(ROOT)}  {size[0]}x{size[1]}{note}")


def play_art():
    dest = OUT / "play"
    dest.mkdir(parents=True, exist_ok=True)

    icon = Image.open(ICON).convert("RGBA").resize((512, 512), Image.LANCZOS)
    # Play wants a 32-bit PNG without transparency in the corners it masks itself.
    flat = Image.new("RGBA", icon.size, (255, 255, 255, 255))
    flat.alpha_composite(icon)
    flat.convert("RGB").save(dest / "icon_512.png", optimize=True)

    back = cover(Image.open(POSTER / "backdrop.jpg").convert("RGB"), (1024, 500))
    mark = Image.open(POSTER / "wordmark.png").convert("RGBA")
    k = min(760 / mark.width, 200 / mark.height)
    mark = mark.resize((round(mark.width * k), round(mark.height * k)), Image.LANCZOS)
    feature = back.convert("RGBA")
    feature.alpha_composite(mark, ((1024 - mark.width) // 2, (500 - mark.height) // 2))
    feature.convert("RGB").save(dest / "feature_1024x500.png", optimize=True)
    print(f"{(dest / 'icon_512.png').relative_to(ROOT)}\n{(dest / 'feature_1024x500.png').relative_to(ROOT)}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("shots", nargs="*", help="device screenshots (PNG or JPEG)")
    ap.add_argument("--target", choices=sorted(TARGETS), help="store slot")
    ap.add_argument("--fit", choices=["cover", "pad"], default="cover")
    ap.add_argument("--play-art", action="store_true", help="512 icon + 1024x500 feature graphic")
    a = ap.parse_args()
    if a.play_art:
        play_art()
    if a.shots:
        if not a.target:
            ap.error("--target is required with screenshots")
        fit_shots(a.shots, a.target, a.fit)
    if not a.play_art and not a.shots:
        ap.print_help()
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
