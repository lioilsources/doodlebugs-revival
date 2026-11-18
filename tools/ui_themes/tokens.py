#!/usr/bin/env python3
"""Design tokens of the UI themes (Prompts/31-CLAUDE-PLAN-ui-themes.md).

One entry per theme: palette, type, shape and the style switches the kit
needs. This file is the offline mirror of `Scripts/UI/Kit/UiThemeCatalog.cs`
- ids and keys must match the C# side, exactly like skins.py / planes.py /
elements.py do for their catalogs. It exists for two reasons:

  1. Unity cannot run on the dev Mac, so the legibility gate lives here:
     `python3 tools/ui_themes/tokens.py` computes WCAG contrast for every
     text/surface pair of every theme and exits 1 on a failure.
  2. It is the single place a palette is read from when the art pipeline
     composes 9-slice frames and procedural wordmarks.

The numbers are the ones drawn in `Prompts/31-ui-themes-mockup.html` (one
block of CSS custom properties per theme, same names with dashes).

Colours are "#RRGGBB" or ("#RRGGBB", alpha). A surface may be a list of
stops (a gradient): text on it must pass against every stop.

Ids are append-only and stable once shipped (they are persisted in
PlayerPrefs / PlayerProfile and sent in telemetry).
"""
import sys

# Sky luminance extremes of the shipped arenas (top half, where the title
# sits): Orbit is the darkest, MistyPeaks the brightest.
BACKDROP_DARK = "#0D1220"
BACKDROP_BRIGHT = "#EEF2F5"

SIZES = dict(title=30, card=25, tab=17, label=13, body=12, slot=17, cta=30, name=26)


def theme(**kw):
    kw["sizes"] = {**SIZES, **kw.get("sizes", {})}
    return kw


