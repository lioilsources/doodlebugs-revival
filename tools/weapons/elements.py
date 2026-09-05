"""Element catalogue for projectile art and sound.

Mirrors Assets/Doodlebugs/Scripts/Weapons/ProjectileElement.cs exactly - same
ids, same "key" strings (used as the sprite/sfx FOLDER name), same tints. The
ids travel over the network on every bullet spawn, so they are frozen once
shipped, same rule as WeaponType. Keep the two files in sync by hand; they are
small and rarely change together.

Three flavour fields drive the three pipelines:

  material  what the projectile is MADE OF - goes into the FLUX prompt in
            generate_projectiles.py, after the form's shape phrase
  burst     what the impact/explosion looks like - the FLUX contact-sheet
            prompt in generate_effects.py, and the motif the procedural
            fallback draws
  sfx       the words ElevenLabs gets in generate_sfx.py

`tint` is the fallback colour Bullet.ApplyVisual() uses when an element sprite
is missing and the HUD badge colour; `ramp` is the <= 6 shade palette the
procedural flipbooks draw with (core -> hot -> mid -> cool -> dark), derived
from the tint so a procedural burst and a rendered projectile read as the same
material. Everything stays under the 16-colour ceiling gate.py enforces.

Batch one (plan 24, D2) is metal, fire, lightning, venom; plasma and air are
batch two. BATCH1 lists the first four so `--keys batch1` works everywhere.
"""

# ---------------------------------------------------------------- the six --
ELEMENTS = {
    "metal": dict(
        id=0, name="Metal",
        tint=(198, 178, 120),
        material=(
            "made of polished brass and gunmetal steel, warm yellow metal with "
            "grey steel highlights and a riveted seam"),
        burst="a burst of grey smoke with bright yellow-white shrapnel sparks flying out",
        sfx="dry mechanical brass gunmetal, a metallic clank and a spray of sparks",
        motif="sparks"),
    "fire": dict(
        id=1, name="Fire",
        tint=(255, 138, 46),
        material=(
            "made of burning fire, a molten orange core with yellow-white heat "
            "at the front and dark red flame licking off the back"),
        burst="a bloom of orange flame with rising embers and black smoke",
        sfx="a whooshing gout of flame, roaring fire and crackling embers",
        motif="embers"),
    "lightning": dict(
        id=2, name="Lightning",
        tint=(150, 210, 255),
        material=(
            "made of crackling electricity, a white-hot core wrapped in pale "
            "blue arcs and small forked sparks"),
        burst="a forked arc flash, white-blue lightning branching outward in all directions",
        sfx="a sharp electric zap, crackling arc discharge and a static snap",
        motif="forks"),
    "venom": dict(
        id=3, name="Venom",
        tint=(120, 230, 90),
        material=(
            "made of thick acid venom, a glossy toxic green blob with a pale "
            "lime highlight and dark green drips"),
        burst="a splattering green acid splash with flying droplets and hissing vapour",
        sfx="a wet acidic splat, bubbling hiss and dripping slime",
        motif="droplets"),
    "plasma": dict(
        id=4, name="Plasma",
        tint=(235, 110, 255),
        material=(
            "made of glowing plasma energy, a white core inside a magenta glow "
            "with a clean hard-edged outline and no smoke"),
        burst="a clean expanding energy ring, white core fading to magenta, no smoke",
        sfx="a synthetic energy pulse, a clean sci-fi zap with a short digital tail",
        motif="ring"),
    "air": dict(
        id=5, name="Air",
        tint=(225, 240, 255),
        material=(
            "made of folded white paper and pale feathers, off-white with soft "
            "grey-blue shading and a crisp folded edge"),
        burst="a swirling gust ring of pale dust with white feathers scattering outward",
        sfx="a sharp air whoosh, a puff of wind and fluttering paper",
        motif="feathers"),
}

BATCH1 = ("metal", "fire", "lightning", "venom")

# Shape -> element, mirroring PlaneModelCatalog's fourth constructor arg. Kept
# here so `sheet` can say WHO shoots this and so a shape added on the C# side
# without an element entry shows up as a diff in review. Everything absent is
# metal (PlaneModelDef.Element defaults to Metal - nothing is ever missing).
SHAPES = {
    "fire": ("dragon",),
    "lightning": ("unicorn",),
    "venom": ("wasp",),
    "plasma": ("rocket", "starfighter", "shuttle", "interceptor", "saucer",
               "stealth", "hover_pod"),
    "air": ("goose", "ornithopter", "paper_plane", "delta_glider"),
}

