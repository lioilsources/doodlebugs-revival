# Projectile Elements — Implementation Plan

Goal: a plane's **shape decides what it shoots**. A dragon breathes fire, a
unicorn throws lightning, a biplane fires brass. Every weapon keeps its
physics (damage, cooldown, pellets, gravity…) and gains an **element**: its
own projectile art, trail, impact splash, explosion and sound. Art comes
from FLUX on SPARK through the same kind of pipeline as `tools/planes/`,
sound from the ElevenLabs sound-effects API, both gated and applied by
script so a style change is a re-run, not an asset hunt.

Status (2026-09-05): **proposal**, nothing implemented. Builds on plan 23
(shapes, `PlaneModelCatalog`), `WeaponProfile.cs`, `Bullet.cs`,
`EffectAssets.cs` and the procedural SFX in `Resources/Sfx`.

Where things will live: `Scripts/Weapons/ProjectileElement.cs`,
`Scripts/Effects/EffectLibrary.cs` + `FlipbookEffect.cs`,
`tools/weapons/` (pipeline + gates), `Resources/Sprites/Projectiles/<element>/`,
`Resources/Sprites/Effects/<element>/`, `Resources/Sfx/Elements/<element>/`.

---

## 0. What the code does today (the seams we plug into)

- `WeaponProfile` (8 weapons) is purely parametric: physics + `ProjectileScale`,
  `ProjectileTint`, optional `ProjectileSpriteName`. Only the bomb has its own
  sprite (`bomb_littleboy`); everything else is the shared tracer
  (`Sprites/Bullet/uzi 2_00006.png`) tinted.
- `Bullet` syncs `_weaponId` (server-write NetworkVariable) and applies
  scale/tint in `ApplyVisual()` on every client; `Shooting.SpawnBullet` calls
  `SetWeapon()` on the server. The shooter is known to the bullet
  (`_shooterClientId`, `_shooterLocalPlayerIndex`) and the shooter's plane
  carries `PlaneAppearance.NetModelId` — so the element is **derivable on
  the server at spawn** with no new input.
- Impact visuals: one prefab (`explosion.prefab`, Animator over 5
  `enemy-death` frames) for everything — plain hits (`PlayHitFxClientRpc`)
  and AoE booms (`ExplodeClientRpc(pos, radius)`, which also digs the crater).
- Sound: `SfxManager` loads 8 procedural 44.1 kHz mono WAVs by name;
  `PlayShoot()` fires from `Bullet.OnNetworkSpawn`, `PlayExplosion()` from
  `ExplodeClientRpc`, shield/hull hits from the victim's `PlaneVisualEffects`.
- Particles: `EffectAssets` builds ParticleSystems entirely in code from a
  runtime `SoftCircle` texture (`CreateSmokeSystem`). Same recipe works for
  trails and bursts — no prefabs, no scene wiring.

Consequence: the whole feature is **one new id on the bullet** plus lookups.
Nothing in gameplay (hitbox, damage, RPC routing) changes.

## 1. Elements and who gets them

Six elements. Shipped shapes as of today mapped by what they *are*:

| element | key | shapes | flavour |
|---|---|---|---|
| Metal | `metal` | biplane, triplane, racer, flying_boat, twin_boom, gull_wing, barnstormer, gyrocopter, crop_duster, seaplane, tiltrotor, gunship, zeppelin, bathtub, flying_car | brass tracers, iron bombs, sparks, grey smoke — today's look, made explicit |
| Fire | `fire` | dragon | fireballs, embers rising, flame splash, black-smoke bloom |
| Lightning | `lightning` | unicorn | crackling bolts that jitter in flight, arc flash, forked ring burst |
| Venom | `venom` | wasp | green droplets that drip, splat on impact, acid cloud |
| Plasma | `plasma` | rocket, starfighter, shuttle, interceptor, saucer, stealth, hover_pod | cyan/magenta energy bolts, glow trail, clean ring burst |
| Air | `air` | goose, ornithopter, paper_plane, delta_glider | feather/paper darts, gust ring, dust puff (goose shoot = honk) |

