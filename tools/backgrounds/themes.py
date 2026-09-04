"""Prompt catalogue for the arena background / foreground generator.

Everything a render needs is data here, so a new arena is a dict entry, not
code. Two prompt families per theme:

  bg  — the 4096x2732 background. Framed so the horizon sits low and the
        upper two thirds are open sky: that is where the biplanes fight and
        where the HUD/kill feed live, so it must stay calm and readable.
  fg  — the destructible parallax strip (Sprites/Foreground/<Name>_fg.png).
        Rendered as a terrain cutout on a flat white ground, keyed to alpha
        on SPARK (RMBG-2.0); the pipeline then fills the ground solid, caps
        the height at FG_MAX_HEIGHT and makes the wrap seam invisible.

STYLES are the landscape-safe subset of Ol1nLLM's ``kStylePresets``
(lib/models/style_preset.dart, ids kept identical so results cross-reference
the app's style matrix). The blocks are the validated originals with the
figure-specific phrases ("elongated figures") dropped — Flux would otherwise
plant people in every scene. ``painterly`` is the house style with no preset.

Franchise-flavoured arenas are written as HOMAGES: mood, palette and
architecture of the source, never its names, characters, logos or signature
props. Same rule as tools/ads/PROMPTS.md — real IP in an App Store build is
a takedown waiting to happen. Rename freely; the ids are just file stems.
"""

# Shared framing appended to every background prompt.
BG_FRAME = (
    "wide panoramic landscape, horizon in the lower third of the frame, "
    "vast open sky filling the upper two thirds, the sky is completely "
    "empty and calm, uninhabited scenery, textless illustration without "
    "any lettering, title or signature, full-bleed artwork"
)

# Shared framing for the foreground cutouts. The white ground is what RMBG
# keys out; "bottom edge" keeps the terrain anchored so the strip has a floor.
FG_FRAME = (
    "side view terrain strip for a 2d scrolling game, tall massive terrain "
    "filling most of the frame from the bottom edge upward, high peaks and "
    "tall structures reaching towards the top, uneven silhouette skyline, "
    "isolated on a plain flat pure white background, only the terrain is "
    "drawn, textless, flat vector cutout with crisp edges"
)

# 1920 px @ PPU 100 = 19.2 wu. Was 1280, but the delivered strips were only
# using 487..1041 of it - the model drew short terrain in a wide frame, so the
# cap was never what limited them. Raised alongside a taller render frame and
# a prompt that asks for height, aiming ~1.5x the previous terrain.
FG_MAX_HEIGHT = 1920

STYLES = {
    # Every entry is a Flux style block. "model" (only on entries that carry
    # one) is a routing override: `bg`/`fg` with no --model flag send that
    # style to the named SDXL checkpoint instead of Flux, and drop the block
    # entirely - the 2026-09-03 model comparison confirmed SDXL checkpoints
    # (tried: Illustrious, Juggernaut, Juggernaut-Lightning) do not react to
    # style text at all, they just render their own default look. Passing
    # --model explicitly overrides this routing for every style, Flux
    # included - that is how the comparison itself was run.
    "realistic": dict(block="", model="juggernaut-lightning"),
    "painterly": (
        "stylized painterly video game background art, clean readable "
        "shapes, soft atmospheric depth, harmonious palette"
    ),
    "artdeco": (
        "art deco illustration style, streamlined geometric forms, strong "
        "symmetry, metallic gold and black accents, flat colour blocks, 1920s "
        "travel illustration aesthetic"
    ),
    "ukiyoe": (
        "ukiyo-e style, bold black outlines, flat color areas, elegant curved "
        "lines, limited color palette, bokashi gradients, japanese woodblock "
        "print aesthetic"
    ),
    "papercut": (
        "layered paper cut style, flat colour silhouettes stacked in depth, "
        "sharp negative space, subtle drop shadows between the paper layers"
    ),
    "impressionist": (
        "impressionist plein air painting, broken brushstrokes, vibrating "
        "complementary colours, soft daylight, loose edges, atmospheric "
        "immediacy"
    ),
    "chineseink": (
        "traditional chinese ink wash painting, flowing black ink "
        "brushstrokes, minimal color, elegant empty space, soft gradients, "
        "misty atmosphere"
    ),
    "stainedglass": (
        "gothic stained glass window style, bold black lead lines, luminous "
        "saturated colour panels, flat shapes, backlit glow"
    ),
    "woodcut": (
        "german expressionist woodcut print, harsh carved lines, stark black "
        "and white with a single spot colour, angular forms, visible gouge "
        "marks"
    ),
    "constructivist": (
        "russian constructivist graphic style, bold diagonal composition, red "
        "black and cream, geometric shapes, photomontage feel"
    ),
    "rinpa": (
        "japanese rinpa screen style, gold leaf background, bold flat "
        "silhouettes, stylized waves and grasses, mineral pigments, "
        "decorative asymmetry"
    ),
    "minhwa": (
        "korean minhwa folk painting style, flat cheerful colours, naive "
        "charming proportions, decorative motifs, hanji paper texture"
    ),
    "huichol": (
        "huichol yarn painting style, dense parallel yarn lines, vivid "
        "contrasting colours, flat filled forms"
    ),
    "secession": (
        "vienna secession style, flat gilded ornament, geometric mosaic "
        "patterns, decorative square motifs, gold and muted green"
    ),
    "aboriginal": (
        "australian aboriginal dot painting style, intricate dot patterns, "
        "earth pigment colors, symbolic story elements, flat composition"
    ),
}

