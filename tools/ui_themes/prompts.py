#!/usr/bin/env python3
"""Prompt catalogue for the UI theme art (Prompts/31-CLAUDE-PLAN-ui-themes.md).

Everything a render needs is data here, so a new theme is a dict entry, not
code - same idea as tools/backgrounds/themes.py. The generator that consumes
it (`generate_theme_art.py`: render / post / sheet / apply) is plan 31 §7.

Two image models, by what each is good at:

  schnell  FLUX.1 schnell, 4 steps. Fast, Apache-2.0, cannot spell. Used for
           everything textless: backdrops, seamless material tiles, single
           ornaments on a white ground.
  qwen     Qwen-Image. Slow (131-167 s per image on SPARK, 2026-11-10) but
           it renders lettering correctly. Used only for the wordmark, the
           one asset that carries text - "DOODLEBUGS", a proper name that is
           never localised. No other UI string is ever baked into a texture
           (ROADMAP §3: one language per locale).

Prompt rules that the validator below enforces:

  * schnell prompts are positive only. FLUX at cfg 1 is ATTRACTED to what a
    prompt negates ("no people" draws people), so there is no "no", "not" or
    "without" in them. What must stay out goes into the kind's `negative`,
    which the ComfyUI route applies through NAGuidance (spark_backgrounds
    `flux_graph(..., negative=...)`). The NIM route has no negatives at all:
    treat its output as drafts.
  * Every asset is an homage to an era or a setting, never to a title: no
    game, studio, console or character names, no logos, no signature props.
    Same rule as tools/backgrounds/themes.py and tools/ads/PROMPTS.md.
  * schnell sizes stay inside the NIM envelope (sides 768..1344, step 64) so
    the same job runs on either route.

Usage:
  python3 tools/ui_themes/prompts.py                    # job table + validation
  python3 tools/ui_themes/prompts.py --prompts          # full prompt per job
  python3 tools/ui_themes/prompts.py --theme canopy --prompts
  python3 tools/ui_themes/prompts.py --model qwen --prompts
  python3 tools/ui_themes/prompts.py --json             # machine-readable job list
"""
import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import tokens as T  # noqa: E402

WORD = "DOODLEBUGS"
ASSET_ROOT = "Assets/Doodlebugs/Resources/Sprites/UiThemes"

# ------------------------------------------------------------------ models --
MODELS = {
    # Graph verified in a sibling project on the same ComfyUI (2026): UNETLoader
    # + DualCLIPLoader(type flux) + ae VAE + EmptySD3LatentImage + KSampler.
    "schnell": dict(
        license="Apache-2.0",
        comfy=dict(unet="flux1-schnell.safetensors", clip=("clip_l.safetensors", "t5xxl_fp16.safetensors"),
                   vae="ae.safetensors", steps=4, cfg=1.0, sampler="euler", scheduler="simple"),
        nim=dict(service="flux-schnell", steps=4, side_min=768, side_max=1344, side_step=64,
                 note="txt2img only, seed < 2**32, answers a JPEG named .png, no negative prompt"),
    ),
    # File names are the stock ComfyUI Qwen-Image set and are NOT verified on
    # SPARK: check them against /object_info before the first batch (a
    # missing input makes ComfyUI drop the node and still report success).
    "qwen": dict(
        license="Apache-2.0",
        comfy=dict(unet="qwen_image_fp8_e4m3fn.safetensors", clip="qwen_2.5_vl_7b_fp8_scaled.safetensors",
                   clip_type="qwen_image", vae="qwen_image_vae.safetensors", shift=3.1,
                   steps=20, cfg=2.5, sampler="euler", scheduler="simple", verify=True),
    ),
}