**D1** — element comes from the **shape** (`PlaneModelDef.Element`), not the
skin and not a separate pick. Skins stay cosmetic liveries; the picker shows
the element as a small badge on the shape card. (Alternatives: by skin — 50
liveries × art is unaffordable; player pick — one more menu, and the point
is that a dragon *is* fire.)

**D2** — ship in two batches: **Metal, Fire, Lightning, Venom** first (the
examples in the brief, and Metal is free — it is the current art re-cut),
**Plasma, Air** second. Every shape not yet mapped defaults to Metal, so
nothing is ever missing.

**D5** — **visual and audio only** in this plan. No burn-over-time, no
stun. The game is 3-HP one-hit-kill-adjacent and the weapon draft is where
balance lives; elements must not become a hidden second draft. A later plan
may add modifiers behind a lobby toggle.

## 2. Visual forms (what a weapon looks like in an element)

Eight weapons collapse to **six forms**; a form is the shape of the art and
its canvas, an element is its material:

| form | weapons | canvas px | facing | notes |
|---|---|---|---|---|
| `tracer` | MG, Twin MG | 32×16 | right | long thin, many on screen — keep it 2-colour simple |
| `pellet` | Flak, Heavy Flak | 16×16 | any | round; 5–7 per shot |
| `bomb` | Aero Bomb | 48×24 | right | replaces `bomb_littleboy` per element (Metal keeps Little Boy) |
| `bolt` | Sniper | 48×12 | right | the fastest thing on screen; needs a bright core |
| `rocket` | Rocket | 48×20 | right | body + exhaust; the trail does the rest |
| `mine` | Mine | 32×32 | any | must read at 2× scale and hide in clouds (dark palette) |

`ProjectileScale`/`ProjectileTint` in `WeaponProfile` stay as the **fallback**
when an element sprite is missing (Phase 0 ships on fallbacks alone).

Per element the game also gets:

- **trail** — a runtime ParticleSystem preset (rate, lifetime, gravity,
  position jitter, colour gradient, texture: soft circle / spark streak /
  droplet / square ember). Procedural, no generated art.
- **impact** flipbook — 6 frames, 64×64, for a non-AoE hit (plane, wall,
  tile).
- **explosion** flipbook — 8 frames, 96×96, scaled by blast radius, for
  Bomb/Rocket/Mine and for the death boom of a plane killed by that element
  (optional, D7).
- **burst** particles — a one-shot ParticleSystem layered under the flipbook
  (shrapnel / embers / forks / droplets / glow / feathers). Procedural.
- **sfx** — shoot (per form group), impact, explosion.

## 3. Unity implementation

### 3.1 Registry (`Scripts/Weapons/ProjectileElement.cs`)

```csharp
public enum ProjectileElement { Metal = 0, Fire = 1, Lightning = 2, Venom = 3, Plasma = 4, Air = 5 }

public class ElementProfile
{
    public ProjectileElement Element; public string Key; public string DisplayName;
    public Color Tint;                 // fallback tint + HUD badge colour
    public TrailPreset Trail;          // rate, lifetime, gravity, jitter, gradient, texture key
    public BurstPreset Burst;          // count, speed, gravity, gradient, texture key
    public string ShootSfxGroup;       // "gun" | "heavy" per form, see §5
    public static ElementProfile Get(ProjectileElement e); public static ElementProfile Get(int id);
}
```

Ids go over the network — stable once shipped, same rule as `WeaponType`.
Keys must match `tools/weapons/elements.py`.

### 3.2 Shape → element

`PlaneModelDef` gains `public ProjectileElement Element = Metal;` set in
`PlaneModelCatalog.All` (a fourth constructor arg, defaulted). New
`PlaneModelCatalog.ElementOf(int modelId)`.

### 3.3 Bullet