# Batch defaults: calm enough behind dogfights, period-appropriate, distinct.
# "realistic" replaced "painterly" here 2026-09-03 - Juggernaut-Lightning's
# photoreal jungle beat Flux's painterly jungle outright in the comparison.
# "painterly" is still selectable via --styles, just not in the default six.
DEFAULT_STYLES = ["realistic", "artdeco", "ukiyoe", "papercut",
                  "impressionist", "chineseink"]

THEMES = {
    # ---- nature / places -------------------------------------------------
    "jungle": dict(
        seed=1101,
        bg="dense tropical rainforest valley, giant kapok trees with buttress "
           "roots, hanging lianas, layered emerald canopy, mist drifting "
           "between the treetops, distant blue mountains, humid golden light",
        fg="jungle treeline with tall palms, ferns, broad-leaf plants, "
           "mossy boulders and a rope bridge slung between two thick trunks",
    ),
    "city": dict(
        seed=1102,
        bg="european city skyline in 1917, church spires, factory chimneys "
           "trailing smoke, tenement rooftops, a river with iron bridges, "
           "warm evening light",
        fg="row of old city rooftops with chimneys, a clock tower, a water "
           "tower, church spire, attic windows and rooftop antenna poles",
    ),
    "mountains": dict(
        seed=1103,
        bg="alpine mountain range with snow-capped peaks, a glacier valley, "
           "pine forest on the lower slopes, dramatic cloud bank behind the "
           "summits, crisp cold morning light",
        fg="rocky mountain ridge with pine trees, boulders, a wooden alpine "
           "hut and a small stone cairn",
    ),
    "ocean": dict(
        seed=1104,
        bg="open ocean with long rolling swells, a lighthouse on a rocky "
           "islet, distant sailing ships on the horizon, towering cumulus "
           "clouds, bright maritime light",
        fg="sea cliffs with a lighthouse, jagged rocks with crashing waves, "
           "a wooden pier and a beached rowing boat",
    ),
    "desert": dict(
        seed=1105,
        bg="sahara sand dunes, a palm oasis, distant sandstone mesas, heat "
           "haze, blazing orange sunset sky",
        fg="sand dunes with cacti, a ruined mud-brick desert fort, palm "
           "trees around a small oasis pool",
    ),
    "waterfalls": dict(
        seed=1106,
        bg="giant tiered waterfalls plunging into a misty gorge, rainbow in "
           "the spray, lush cliff walls, wide river below, soft morning light",
        fg="rocky cliff ledge with a waterfall pouring off it, mossy "
           "boulders, ferns, a fallen log bridge",
    ),
    "space": dict(
        seed=1107,
        bg="outer space vista, a ringed gas giant, asteroid belt, colourful "
           "nebula clouds, distant stars, no planet surface",
        fg="cratered lunar surface with rocky spires, a crashed satellite, "
           "a rocket launch gantry and a domed moon base",
    ),
    "volcano": dict(
        seed=1108,
        bg="erupting volcano at dusk, glowing lava rivers, towering ash "
           "plume lit from below, black basalt plains, ember-red sky",
        fg="jagged basalt rocks with glowing lava cracks, a smoking vent, "
           "dead charred trees",
    ),
    "arctic": dict(
        seed=1109,
        bg="arctic ice shelf, drifting icebergs, aurora borealis rippling "
           "across the sky, pale low sun, cold teal and violet palette",
        fg="ice floes and iceberg blocks, snow drifts, an igloo, a wooden "
           "expedition hut with a radio mast",
    ),
    "countryside": dict(
        seed=1110,
        bg="french countryside in 1917, patchwork fields, poplar alleys, a "
           "windmill, a village with a church tower, soft summer haze",
        fg="hedgerows, hay stacks, a windmill, a stone farmhouse, wooden "
           "fences and old oak trees",
    ),
    # ---- franchise homages (mood only, no IP) -----------------------------
    "gotham": dict(
        seed=1201,
        bg="brooding gothic art deco metropolis at night, gargoyle-topped "
           "skyscrapers, searchlight beams sweeping low storm clouds, rain, "
           "noir blue-black palette with amber windows",
        fg="gothic skyscraper rooftops with gargoyles, gothic spires, "
           "rooftop water tanks and neon-lit fire escapes",
    ),
    "dreamhouse": dict(
        seed=1202,
        bg="candy-pink pastel resort city, palm trees, glossy dreamhouse "
           "villas with balconies, turquoise pools, glittery sky, plastic "
           "toy-like sheen",
        fg="row of pink pastel villas with balconies, palm trees, "
           "pool slides, a pink convertible parked in a driveway",
    ),
    "villainlair": dict(
        seed=1203,
        bg="cartoon supervillain's cliffside lair, giant curved rooftop "
           "tower, wacky gadget rockets, cheerful suburban cul-de-sac in "
           "front, bright saturated 3d-animation look",
        fg="cartoon suburban houses, a crooked villain's mansion tower, "
           "gadget antennas, a freeze-ray cannon on a lawn",
    ),
    "comiccity": dict(
        seed=1204,
        bg="comic book superhero city skyline, halftone dot shading, bold "
           "ink outlines, dramatic sky with speed lines, primary colours, "
           "pow-bang energy",
        fg="comic book city rooftops with skyscrapers, "
           "water towers, cranes and a bridge, bold ink outlines and halftone",
    ),
    "twinsuns": dict(
        seed=1205,
        bg="desert planet with two suns setting, moisture vaporators, a "
           "domed adobe homestead, sand dunes, distant rocky mesas, "
           "space-opera matte painting",
        fg="desert planet surface with adobe domes, tall vaporator towers, "
           "rock formations, a parked sand skiff",
    ),
    "backyard": dict(
        seed=1206,
        bg="cheerful flat-colour cartoon australian suburb, weatherboard "
           "houses on stilts, jacaranda and gum trees, bright blue sky, "
           "soft pastel palette, children's tv animation style",
        fg="cartoon backyard fence with gum trees, a trampoline, a "
           "clothesline, a cubby house and a weatherboard house on stilts",
    ),
    "seafloor": dict(
        seed=1207,
        bg="cartoon underwater town on the sea floor, coral houses, "
           "anemone gardens, rising bubbles, sun rays through blue water, "
           "bright saturated cartoon style",
        fg="sea floor with coral houses, giant anemones, kelp, a sunken "
           "shipwreck and a treasure chest",
    ),
}

