# Plane Shapes — Implementation Plan

Goal: planes differ by **silhouette**, not only by livery, so the image models
can go wild (triplanes, flying boats, pusher props, paper planes…) — while the
game stays **fair**: every shape is an equally hittable target.

Status (2026-09-03, same day): **implemented** — Phases 0–2 in code, art
batch running. Builds on the skin system (`Scripts/Skins/`, `tools/skins/`).
Decisions taken (§7): **D1 = (a)** unique `(model, skin)` combo; **D2** pilot
asked for in the prompt, prop free; **D3** height band top stays 72 px;
**D4** all shapes free, skins remain the paid layer (Phase 3 not done —
`PlaneModelDef.IsPremium/BundleId` exist for later). Two deviations from
the text below, both deliberate:

- **Skins × shapes are composited at runtime** (`PlaneModelCatalog.LoadSprite`,
  from the model's `_mask.png` + the skin's 128×128 swatch), not baked. §3's
  "500 × ~5 KB" was PNG-on-disk; in a build every pair is 64 KB of RGBA32
  (point-filtered pixel art can't be compressed), i.e. ~50 MB for 16 × 50.
  The original biplane keeps its baked skins, pixel-identical.
- **G1 is 55 %, not 95 %, and G4 is ±8 px.** Calibration on `BiPlane1.png`
  itself: core coverage 0.66 (the box spans the plane's full height, wing
  gaps included), mass centroid 4.7 px above centre. 95 % would have failed
  the reference. A **G7 livery gate** (≥ 35 % red body) was added — skins need
  paint to land on, and it catches Kontext drifting the colour scheme.

Where things live: `tools/planes/` (pipeline + gate), `PlaneModelCatalog.cs`,
`Editor/PlaneModelValidator.cs`, picker shape row in `GameHUD.cs`.

---

## 0. The finding that shapes the whole plan

The hitbox is **not** derived from the sprite. `PlaneHolder.prefab` root has a
`BoxCollider2D` with `m_Size: {0.5, 0.5}`, `m_Offset: {0, 0}` — at root scale 2
that is a **1×1 world-unit square at the plane's centre**, i.e. **50×50 px in
the middle of the 128×128 canvas** (px box `(39,39)-(89,89)`). The drawn plane
is 110×55 px. Wings and tail are already un-hittable today; bullets hit via
`IDamagable` on that collider (`Bullet.cs:215`), boundary clamping uses
`planeCollider.bounds` (`PlayerController.cs:927`), nothing in gameplay reads
sprite bounds.

Consequences:

- **Equal collision area is free.** Every model shares the same box. Do NOT
  generate per-model colliders (a `PolygonCollider2D` from alpha would make a
  thin plane harder to hit from above than a fat one — *less* fair, not more).
- What fairness actually needs is **visual honesty**: the box must always sit
  on solid body (never in empty air between wings), and no model may *look*
  like a much bigger or smaller target than the others.
- `firePoint` stays at the canonical nose position → identical muzzle for all.

So the requirement "roughly the same size / same collision area" becomes an
**envelope contract** the art pipeline enforces automatically.

## 1. Envelope contract (fairness gates)

Reference = `BiPlane1.png`: alpha bbox 110×55 px, centre (62,64), bbox fill
ratio 0.54. Canvas 128×128, nose points **right**, pivot centre.

Every generated model must pass, at 128×128 after normalisation:

| gate | rule | why |
|---|---|---|
| G1 core coverage | ≥ 95 % of the 50×50 core box pixels opaque (α>128) | hitbox always on visible body |
| G2 extents | bbox width 96–118 px, height 44–72 px | "roughly the same size"; height band allows a triplane, caps a blimp |
| G3 mass | bbox fill ratio 0.42–0.66 | no wispy outlines that read as small targets |
| G4 centring | bbox centroid within ±6 px of (64,64) | box stays central, no visual lean |
| G5 orientation | nose (rightmost opaque column) within x 108–122; tail leftmost within 4–18 | drawn nose-right like the base, rotation code untouched |
| G6 margin | no opaque pixel in the outer 3 px ring | no clipping when rotated |

Numbers are a starting band (~±10 % width, ±22 % fill); tune after the
Phase 0 spike. The gate is code, not eyeballing: `tools/planes/gate.py`
returns pass/fail + metrics, and an Editor menu re-checks everything in
`Resources/Sprites/PlaneModels/` (`Doodlebugs → Validate Plane Models`).

## 2. Art pipeline (`tools/planes/`)

Reuse `tools/backgrounds/spark_backgrounds.py`'s ComfyUI client and
`tools/skins/` mask/luminance tooling.

**Generator: FLUX Kontext with `BiPlane1.png` as the reference.** Kontext keeps
position, scale and orientation of the reference while changing what it is —
exactly the envelope we want, without a ControlNet setup. Prompt shape:

> Redesign this side-view WWI biplane as a **{concept}**. Keep the same size,
> position, facing direction and pixel-art style. Plain white background, no
> text, single aircraft, pilot visible.

Fallback if Kontext drifts in size: `flux1-dev-controlnet-union-pro-2`
(on SPARK) with a soft "scribble" of the reference silhouette at strength
0.3–0.5 — hard envelope, free interior.

Post-process per model (all local, Pillow):

1. RMBG-2.0 on SPARK → alpha (same as fg strips)
2. downsample to 128, palette-quantise (~12 colours) → matches the game's
   pixel look; optional 1 px dark outline pass
3. **normalise**: uniform-scale so bbox width = 110 px, centre on (64,64)
4. **team accent**: paint the leftmost 18 % of the bbox's body pixels pure
   `(255,0,0)` — the existing `ColorReplace` shader then tints it per player
   with **zero runtime changes**. Everything else is generated in a neutral
   grey base coat so the skin luminance trick (`tools/skins`) applies.
5. run the gate; on fail → retry with next seed (max 4), then report

Output per model: `base.png` (grey coat, red tail band), `masks/` (accent,
paint, fixed, luminance, alpha) — the same five masks `tools/skins` already
uses, just per model.

Concept list for the first batch (all "side view, nose right, one pilot"):
triplane, monoplane racer, flying boat with floats, pusher-prop canard,
twin-boom, gull-wing, stubby barnstormer, rocket-assisted, gyrocopter,
ornithopter, paper plane, bathtub-with-wings, crop duster, delta glider,
zeppelin gondola (expect G2 fail — good calibration case).

## 3. Skins × shapes

Skins become **materials**, models become **shapes**. Bake at build time via
the pipeline: `Resources/Sprites/PlaneModels/{model}/skin_{key}.png`. With 10
models × 50 skins = 500 × ~5 KB — negligible. Same luminance-multiply
compositing as today, per-model masks. Runtime compositing (Texture2D ops on
load) is the alternative if the count grows; not needed now.

Free vs premium: a model can be premium independently of skins
(`PlaneModelCatalog.IsPremium`, own IAP bundle "Hangar Pack" — reuse
`IAPManager.SkinBundles` shape).

## 4. Code changes

| file | change |
|---|---|
| `Scripts/Skins/PlaneModelCatalog.cs` (new) | static registry like `PlaneSkinCatalog`: id, key, name, premium/bundle; `LoadSprite(modelId, skinId)` |
| `PlaneAppearance.cs` | add `NetworkVariable<int> NetModelId` + `RequestSelectModelServerRpc`; claim goes through `PlaneSkinManager` (see §5) |
| `PlayerController.SetPlaneColor()` | sprite = `PlaneModelCatalog.LoadSprite(model, skin)`; also swap the **GlowOutline** child's sprite (owner outline must follow the silhouette) |
| `PlaneVisualEffects.cs` | rebuild the cached damage-flash material instance on sprite swap (it caches `_MainTex` once, line ~205–211) |
| `PlaneHolder.prefab` | **no collider change**; no firePoint change |
| `Editor/PlaneModelValidator.cs` (new) | menu item running the §1 gates over `Resources/Sprites/PlaneModels/`, prints a table, fails loudly |
| `GameHUD.cs` picker | second row: shape cards above the skin grid; preview card composes model+skin; "TAKEN" logic per §5 |
| `PlaneSkinManager.cs` | claim key becomes `(modelId, skinId)` or stays `skinId` — decision D1 |

Nothing in movement, shooting, collisions or netcode authority changes.

## 5. Uniqueness rule (decision D1)

Today: no two players may hold the same **skin**. With shapes there are three
options — pick one before Phase 1:

- **(a) unique combo** `(model, skin)` — most permissive, visual identity is
  the combo; 10×50 pairs means it never blocks anyone. *Recommended.*
- (b) unique skin only (current) — two players can fly the same shape.
- (c) unique model **and** unique skin — strictest, blocks fast in a 6-player
  lobby.

## 6. Phases

| phase | scope | cost |
|---|---|---|
| **0 spike** | 5 concepts × 4 seeds via Kontext, gate on, look at what passes; tune §1 bands | ~2 h GPU, half a day |
| 1 | catalog + `NetModelId` + sprite/glow swap + validator + picker row; 6–8 models shipped with default livery | 1–2 days |
| 2 | bake skins × models; `tools/skins` generalised to per-model masks | half a day + GPU |
| 3 | premium models bundle in `IAPManager`; store products (manual) | half a day |

Phase 0 goes first and is allowed to kill the idea cheaply: if Kontext can't
hold the envelope, the ControlNet fallback is the next spike, not Phase 1.

## 7. Decisions for the owner

- **D1** uniqueness scope (§5) — recommend (a).
- **D2** pilot/prop mandatory? Recommend *pilot yes* (readability, "someone is
  flying it"), *prop free* (jets/rockets/ornithopters allowed).
- **D3** height band top (72 px): allow tall triplanes/ornithopters or cap at
  the biplane's 55? Affects how "wild" wild can be.
- **D4** ship models as an IAP tier or all free with skins as the paid layer?

## 8. Risks

- Kontext may homogenise (everything ends up biplane-ish) → ControlNet fallback,
  or txt2img + normalisation instead of img2img.
- Palette-quantised AI output can look "mushy" next to the hand-drawn base →
  outline pass + optional manual touch-up of the 6–8 shipped models.
- Visual-vs-hitbox honesty is a *band*, not equality; G1 (core coverage) is the
  gate that must never be relaxed.
- `GlowOutline` and `WreckEffect` read the sprite; both must follow the swap
  (listed in §4) or the outline shows the wrong shape.
