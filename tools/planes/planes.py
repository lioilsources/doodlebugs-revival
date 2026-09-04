"""Concept catalogue for the plane models (silhouettes).

Mirrors Assets/Doodlebugs/Scripts/Skins/PlaneModelCatalog.cs exactly - same
ids, same "key" strings (used as the sprite filename stem). Keep the two in
sync by hand; they're small and rarely change together.

Every model is a FLUX Kontext redesign of BiPlane1.png: Kontext keeps the
reference's position, scale, facing and colour scheme while changing what
the thing is, which is precisely the envelope the shared hitbox needs (see
Prompts/23-CLAUDE-PLAN-plane-shapes.md). The prompt therefore only names the
concept; FRAME carries the invariants and the post-process in
generate_planes.py enforces them numerically (gate.py).

The red livery is deliberate: post-processing detects it (gate.is_livery_red)
to split each model into paint / tail accent / fixed masks exactly like
tools/skins does for the original, so the 50 skins composite onto every
model with no per-model mask painting.
"""

FRAME = (
    "Redesign this side-view WWI biplane as a {concept}. Keep exactly the same "
    "overall size, the same position in the frame and the same facing "
    "direction with the nose pointing right. Keep the same bright red livery "
    "with a grey engine cowling, a visible pilot in an open cockpit, and the "
    "same flat pixel-art game sprite style with crisp clean edges and no "
    "outline. Plain pure white background, one single aircraft, nothing else, "
    "no text."
)

# Fallback from plan section 8: Kontext homogenises concepts whose difference
# from the reference is structural-but-subtle (wing count, canard, twin boom,
# gull wing all came back as biplanes on 2026-09-03) while it nails concepts
# with a strong silhouette (racer, flying boat). txt2img has no reference to
# cling to; the post-process normalisation supplies the envelope instead.
TXT_FRAME = (
    "flat pixel-art game sprite of a {concept}, strict side view profile, "
    "facing right with the front pointing right, roughly twice as wide as it "
    "is tall, filling the frame, mostly bright red paintwork with grey metal "
    "fittings, crisp clean edges, no outline, centred on a plain pure white "
    "background, one single craft, nothing else, no text, in the style of a "
    "1990s 16-bit arcade game"
)
# "twice as wide as tall" is aimed straight at G1/G2. Without it a strict side
# view of a monoplane came out 110x16..41 - a correct drawing, but the shared
# 50x50 hitbox would then be mostly air, which is exactly the unfairness the
# gate exists to catch. The other half of the batch had the opposite problem
# (galleon, balloon, lander drawn as tall 110x122 compositions).
# The red is not decoration: G7 wants >=35 % of the body red, and the paint
# mask that every skin composites into is derived from exactly those pixels.
# The pilot used to be mandatory here and in FRAME - dropped, because it means
# nothing to a galleon or a balloon and it was what dragged Pony into drawing
# furry characters. Grey "fittings" stay: they become the fixed mask.

NEG = (
    "text, letters, watermark, logo, signature, two aircraft, multiple "
    "aircraft, formation, background scenery, landscape, sky, clouds, ground, "
    "horizon, shadow, ground shadow, drop shadow, reflection, frame, border, "
    "blurry, photograph, 3d render, realistic"
)

# Pony Diffusion V6 XL + the Spacecraft LoRA, kept for the record: it does not
# work for this job. Pony reads danbooru tags rather than prose, so the first
# pass (sentences, "visible pilot") produced painterly 3/4 concept art on warm
# grey with furry pilots. Tags and a creature negative fixed the subject, but
# the 3/4 framing is the base model's own habit: 1 of 18 seeds cleared the
# envelope gate against FLUX's near-100 %. Use --mode txt2img instead.
PONY_FRAME = (
    "score_9, score_8_up, score_7_up, {concept}, mecha, science fiction, "
    "vehicle focus, no humans, from side, side view, facing right, "
    "simple background, white background, flat color, thick outlines, "
    "centered, full body"
)
PONY_NEG = (
    "score_6, score_5, score_4, furry, anthro, animal ears, tail, fur, "
    "dragon, creature, monster, 1girl, 1boy, human, people, face, portrait, "
    "grey background, gradient background, scenery, landscape, stars, "
    "shadow, blurry, sketch, painterly, realistic, 3d, text, watermark, "
    "signature, multiple views"
)
PONY_CKPT = "ponyDiffusionV6XL_v6StartWithThisOne.safetensors"
PONY_LORA = "Spacecraft.safetensors"