# The prompt block every projectile and every contact sheet ends with. One
# string, one style: plan 24 risk "style drift between elements" is answered by
# never varying this, only the material in front of it.
STYLE = (
    "flat pixel-art game sprite, side view facing right, hard edges, "
    "crisp clean pixel edges, bold saturated colours, no text, no background, "
    "no drop shadow, plain pure white background, centred, filling the frame, "
    "in the style of a 1990s 16-bit arcade game")

# The same block for the effect contact sheets, minus the facing clause (a
# burst has no nose) and plus the panel discipline the slicer depends on.
STYLE_FX = (
    "flat pixel-art game effect sprite, hard edges, crisp clean pixel edges, "
    "bold saturated colours, no text, no background, no drop shadow, "
    "plain pure white background, in the style of a 1990s 16-bit arcade game")

# NAG negatives (flux_graph wires these through NAGuidance - FLUX has no real
# CFG negative at guidance 1.0).
NEG = (
    "text, letters, numbers, watermark, logo, signature, caption, "
    "multiple objects, duplicates, grid, contact sheet, frame, border, "
    "background scenery, landscape, sky, clouds, ground, horizon, table, "
    "hand, person, shadow, ground shadow, drop shadow, reflection, "
    "blurry, soft focus, gradient background, photograph, 3d render, realistic")

# The tail every ElevenLabs prompt gets. The eight clips already in
# Resources/Sfx are procedurally generated 8-bit blips; a cinematic whoosh next
# to them sounds like a different game (plan 24, D3).
SFX_STYLE = (
    "short retro arcade game sound effect, 8-bit chiptune style, "
    "no music, no reverb tail, dry, mono")

DEFAULT_SEED = 4200


# ------------------------------------------------------------- accessors --
def keys(arg=None):
    """Resolve a --elements argument: None = all six, "batch1" = plan D2's
    first four, otherwise a comma list."""
    import sys
    if not arg:
        return list(ELEMENTS)
    if arg == "batch1":
        return list(BATCH1)
    names = arg.split(",")
    unknown = [n for n in names if n not in ELEMENTS]
    if unknown:
        sys.exit(f"unknown element(s) {unknown}; have {list(ELEMENTS)}")
    return names


def get(key):
    return ELEMENTS[key]


def by_id(eid):
    return next(k for k, v in ELEMENTS.items() if v["id"] == eid)


def tint(key):
    return tuple(ELEMENTS[key]["tint"])


def hex_tint(key):
    return "#%02x%02x%02x" % tint(key)


def _mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def ramp(key, n=6):
    """n shades from a near-white core through the tint to a dark edge, as a
    list of RGB tuples. Deterministic, and small enough that a whole flipbook
    drawn from one ramp never trips the <= 16 colour gate. Duplicates are
    removed rather than merged so the palette count is honest."""
    c = tint(key)
    stops = [_mix(c, (255, 255, 255), 0.80),   # core flash
             _mix(c, (255, 255, 255), 0.45),   # hot
             c,                                # the element itself
             _mix(c, (0, 0, 0), 0.25),         # cool
             _mix(c, (0, 0, 0), 0.50),         # dark
             _mix(c, (0, 0, 0), 0.72)]         # edge / soot
    out = []
    for s in stops[:n]:
        if s not in out:
            out.append(s)
    return out


def prompt_material(key):
    return ELEMENTS[key]["material"]


def prompt_burst(key):
    return ELEMENTS[key]["burst"]


def sfx_flavour(key):
    return ELEMENTS[key]["sfx"]


def seed_for(key, i):
    """One seed lane per element so adding seeds to one never shifts another."""
    return DEFAULT_SEED + ELEMENTS[key]["id"] * 100 + i


if __name__ == "__main__":
    print(f"{'key':10s} {'id':>2s}  {'tint':16s} ramp")
    for k, v in ELEMENTS.items():
        shades = " ".join("%02x%02x%02x" % s for s in ramp(k))
        print(f"{k:10s} {v['id']:>2d}  {str(v['tint']):16s} {shades}")