THEMES = {
    # ---------------------------------------------------------------- default
    "skyposter": theme(
        id=0, name="SKY POSTER", group="default",
        ink="#0D1730", paper="#F2F6FB", paper_dim="#D5DEE9", text="#0D1730", text_dim="#33425E",
        frame="#F2F6FB", frame_text="#0D1730",
        on_backdrop="#FFFFFF", backdrop_shadow="#0D1730",
        weapon="#FF8A1F", gadget="#22C7DB", special="#E0489A", highlight="#FFD23F", danger="#FF5A4A",
        band=["#FF8A1F", "#22C7DB"], band_text="#0D1730",
        confirm="#3DDC68", on_confirm="#0D1730",
        tab_on="#F2F6FB", tab_on_text="#0D1730",
        glass=("#0D1730", .74), glass_light=("#FFFFFF", .16),
        veil_top=("#0D1730", .30), veil_bottom=("#0D1730", .92),
        fonts=dict(display="BarlowCondensed-ExtraBoldItalic", label="BarlowCondensed-Bold",
                   body="Barlow-Medium", strong="Barlow-SemiBold"),
        tracking=dict(display=.01, label=.08), case="upper",
        shape=dict(panel="rounded", radius_card=14, radius_button=12, radius_cta=16, outline=3,
                   shadow=("hard", 0, 6, "#0D1730"), select=dict(lift=8, tilt=-1.2, bob=3, scale=1)),
        bar="solid", badge="disc", backdrop_filter="none", overlay="none",
    ),
    # ------------------------------------------------------------------ 1970s
    "phosphor": theme(
        id=1, name="PHOSPHOR", group="1970s",
        ink="#63FF9A", paper="#000A04", paper_dim="#0F2A19", text="#63FF9A", text_dim="#4BC074",
        frame="#000A04", frame_text="#63FF9A",
        on_backdrop="#63FF9A", backdrop_shadow=None,
        weapon="#63FF9A", gadget="#63FF9A", special="#FFC857", highlight="#FFC857", danger="#FF6B5A",
        band=["#000A04"], band_text="#63FF9A",          # outline band; selected = amber fill, black text
        confirm="#FFC857", on_confirm="#031006",
        tab_on="#63FF9A", tab_on_text="#031006",
        glass=("#000A04", .0), glass_light=("#000A04", .0),
        veil_top=("#000803", .50), veil_bottom=("#000803", .84),
        fonts=dict(display="MajorMonoDisplay-Regular", label="IBMPlexMono-Regular",
                   body="IBMPlexMono-Regular", strong="IBMPlexMono-Medium"),
        sizes=dict(title=28, card=20, tab=14, name=22, cta=24, slot=13, label=12),
        tracking=dict(display=0, label=.03), case="lower-display",
        shape=dict(panel="outline", radius_card=0, radius_button=0, radius_cta=0, outline=2,
                   shadow=("glow", 0, 0, "#63FF9A"), select=dict(lift=4, tilt=0, bob=0, scale=1)),
        bar="line", badge="ring", backdrop_filter="mono:#63FF9A", overlay="scanlines",
    ),
    # ------------------------------------------------------------------ 1980s
    "coinop": theme(
        id=2, name="COIN-OP", group="1980s",
        ink="#000000", paper="#07061A", paper_dim="#1D1B45", text="#FFFFFF", text_dim="#B9B6E8",
        frame="#07061A", frame_text="#FFFFFF",
        on_backdrop="#FFFFFF", backdrop_shadow="#000000",
        weapon="#FF9A2E", gadget="#2DE2E6", special="#FF3EA5", highlight="#FFE14D", danger="#FF3EA5",
        band=["#2DE2E6", "#FFE14D"], band_text="#07061A",
        confirm="#FFE14D", on_confirm="#07061A",
        tab_on="#FFE14D", tab_on_text="#07061A",
        glass=("#07061A", 1.0), glass_light=("#07061A", .0),
        veil_top=("#07061A", .58), veil_bottom=("#07061A", .90),
        fonts=dict(display="PressStart2P", label="PixelifySans-Bold",
                   body="PixelifySans-Medium", strong="PixelifySans-Bold"),
        sizes=dict(title=20, card=13, tab=16, label=13, body=13, name=14, cta=20, slot=15),
        tracking=dict(display=0, label=0), case="upper",
        shape=dict(panel="square", radius_card=0, radius_button=0, radius_cta=0, outline=3,
                   shadow=("hard", 5, 5, "#000000"), select=dict(lift=7, tilt=0, bob=0, scale=1)),
        bar="blocks", badge="square", backdrop_filter="posterize:5", overlay="scanlines",
    ),
    # ------------------------------------------------------------------ 1990s
    "chrome": theme(
        id=3, name="CHROME", group="1990s",
        ink="#0B0E13", paper="#121820", paper_dim="#0A0F14", text="#E9F0F8", text_dim="#9FB0C4",
        frame=["#E2E7EE", "#AEB6C3", "#8B95A5", "#B9C1CD"], frame_text="#11161D",
        on_backdrop="#FFFFFF", backdrop_shadow="#000000",
        weapon="#FF7A1A", gadget="#19C3D6", special="#E0489A", highlight="#FFD23F", danger="#FF4B3A",
        band=["#E2E7EE", "#AEB6C3", "#8B95A5", "#B9C1CD"], band_text="#11161D",   # engraved on the steel
        confirm=["#8DFF7C", "#2EBB42", "#178A2A", "#46D85C"], on_confirm="#06210B",
        tab_on=["#E2E7EE", "#8B95A5"], tab_on_text="#11161D",
        glass=("#0C1410", 1.0), glass_light=("#0A0D12", .55),
        veil_top=("#08080C", .30), veil_bottom=("#08080C", .88),
        fonts=dict(display="RussoOne-Regular", label="SairaCondensed-Bold",
                   body="SairaCondensed-SemiBold", strong="SairaCondensed-Bold"),
        sizes=dict(title=30, card=18, tab=17, body=13, name=22, cta=26, label=14),
        tracking=dict(display=.04, label=.06), case="upper",
        shape=dict(panel="bevel", radius_card=6, radius_button=5, radius_cta=6, outline=2,
                   shadow=("soft", 0, 8, "#000000"), select=dict(lift=8, tilt=0, bob=2, scale=1)),
        bar="led", badge="plate", backdrop_filter="none", overlay="vignette",
    ),
    # ------------------------------------------------------------------ 2000s
    "aqua": theme(
        id=4, name="AQUA", group="2000s",
        ink="#FFFFFF", paper=["#FFFFFF", "#E0EFFF"], paper_dim="#D8E3F0", text="#12325C", text_dim="#44628C",
        frame=["#FFFFFF", "#E0EFFF"], frame_text="#12325C",
        on_backdrop="#FFFFFF", backdrop_shadow="#0A2C6E",
        weapon="#FF8A1F", gadget="#18B6E8", special="#E0489A", highlight="#FFE45C", danger="#D61F1F",
        band=["#FFCB7A", "#FF9A2E", "#F27800", "#FFAE52"], band_text="#12325C",
        confirm=["#BDF793", "#5FD62E", "#35B80E", "#6FE03C"], on_confirm="#0B3D02",
        tab_on=["#FFFFFF", "#C3DBFA"], tab_on_text="#12325C",
        glass=("#0E2A52", .80), glass_light=("#FFFFFF", .30),
        veil_top=("#4096EB", .30), veil_bottom=("#0A2C6E", .86),
        fonts=dict(display="LilitaOne-Regular", label="Fredoka-SemiBold",
                   body="Fredoka-Medium", strong="Fredoka-SemiBold"),
        sizes=dict(title=32, card=23, name=25, cta=30, slot=15, tab=16),
        tracking=dict(display=.03, label=.03), case="upper",
        shape=dict(panel="gloss", radius_card=16, radius_button=14, radius_cta=27, outline=2,
                   shadow=("soft", 0, 10, "#08286466"), select=dict(lift=8, tilt=0, bob=3, scale=1)),
        bar="tube", badge="orb", backdrop_filter="none", overlay="bokeh",
    ),
    # ------------------------------------------------------------------ 2010s
    "flat": theme(
        id=5, name="FLAT", group="2010s",
        ink="#00796B", paper="#FFFFFF", paper_dim="#E6EEED", text="#212121", text_dim="#6B6B6B",
        frame="#FFFFFF", frame_text="#212121",
        on_backdrop="#FFFFFF", backdrop_shadow=None,
        weapon="#FF7043", gadget="#26C6DA", special="#EC407A", highlight="#FFC107", danger="#D84315",
        band=["#FF7043", "#26C6DA"], band_text="#212121",     # selected band = primary, white text
        confirm="#FF5722", on_confirm="#FFFFFF",
        tab_on="#00796B", tab_on_text="#FFFFFF",              # text tab with an amber underline
        glass=("#00332C", .70), glass_light=("#000000", .0),
        veil_top=("#00796B", .74), veil_bottom=("#004D40", .90),
        fonts=dict(display="Montserrat-ExtraBold", label="Roboto-Medium",
                   body="Roboto-Regular", strong="Roboto-Medium"),
        sizes=dict(title=22, card=18, tab=14, name=21, cta=19, slot=14, label=12),
        tracking=dict(display=.06, label=.06), case="upper",
        shape=dict(panel="rounded", radius_card=3, radius_button=3, radius_cta=27, outline=0,
                   shadow=("soft", 0, 3, "#00000040"), select=dict(lift=7, tilt=0, bob=0, scale=1)),
        bar="hairline", badge="longshadow", backdrop_filter="grayscale", overlay="facets",
    ),
    # ------------------------------------------------------------------ 2020s
    "clay": theme(
        id=6, name="CLAY", group="2020s",
        ink="#A58BFF", paper="#FFF8F1", paper_dim="#EADFF5", text="#3B2A5C", text_dim="#77679A",
        frame="#FFF8F1", frame_text="#3B2A5C",
        on_backdrop="#3B2A5C", backdrop_shadow=None,
        weapon="#FFB38A", gadget="#8FE3C0", special="#F7A6D0", highlight="#B5380F", danger="#B5380F",
        band=["#FFB38A", "#FFE08A"], band_text="#3B2A5C",     # pills inside the card, no full-width band
        confirm=["#A6F0CB", "#6FDCA6"], on_confirm="#1F4A38",
        tab_on="#7C5CE0", tab_on_text="#FFFFFF",
        glass=("#FFFFFF", .86), glass_light=("#FFFFFF", .46),
        veil_top=("#FFF0E6", .56), veil_bottom=("#E8D7FF", .80),
        fonts=dict(display="Baloo2-ExtraBold", label="Baloo2-Bold",
                   body="Baloo2-Medium", strong="Baloo2-Bold"),
        sizes=dict(title=32, card=24, name=26, cta=28, body=13, slot=15, label=12),
        tracking=dict(display=0, label=.02), case="upper",
        shape=dict(panel="clay", radius_card=26, radius_button=20, radius_cta=27, outline=0,
                   shadow=("soft", 0, 14, "#6E46A047"), select=dict(lift=8, tilt=0, bob=3, scale=1.02)),
        bar="pill", badge="squircle", backdrop_filter="blur", overlay="blobs",
    ),
    # ------------------------------------------------------------------ 2030s
    "lightfield": theme(
        id=7, name="LIGHTFIELD", group="2030s",
        ink="#FFFFFF", paper="#1C1E3C", paper_dim="#2A2C4A", text="#F4F6FF", text_dim="#B9BCD6",
        frame="#1C1E3C", frame_text="#F4F6FF",
        on_backdrop="#F4F6FF", backdrop_shadow=None,
        weapon="#FFB57A", gadget="#7CF7FF", special="#FF9DE1", highlight="#FFE29C", danger="#FF8A9A",
        band=["#1C1E3C"], band_text="#B9BCD6",                # hairline band with a glowing dot
        confirm=["#7CF7FF", "#B69CFF", "#FF9DE1", "#FFE29C"], on_confirm="#12142C",
        tab_on="#EDEEFB", tab_on_text="#12142C",
        glass=("#14182E", .42), glass_light=("#FFFFFF", .07),
        veil_top=("#0A0822", .50), veil_bottom=("#0A0822", .80),
        fonts=dict(display="Unbounded-Medium", label="Manrope-Bold",
                   body="Manrope-Medium", strong="Manrope-SemiBold"),
        sizes=dict(title=22, card=15, tab=14, label=12, name=18, cta=19, slot=14),
        tracking=dict(display=.14, label=.08), case="upper",
        shape=dict(panel="glass", radius_card=22, radius_button=18, radius_cta=27, outline=1,
                   shadow=("soft", 0, 18, "#00000073"), select=dict(lift=6, tilt=0, bob=2, scale=1.03)),
        bar="beam", badge="halo", backdrop_filter="blur", overlay="aurora",
    ),
    # ------------------------------------------------------------------ space
    "cockpit": theme(
        id=8, name="COCKPIT", group="space",
        ink="#3FE0FF", paper="#07182B", paper_dim="#0D2A40", text="#E6F7FF", text_dim="#8FB7CC",
        frame="#07182B", frame_text="#E6F7FF",
        on_backdrop="#E6F7FF", backdrop_shadow="#04101E",
        weapon="#FF9B2F", gadget="#7CFFCB", special="#FF6FB1", highlight="#FFD23F", danger="#FF5A4A",
        band=["#FF9B2F", "#7CFFCB", "#FFD23F"], band_text="#0A0F18",
        confirm="#FF9B2F", on_confirm="#0A0F18",
        tab_on="#3FE0FF", tab_on_text="#06121F",
        glass=("#061224", .72), glass_light=("#061224", .0),
        veil_top=("#040A18", .25), veil_bottom=("#040A18", .92),
        fonts=dict(display="Oxanium-Bold", label="Rajdhani-Bold",
                   body="Rajdhani-SemiBold", strong="Rajdhani-Bold"),
        sizes=dict(title=28, card=21, name=22, cta=26, body=13, label=13),
        tracking=dict(display=.12, label=.06), case="upper",
        shape=dict(panel="chamfer", radius_card=12, radius_button=9, radius_cta=14, outline=2,
                   shadow=("glow", 0, 0, "#3FE0FF"), select=dict(lift=8, tilt=0, bob=2, scale=1)),
        bar="slant", badge="hex", backdrop_filter="none", overlay="grid",
    ),
    # ----------------------------------------------------------------- jungle
    "canopy": theme(
        id=9, name="CANOPY", group="jungle",
        ink="#3A2010", paper="#F4E4BC", paper_dim="#E2CD9C", text="#3A2010", text_dim="#6A4827",
        frame="#F4E4BC", frame_text="#3A2010",                # slots and the gear sit on parchment
        on_backdrop="#FFE9A8", backdrop_shadow="#3A2010",
        weapon="#F08A2B", gadget="#3CC6B0", special="#E5482E", highlight="#FFE27A", danger="#E5482E",
        band=["#73492A", "#5B3A1E"], band_text="#FFF3D0",     # darker carved plank on the wooden frame
        confirm=["#FFE98F", "#F6B928", "#D98E12"], on_confirm="#3A2010",
        tab_on=["#FFE98F", "#F2A81D"], tab_on_text="#3A2010",
        glass=("#1E1408", .68), glass_light=("#1E1408", .40),
        veil_top=("#0A2818", .10), veil_bottom=("#081A10", .86),
        fonts=dict(display="TitanOne-Regular", label="Nunito-ExtraBold",
                   body="Nunito-Bold", strong="Nunito-ExtraBold"),
        sizes=dict(title=30, card=21, name=24, cta=27, slot=14, tab=15, label=12),
        tracking=dict(display=.03, label=.02), case="upper",
        shape=dict(panel="plank", radius_card=12, radius_button=10, radius_cta=14, outline=3,
                   shadow=("hard", 0, 6, "#24130A"), select=dict(lift=8, tilt=-1.2, bob=3, scale=1)),
        bar="bamboo", badge="disc", backdrop_filter="none", overlay="vines",
    ),
    # ----------------------------------------------------------------- utopia
    "solaria": theme(
        id=10, name="SOLARIA", group="utopia",
        ink="#C39A3B", paper=["#FFFFFF", "#F1F7F4"], paper_dim="#E3EFEB", text="#0F3B3A", text_dim="#4A6B66",
        frame=["#FFFFFF", "#F1F7F4"], frame_text="#0F3B3A",
        on_backdrop="#0F3B3A", backdrop_shadow=None,
        weapon="#F6A04D", gadget="#3CC6C0", special="#E98BB0", highlight="#8A5C0C", danger="#B5361F",
        band=["#FFFFFF", "#F1F7F4"], band_text="#0E7C73",     # centred label inside the arch
        confirm=["#149488", "#0E7C73"], on_confirm="#FFFFFF",
        tab_on="#FFFFFF", tab_on_text="#0F3B3A",
        glass=("#FFFFFF", .74), glass_light=("#FFFFFF", .40),
        veil_top=("#FFFFFF", .55), veil_bottom=("#E8F6F0", .90),
        fonts=dict(display="PoiretOne-Regular", label="Jost-SemiBold",
                   body="Jost-Regular", strong="Jost-Medium"),
        sizes=dict(title=32, card=18, name=21, cta=22, tab=15, slot=14, label=12),
        tracking=dict(display=.16, label=.10), case="upper",
        shape=dict(panel="arch", radius_card=18, radius_button=24, radius_cta=27, outline=1,
                   shadow=("soft", 0, 10, "#145A5033"), select=dict(lift=8, tilt=0, bob=2, scale=1)),
        bar="leaves", badge="ringed", backdrop_filter="none", overlay="none",
    ),
}