# ------------------------------------------------------------------ frames --
BACKDROP_FRAME = (
    "wide establishing shot composed as a game menu background, the subject sits "
    "in the upper half and toward the right side, the left third and the bottom "
    "quarter are calm, dark and low in detail, deserted and quiet scene, "
    "textless image, full-bleed artwork"
)
TILE_FRAME = (
    "seamless tileable material texture filling the whole frame, seen straight "
    "from above in flat even light, the same detail density from edge to edge, "
    "textless surface"
)
ORNAMENT_FRAME = (
    "one single object centred on a plain flat pure white background, the whole "
    "object inside the frame with a generous empty margin, soft even studio "
    "light, crisp clean silhouette"
)
GLOW_FRAME = (
    "one single glowing shape centred on a pure black background, the whole "
    "shape inside the frame with a generous empty margin"
)
WORDMARK_SOLID = (
    "one horizontal line, centred, all ten letters fully visible with a wide "
    "empty margin on every side, correct spelling D-O-O-D-L-E-B-U-G-S, the "
    "lettering is the only element in the image, on a flat plain light grey "
    "background"
)
WORDMARK_GLOW = (
    "one horizontal line, centred, all ten letters fully visible with a wide "
    "empty margin on every side, correct spelling D-O-O-D-L-E-B-U-G-S, the "
    "lettering is the only element in the image, on a pure black background"
)

NEG_SCENE = ("people, person, character, face, pilot, aircraft, airplane, vehicle, text, letters, "
             "numbers, logo, watermark, signature, caption, user interface, frame, border")
NEG_TILE = ("text, letters, logo, watermark, object, perspective, horizon, vignette, dark corners, "
            "cast shadow, frame, border, seam")
NEG_ORNAMENT = ("text, letters, logo, watermark, hands, people, several objects, scenery, "
                "cast shadow on the ground, frame, border")
NEG_WORD = ("extra words, extra letters, missing letters, misspelling, duplicated letters, subtitle, "
            "tagline, small print, watermark, signature, people, scenery")

# ------------------------------------------------------------------- kinds --
# matte: how `post` turns the render into alpha.
#   None    opaque, kept as is
#   cutout  BEN2/RMBG mask UNION white-key, biggest component, trim
#           (same recipe as tools/weapons/generate_projectiles.py)
#   luma    alpha = max(r, g, b), colour un-premultiplied - for glows on black
KINDS = {
    "backdrop": dict(model="schnell", size=(1344, 768), seeds=4, matte=None, frame=BACKDROP_FRAME,
                     negative=NEG_SCENE, deliver=(1344, 768), ext="jpg", file="backdrop"),
    "tile": dict(model="schnell", size=(1024, 1024), seeds=3, matte=None, frame=TILE_FRAME,
                 negative=NEG_TILE, deliver=(256, 256), ext="png", file="tile_{name}",
                 post="make seamless (roll by half, cross-fade the seam like `--seam blend`), then downscale"),
    "ornament": dict(model="schnell", size=(1024, 1024), seeds=3, matte="cutout", frame=ORNAMENT_FRAME,
                     negative=NEG_ORNAMENT, deliver=(256, 256), ext="png", file="orn_{name}"),
    "strip": dict(model="schnell", size=(1344, 768), seeds=3, matte="cutout", frame=ORNAMENT_FRAME,
                  negative=NEG_ORNAMENT, deliver=(512, 160), ext="png", file="orn_{name}"),
    "glow": dict(model="schnell", size=(1024, 1024), seeds=3, matte="luma", frame=GLOW_FRAME,
                 negative=NEG_ORNAMENT, deliver=(256, 256), ext="png", file="orn_{name}"),
    "wordmark": dict(model="qwen", size=(1664, 928), seeds=4, matte="per-asset", frame=None,
                     negative=NEG_WORD, deliver=(1024, 320), ext="png", file="wordmark",
                     fallback="render the word from the theme's display TTF with Pillow (always spelled right)"),
}


def word(look, matte):
    """A wordmark asset. `look` describes the lettering; the frame adds layout."""
    return dict(kind="wordmark", matte=matte,
                prompt=f'The single word "{WORD}" {look}, {WORDMARK_GLOW if matte == "luma" else WORDMARK_SOLID}')


