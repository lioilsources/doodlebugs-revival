"""The six projectile forms - what a weapon looks like, before the element
decides what it is made of.

Eight weapons collapse to six forms (plan 24 section 2). A form owns the
canvas, the facing contract and the shape half of the FLUX prompt; an element
owns the material half and the tint. 6 x 6 = 36 sprites, 24 in batch one.

Mirrors the WeaponType -> form mapping in Assets/Doodlebugs/Scripts/Weapons/ -
`weapons` here is the display name of each WeaponProfile that resolves to this
form, and `group` is the SFX bucket (plan D6: shoot clips are per form GROUP,
not per form; per-form shoot would be 48 clips for a difference nobody hears
under Bullet's +-8 % pitch jitter).

The canvases are small on purpose: the game draws these at ProjectileScale on
a 54-world-unit-wide camera, and a 1024 px FLUX painting shrunk to 32x16 is a
smear unless the prompt asks for a big centred subject and the quantiser runs
AFTER the downscale (plan 24, risk 1). gate.py is what actually holds the line.
"""

FORMS = {
    "tracer": dict(
        canvas=(32, 16), facing="right", group="gun",
        weapons=("MG", "Twin MG"),
        shape=("a single long thin pointed bullet tracer round, a slim "
               "horizontal dart with a sharp tip at the right and a blunt "
               "flat tail at the left, four times as wide as it is tall"),
        # Many on screen at once; two or three colours is all that survives.
        note="keep it 2-3 colours simple - dozens are alive at once"),
    "pellet": dict(
        canvas=(16, 16), facing="any", group="gun",
        weapons=("Flak", "Heavy Flak"),
        shape=("a single small round ball pellet, a perfect circle with a "
               "highlight on the upper left, filling the frame"),
        note="5-7 per shot; must read as one dot at 16 px"),
    "bomb": dict(
        canvas=(48, 24), facing="right", group="heavy",
        weapons=("Aero Bomb",),
        # Pivot 40 % back from the nose: Bullet tumbles the bomb about its
        # fuse, not its middle, so the sprite has to rotate around the fat end.
        pivot=(0.60, 0.5),
        shape=("a single aerial bomb seen from the side, a fat teardrop body "
               "with a rounded nose pointing right and four small fins at the "
               "tail on the left, twice as wide as it is tall"),
        note="replaces bomb_littleboy per element; metal keeps Little Boy"),
    "bolt": dict(
        canvas=(48, 12), facing="right", group="gun",
        weapons=("Sniper",),
        shape=("a single long thin needle bolt, a very slim horizontal spike "
               "with a bright glowing core running down its length, a sharp "
               "point at the right and a tapering trail at the left, six "
               "times as wide as it is tall"),
        note="fastest thing on screen - needs a bright core to read at all"),
    "rocket": dict(
        canvas=(48, 20), facing="right", group="heavy",
        weapons=("Rocket",),
        shape=("a single small rocket missile seen from the side, a cylinder "
               "body with a pointed nose cone at the right, two small fins at "
               "the left and a short exhaust flare behind it"),
        note="body + exhaust; the runtime trail does the rest"),
    "mine": dict(
        canvas=(32, 32), facing="any", group="heavy",
        weapons=("Mine",),
        shape=("a single round naval sea mine, a dark sphere covered in short "
               "blunt spikes sticking out all around it, filling the frame"),
        note="renders below the clouds and must hide in them - dark palette"),
}

GROUPS = ("gun", "heavy")


def keys(arg=None):
    """Resolve a --forms argument: None = all six, otherwise a comma list."""
    import sys
    if not arg:
        return list(FORMS)
    names = arg.split(",")
    unknown = [n for n in names if n not in FORMS]
    if unknown:
        sys.exit(f"unknown form(s) {unknown}; have {list(FORMS)}")
    return names


def get(key):
    return FORMS[key]


def canvas(key):
    return tuple(FORMS[key]["canvas"])


def facing(key):
    return FORMS[key]["facing"]


def group(key):
    return FORMS[key]["group"]


def pivot(key):
    """(x, y) in 0..1 sprite space, or None for the centred default. Only the
    bomb differs; unity_meta.write_meta(..., pivot=None) writes exactly the
    meta every other generated sprite in this repo already ships."""
    return FORMS[key].get("pivot")


def forms_in_group(g):
    return [k for k, v in FORMS.items() if v["group"] == g]


def prompt_shape(key):
    return FORMS[key]["shape"]


if __name__ == "__main__":
    print(f"{'form':8s} {'canvas':9s} {'facing':6s} {'group':6s} weapons")
    for k, v in FORMS.items():
        c = "%dx%d" % tuple(v["canvas"])
        print(f"{k:8s} {c:9s} {v['facing']:6s} {v['group']:6s} {', '.join(v['weapons'])}")