# Font files the themes need (Resources/Fonts/<file>.ttf). All OFL-1.1
# (checked in google/fonts on 2026-11-18, Roboto included); every family
# covers Latin + Latin Extended, so Czech and the other Latin-script locales
# render in the theme's own face.
# `static=False`: Google ships only a variable TTF - Unity's dynamic font
# has no axis support, so cut static instances (fonttools varLib.instancer).
FONTS = {
    "MajorMonoDisplay-Regular": dict(family="Major Mono Display", static=True),
    "IBMPlexMono-Regular": dict(family="IBM Plex Mono", static=True),
    "IBMPlexMono-Medium": dict(family="IBM Plex Mono", static=True),
    "PressStart2P": dict(family="Press Start 2P", static=True),          # already in the repo
    "PixelifySans-Medium": dict(family="Pixelify Sans", static=False, wght=500),
    "PixelifySans-Bold": dict(family="Pixelify Sans", static=False, wght=700),
    "RussoOne-Regular": dict(family="Russo One", static=True),
    "SairaCondensed-SemiBold": dict(family="Saira Condensed", static=True),
    "SairaCondensed-Bold": dict(family="Saira Condensed", static=True),
    "LilitaOne-Regular": dict(family="Lilita One", static=True),
    "Fredoka-Medium": dict(family="Fredoka", static=False, wght=500),
    "Fredoka-SemiBold": dict(family="Fredoka", static=False, wght=600),
    "Montserrat-ExtraBold": dict(family="Montserrat", static=False, wght=800),
    "Roboto-Regular": dict(family="Roboto", static=False, wght=400),
    "Roboto-Medium": dict(family="Roboto", static=False, wght=500),
    "Baloo2-Medium": dict(family="Baloo 2", static=False, wght=500),
    "Baloo2-Bold": dict(family="Baloo 2", static=False, wght=700),
    "Baloo2-ExtraBold": dict(family="Baloo 2", static=False, wght=800),
    "Unbounded-Medium": dict(family="Unbounded", static=False, wght=500),
    "Manrope-Medium": dict(family="Manrope", static=False, wght=500),
    "Manrope-SemiBold": dict(family="Manrope", static=False, wght=600),
    "Manrope-Bold": dict(family="Manrope", static=False, wght=700),
    "Oxanium-Bold": dict(family="Oxanium", static=False, wght=700),
    "Rajdhani-SemiBold": dict(family="Rajdhani", static=True),
    "Rajdhani-Bold": dict(family="Rajdhani", static=True),
    "TitanOne-Regular": dict(family="Titan One", static=True),
    "Nunito-Bold": dict(family="Nunito", static=False, wght=700),
    "Nunito-ExtraBold": dict(family="Nunito", static=False, wght=800),
    "PoiretOne-Regular": dict(family="Poiret One", static=True),
    "Jost-Regular": dict(family="Jost", static=False, wght=400),
    "Jost-Medium": dict(family="Jost", static=False, wght=500),
    "Jost-SemiBold": dict(family="Jost", static=False, wght=600),
}


