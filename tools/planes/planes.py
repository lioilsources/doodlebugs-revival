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
    "flat pixel-art game sprite of a {concept}, side view, facing right with "
    "the nose pointing right, bright red livery with a grey engine cowling, a "
    "visible pilot in an open cockpit, crisp clean edges, no outline, centred "
    "on a plain pure white background, one single aircraft, nothing else, no "
    "text, in the style of a 1990s 16-bit arcade game"
)

NEG = (
    "text, letters, watermark, logo, signature, two aircraft, multiple "
    "aircraft, formation, background scenery, landscape, sky, clouds, ground, "
    "horizon, shadow, ground shadow, drop shadow, reflection, frame, border, "
    "blurry, photograph, 3d render, realistic"
)

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
}

DEFAULT_SEED = 7000


def prompt_for(key, mode="kontext"):
    spec = CONCEPTS[key]
    if spec["concept"] is None:
        return None
    frame = TXT_FRAME if mode == "txt2img" else FRAME
    return frame.format(concept=spec["concept"])


def seed_for(key, i):
    """One seed lane per concept so adding seeds to one never shifts another."""
    return DEFAULT_SEED + CONCEPTS[key]["id"] * 10 + i