# ------------------------------------------------------------------ themes --
# style  - art direction of the backdrop (a scene)
# finish - rendering style of tiles and ornaments (a material, no scene words)
THEMES = {
    "skyposter": dict(
        style=("bold flat screen-print poster illustration, limited palette of navy, orange, cyan, "
               "cream and yellow, thick clean shapes, subtle paper grain"),
        finish="flat screen-print illustration, thick clean shapes, limited palette",
        assets={
            "backdrop": dict(kind="backdrop", prompt=(
                "a golden-hour sky over a grass airstrip: towering stacked cumulus clouds in orange "
                "and cream, deep navy upper sky, a row of small empty hangars with striped windsocks "
                "low on the right, long soft shadows across the field")),
            "wordmark": word("as a bold aviation poster logo: heavy condensed italic capital letters in "
                             "white with a thick navy outline and a hard navy drop shadow straight down, "
                             "flat colours, one small orange speed stripe under the word", "cutout"),
        },
    ),
    "phosphor": dict(
        style=("1970s vector monitor arcade graphics, glowing green phosphor line art on a pure black "
               "screen, thin luminous wireframe strokes with soft bloom, every surface is an open "
               "outline, faint horizontal scanlines, slight glass curvature at the corners"),
        finish="glowing green phosphor line art on black",
        assets={
            "backdrop": dict(kind="backdrop", prompt=(
                "a wireframe airfield at night: an empty runway drawn as a perspective grid receding "
                "to a low horizon, the outline of an arched hangar on the right, jagged mountain "
                "ridges as open polylines, scattered star dots and the outline of a ringed moon high "
                "on the right")),
            "wordmark": word("drawn as a vector arcade logo: angular monoline capital letters made of "
                             "thin glowing green phosphor strokes, each letter built from straight "
                             "line segments with small bright dots at the joints, soft bloom", "luma"),
        },
    ),
    "coinop": dict(
        style=("1980s 8-bit arcade pixel art, large clearly visible square pixels, strictly limited "
               "palette of black, magenta, cyan, yellow and orange, flat colours with ordered "
               "dithering, hard aliased edges"),
        finish="8-bit pixel art, large square pixels, black, magenta, cyan and yellow",
        assets={
            "backdrop": dict(kind="backdrop", prompt=(
                "a night airfield seen side-on: single-pixel stars over a black sky, a dithered "
                "magenta-to-orange glow on the horizon, chunky pixel clouds, a row of runway lights, "
                "a blocky empty hangar with a checkerboard apron on the right")),
            "wordmark": word("as a chunky 8-bit arcade marquee logo: blocky pixel capital letters "
                             "filled with three horizontal bands of yellow, orange and magenta, a thick "
                             "black pixel outline and a hard black drop shadow offset down and right, "
                             "large visible square pixels", "cutout"),
        },
    ),
    "chrome": dict(
        style=("mid-1990s pre-rendered 3D computer graphics, early raytraced look, polished chrome "
               "and brushed steel, hard specular highlights, lens flares, slightly plastic shading, "
               "dramatic orange rim light against cool blue shadow"),
        finish="1990s pre-rendered 3D render, hard specular highlights, cool grey metal",
        assets={
            "backdrop": dict(kind="backdrop", prompt=(
                "the interior of a huge empty steel hangar: riveted wall plates, chrome pipes along "
                "the ceiling, a glossy checkerboard floor reflecting orange light, half-open hangar "
                "doors on the right showing a fiery sunset sky, volumetric light shafts, yellow and "
                "black hazard stripes on the door frame")),
            "tile_steel": dict(kind="tile", prompt=(
                "brushed stainless steel plate, fine straight horizontal brushing lines, one soft "
                "broad reflection band, neutral cool grey")),
            "orn_rivet": dict(kind="ornament", prompt=(
                "a round dome-head steel rivet seen from straight above, polished, with one small "
                "specular highlight")),
            "orn_flare": dict(kind="glow", prompt=(
                "a four-point star lens flare with a small white-hot core and four thin tapering rays")),
            "wordmark": word("as a 1990s chrome 3D logo: heavy bold italic capital letters extruded in "
                             "depth, mirror-chrome faces reflecting a blue sky above a brown horizon, "
                             "bevelled gold edges, one star-shaped glint on the first letter", "cutout"),
        },
    ),
    "aqua": dict(
        style=("mid-2000s glossy vector illustration from the browser-game era, smooth candy "
               "gradients, glassy highlights and soft reflections, thick clean outlines, bright "
               "saturated sky blue and grass green"),
        finish="glossy mid-2000s vector illustration, candy gradients, glassy highlights",
        assets={
            "backdrop": dict(kind="backdrop", prompt=(
                "a sunny cartoon airfield: rolling green hills, an empty red-roofed hangar with a "
                "striped windsock on the right, puffy white clouds with glossy highlights, a bright "
                "sun with a soft flare in the upper right, a few translucent bubbles drifting in a "
                "deep blue sky")),
            "orn_flare": dict(kind="glow", prompt=(
                "a soft circular sun flare: a bright white core, one faint halo ring and three small "
                "pale hexagonal ghosts in a diagonal row")),
            "wordmark": word("as a glossy mid-2000s casual game logo: fat rounded capital letters in "
                             "sky blue with a white outline, a glass highlight band across the upper "
                             "half of every letter, a soft dark blue drop shadow, a slightly bouncy "
                             "baseline", "cutout"),
        },
    ),
    "flat": dict(
        style=("mid-2010s flat design illustration, low-poly triangulated shapes, each facet one "
               "solid flat colour, long diagonal shadows, palette of teal, deep orange, amber and "
               "white, minimal and geometric"),
        finish="flat design illustration, solid flat colours, geometric",
        assets={
            "backdrop": dict(kind="backdrop", prompt=(
                "a low-poly mountain range under a large flat sun disc in the upper right, a small "
                "geometric hangar and a control tower on the right casting long diagonal shadows, a "
                "faceted teal sky with flat triangular clouds")),
            "wordmark": word("as a flat design logo: bold geometric sans-serif capital letters in "
                             "pure white with a deep orange long shadow running down and to the right "
                             "at 45 degrees, perfectly flat colours", "cutout"),
        },
    ),
    "clay": dict(
        style=("claymation stop-motion diorama, soft plasticine with visible fingerprints and "
               "rounded edges, pastel lilac, peach, mint and butter yellow, soft studio light with "
               "gentle shadows, shallow depth of field, macro photograph of a handmade miniature set"),
        finish="handmade plasticine, visible fingerprints, soft studio light, pastel colours",
        assets={
            "backdrop": dict(kind="backdrop", prompt=(
                "a tiny clay airfield: a rounded peach hangar with a mint roof on the right, puffy "
                "plasticine clouds held up on thin sticks, soft lilac hills, a butter-yellow sun, a "
                "striped clay windsock")),
            "tile_clay": dict(kind="tile", prompt=(
                "smooth cream-white plasticine surface with faint fingerprints and tiny soft dents")),
            "orn_star": dict(kind="ornament", prompt=(
                "a chubby five-point star modelled from butter-yellow plasticine")),
            "orn_cloud": dict(kind="ornament", prompt=(
                "a puffy little cloud modelled from white plasticine")),
            "wordmark": word("as a claymation logo: chubby rounded capital letters hand-modelled from "
                             "plasticine, alternating lilac, peach, mint and butter yellow, visible "
                             "fingerprints, soft studio light, a gentle contact shadow", "cutout"),
        },
    ),
    "lightfield": dict(
        style=("speculative near-future spatial interface art, translucent glass planes floating in "
               "depth, iridescent thin-film caustics, soft volumetric light, deep indigo and violet "
               "darkness with cyan, lilac and rose glows, immaculate and cinematic"),
        finish="iridescent thin-film light, cyan, lilac, rose and pale gold",
        assets={
            "backdrop": dict(kind="backdrop", prompt=(
                "a vast dark hall where sheets of frosted glass hover at different depths, thin "
                "lines of light tracing an empty runway that fades into the distance, an aurora of "
                "cyan and rose light drifting across the upper right, fine luminous dust")),
            "tile_iris": dict(kind="tile", prompt=(
                "iridescent holographic foil, smooth flowing bands of cyan, lilac, rose and pale "
                "gold melting into each other")),
            "wordmark": word("as a light-sculpture logo: very wide, thin, evenly spaced capital "
                             "letters drawn with fine tubes of white light, an iridescent cyan-to-rose "
                             "halo around every stroke, floating in darkness", "luma"),
        },
    ),
    "cockpit": dict(
        style=("hard science fiction concept art, industrial spacecraft interior, cool navy shadows "
               "with cyan instrument light and orange warning light, crisp panel lines, painterly "
               "but precise"),
        finish="hard science fiction hull material, navy with cyan light",
        assets={
            "backdrop": dict(kind="backdrop", prompt=(
                "an empty orbital hangar bay seen from inside: a wide rectangular bay door on the "
                "right opening onto a ringed gas giant and a field of stars, docking clamps and "
                "gantry arms framing the opening, floor guidance lights converging toward the door, "
                "thin haze in the light beams")),
            "tile_hull": dict(kind="tile", prompt=(
                "dark navy spacecraft hull plating, fine panel seams in a rectangular grid, subtle "
                "scuffs, matte metal")),
            "wordmark": word("as a spacecraft head-up display logo: angular capital letters with "
                             "chamfered corners drawn as a glowing cyan outline, a thin orange "
                             "underline with small tick marks, faint scanlines inside the letters",
                             "luma"),
        },
    ),
    "canopy": dict(
        style=("hand-painted adventure game art, lush tropical jungle, warm golden light filtering "
               "through leaves, saturated greens with warm browns and gold, bold readable shapes "
               "with soft painterly shading"),
        finish="hand-painted game texture, bold readable shapes, soft painterly shading, warm colours",
        assets={
            "backdrop": dict(kind="backdrop", prompt=(
                "a jungle clearing with an empty hangar built from bamboo and woven palm leaves on "
                "the right, rope bridges between giant trees, hanging lianas, a waterfall in the "
                "misty distance, shafts of golden sunlight, a mossy stone idol half hidden in ferns")),
            "tile_wood": dict(kind="tile", prompt=(
                "weathered warm brown wooden planks laid horizontally, visible grain and a few "
                "knots, thin dark gaps between the planks")),
            "tile_parchment": dict(kind="tile", prompt=(
                "old cream parchment, soft paper fibres and faint pale stains, even tone")),
            "tile_stone": dict(kind="tile", prompt=(
                "grey-green carved stone, shallow chisel marks, small patches of moss")),
            "tile_gold": dict(kind="tile", prompt=(
                "hammered polished gold, small soft dents, warm reflections")),
            "orn_leaf_a": dict(kind="ornament", prompt=(
                "a broad glossy green monstera leaf with deep splits, lying flat, seen from above")),
            "orn_leaf_b": dict(kind="ornament", prompt=(
                "a long bright green banana leaf with a pale midrib, lying flat, seen from above")),
            "orn_vine": dict(kind="strip", prompt=(
                "a length of jungle liana running horizontally from the left edge to the right "
                "edge, small leaves and thin tendrils hanging down from it")),
            "orn_knot": dict(kind="ornament", prompt=(
                "a knot of thick tan rope tied as a square lashing, seen from the front")),
            "wordmark": word("as a jungle adventure logo: thick rounded capital letters carved from "
                             "warm brown wood with a golden bevelled edge, small green leaves and one "
                             "curl of vine growing over the first and the last letter, a bold dark "
                             "brown outline", "cutout"),
        },
    ),
    "solaria": dict(
        style=("optimistic solarpunk concept art, clean bright daylight, white curved architecture "
               "wrapped in hanging gardens, teal glass and thin gold details, soft pastel sky, airy "
               "and elegant, crisp painterly illustration"),
        finish="clean elegant illustration, white, teal and thin gold",
        assets={
            "backdrop": dict(kind="backdrop", prompt=(
                "a utopian sky-port city: tall white towers with rounded tops and garden terraces, "
                "slender sky bridges, an empty domed glasshouse hangar with arched windows on the "
                "right, airship mooring masts, a large pale sun with thin halo rings in the upper "
                "right, wind turbines on distant hills")),
            "tile_ceramic": dict(kind="tile", prompt=(
                "glossy white glazed ceramic with a very faint pearl sheen and fine hairline crazing")),
            "orn_sprig": dict(kind="ornament", prompt=(
                "a slender green olive sprig with small paired leaves, lying flat")),
            "orn_sunburst": dict(kind="ornament", prompt=(
                "a thin gold line-art sunburst: a small circle with fine evenly spaced straight "
                "rays, art deco")),
            "wordmark": word("as an elegant utopian logo: tall thin art deco capital letters in deep "
                             "teal with a fine gold inline stroke, wide letter spacing, one small gold "
                             "sunburst above the middle of the word", "cutout"),
        },
    ),
}