# ------------------------------------------------------------------ gate --
def rgb(c):
    c = c.lstrip("#")
    return tuple(int(c[i:i + 2], 16) for i in (0, 2, 4))


def over(fg, alpha, bg):
    """sRGB-space alpha blend, the way uGUI and CSS composite."""
    f, b = rgb(fg), rgb(bg)
    return "#%02X%02X%02X" % tuple(round(alpha * f[i] + (1 - alpha) * b[i]) for i in range(3))


def lum(c):
    def ch(v):
        v /= 255
        return v / 12.92 if v <= .03928 else ((v + .055) / 1.055) ** 2.4
    r, g, b = rgb(c)
    return .2126 * ch(r) + .7152 * ch(g) + .0722 * ch(b)


def contrast(a, b):
    la, lb = sorted((lum(a), lum(b)), reverse=True)
    return (la + .05) / (lb + .05)


def stops(v):
    return v if isinstance(v, list) else [v]


def check(key, t):
    """Yield (ok, what, ratio, need) for every text/surface pair of a theme."""
    def pair(what, ink, surfaces, need):
        worst = min(contrast(ink, s) for s in stops(surfaces))
        return worst >= need, what, worst, need

    yield pair("card text / paper", t["text"], t["paper"], 4.5)
    yield pair("card description / paper", t["text_dim"], t["paper"], 4.5)
    yield pair("slot + gear text / frame", t["frame_text"], t["frame"], 4.5)
    yield pair("band label / band", t["band_text"], t["band"], 4.5)
    yield pair("CTA label / confirm (large)", t["on_confirm"], t["confirm"], 3.0)
    yield pair("active tab / tab (large bold)", t["tab_on_text"], t["tab_on"], 3.0)

    # Glass pills and pilot chips: glass over (veil over the arena), both extremes.
    g_hex, g_a = t["glass"]
    v_hex, v_a = t["veil_top"]
    if g_a > 0:
        surf = [over(g_hex, g_a, over(v_hex, v_a, b)) for b in (BACKDROP_DARK, BACKDROP_BRIGHT)]
        yield pair("pill text / glass", t["on_backdrop"], surf, 4.5)
        yield pair("pill number / glass", t["highlight"], surf, 4.5)

    # Text straight on the backdrop (title, plane name): must clear the veil
    # over both extremes, or carry a shadow/outline that does.
    surf = [over(v_hex, v_a, b) for b in (BACKDROP_DARK, BACKDROP_BRIGHT)]
    worst = min(contrast(t["on_backdrop"], s) for s in surf)
    shadow = t.get("backdrop_shadow")
    ok = worst >= 3.0 or (shadow is not None and contrast(t["on_backdrop"], shadow) >= 4.5)
    yield ok, "title / veiled backdrop (or its shadow)", worst, 3.0


def main():
    failed = 0
    ids = [t["id"] for t in THEMES.values()]
    assert ids == list(range(len(ids))), "ids must be 0..n-1 in declaration order (array index == id)"
    for key, t in THEMES.items():
        missing = [f for f in t["fonts"].values() if f not in FONTS and not f.startswith("Barlow")]
        assert not missing, (key, missing)
        rows = list(check(key, t))
        bad = [r for r in rows if not r[0]]
        failed += len(bad)
        print(f"{t['id']:2d} {key:11s} {t['name']:11s} {'ok' if not bad else 'FAIL'}")
        for ok, what, ratio, need in rows:
            if not ok or "-v" in sys.argv:
                print(f"     {'ok  ' if ok else 'FAIL'} {ratio:5.2f} (need {need}) {what}")
    print(f"{len(THEMES)} themes, {failed} failing pairs")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
