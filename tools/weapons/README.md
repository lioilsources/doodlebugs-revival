# Projectile elements — art and sound pipeline

A plane's **shape** decides what it shoots. A dragon breathes fire, a unicorn
throws lightning, a biplane fires brass. Every weapon keeps its physics and
gains an **element**: its own projectile sprite, impact splash, explosion and
sound. This directory generates all of it and gates all of it, so a style
change is a re-run, not an asset hunt. Design notes:
`Prompts/24-CLAUDE-PLAN-projectile-elements.md`.

Three pipelines, one shape: `render` (the only step that touches the network)
caches raw output and resumes, `post` redoes everything local with no GPU,
`sheet` writes an HTML review, `apply` copies into `Assets/` and writes the
`.meta`.

## Pipeline

```bash
cd tools/weapons

# projectiles - 6 elements x 6 forms
python3 generate_projectiles.py render --elements batch1 --seeds 2
python3 generate_projectiles.py post                      # redo the local steps, no GPU
python3 generate_projectiles.py sheet && open index.html  # every seed on grey AND cloud-white
python3 generate_projectiles.py apply                     # best passing seed -> Assets
python3 generate_projectiles.py apply --pick fire.bomb=4301,venom.mine=4502

# effect flipbooks - impact 6f 64px, explosion 8f 96px
python3 generate_effects.py --procedural render           # no GPU, no network, always passes
python3 generate_effects.py --procedural apply
python3 generate_effects.py render --elements batch1      # FLUX contact sheets instead
python3 generate_effects.py sheet && open effects.html

# sound - 24 clips, 3 candidates each
export ELEVENLABS_API_KEY=$(bw get password 'CI / ELEVENLABS_API_KEY')
python3 generate_sfx.py render --elements batch1
python3 generate_sfx.py post --bitcrush
python3 generate_sfx.py sheet && open sfx.html
python3 generate_sfx.py apply --pick fire.shoot_gun=2

# gate anything, any time
python3 gate.py ../../Assets/Doodlebugs/Resources/Sprites/Projectiles/*/*.png
python3 gate.py ../../Assets/Doodlebugs/Resources/Sprites/Effects/*/*.png
```

`--dry-run` on any `render` prints the prompts and stops. `--elements batch1`
is plan D2's first four (metal, fire, lightning, venom); the other two are
batch two. Every `render` skips what `out/` already holds, so a killed batch
resumes — `--force` re-renders.

## How a projectile is built

FLUX.1-dev paints it at 1024×1024 on a white ground with
`forms.prompt_shape` + `elements.prompt_material` + one shared style block,
RMBG-2.0 keys it in the same job, then locally:

1. alpha = RMBG mask ∪ white-key of the render (RMBG alone eats a tracer's
   thin tail), colours un-matted against the white
2. drop every detached component but the biggest — FLUX leaves specks
3. crop to the silhouette, uniform-scale into the form canvas leaving a 1 px
   clear ring, centre
4. mirror if the mass landed on the wrong side (gate P5 *reports* the flip
   rather than failing — a mirror is the whole fix)
5. palette-quantise to ≤ 16 colours **after** the downscale, hard alpha
6. gate

The review sheet shows every seed on a **mid-grey and a cloud-white** backdrop
side by side. That pair is the review that matters: a venom mine has to
disappear against a cloud and a sniper bolt must not.

`apply` writes `Resources/Sprites/Projectiles/<element>/<form>.png`. The bomb
gets a **pivot 40 % back from the nose** (`forms.pivot`), so `Bullet`'s tumble
rotates about the fuse; everything else keeps the centred default.

## How a flipbook is built

The generator owns the **timing**, the renderer only supplies the **look**.
Frame *i* is drawn at `reach_profile(n)[i]` × the canvas radius — an arc that
rises to a peak in the middle and dies to a remnant — so gate F3 holds by
construction and a FLUX sheet whose four phases came back all the same size
still animates.

- **FLUX mode.** One 2048×512 contact sheet per (element, kind): four phases
  left to right — spark, burst, bloom, fade. Sliced into four panels, keyed,
  scaled by one common factor (relative sizes survive), cross-faded into the
  6/8 frames, then quantised as **one filmstrip**. Quantising frame by frame
  is the obvious version and it is wrong: six independent median cuts of the
  same fireball give six slightly different oranges, the union blows past 16
  and the animation shimmers.
- **`--procedural`.** Deterministic Pillow flipbooks from primitives using the
  element's own 6-shade ramp: shrapnel sparks (metal), ember specks (fire),
  radial forks (lightning), droplet scatter (venom), a clean glow ring
  (plasma), a feather fan (air). Zero GPU, zero network, ≤ 6 colours, and it
  always passes the gate — the safety net of plan D4, and probably the right
  answer for lightning, where a fork drawn by code reads better at 64 px than
  a painting shrunk to it.

`apply` prefers a passing FLUX seed and falls back to the procedural sequence;
`--procedural apply` forces the procedural one. Frames land in
`Resources/Sprites/Effects/<element>/<kind>_NN.png`, which is what
`EffectLibrary.Frames()` loads and caches.

## How a clip is built

ElevenLabs `POST /v1/sound-generation` with the element's flavour words × the
event × a fixed 8-bit tail. The key is read from `$ELEVENLABS_API_KEY` (in
Bitwarden as `CI / ELEVENLABS_API_KEY`, same convention as the signing
secrets) and `render` refuses to run without it — it is never defaulted and
never committed. `render` is the only subcommand that needs it.