- New `NetworkVariable<int> _elementId` (server-write, everyone-read) next
  to `_weaponId`, `SetElement(int)` server-only. `Shooting.SpawnBullet`
  resolves it: `PlaneModelCatalog.ElementOf(shooterPlane.GetComponent<PlaneAppearance>().NetModelId.Value)`.
  Late joiners get it with the object's initial sync — same reason
  `_weaponId` is a NetworkVariable.
- `ApplyVisual()` resolution order: `Sprites/Projectiles/<element>/<form>`
  → `Sprites/Projectiles/metal/<form>` → `WeaponProfile.ProjectileSpriteName`
  → shared tracer + `ProjectileTint`. Sprites cached in a static dictionary
  (`Resources.Load` per shot would allocate).
- On spawn (every client): `EffectAssets.CreateTrailSystem(transform, element)`.
  On despawn: detach the trail, stop emission, destroy after its lifetime —
  otherwise the last puff vanishes with the bullet.
- `PlayHitFxClientRpc(pos)` → `PlayHitFxClientRpc(pos, int elementId)`;
  `ExplodeClientRpc(pos, radius)` → `(pos, radius, int elementId)`. A plane hit
  (the `IDamagable` branch) today plays no bullet-side visual at all — add
  `PlayHitFxClientRpc` there too so the victim wears the attacker's splash.
  Shield/hull SFX stay victim-side in `PlaneVisualEffects` (they say *what
  was hit*; the element says *by what*).
- Render order unchanged: bullets and trails below clouds, so a venom mine
  still lurks.

### 3.4 Effects (`Scripts/Effects/`)

- `FlipbookEffect` — one MonoBehaviour: `Play(Sprite[] frames, float fps, float scale)`,
  SpriteRenderer, self-destroys at the end. Frames come from
  `EffectLibrary.Frames("<element>/<kind>")`, which `Resources.LoadAll<Sprite>`s
  the folder once and caches. No Animator, no controller asset per element —
  the current `explosion.prefab` stays as the ultimate fallback.
- `EffectLibrary.SpawnImpact(element, pos)` / `SpawnExplosion(element, pos, radius)`:
  flipbook + `EffectAssets.CreateBurst(element)` (one-shot ParticleSystem,
  `SoftCircle`/`Spark`/`Droplet`/`Square` textures generated at runtime like
  `SoftCircle` is today).
- Trails and bursts are **local-visual** on every client, driven by synced
  ids — exactly the terrain-crater pattern.

### 3.5 Sound

`SfxManager` gains `PlayShoot(element, form)`, `PlayImpact(element)`,
`PlayExplosion(element)`. Clips load lazily from
`Resources/Sfx/Elements/<element>/sfx_shoot_<group>.wav`, `sfx_impact.wav`,
`sfx_explosion.wav`; a missing clip falls back to today's generic one, so
Phase 0 sounds exactly like today. Callers: `Bullet.OnNetworkSpawn`
(shoot), the two hit RPCs (impact), `ExplodeClientRpc` (explosion). Pitch
jitter stays (±8 % / ±6 %), it hides repetition better than more clips.

### 3.6 HUD

- Shape card in the plane picker: element badge (tinted dot + key) so the
  choice is visible before the first shot.
- Weapon draft card: projectile preview drawn with the **local pilot's**
  element sprite (the card already knows the weapon; the plane's element is
  one lookup away).

### 3.7 Editor

`Doodlebugs → Validate Projectile Art`: runs the same gates as
`tools/weapons/gate.py` over `Resources/Sprites/Projectiles` and `Effects`
(missing element/form pairs listed as warnings, not errors — fallbacks
cover them).

## 4. Art pipeline (`tools/weapons/`)

Reuses `tools/backgrounds/spark_backgrounds.py` (ComfyUI client, FLUX graph
with NAG negatives, RMBG tail, `run_jobs`, `white_key`) and
`tools/planes/unity_meta.py`. Same conventions: `render` caches raw output
in `out/raw/` and resumes; `post` is GPU-free; `sheet` writes an
`index.html` review; `apply` copies into `Assets/` and writes `.meta`.
`SPARK_INFLIGHT=1` — one job at a time, the box OOMs otherwise.