# Photographs already in Assets that the restyle branch repaints (Kontext).
RESTYLE_SOURCES = ["Sky", "Rainbow", "Sunny_beach", "Smart_wings",
                   "Manhattan", "Teheran"]

RESTYLE_PROMPT = (
    "Convert this photograph into {style}. Keep exactly the same scene, "
    "composition, horizon and colour mood. Remove every person, sign, logo "
    "and piece of text. Full-bleed image that runs to all four edges, "
    "without any frame, border, lettering, seal or signature"
)

# Kontext needs structure to hold on to; pure sky photos (Sky, Rainbow) turn
# into abstract texture. They stay in the list so the batch shows it, but
# Sunny_beach / Manhattan / Teheran are the ones worth picking from.

def restyle_style_text(style):
    """restyle always runs on Flux Kontext regardless of a style's SDXL
    routing, so 'realistic' needs real text here instead of an empty block."""
    if style == "realistic":
        return ("photorealistic photograph, natural lighting, cinematic "
                "depth of field")
    return style_block(style)


def style_block(style):
    s = STYLES[style]
    return s["block"] if isinstance(s, dict) else s


def style_model(style, requested):
    """requested is the CLI --model value; 'flux' (its default) means 'let
    the style decide', anything else is an explicit override that wins."""
    if requested != "flux":
        return requested
    s = STYLES[style]
    return s.get("model", "flux") if isinstance(s, dict) else "flux"


def bg_prompt(theme, style):
    block = style_block(style)
    tail = f", {block}" if block else ""
    return f"{THEMES[theme]['bg']}, {BG_FRAME}{tail}"


def fg_prompt(theme, style):
    block = style_block(style)
    tail = f", {block}" if block else ""
    return f"{THEMES[theme]['fg']}, {FG_FRAME}{tail}"
