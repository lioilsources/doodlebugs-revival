# Plane models (shapes) — art pipeline

Planes differ by **silhouette**, not only by livery, and the game stays
fair because the hitbox is not the sprite: `PlaneHolder.prefab` has one
shared `BoxCollider2D` (50×50 px at the sprite centre) for every model. What
fairness needs is that the box always sits on solid body and that no model
*looks* like a much bigger or smaller target — that is the envelope
contract in `gate.py`, enforced automatically here and re-checkable in Unity
(**Doodlebugs → Validate Plane Models**). Design notes:
`Prompts/23-CLAUDE-PLAN-plane-shapes.md`.

## Pipeline

```bash
cd tools/planes
python3 generate_planes.py render --seeds 2              # 15 concepts x 2 seeds on SPARK (Kontext + RMBG)
python3 generate_planes.py render --keys canard --seeds 4 # more seeds for a concept that keeps failing
python3 generate_planes.py post                          # redo the local steps (no GPU) after a tweak
python3 generate_planes.py sheet && open index.html      # every seed, gate verdict, best pick starred
python3 generate_planes.py apply                         # best passing seed per concept -> Assets
python3 generate_planes.py apply --pick triplane=7011    # hand-pick a seed
python3 gate.py ../../Assets/Doodlebugs/Sprites/BiPlane/BiPlane1.png out/models/*.png
```

`render` skips cached seeds (`out/raw/`), so a killed batch resumes;
`--no-free` when another batch is using the box. Concepts, prompts and ids
live in `planes.py` and must match `PlaneModelCatalog.cs`.

**Two render modes.** `--mode kontext` (default) redesigns the BiPlane1
reference and nails concepts with a strong silhouette (racer, flying boat)
but homogenises subtle structural ones back into a biplane — on the first
batch triplane, canard, twin boom and gull wing all came back as biplanes.
`--mode txt2img` renders from the prompt alone (job ids get a `__txt` tag,
both modes coexist per concept; `apply --pick canard=txt__s7043`). The
post-process normalisation supplies the envelope either way.

**Ground shadows.** FLUX sometimes paints a shadow ellipse under the wheels
even with it in the negative. A shadow *detached* from the body is dropped
automatically (component filter); one touching the wheels is not — it would
need a heuristic that also eats seaplane floats — so look at the review
sheet and pick a clean seed.

## How a model is built

FLUX Kontext gets `BiPlane1.png` (upscaled 8× NEAREST on white) as the
reference and a prompt that only names the concept — Kontext keeps size,
position, facing and the red/grey colour scheme, which is exactly the
envelope. The render is keyed with RMBG-2.0 ∪ white-key, then locally:

1. crop to the silhouette, uniform-scale to a 110 px wide bbox (or 122 px
   tall if that binds), centre on (64,64) of a 128×128 canvas, hard alpha
2. palette-quantise to ~20 colours (pixel look next to the hand-drawn base)
3. split the body the way `tools/skins` splits the original: red pixels →
   **paint** (stored as `(value,0,0)`, value band normalised to BiPlane1's
   141..255 so every model shades skins alike), leftmost 18 % of the bbox →
   **tail accent** (forced red, `ColorReplace` tints it per player),
   everything else → **fixed** (engine, pilot, wheels) copied into every skin
4. gate

Per model the game gets `model_<key>.png` (the base = starter livery) and
`model_<key>_mask.png` (R = paint, G = accent, A = alpha) in
`Resources/Sprites/PlaneModels/`. Skins are composited on top **at runtime**
(`PlaneModelCatalog.LoadSprite`) from the skin's 128×128 swatch
(`tools/skins/generate_skins.py swatches` + `apply`) — baking 16 × 50 pairs
would be ~50 MB of RGBA32 in a mobile build. The original biplane keeps its
baked skins, pixel-identical.

## Gates (gate.py, mirrored in PlaneModelValidator.cs)

| gate | rule |
|---|---|
| G1 core | ≥ 55 % of the 50×50 hitbox box opaque (BiPlane1: 66 % — the box spans its full height, wing gaps included) |
| G2 extents | bbox 96–118 wide, 44–72 tall |
| G3 mass | bbox fill 0.42–0.66 |
| G4 centre | mass centroid within ±8 px of (64,64) (BiPlane1 sits 4.7 px high) |
| G5 orientation | nose (rightmost column) 108–122, tail 4–18 |
| G6 margin | outer 3 px ring empty |
| G7 livery | ≥ 35 % of the body is red (skins need paint to land on) |

A concept with no passing seed is simply not applied: `PlaneModelCatalog`
lists it, `IsAvailable` says no, the picker hides it. Render more seeds or
reword `planes.py` and try again. Expected calibration failure: zeppelin
(too tall/fat — G2/G3).

Gitignored: `out/`, `review/`, `index.html`.