# Arenas that would give the Jungle and Utopia themes a matching map. These go
# through the EXISTING pipeline (tools/backgrounds/spark_backgrounds.py, FLUX
# dev + RMBG), not through this one: paste the entry into themes.py THEMES.
# `jungle` is already there (seed 1101); Orbit already covers space.
ARENAS = {
    "utopia": dict(
        seed=1118,
        bg=("utopian garden city of the future, white towers with rounded tops and hanging "
            "gardens, slender sky bridges, glass domes, wind turbines on distant hills, warm "
            "morning light"),
        fg=("row of white futuristic towers with rounded tops, garden terraces, a glass dome, "
            "slender spires, arched sky bridges and rooftop trees"),
    ),
}


# -------------------------------------------------------------------- jobs --
def compose(theme_key, asset_key):
    th, a = THEMES[theme_key], THEMES[theme_key]["assets"][asset_key]
    k = KINDS[a["kind"]]
    if a["kind"] == "wordmark":
        return a["prompt"]
    block = th["style"] if a["kind"] == "backdrop" else th["finish"]
    return ", ".join((a["prompt"], block, k["frame"]))


def jobs(theme=None, model=None):
    for tk, th in THEMES.items():
        if theme and tk != theme:
            continue
        base = 3100 + T.THEMES[tk]["id"] * 10
        for i, (ak, a) in enumerate(th["assets"].items()):
            k = KINDS[a["kind"]]
            if model and k["model"] != model:
                continue
            name = ak.split("_", 1)[1] if "_" in ak else ak
            matte = a.get("matte", k["matte"])
            yield dict(
                theme=tk, asset=ak, kind=a["kind"], model=k["model"], size=k["size"],
                seeds=[base + i * 100 + s for s in range(k["seeds"])], matte=matte,
                deliver=k["deliver"], prompt=compose(tk, ak), negative=k["negative"],
                out=f"{ASSET_ROOT}/{tk}/{k['file'].format(name=name)}.{k['ext']}",
            )