```
tools/weapons/
  elements.py             element keys/ids/tints + prompt flavour + SFX flavour (mirror of C#)
  forms.py                the six forms: canvas, facing, prompt shape, scale hints
  gate.py                 projectile + flipbook gates (pure Pillow, importable by the Editor mirror)
  generate_projectiles.py render | post | sheet | apply
  generate_effects.py     render | post | sheet | apply   (--procedural fallback)
  generate_sfx.py         render | post | sheet | apply   (ElevenLabs)
  README.md
```

### 4.1 Projectile sprites (36 = 6 elements × 6 forms; 24 in batch one)

FLUX txt2img on SPARK, 1024×1024 on pure white, prompt =
`forms.py` shape + `elements.py` material + the house style block
("flat pixel-art game sprite, side view facing right, hard edges, no
text, no background, no shadow"), NAG negatives for text/ground/duplicates.
Then locally:

1. key: RMBG-2.0 ∪ white-key (as planes), drop detached components
2. crop to alpha bbox, uniform-scale into the form canvas with a 1 px clear
   ring, centre; **tracer/bolt/rocket must face right** (rightmost opaque
   column = nose) — flip if the gate says the mass is on the wrong side
3. palette-quantise to ≤ 16 colours, hard alpha
4. gate, review sheet with every seed on a mid-grey and a cloud-white
   backdrop (a mine has to disappear on the second, a bolt must not)

`apply` writes `Resources/Sprites/Projectiles/<element>/<form>.png` + meta
(`kind="sprite"`, point filter, PPU 100, pivot centre). The bomb form gets a
**pivot at 40 % from the nose** so the tumble in `Bullet` looks right; the
others keep centre.

### 4.2 Flipbooks (impact 6 f + explosion 8 f per element)

**D4** — try FLUX first, keep a procedural fallback:

- FLUX renders a **contact sheet** per (element, kind): "four phases left
  to right — spark, burst, bloom, fade" at 2048×512 on white. Slice into 4
  panels, key, centre each on its own canvas, then Pillow makes the
  in-betweens (cross-fade + radial scale ramp) to reach 6/8 frames. One
  prompt gives one consistent palette across the sequence, which is what
  separately rendered frames never do.
- `--procedural`: deterministic Pillow flipbooks from primitives (expanding
  rings, radial forks, droplet scatter, feather fan) using the element's
  gradient. Always passes the gate. This is the safety net if FLUX sheets
  come back inconsistent — and probably the right answer for `lightning`,
  where a fork drawn by code reads better at 64 px than a painting.

Gate: frame count, alpha bbox radius monotonic up then down, frame 0 small
(< 30 % of canvas), last frame nearly empty (< 5 % alpha), palette ≤ 16.

`apply` → `Resources/Sprites/Effects/<element>/<kind>_00.png…` (point
filter, PPU 100, pivot centre).

### 4.3 Trail / burst textures

Procedural at runtime (`EffectAssets`): `SoftCircle` exists; add `Spark`
(thin horizontal streak), `Droplet` (teardrop), `Square` (crisp ember),
`Feather` (thin leaf). 32×32, generated once. No pipeline step.

## 5. Sound pipeline (`generate_sfx.py`, ElevenLabs)

- API: `POST https://api.elevenlabs.io/v1/sound-generation` with
  `{"text": ..., "duration_seconds": 0.3–1.5, "prompt_influence": 0.6}`,
  header `xi-api-key: $ELEVENLABS_API_KEY`. Key lives in Bitwarden as
  `CI / ELEVENLABS_API_KEY` (same convention as the signing secrets), read
  from the environment, never committed.
- Prompts = `elements.py` SFX flavour × event, always ending in "short
  retro arcade game sound effect, 8-bit chiptune style, no music, no
  reverb tail" — the existing set is procedural 8-bit and a cinematic
  whoosh next to it would sound like a different game.
- **D6** — clip matrix: shoot per (element, form group) where groups are
  `gun` (tracer, pellet, bolt) and `heavy` (bomb, rocket, mine) = 12,
  impact 6, explosion 6 → **24 clips**, 3 candidates each = 72 generations.
  (Per-form shoot would be 48 clips for a difference nobody hears under
  pitch jitter.)
- `render`: 3 candidates per key → `out/raw/<key>_<n>.mp3`, cached/resumable.
- `post` (ffmpeg, already on the Mini): decode → mono 44.1 kHz 16-bit WAV
  (matches `sfx_shoot.wav`), trim leading/trailing silence at −50 dBFS,
  peak-normalise to −1 dBFS, hard length cap per event (shoot ≤ 0.25 s,
  impact ≤ 0.4 s, explosion ≤ 0.9 s), optional `--bitcrush` (8-bit depth,
  11 kHz resample-and-back) so ElevenLabs output sits next to the
  procedural clips.
- `sheet`: `index.html` with an `<audio>` per candidate, the existing
  generic clip as the reference, and a loudness bar (RMS) so a quiet pick
  stands out before it reaches the game.
- `apply --pick fire.shoot_gun=2`: copies the chosen candidate to
  `Resources/Sfx/Elements/<element>/sfx_<event>[_<group>].wav` + meta.

## 6. Phases

**Phase 0 — plumbing, no art (one session).** `ProjectileElement` +
registry, `PlaneModelDef.Element`, bullet `_elementId` + spawn resolution,
sprite/sfx lookups with fallbacks, runtime trails and bursts, `FlipbookEffect`
+ `EffectLibrary` (falling back to `explosion.prefab`), RPC signatures,
picker badge, CLAUDE.md section. Playable immediately: a dragon shoots
orange-tinted tracers with an ember trail and today's sounds. ParrelSync
test: late joiner sees the right trail on an in-flight bullet.

**Phase 1 — batch one art + sound.** `tools/weapons/` pipeline; projectiles
for Metal/Fire/Lightning/Venom (24), flipbooks (8), SFX (16 clips). Gate,
review, apply, validate in Editor. Metal is a re-cut of the current look so
the default player notices nothing but the new impact splash.

**Phase 2 — Plasma + Air**, weapon-card projectile preview, optional death
boom by killing element (**D7**, default off: the wreck's ground explosion
is the victim's, not the attacker's).

**Phase 3 (separate plan, not now)** — gameplay modifiers behind a lobby
toggle.

## 7. Decisions

| # | decision | recommendation |
|---|---|---|
| D1 | element source | **shape** (`PlaneModelDef.Element`); skins stay liveries |
| D2 | element set | 6 total; Metal, Fire, Lightning, Venom first; Plasma, Air second |
| D3 | style | **retro**: ≤ 16-colour quantised sprites, point filter, bit-crushed SFX — the HUD font and current SFX already set this |
| D4 | flipbooks | FLUX contact sheet + Pillow in-betweens, `--procedural` fallback per element |
| D5 | gameplay | **visual/audio only**; modifiers are a later, toggleable plan |
| D6 | SFX matrix | 24 clips (shoot × 2 form groups, impact, explosion) |
| D7 | death boom by element | off by default |

## 8. Risks

- **FLUX and tiny sprites.** A 1024 px painting shrunk to 32×16 can turn
  into a smear. Mitigation: prompt for "large, centred, fills the frame",
  quantise *after* the downscale, and let the gate reject low-contrast
  results; `tracer` may end up hand-tuned procedural (two pixels of brass).
- **Style drift between elements.** Same style block, same seed policy,
  same quantiser; the review sheet shows all six forms of an element side
  by side, and all six elements of a form.
- **SPARK memory.** One job in flight, `POST /free` before a batch, fp8 FLUX
  — all already in the backgrounds client.
- **Sound cohesion.** ElevenLabs will lean cinematic; the bit-crusher and
  the length caps are not optional if D3 holds. Budget: 72 generations for
  batch one.
- **Network.** One extra int per bullet spawn; nothing per tick.