# id 0 is BiPlane1 itself - never rendered.
CONCEPTS = {
    "biplane": dict(id=0, name="Doodlebug", concept=None),
    # Seeds 7010/7011 (guidance 3.5) came back as biplanes - the wing COUNT is
    # too subtle for Kontext next to the biplane reference; spell it out.
    "triplane": dict(id=1, name="Triplane", concept=(
        "Fokker Dr.I style TRIPLANE with THREE separate stacked wings, one "
        "above the other (top wing, middle wing, bottom wing), short struts "
        "between all three decks, and a rotary engine")),
    "racer": dict(id=2, name="Racer", concept=(
        "sleek 1930s monoplane air racer with a single low wing, wheel spats and "
        "a pointed spinner")),
    "flying_boat": dict(id=3, name="Flying Boat", concept=(
        "flying boat with a boat-shaped hull, small wing floats and a pusher "
        "engine mounted above the wing")),
    "canard": dict(id=4, name="Canard", concept=(
        "canard aircraft with a small front wing at the nose and a pusher "
        "propeller at the back")),
    "twin_boom": dict(id=5, name="Twin Boom", concept=(
        "twin-boom aircraft with a short central pod and two tail booms joined by "
        "a horizontal stabiliser")),
    "gull_wing": dict(id=6, name="Gull Wing", concept=(
        "gull-wing monoplane with inverted gull wings bent like a seagull")),
    "barnstormer": dict(id=7, name="Barnstormer", concept=(
        "stubby short barnstormer biplane with a fat round fuselage and stubby "
        "wings")),
    "rocket": dict(id=8, name="Rocket", concept=(
        "biplane with a big rocket booster strapped under the fuselage and flames "
        "shooting out the back")),
    "gyrocopter": dict(id=9, name="Gyrocopter", concept=(
        "gyrocopter autogyro with a large free-spinning rotor on a mast on top and "
        "a small pusher engine")),
    "ornithopter": dict(id=10, name="Ornithopter", concept=(
        "ornithopter with feathered flapping bird wings and a fan-shaped bird tail")),
    "paper_plane": dict(id=11, name="Paper Plane", concept=(
        "folded red paper plane dart with a tiny pilot sitting on top of it")),
    "bathtub": dict(id=12, name="Bathtub", concept=(
        "red bathtub with biplane wings bolted on, a propeller on the front and a "
        "pilot sitting inside")),
    "crop_duster": dict(id=13, name="Crop Duster", concept=(
        "crop duster monoplane with a fat radial engine, a low wing and spray "
        "booms under the wing")),
    "delta_glider": dict(id=14, name="Delta Glider", concept=(
        "delta-wing glider with one big triangular wing, a fin and no propeller")),
    "zeppelin": dict(id=15, name="Zeppelin", concept=(
        "small zeppelin airship with a cigar-shaped red envelope, a gondola "
        "underneath with the pilot and a rear propeller")),

    # --- Space (16-21) -------------------------------------------------------
    # No WWI counterpart for Kontext to hold onto, so these are --mode txt2img:
    # the reference would only drag them back towards a biplane.
    "starfighter": dict(id=16, name="Starfighter", concept=(
        "sleek space starfighter with swept delta wings and glowing engine "
        "nozzles at the back")),
    "shuttle": dict(id=17, name="Shuttle", concept=(
        "small space shuttle orbiter with stubby delta wings, a rounded nose "
        "and a tall tail fin")),
    "interceptor": dict(id=18, name="Interceptor", concept=(
        "needle-nosed space interceptor with a long pointed nose, a bubble "
        "canopy and thruster pods on the sides")),
    "saucer": dict(id=19, name="Saucer", concept=(
        "flying saucer with a wide flat disc hull and a glass bubble cockpit "
        "dome on top")),
    "lander": dict(id=20, name="Lander", concept=(
        "boxy lunar lander spacecraft with landing legs, a descent thruster "
        "underneath and a small viewport")),
    "gunship": dict(id=21, name="Gunship", concept=(
        "armored space gunship with a heavy blocky hull, gun pods under the "
        "wings and twin rear engines")),

    # --- Ocean (22-24) -------------------------------------------------------
    "galleon": dict(id=22, name="Galleon", concept=(
        "flying pirate galleon, a wooden sailing ship hull with a bowsprit at "
        "the front, two masts with billowing sails and small wings on the sides")),
    "manta": dict(id=23, name="Manta", concept=(
        "manta ray shaped glider, one wide flat triangular wing body with "
        "swept wingtips and a long thin whip tail")),
    "seaplane": dict(id=24, name="Seaplane", concept=(
        "floatplane seaplane, a monoplane standing on two long pontoon floats "
        "under the fuselage instead of wheels")),

    # --- History (25-27) -----------------------------------------------------
    "wright_flyer": dict(id=25, name="Wright Flyer", concept=(
        "1903 Wright Flyer style box-kite aeroplane, two thin wooden wings held "
        "apart by many vertical struts, a small elevator sticking out in front "
        "on booms, landing skids instead of wheels")),
    "aerial_screw": dict(id=26, name="Aerial Screw", concept=(
        "da Vinci aerial screw flying machine, a wooden platform with a large "
        "helical corkscrew canvas rotor mounted above it")),
    "balloon": dict(id=27, name="Balloon", concept=(
        "hot air balloon, a round striped envelope with a wicker basket hanging "
        "underneath and a small propeller on the back of the basket")),

    # --- Future (28-30) ------------------------------------------------------
    "stealth": dict(id=28, name="Stealth", concept=(
        "angular stealth flying wing, one sharp faceted arrowhead wing with no "
        "fuselage and no tail, flat angular panels")),
    "hover_pod": dict(id=29, name="Hover Pod", concept=(
        "futuristic hovering pod with no wings at all, a smooth egg-shaped "
        "capsule with a glass canopy and glowing antigravity thrusters below")),
    "tiltrotor": dict(id=30, name="Tiltrotor", concept=(
        "VTOL tiltrotor aircraft with a stubby fuselage and two big rotors on "
        "nacelles tilted upward at the wingtips")),

    # --- WWI / WWII (31-33) --------------------------------------------------
    "gotha_bomber": dict(id=31, name="Gotha", concept=(
        "large WWI twin-engine biplane bomber with very long wings, two engines "
        "mounted between the wings and a long slab-sided fuselage")),
    "elliptical_fighter": dict(id=32, name="Spitfire", concept=(
        "WWII monoplane fighter with distinctive elliptical rounded wings, a "
        "long nose with an inline engine and a bubble canopy")),
    "heavy_bomber": dict(id=33, name="Fortress", concept=(
        "WWII four-engine heavy bomber with a very wide straight wing, four "
        "propeller engines along it, a glazed nose and a tall tail fin")),

    # --- Wildcards (34-35) ---------------------------------------------------
    "dragonfly": dict(id=34, name="Dragonfly", concept=(
        "mechanical dragonfly aircraft with a long slender segmented body and "
        "four narrow translucent insect wings, a big round compound-eye canopy")),
    "flying_car": dict(id=35, name="Flying Car", concept=(
        "1950s retro-futuristic flying car, a finned automobile body with "
        "whitewall wheels, small wings on the sides and a jet exhaust")),

    # --- Creatures (36-41) ---------------------------------------------------
    # The red livery is not negotiable even here: G7 wants >=35 % of the body
    # red because the paint mask every skin composites into is derived from
    # exactly those pixels. A scarlet wasp is odd zoology and fine arcade art.
    # Wings are the risk - they are gaps, and gaps in the middle 50x30 box are
    # what G1 rejects - so every prompt asks for a long body down the centre.
    # Seeds 7360-7362 came back at fill 0.36 against a 0.42 floor: spread bat
    # wings put a big empty bbox around a thin animal. Folded wings keep the
    # mass inside the silhouette.
    "dragon": dict(id=36, name="Dragon", concept=(
        "flying dragon with a thick heavy red scaly body stretched "
        "horizontally, leathery wings FOLDED CLOSE against its flanks rather "
        "than spread, a horned head at the front, a thick muscular tail "
        "behind, stocky and compact")),
    "unicorn": dict(id=37, name="Unicorn", concept=(
        "flying unicorn with a long horse body stretched horizontally, "
        "feathered wings swept back along its flanks, a spiral horn on its "
        "forehead and a flowing mane and tail")),
    "wasp": dict(id=38, name="Wasp", concept=(
        "giant wasp with a long segmented red and black striped body, narrow "
        "transparent wings held back along the body, antennae at the front "
        "and a pointed sting at the rear")),
    # Seeds 7390-7392 were 110x86 against a 72 ceiling - the only gate they
    # missed, with ideal mass otherwise. A fly drawn with its wings up is
    # tall; flat along the back is what fits.
    "fly": dict(id=39, name="Fly", concept=(
        "giant housefly seen from the side, long low fat red body, huge round "
        "compound eyes at the front, short transparent wings lying FLAT along "
        "its back rather than raised, tiny stubby legs tucked underneath, "
        "wide and low rather than tall")),
    "eagle": dict(id=40, name="Eagle", concept=(
        "large bird of prey gliding, long red body stretched horizontally, "
        "broad wings swept back, a hooked beak at the front and a fanned tail "
        "at the rear")),
    "goose": dict(id=41, name="Goose", concept=(
        "goose in level flight with a long stretched red body and outstretched "
        "neck at the front, wings swept back along its flanks, webbed feet "
        "tucked up underneath")),
}

DEFAULT_SEED = 7000


def prompt_for(key, mode="kontext"):
    spec = CONCEPTS[key]
    if spec["concept"] is None:
        return None
    frame = {"txt2img": TXT_FRAME, "pony": PONY_FRAME}.get(mode, FRAME)
    return frame.format(concept=spec["concept"])


def negative_for(mode):
    return PONY_NEG if mode == "pony" else NEG


def seed_for(key, i):
    """One seed lane per concept so adding seeds to one never shifts another."""
    return DEFAULT_SEED + CONCEPTS[key]["id"] * 10 + i