def validate():
    errors = []
    if set(THEMES) != set(T.THEMES):
        errors.append(f"theme keys differ from tokens.py: {set(THEMES) ^ set(T.THEMES)}")
    nim = MODELS["schnell"]["nim"]
    for j in jobs():
        tag = f"{j['theme']}.{j['asset']}"
        if j["model"] == "schnell":
            if re.search(r"\b(no|not|without|never)\b", j["prompt"], re.I):
                errors.append(f"{tag}: negation in a schnell prompt (FLUX is attracted to it)")
            for side in j["size"]:
                if not (nim["side_min"] <= side <= nim["side_max"]) or side % nim["side_step"]:
                    errors.append(f"{tag}: side {side} is outside the NIM envelope")
        if j["kind"] == "wordmark":
            if j["prompt"].count(f'"{WORD}"') != 1:
                errors.append(f"{tag}: the word must be quoted exactly once")
            if j["matte"] not in ("luma", "cutout"):
                errors.append(f"{tag}: wordmark needs a matte")
        elif WORD.lower() in j["prompt"].lower():
            errors.append(f"{tag}: only the wordmark may carry text")
        if "backdrop" not in THEMES[j["theme"]]["assets"] or "wordmark" not in THEMES[j["theme"]]["assets"]:
            errors.append(f"{j['theme']}: every theme needs a backdrop and a wordmark")
    return sorted(set(errors))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--theme")
    ap.add_argument("--model", choices=sorted(MODELS))
    ap.add_argument("--prompts", action="store_true", help="print the composed prompt of every job")
    ap.add_argument("--json", action="store_true")
    a = ap.parse_args()

    js = list(jobs(a.theme, a.model))
    if a.json:
        json.dump(js, sys.stdout, indent=1)
        print()
    else:
        for j in js:
            w, h = j["size"]
            print(f"{j['theme']:11s} {j['asset']:15s} {j['model']:7s} {w}x{h}  x{len(j['seeds'])}  "
                  f"{str(j['matte']):6s} -> {j['out']}")
            if a.prompts:
                print(f"    + {j['prompt']}\n    - {j['negative']}\n")
        images = sum(len(j["seeds"]) for j in js)
        qwen = sum(len(j["seeds"]) for j in js if j["model"] == "qwen")
        print(f"{len(js)} jobs, {images} images ({images - qwen} schnell, {qwen} qwen ~ {qwen * 150 // 60} min)")

    errors = validate()
    for e in errors:
        print("INVALID", e, file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
