#!/usr/bin/env python3
"""Fetch, instance and subset the UI theme fonts (Prompts/31 §5).

Reads tokens.FONTS, downloads each family from github.com/google/fonts,
cuts a static instance out of variable fonts (Unity's dynamic Font has no
axis support), subsets to Latin + Latin Extended and writes
Assets/Doodlebugs/Resources/Fonts/<name>.ttf with a .meta and the family's
OFL text next to it.

Needs fontTools:  .venv/bin/pip install fonttools
  .venv/bin/python tools/ui_themes/fetch_fonts.py            # missing only
  .venv/bin/python tools/ui_themes/fetch_fonts.py --force
"""
import argparse
import hashlib
import io
import json
import sys
import urllib.request
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DEST = ROOT / "Assets/Doodlebugs/Resources/Fonts"
sys.path.insert(0, str(HERE))
import tokens as T  # noqa: E402

RAW = "https://raw.githubusercontent.com/google/fonts/main/ofl/{dir}/{file}"
API = "https://api.github.com/repos/google/fonts/contents/ofl/{dir}"
# Latin, Latin-1, Latin Extended-A/B, Latin Extended Additional, punctuation, currency, arrows
UNICODES = ("U+0000-024F,U+0259,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+1E00-1EFF,"
            "U+2000-206F,U+20A0-20CF,U+2100-214F,U+2190-2199,U+2212,U+2215,U+FB01-FB02")
ALREADY = {"PressStart2P"}   # shipped before this plan


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": "doodlebugs-font-fetch"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def family_dir(family):
    return family.lower().replace(" ", "")


def load_font(name, spec):
    d = family_dir(spec["family"])
    if spec["static"]:
        return TTFont(io.BytesIO(get(RAW.format(dir=d, file=name + ".ttf"))))
    files = [f["name"] for f in json.loads(get(API.format(dir=d)))]
    var = next(f for f in files if f.endswith(".ttf") and "[" in f and "Italic" not in f)
    font = TTFont(io.BytesIO(get(RAW.format(dir=d, file=urllib.request.quote(var)))))
    axes = {a.axisTag: a.defaultValue for a in font["fvar"].axes}
    axes["wght"] = spec["wght"]
    return instancer.instantiateVariableFont(font, axes, updateFontNames=False)


def subset_font(font):
    opts = subset.Options()
    opts.layout_features = ["kern", "liga", "calt", "locl", "mark", "mkmk", "ccmp"]
    opts.name_IDs = ["*"]
    opts.hinting = False
    opts.notdef_outline = True
    s = subset.Subsetter(opts)
    s.populate(unicodes=subset.parse_unicodes(UNICODES))
    s.subset(font)
    return font


def guid(rel):
    return hashlib.md5(("doodle-font:" + rel).encode()).hexdigest()


def write_meta(path, family):
    m = path.with_name(path.name + ".meta")
    if m.exists():
        return
    rel = str(path.relative_to(ROOT))
    if path.suffix == ".txt":
        m.write_text(f"fileFormatVersion: 2\nguid: {guid(rel)}\nTextScriptImporter:\n  externalObjects: {{}}\n"
                     "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
        return
    m.write_text(f"""fileFormatVersion: 2
guid: {guid(rel)}
TrueTypeFontImporter:
  externalObjects: {{}}
  serializedVersion: 4
  fontSize: 16
  forceTextureCase: -2
  characterSpacing: 0
  characterPadding: 1
  includeFontData: 1
  fontNames:
  - {family}
  fallbackFontReferences: []
  customCharacters:
  fontRenderingMode: 0
  ascentCalculationMode: 1
  useLegacyBoundsCalculation: 0
  shouldRoundAdvanceValue: 1
  userData:
  assetBundleName:
  assetBundleVariant:
""")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--force", action="store_true")
    a = ap.parse_args()
    total, licences = 0, set()
    for name, spec in T.FONTS.items():
        out = DEST / f"{name}.ttf"
        if name in ALREADY:
            continue
        if out.exists() and not a.force:
            total += out.stat().st_size
            licences.add(spec["family"])
            continue
        font = subset_font(load_font(name, spec))
        font.save(out)
        write_meta(out, spec["family"])
        total += out.stat().st_size
        licences.add(spec["family"])
        print(f"  {name:28s} {out.stat().st_size // 1024:4d} KB")
    for family in sorted(licences):
        lic = DEST / (family.replace(" ", "") + "-OFL.txt")
        if not lic.exists():
            lic.write_bytes(get(RAW.format(dir=family_dir(family), file="OFL.txt")))
            write_meta(lic, family)
    print(f"{len(T.FONTS) - len(ALREADY)} fonts, {total / 1024 / 1024:.2f} MB")


if __name__ == "__main__":
    main()