24 clips (plan D6): shoot × 2 form groups (`gun` = tracer/pellet/bolt,
`heavy` = bomb/rocket/mine) × 6 elements, impact × 6, explosion × 6. Three
candidates each = 72 generations for the full set, 48 for batch one.

`post` is ffmpeg: decode → mono 44.1 kHz 16-bit WAV (what `SfxManager` loads
and what the eight procedural clips in `Resources/Sfx` already are), trim
silence at −50 dBFS both ends, peak-normalise to −1 dBFS (two passes —
`volumedetect` then a fixed `volume` gain; `loudnorm` would gate and pump a
200 ms transient), hard length cap per event (shoot ≤ 0.25 s, impact ≤ 0.4 s,
explosion ≤ 0.9 s), and `--bitcrush` for 8-bit at 11 kHz and back. The
crusher and the caps are not optional if D3 holds: ElevenLabs leans cinematic,
and a reverb tail next to an 8-bit blip sounds like two different games.

The sheet plays every candidate next to **today's** generic clip with an RMS
bar, so a limp pick is obvious before it reaches the game.

## Gates (`gate.py`, mirrored in the Editor)

Pure Pillow, no numpy, no GPU — the same rules run from `post` and from
**Doodlebugs → Validate Projectile Art**. `gate.py` on its own gates whatever
you hand it: files named `<form>.png` as projectiles, files named
`<kind>_NN.png` grouped per directory as flipbooks.

| gate | rule |
|---|---|
| P1 size | exactly the form canvas |
| P2 extent | alpha bbox ≥ 60 % of the canvas along its long axis |
| P3 ring | outer 1 px ring empty |
| P4 palette | ≤ 16 distinct opaque colours |
| P5 facing | right-facing forms: mass centroid in the right half (a fail is reported as `flip`, and `post` mirrors instead of rejecting) |
| P6 mass | bbox fill ≥ 0.28 — a wispy outline reads as nothing at 32 px |
| F1 count | impact 6, explosion 8 |
| F2 size | every frame 64×64 / 96×96 |
| F3 arc | bbox radius rises then falls, no dip up, no bump down, peak > frame 0 |
| F4 start | frame 0 bbox < 30 % of the canvas |
| F5 end | last frame < 5 % opaque coverage |
| F6 palette | ≤ 16 distinct opaque colours across the **whole** sequence |

A missing (element, form) or (element, kind) is not an error: `Bullet` falls
back to `metal`, then to the shared tracer + `ProjectileTint`, and
`EffectLibrary` falls back to `explosion.prefab`. Render more seeds or reword
`elements.py` / `forms.py` and try again.

## What must stay in sync with the C# side

Ids travel over the network on every bullet spawn, so they are frozen once
shipped — same rule as `WeaponType`. Keep these by hand; they are small and
rarely change together.

| here | there |
|---|---|
| `elements.ELEMENTS` ids, keys, tints | `Scripts/Weapons/ProjectileElement.cs` (`ProjectileElement` enum + `ElementProfile`) |
| `elements.SHAPES` | `PlaneModelCatalog.All`'s `Element` argument (anything absent is Metal) |
| `forms.FORMS` keys + `weapons` | the `WeaponType` → form mapping in `Scripts/Weapons/` |
| `forms.pivot("bomb")` | nothing in code — it is baked into the `.meta` |
| `gate.KINDS` (frames, canvas, fps) | `Scripts/Effects/EffectLibrary.cs` / `FlipbookEffect` |
| `gate.py` P/F rules | `Assets/Doodlebugs/Editor/` (Validate Projectile Art) |
| `generate_sfx.EVENTS` filenames | `SfxManager.PlayShoot/PlayImpact/PlayExplosion` lookups |

Asset paths, all under `Assets/Doodlebugs/Resources/`:

```
Sprites/Projectiles/<element>/<form>.png        point filter, PPU 100, pivot centre (bomb: 0.60, 0.5)
Sprites/Effects/<element>/<kind>_NN.png         00-05 impact, 00-07 explosion
Sfx/Elements/<element>/sfx_shoot_gun.wav        sfx_shoot_heavy, sfx_impact, sfx_explosion
```

## SPARK memory rules

Same box, same rules as `tools/backgrounds` and `tools/planes` — the ComfyUI
client is imported from `tools/backgrounds/spark_backgrounds.py`, not copied:

- **`SPARK_INFLIGHT=1`.** One job at a time. Two in flight halved throughput on
  the 2026-09-03 batch (memory pressure, not overlap) and then OOM-killed
  ComfyUI mid-job. Override only for a genuinely lighter graph.
- **`POST /free` before a batch** — `render` does it automatically;
  `--no-free` when another batch is already using the box.
- **fp8 FLUX** for txt2img (`weight_dtype: fp8_e4m3fn`): the fp16 dev
  checkpoint is 24 GB and SPARK's unified memory is shared with the LLM
  containers.

## Pillow note

Pillow ≥ 11 hands `Image.point()` on an **I or F mode** image an
`ImagePointTransform` instead of evaluating the lambda per value, so
`labels.point(lambda v: 255 if v == keep else 0, "L")` silently returns the
untouched I image and the next `ImageChops` call dies with *"images do not
match"*. `L` images still build a LUT and are fine. Per-pixel float maths goes
through `ImageMath.lambda_eval` (see `spark_backgrounds.unmatte_white`);
`generate_projectiles.largest_component` sidesteps it by collecting the
component's pixels instead of thresholding a label image.

Gitignored: `out/`, `review/`, `index.html`, `effects.html`, `sfx.html`.
