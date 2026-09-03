# Plane skins — art pipeline

50 skins on ONE locked 128×128 silhouette (`BiPlane1.png`) — no new
hitboxes, no new colliders, every skin is a texture swap only. The tail fin
stays pure red in every skin and keeps going through `PlayerController`'s
existing `ColorReplace` shader, so per-player colour identity (10-colour
`PlayerColorManager`) survives skin choice — a small accent on top of
whatever livery you picked.

## Pipeline

```bash
cd tools/skins
python3 generate_skins.py masks             # (re)build masks/ from BiPlane1.png — run once
python3 generate_skins.py render            # all 49 generated skins on SPARK (starter is a plain copy)
python3 generate_skins.py render --keys jungle_camo,gold_leaf   # just a few
python3 generate_skins.py sheet && open index.html
python3 generate_skins.py apply             # copy out/*.png -> Assets/.../Resources/Sprites/PlaneSkins/
```

`--dry-run` on `render` prints prompts only. Existing `out/skin_<key>.png`
files are skipped (`--force` to redo); the game falls back to the starter
skin for anything not yet applied (`PlaneSkinCatalog.LoadSprite`), so it's
safe to `apply` a partial batch.

## How a skin is built

Masks (`masks/*.png`, built once from `BiPlane1.png`, re-run `masks` if that
sprite is repainted):

| mask | meaning |
|---|---|
| `accent.png` | tail fin (x≤26 of the red-keyed area) — stays pure red, per-player tinted at runtime |
| `paint.png`  | rest of the red-keyed body — this is what a skin actually paints |
| `fixed.png`  | grey engine/prop/pilot/wheel — copied unchanged into every skin |
| `luminance.png` | value channel of the original red pixels (255=highlight, 141=shadow) |
| `alpha.png`  | overall silhouette — identical across every skin |

Per skin: FLUX renders a flat *material swatch* (no scene framing — see
`FRAME` in `skins.py`) at 768×768 on SPARK, no upscale pass (it gets
downsampled into a masked area a few dozen pixels wide anyway). The swatch
is centre-cropped to square, resized to 128×128, then:

1. multiplied by `luminance.png` (classic colourise-a-photo trick — this is
   what reproduces the original sprite's panel shading on every pattern with
   zero manual shading work per skin)
2. pasted into `paint.png`
3. `accent.png` forced back to pure red `(255,0,0)`
4. `fixed.png` alpha-composited on top unchanged
5. cropped to `alpha.png`

Reuses `tools/backgrounds/spark_backgrounds.py`'s ComfyUI client directly
(same `api`/`enqueue`/`wait`/`fetch`/`flux_graph`, same FLUX+NAG negative
setup) — no duplicated SPARK plumbing.

## Prompts / tiers

`skins.py` — 50 entries, ids/keys match
`Assets/Doodlebugs/Scripts/Skins/PlaneSkinCatalog.cs` exactly (keep both in
sync by hand). 12 free starter skins, 38 premium in 4 bundles (camo,
metallic, cosmic, homage) — see `IAPManager.SkinBundles` for the bundle
split and `PlaneSkinCatalog`'s header comment for why bundles instead of 38
individual store products.

Franchise-flavoured "homage" skins (Gotham Night, Dream Pink, Hero Comic...)
follow the same rule as `tools/ads/PROMPTS.md` and
`tools/backgrounds/themes.py`: mood/palette only, never a borrowed name,
character, logo or signature mark.

## What still needs a human

- **Store products**: nobody but the App Store Connect / Google Play
  Console account owner can create the 4 bundle products
  (`IAPManager.SkinBundles[].StoreId`) or set real prices — the code ships
  with a `$2.99`/`$3.99` placeholder.
- **Unity IAP package**: `Packages/manifest.json` has a version pin for
  `com.unity.purchasing` — open Package Manager once and let it resolve/
  update to whatever's current, then link Unity Gaming Services (Services
  window) for receipt validation.
- **Scene wiring**: run `Doodlebugs → Setup Plane Skin Manager` once in the
  Unity editor (adds the server-authoritative claims registry to the scene,
  same shape as `BackgroundManager`) and commit the updated `Scene01.unity`.

Gitignored (regenerate any time): `out/`, `review/`, `masks/`, `index.html`.
