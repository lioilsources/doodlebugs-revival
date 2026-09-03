"""Prompt catalogue for the 50 plane skins.

Mirrors Assets/Doodlebugs/Scripts/Skins/PlaneSkinCatalog.cs exactly - same
ids, same "key" strings (used as the sprite filename stem). Keep the two in
sync by hand; they're small and rarely change together.

Every prompt describes a flat MATERIAL/PATTERN SWATCH, not a scene - no
horizon, no framing, no lighting setup. generate_skins.py samples the
rendered swatch into the plane's paintable-body mask and multiplies by the
original sprite's luminance, so the prompt only needs to nail colour and
pattern; shading is reapplied afterwards for every skin uniformly.
"""

FRAME = (
    "flat seamless material swatch, texture reference sheet, uniform "
    "lighting, no vignette, no frame, no border, no text, no logo"
)

SKINS = {
    # --- Free starter skins (12) -------------------------------------------
    "doodle_red": dict(id=0, prompt=None),  # the original sprite, no render needed
    "raf_khaki": dict(id=1, prompt=(
        "matte khaki drab canvas fabric texture, subtle woven grain, "
        "WWI Royal Flying Corps livery")),
    "luftstreit_grey": dict(id=2, prompt=(
        "pale battleship grey linen fabric texture, subtle woven grain, "
        "WWI Luftstreitkrafte livery")),
    "aeronautique_blue": dict(id=3, prompt=(
        "horizon blue canvas fabric texture, subtle woven grain, "
        "WWI French Aeronautique Militaire livery")),
    "racing_stripe": dict(id=4, prompt=(
        "bold diagonal racing stripes, white and crimson red, glossy "
        "automotive paint")),
    "checkerboard": dict(id=5, prompt=(
        "black and white checkerboard pattern, medium-size squares, matte "
        "paint")),
    "candy_stripe": dict(id=6, prompt=(
        "thin candy stripe pattern, red and white pinstripes, glossy "
        "vintage paint")),
    "polka_dot": dict(id=7, prompt=(
        "cheerful polka dot pattern, cream background with red dots, "
        "glossy paint")),
    "woodgrain": dict(id=8, prompt=(
        "warm honey woodgrain texture, varnished plywood, visible grain "
        "lines, vintage aircraft fuselage")),
    "barnstormer_yellow": dict(id=9, prompt=(
        "bright barnstormer yellow paint, thin black pinstripe trim, "
        "glossy vintage paint")),
    "sunset_fade": dict(id=10, prompt=(
        "smooth gradient fade from orange to magenta to purple, glossy "
        "paint, airbrushed sunset colours")),
    "silver_dart": dict(id=11, prompt=(
        "brushed aluminium metal texture, fine linear grain, natural "
        "metal aircraft skin, riveted panels")),

    # --- Camo pack (9, premium) ---------------------------------------------
    "jungle_camo": dict(id=12, prompt=(
        "dense jungle camouflage pattern, mottled dark green mid green and "
        "brown blotches, military fabric texture")),
    "desert_camo": dict(id=13, prompt=(
        "desert camouflage pattern, mottled tan khaki and sand blotches, "
        "military fabric texture")),
    "arctic_camo": dict(id=14, prompt=(
        "arctic snow camouflage pattern, mottled white and pale grey "
        "blotches, military fabric texture")),
    "naval_dazzle": dict(id=15, prompt=(
        "WWI naval dazzle camouflage, sharp geometric black white and grey "
        "stripes at bold angles, high contrast")),
    "volcanic_camo": dict(id=16, prompt=(
        "volcanic rock camouflage pattern, mottled charcoal black and "
        "ember orange blotches, military fabric texture")),
    "forest_camo": dict(id=17, prompt=(
        "temperate forest camouflage pattern, mottled deep green olive and "
        "brown blotches, military fabric texture")),
    "storm_grey_camo": dict(id=18, prompt=(
        "storm cloud camouflage pattern, mottled slate grey and charcoal "
        "blotches, military fabric texture")),
    "autumn_camo": dict(id=19, prompt=(
        "autumn leaf camouflage pattern, mottled rust orange amber and "
        "brown blotches, military fabric texture")),
    "night_camo": dict(id=20, prompt=(
        "night ops camouflage pattern, mottled near-black and dark navy "
        "blotches, matte military fabric texture")),

    # --- Metallic pack (9, premium) -----------------------------------------
    "chrome_shine": dict(id=21, prompt=(
        "polished chrome metal texture, mirror-like reflections, cool blue "
        "white highlights")),
    "gold_leaf": dict(id=22, prompt=(
        "hammered gold leaf texture, warm yellow gold with soft highlight "
        "variation, luxurious metallic finish")),
    "copper_patina": dict(id=23, prompt=(
        "aged copper metal texture with green-blue patina streaks, weathered "
        "verdigris finish")),
    "gunmetal": dict(id=24, prompt=(
        "dark gunmetal steel texture, subtle blue-grey sheen, brushed metal "
        "finish")),
    "holo_shift": dict(id=25, prompt=(
        "holographic iridescent metal texture, shifting rainbow sheen over "
        "silver base, pearlescent finish")),
    "rose_gold": dict(id=26, prompt=(
        "rose gold metal texture, warm pink-copper sheen, polished "
        "metallic finish")),
    "obsidian_gloss": dict(id=27, prompt=(
        "glossy black obsidian texture, deep reflective black with subtle "
        "purple sheen")),
    "circuit_board": dict(id=28, prompt=(
        "green circuit board texture, fine copper traces and tiny gold "
        "pads, dense electronic pattern")),
    "carbon_weave": dict(id=29, prompt=(
        "carbon fiber weave texture, fine black diagonal criss-cross "
        "pattern, glossy clear coat")),

    # --- Cosmic pack (9, premium) -------------------------------------------
    "galaxy_nebula": dict(id=30, prompt=(
        "deep space nebula texture, swirling purple blue and pink cosmic "
        "clouds with tiny stars")),
    "aurora_borealis": dict(id=31, prompt=(
        "aurora borealis texture, flowing ribbons of green teal and violet "
        "light against dark sky")),
    "lava_flow": dict(id=32, prompt=(
        "molten lava texture, glowing orange-red cracks through black "
        "basalt rock")),
    "deep_ocean": dict(id=33, prompt=(
        "deep ocean water texture, rich teal and navy currents with subtle "
        "caustic light patterns")),
    "lightning_bolt": dict(id=34, prompt=(
        "electric lightning bolt pattern, bright cyan-white jagged bolts on "
        "a dark stormy blue background")),
    "toxic_glow": dict(id=35, prompt=(
        "toxic radioactive glow texture, mottled acid green and lime with "
        "faint glowing cracks")),
    "crystal_ice": dict(id=36, prompt=(
        "crystal ice texture, faceted pale blue-white crystalline shards "
        "with sharp highlights")),
    "dragon_scale": dict(id=37, prompt=(
        "dragon scale texture, overlapping emerald green scales with dark "
        "outlines and glossy highlights")),
    "phoenix_flame": dict(id=38, prompt=(
        "phoenix flame texture, licking flames in red orange and gold, "
        "glowing ember highlights")),

    # --- Homage pack (11, premium) — mood/palette only, no borrowed names,
    # characters, logos or signature marks (same rule as tools/ads/PROMPTS.md
    # and tools/backgrounds/themes.py's franchise arenas). --------------------
    "gotham_night": dict(id=39, prompt=(
        "dark gothic metal texture, deep charcoal black with subtle bat-wing "
        "motif etching, moody noir finish")),
    "dream_pink": dict(id=40, prompt=(
        "glossy candy pink plastic texture, bright toy-like sheen, "
        "playful dreamhouse finish")),
    "hero_comic": dict(id=41, prompt=(
        "comic book halftone dot texture, bold red and blue with black ink "
        "outlines, pow-action energy pattern")),
    "galaxy_saber": dict(id=42, prompt=(
        "matte grey spacecraft hull panel texture, fine seam lines, subtle "
        "glowing blue energy conduit accents")),
    "koala_pastel": dict(id=43, prompt=(
        "soft pastel blue and cream texture, playful cartoon cloud pattern, "
        "gentle rounded shapes")),
    "sponge_yellow": dict(id=44, prompt=(
        "bright cheerful yellow textured pattern, small square pores like a "
        "sea sponge, cartoon underwater finish")),
    "villain_purple": dict(id=45, prompt=(
        "deep sinister purple and green texture, sharp jagged edge motif, "
        "cartoon supervillain finish")),
    "brick_hero": dict(id=46, prompt=(
        "glossy plastic brick-stud texture, primary red and yellow blocks, "
        "toy construction finish")),
    "arcade_pixel": dict(id=47, prompt=(
        "chunky 8-bit pixel art texture, bold magenta and cyan blocky "
        "pattern, retro arcade cabinet finish")),
    "retro_wave": dict(id=48, prompt=(
        "synthwave gradient texture, hot pink to purple to cyan, thin "
        "horizontal neon grid lines")),
    "tiger_stripe": dict(id=49, prompt=(
        "bold tiger stripe pattern, orange and black jagged stripes, "
        "glossy cartoon finish")),
}

DEFAULT_SEED = 4200


def prompt_for(key):
    spec = SKINS[key]
    if spec["prompt"] is None:
        return None
    return f"{spec['prompt']}, {FRAME}"
