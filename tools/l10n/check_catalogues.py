#!/usr/bin/env python3
"""Checks the UI string catalogues and the fastlane store metadata.

UI (Assets/Doodlebugs/Resources/Localization/<code>.txt, see Loc.cs):
  - every catalogue has exactly the keys of en.txt, no duplicates
  - every value keeps the {N} placeholders of its English source
  - with fontTools installed: a catalogue whose text needs glyphs the
    Press Start 2P pixel font lacks must carry "@font=system"
    (--fix-font adds/removes the line)

Store (fastlane/metadata/{ios,android}/<locale>/*.txt): Apple and Google
length limits, keywords without spaces after commas.

Exit code 1 on any error. No Unity needed:  python3 tools/l10n/check_catalogues.py
"""
import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CAT = ROOT / "Assets/Doodlebugs/Resources/Localization"
FONT = ROOT / "Assets/Doodlebugs/Resources/Fonts/PressStart2P.ttf"
META = ROOT / "fastlane/metadata"
PLACEHOLDER = re.compile(r"\{\d+\}")

IOS_LIMITS = {"name.txt": 30, "subtitle.txt": 30, "keywords.txt": 100,
              "promotional_text.txt": 170, "description.txt": 4000, "release_notes.txt": 4000}
ANDROID_LIMITS = {"title.txt": 30, "short_description.txt": 80, "full_description.txt": 4000}


def parse(path):
    keys, font_system, dupes = {}, False, []
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue
        if line.startswith("@"):
            font_system |= line.strip() == "@font=system"
            continue
        if "=" not in line:
            continue
        k, v = line.split("=", 1)
        k = k.strip()
        if k in keys:
            dupes.append(k)
        keys[k] = v
    return keys, font_system, dupes


def check_ui(fix_font):
    errors = []
    en, _, _ = parse(CAT / "en.txt")
    cmap = None
    try:
        from fontTools.ttLib import TTFont
        cmap = set(TTFont(FONT).getBestCmap())
    except ImportError:
        print("fontTools not installed - skipping the glyph check")

    for path in sorted(CAT.glob("*.txt")):
        code = path.stem
        keys, font_system, dupes = parse(path)
        for k in dupes:
            errors.append(f"{code}: duplicate key {k}")
        for k in en.keys() - keys.keys():
            errors.append(f"{code}: missing {k}")
        for k in keys.keys() - en.keys():
            errors.append(f"{code}: unknown key {k}")
        for k, v in keys.items():
            if k in en and sorted(PLACEHOLDER.findall(v)) != sorted(PLACEHOLDER.findall(en[k])):
                errors.append(f"{code}: placeholders differ in {k}: {v!r}")
            if not v.strip():
                errors.append(f"{code}: empty {k}")
        if cmap is None:
            continue
        # perm.* go to iOS system dialogs, not the pixel font
        text = "".join(v for k, v in keys.items() if not k.startswith("perm."))
        missing = sorted({c for c in text if ord(c) not in cmap and c not in "\n"})
        needs_system = bool(missing)
        if needs_system != font_system:
            if fix_font:
                lines = [l for l in path.read_text(encoding="utf-8").splitlines() if l.strip() != "@font=system"]
                if needs_system:
                    lines.insert(1, "@font=system")
                path.write_text("\n".join(lines) + "\n", encoding="utf-8")
                print(f"{code}: @font=system {'added' if needs_system else 'removed'}")
            else:
                errors.append(f"{code}: pixel font lacks {''.join(missing[:20])!r} - needs @font=system"
                              if needs_system else f"{code}: @font=system but the pixel font covers it")
    return errors


def check_store():
    errors = []
    for platform, limits in (("ios", IOS_LIMITS), ("android", ANDROID_LIMITS)):
        base = META / platform
        if not base.exists():
            continue
        for loc in sorted(p for p in base.iterdir() if p.is_dir()):
            for name, limit in limits.items():
                f = loc / name
                if not f.exists():
                    if name != "release_notes.txt":
                        errors.append(f"{platform}/{loc.name}: missing {name}")
                    continue
                text = f.read_text(encoding="utf-8").rstrip("\n")
                if len(text) > limit:
                    errors.append(f"{platform}/{loc.name}/{name}: {len(text)} > {limit}")
                if not text.strip():
                    errors.append(f"{platform}/{loc.name}/{name}: empty")
                if name == "keywords.txt" and (", " in text or " ," in text):
                    errors.append(f"{platform}/{loc.name}/keywords.txt: space next to a comma")
    return errors


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--fix-font", action="store_true", help="add/remove @font=system lines")
    args = ap.parse_args()
    errors = check_ui(args.fix_font) + check_store()
    for e in errors:
        print("ERROR", e)
    n_ui = len(list(CAT.glob("*.txt")))
    print(f"{n_ui} catalogues, {len(errors)} errors")
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
