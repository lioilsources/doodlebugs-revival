# CLAUDE.md

Instructions for Claude Code when working in this repository.

## Project

2D multiplayer arcade air combat game. Unity 6000.3.13f1. WWI biplanes. Physics-based movement, one-hit kill, WiFi/couch multiplayer.

## Tech Stack

- Unity 6000.3.13f1
- Netcode for GameObjects 2.11.0
- Unity Input System 1.19.0
- Platforms: Desktop (Win/macOS), iOS, Android
- Built-in module `com.unity.modules.androidjni` must stay enabled —
  NetworkDiscovery (MulticastLock) and NativeLocalCoop_Android use
  AndroidJavaObject; without it every player build fails with CS1069

## Key Commands

**Run the game:**
1. Open in Unity 6000.3.13f1
2. Scene: `Assets/Doodlebugs/Scenes/Scene01.unity`
3. Play → Start Host

**Multiplayer testing:** Unity menu → ParrelSync → Clone Manager → open clone → Start Client

**Logs (macOS):** `~/Library/Company/Project/Player.log`

**Release:** `git tag v2.1.1 && git push --tags` builds every platform.
Desktop goes through `release.yml` (game-ci, GitHub-hosted); iOS and Android go
through `release-ios.yml` / `release-android.yml` on the self-hosted Mac Mini and
ship to TestFlight and Firebase App Distribution. Both mobile builds run locally too:

```bash
./ci/unity-build.sh android /tmp/out/doodlebugs.apk
./ci/unity-build.sh ios /tmp/out/ios
```

See `ci/README.md` for secrets, runner requirements and the signing details.

## Architecture

**Networking:** Host/Client topology. Client authority for movement, server authority for collisions and damage.

```
Input (Owner) → ShootServerRpc → SpawnBulletClientRpc → all clients instantiate
```

**Ownership pattern:**
```csharp
void Update() {
    if (!IsOwner) return;
    // input only
}
```

**Server authority pattern:**
```csharp
void OnTriggerEnter2D(Collider2D col) {
    if (!IsServer) return;
    // validate collision
}
```

**Spawning/despawning network objects:**
```csharp
NetworkObjectSpawner.SpawnNewNetworkObject(prefab, pos, rot);  // server only
NetworkObjectDespawner.DespawnNetworkObject(networkObject);
```

All network prefabs must be registered in `Assets/Doodlebugs/Prefabs/NetworkPrefabsList.asset`.

## Key Files

| File | Purpose |
|---|---|
| `Scripts/PlayerController.cs` | Movement, collision handling |
| `Scripts/Shooting.cs` | Bullet spawning (ServerRpc → ClientRpc) |
| `Scripts/Bullet.cs` | Projectile, hit detection |
| `Scripts/GameState/GameSetup.cs` | Game flow, singleton access |
| `Scripts/GameState/MatchManager.cs` | Round flow: first to 10 kills / 3 min, results, auto-restart |
| `Scripts/Audio/SfxManager.cs` | One-shot SFX + mobile haptics (clips from `Resources/Sfx`) |
| `Scripts/UI/GameHUD.cs` | HUD: detailed own panel, compact opponents, results overlay |
| `Scripts/Network/ConnectionManager.cs` | Host/client connect flow |
| `Scripts/Input/` | Multi-platform input (desktop/gamepad/gyro/touch) |
| `Scripts/Camera/` | Dynamic screen size, boundaries |
| `Scripts/ForegroundScroller.cs` | Infinite parallax scroll (N sprite copies, loops left) |
| `Scripts/ForegroundTile.cs` | Single destructible tile — disappears on bullet hit |
| `Scripts/ForegroundSpriteGenerator.cs` | Generates placeholder foreground sprite at runtime from alpha |

## Code Rules

- Always `if (!IsOwner) return;` before input handling
- Always `if (!IsServer) return;` before collision/damage logic
- Use `NetworkObjectSpawner` / `NetworkObjectDespawner` — never `Instantiate`/`Destroy` directly on networked objects
- No `SendMessage()` — use direct calls or events
- No hardcoded collision strings — prefer tags or layers
- Test multiplayer changes with ParrelSync before committing

## Foreground / Parallax Destruction Layer

- Foreground scrolls infinitely left via `ForegroundScroller` using N sprite copies,
  N = ceil(cameraWidth / spriteWidth) + 1, so the wrap/heal always happens off-screen
- Foreground bottom edge is anchored to the bottom of the visible screen via
  `BackgroundProfile.foregroundBottomOffset` (0 = flush with screen bottom)
- Each background has its own foreground sprite; tiles are 100×100 px
- Planes fly **behind** the foreground (render order), bullets collide with it
- On bullet hit: `ForegroundTile` destroys itself (local, non-networked — visual only)
- Colliders are `BoxCollider2D` auto-generated per tile from sprite alpha
- `ForegroundSpriteGenerator` creates a runtime silhouette if no sprite is assigned

**Art asset sizes:**
- Background: 4096×4096 px; `ScreenSetup` stretches it to exactly fill the camera,
  so screen bottom == background bottom
- Foreground: strip 4096 px wide @ PPU 100 = 40.96 world units; height in px = ground
  height (100 px = 1 world unit). Camera always shows 54 world units of width
  (`CameraAspectHandler.minVisibleWidth`), so 3 copies exist at runtime
- Foreground textures **must have Read/Write enabled** (tile splitting reads pixels)
- If `maxTextureSize` downscales the texture, the code compensates via `ppuScale`,
  but tiles get coarser in world units
- Power-up icons: `Resources/Sprites/PowerUps/powerup_{health,shield,repair,damage}.png`,
  96×96 px pixel-art (point filtering), wired into `PowerUp.prefab` via the
  `typeSprites` array on `PowerUp.cs` (enum order) AND loaded by the HUD via
  `Resources.Load`; regenerate with Pillow-based script if the style changes

## Match Flow / Audio / HUD

- Boot: GameHUD opens an OPAQUE "Searching" hangar on the very first frame
  (discovery status + version, no arena visible); it morphs into the Waiting
  hangar on host start or the PreBattle/LateJoin hangar from server RPCs.
  The arena is first revealed by battle start or the FLY warm-up button.
  Searching/Waiting overlays are opaque; PreBattle/LateJoin/Intermission
  keep 0.82 alpha (battle/results context behind).
- Game loop (hangar-as-lobby): `MatchManager.GamePhase` is server-authoritative
  — WaitingForPlayers (host opens the Waiting hangar, may FLY out for a
  scoreless warm-up; corner HANGAR button returns) → PreBattleCountdown (2nd
  client connects; `PreBattleSeconds` = 10 s weapon-pick window on every
  device, READY skips early) → Battle → Intermission/Podium → Battle…
  Matches are NEVER auto-started on connect (`ScoreManager` no longer listens
  to `OnClientConnectedCallback`) — only `MatchManager` calls `RestartMatch()`.
- Late join: a client connecting mid-battle does NOT reset the match. Its
  plane spawns parked (`PlayerController.NetInHangar` — hidden, frozen,
  collider off on every client, no shooting), the client requests a state
  snapshot via `RequestStateSnapshotServerRpc` once its player object is ready
  (phase, full score table, timer, round wins, run state — targeted RPCs), and
  gets a personal LateJoin hangar (`LateJoinSeconds` = 10 s, JOIN deploys
  early). Deploy = `ServerRespawn()` (CloudManager position + 2 s
  invulnerability). If a battle drops below 2 clients the server freezes back
  to WaitingForPlayers (timer frozen, scores kept, run state kept).
- Round = first to `MatchManager.KillTarget` (3) kills or
  `MatchManager.TimeLimitSeconds` (3 min); winner by kills → deaths → collisions.
  Results overlay (`ResultsSeconds`, 4 s) → hangar (`HangarSeconds`, 30 s):
  weapon draft (keep-current + 2 random from `WeaponProfile.DraftPool`) + READY
  check; server restarts early when every connected client is ready, otherwise
  at the auto-start timeout (all synced via PlayerController ClientRpcs, same
  routing pattern as ScoreManager).
- Weapons: static registry in `Scripts/Weapons/WeaponProfile.cs` (MG, Twin MG,
  Flak, Heavy Flak, Aero Bomb, Sniper, Rocket, Mine) — every weapon is a
  parametric bullet variant (damage, cooldown, force, gravity, pellets, spread,
  lifetime, explosion radius, acceleration, drag, arm delay, visual scale/tint).
  `Shooting.NetWeaponId` is server-write; hangar picks go through
  `RequestSelectWeaponServerRpc`. The Weapon power-up crate climbs one tier of
  the current weapon's `UpgradesTo` chain and is lost on death/round restart
  (back to the hangar pick). Maturity profiles still scale ROF/force as
  multipliers.
- Explosive weapons (`ExplosionRadius > 0`): server does an AoE
  `Physics2D.OverlapCircleAll` (damages every plane in range, incl. the
  shooter's own outside the spawn grace), then `ExplodeClientRpc` plays the
  boom and calls `ForegroundScroller.DestroyTilesInRadius` — terrain craters
  are local-visual, synced by position+radius. Bomb = dropped (force 0,
  gravity), Rocket = accelerating thrust, Mine = drag-brakes and lurks (arm
  delay 1.2 s, renders below clouds so it hides in them; unarmed projectiles
  don't carve terrain — `ForegroundTile` checks `Bullet.IsArmed`).
- Run (best-of-5): first client to `MatchManager.RoundWinsTarget` (3) round
  wins takes the run → podium (`PodiumSeconds`, 10 s) → full reset (upgrades,
  weapons, wins). Run points (1 účast / +1 top half / +1 round win, tracked
  per clientId on the server) buy `RunUpgrades` in the hangar: Shield/Hull
  (+1 max segment, cap 5), FireRate (+15 %), Engine (+10 % speed), max 2
  levels each. Bonuses live in synced `PlaneStats.NetMax*`/multiplier
  NetworkVariables and apply to all planes of the buying client.
- SFX are procedurally generated 8-bit WAVs in `Resources/Sfx` played through the
  runtime-created `SfxManager` singleton (no scene wiring); haptics =
  `Handheld.Vibrate()` on own death. Regenerate WAVs with a Pillow-free pure
  Python script if the style changes.
- HUD font: Press Start 2P (OFL, license next to the .ttf) in `Resources/Fonts`.
- HUD panels (bottom-right — the parallax foreground is calmest there): the
  local device's planes get detailed panels (segmented
  shield/health 3+3, speed 12 segments, power-up chips with DMG countdown +
  handling %); remote opponents get compact rows (name + K|D|C + 3+3 pips)
  to save space on mobile.
- Kill feed (top-left, max 4 fading lines): all death paths send
  `PlayerController.SyncKillFeedClientRpc` with a `KillCause`; lines like
  "A > B" (shot), "A >< B" (midair), "B CRASHED" / "B LOST".
- Damage smoke: `PlaneVisualEffects` drives a runtime-created ParticleSystem
  from synced `NetHealth` (2 HP light grey, 1 HP heavy dark). On death a local
  `WreckEffect` (visual only, non-networked) tumbles down with smoke and
  explodes on the ground. Shared particle texture/material in `EffectAssets`.
- Mobile gyro: gravity is low-pass filtered, dead-zone remap is continuous,
  response has an expo curve, and the neutral hold angle auto-calibrates
  ~0.5 s after start (`MobileInputProvider.Recenter()` re-captures it).

## Plane Skins & Shapes

- A plane's look = (shape, skin): `PlaneModelCatalog` (16 concepts, 15
  shipped — canard never passed the gate; all free) × `PlaneSkinCatalog`
  (50 liveries: 12 free + 4 IAP bundles via `IAPManager`, store products not
  created yet so premium skins are effectively unbuyable — the paywall is
  deliberately inert until then). Both are server-write NetworkVariables on `PlaneAppearance`
  (`NetModelId` / `NetSkinId`); hangar picks go through
  `RequestSelectModelServerRpc` / `RequestSelectSkinServerRpc`.
  `PlaneSkinManager` (scene NetworkObject — `Doodlebugs → Setup Plane Skin
  Manager` once, commit Scene01) arbitrates uniqueness **per shape** - no two
  players fly the same silhouette, whatever it is painted (2026-09-06,
  superseding plan 23 D1a's per-combo rule). Skins may repeat freely: two
  identical liveries on different shapes still read apart mid-dogfight, two
  identical shapes in different colours do not. Picker = PLANE button in every
  hangar (`GameHUD`): shape row above the skin grid, every card previews the
  combo with the other current half; only shape cards can show TAKEN.
- **The hitbox is not the sprite**: one shared 50×50 px `BoxCollider2D` on
  `PlaneHolder` for every shape. Every model must pass the envelope gate
  (`tools/planes/gate.py`, mirrored in `Doodlebugs → Validate Plane
  Models`) — never add per-model colliders or read sprite bounds in gameplay.
- Assets: `Resources/Sprites/PlaneSkins/skin_<key>.png` (baked skins on the
  original BiPlane1) + `PlaneSkins/Swatches/swatch_<key>.png` (128×128
  patterns); `Resources/Sprites/PlaneModels/model_<key>.png` (red base =
  starter livery) + `model_<key>_mask.png` (R paint, G tail accent, A alpha).
  Skins are composited onto non-base shapes **at runtime**
  (`PlaneModelCatalog.LoadSprite`, textures must stay Read/Write) with the
  same luminance multiply the offline skin bake uses; the tail accent stays
  pure red so `ColorReplace` still tints it per player.
- Pipelines (SPARK ComfyUI, `tools/backgrounds/spark_backgrounds.py` client):
  `tools/skins/generate_skins.py` (render / swatches / apply) and
  `tools/planes/generate_planes.py` (render → post → sheet → apply; FLUX
  Kontext with BiPlane1 as reference, RMBG + white-key, normalise, quantise,
  mask split, gate). Keys/ids in `skins.py` / `planes.py` must match the C#
  catalogs; `apply` writes the `.meta` files (`tools/planes/unity_meta.py`).

## Projectile Elements

- A plane's **shape** decides what its projectiles are made of
  (`PlaneModelDef.Element`, plan 24 decision D1): dragon = Fire, unicorn =
  Lightning, wasp/fly = Venom, spacecraft + Rocket = Plasma,
  goose/ornithopter/paper plane/delta glider = Air, everything else = Metal.
  Skins stay pure liveries. Registry: `Scripts/Weapons/ProjectileElement.cs`
  (ids go over the network - stable once shipped, like `WeaponType`).
- **Presentation only** (D5). No element changes damage, cooldown or range;
  the weapon draft stays the only place balance lives. Elements change the
  projectile sprite, its trail, the impact/explosion visuals and the sounds.
- `Bullet` syncs a server-write `_elementId` next to `_weaponId`;
  `Shooting.SpawnBullet` resolves it from the shooter's
  `PlaneAppearance.NetModelId`. Both impact and explosion ClientRpcs carry
  the element, so a late joiner and every spectator see the same splash.
- **Everything degrades to the old look.** Sprite lookup order is
  `Sprites/Projectiles/<element>/<form>` → the same under `metal` →
  `WeaponProfile.ProjectileSpriteName` → the shared tracer tinted with the
  element colour. Flipbooks fall back `Effects/<element>/<kind>` →
  `Effects/metal/<kind>` → the legacy `explosion.prefab`. Sounds fall back to
  the generic procedural clips. A build with no generated art still runs.
- Eight weapons draw as **six forms** (`ElementProfile.SpriteForm`): tracer
  (MG, Twin MG), pellet (Flak, Heavy Flak), bomb, bolt (Sniper), rocket,
  mine. Sounds collapse further into two groups, `gun` and `heavy` (D6) -
  under the shot pitch jitter per-weapon clips are inaudible.
- Trails and bursts are **runtime ParticleSystems**, not assets:
  `EffectAssets.CreateTrailSystem` / `CreateBurst` with per-element presets
  and five generated 32x32 particle shapes (soft circle, spark, droplet,
  square, feather). The trail is **detached on despawn** (`Bullet.ReleaseTrail`)
  or its last puffs would vanish with the bullet.
- Flipbooks are numbered PNGs (`<kind>_00.png`...) played by
  `FlipbookEffect` via `EffectLibrary` - no Animator controller per element.
  Impact = 6 frames 64x64, explosion = 8 frames 96x96 scaled by blast radius.
- **Art assets:** `Resources/Sprites/Projectiles/<element>/<form>.png`
  (point filter, PPU 100), `Resources/Sprites/Effects/<element>/<kind>_NN.png`,
  `Resources/Sfx/Elements/<element>/sfx_{shoot_gun,shoot_heavy,impact,explosion}.wav`
  (mono 44.1 kHz 16-bit, matching the procedural set).
- **Pipeline:** `tools/weapons/` - `generate_projectiles.py` (FLUX on SPARK),
  `generate_effects.py` (FLUX contact sheet, or `--procedural` Pillow
  flipbooks that need no GPU), `generate_sfx.py` (ElevenLabs sound-generation
  API, key in `ELEVENLABS_API_KEY`). Same render/post/sheet/apply shape as
  `tools/planes/`. Keys and ids in `elements.py` / `forms.py` must match
  `ProjectileElement.cs`. Design notes:
  `Prompts/24-CLAUDE-PLAN-projectile-elements.md`.

## Backgrounds / Parallax Profiles

- A map = `BackgroundProfile` ScriptableObject (`Prefabs/Backgrounds/Profile_*.asset`):
  background sprite + optional foreground sprite + scroll speed (-2) / bottom
  offset (0) / scale (1). Registered in the `profiles` array on the
  `BackgroundManager` object in Scene01; selection is a server-write
  `NetworkVariable<int>` so clients and late joiners always match.
- The arena ROTATES every round: `MatchManager` calls
  `BackgroundManager.SelectRandomBackground()` at battle start and at the top
  of every intermission (terrain rebuild pops behind the hangar overlay).
  `SelectRandomBackground` never repeats the current index.
- **Asset sizes:** background 4096×2732 px landscape PNG (Sprite, PPU 100,
  pivot Center, Read/Write OFF — it is only stretched by `ScreenSetup`).
  Foreground strip: 4096 px wide, height = terrain height in px
  (100 px = 1 world unit, existing strips ~1250-1300 px), transparent
  silhouette PNG (Sprite, PPU 100, pivot **Bottom-Left**, **Read/Write ON** —
  tile splitting reads pixels). No foreground = runtime placeholder.
- **Pipeline for new maps:** drop `Sprites/Background/<Name>.png` and
  optionally `Sprites/Foreground/<Name>_fg.png`, then run
  **Doodlebugs → Sync Background Profiles** (menu,
  `Editor/BackgroundProfileSync.cs`) — it fixes import settings, creates
  `Profile_<Name>.asset` and re-registers all profiles on the scene's
  BackgroundManager. Textures already claimed by a profile are skipped, so
  the sync is idempotent. In batchmode/CI use
  `-executeMethod BackgroundProfileSync.SyncBatch` — it opens Scene01 first
  (the menu item works on whatever scene is open, which in batchmode is an
  empty untitled one). Profiles can also be authored by hand:
  Create → Doodlebugs → Background Profile, then add to the scene array.
- **Current maps (2026-09-04):** Metropolis, MistyPeaks, DuneSea, Volcano,
  Orbit — generated by `tools/backgrounds/` (FLUX on SPARK, `--seam blend`).
  The seven photo-era maps (Manhattan, Teheran, SierraNevada, Sky, Rainbow,
  Sunny_beach, Smart_wings) and their sprites were deleted with this batch;
  they are in git history if one is ever wanted back.

## LAN Discovery / Platform Notes

- Flow: every instance listens for UDP broadcasts on port 47777 (5 s desktop / 10 s
  mobile, + per-instance 0–2 s jitter) → on timeout it becomes host, broadcasts once
  per second, game runs on UDP 7777. First device to give up searching hosts;
  everyone else joins.
- Split-brain guard: a host with an empty lobby keeps listening (host-watch mode);
  when it hears a competing host's broadcast, the one with the larger `instanceId`
  shuts down and joins the other (deterministic tie-break, mirrors the NativeP2P
  path).
- Broadcast goes to the subnet-directed address computed from the interface netmask
  (works on non-/24 nets like iPhone hotspot) plus 255.255.255.255 as fallback.
- Own broadcasts are filtered by a per-process `instanceId` (not by IP), so
  ParrelSync clones on one machine can discover each other.
- **iOS:** `iOSLocalNetworkPostProcess` auto-adds `NSLocalNetworkUsageDescription`
  at build time. UDP broadcast on iOS 14+ additionally needs the Apple-granted
  `com.apple.developer.networking.multicast` entitlement — flip
  `AddMulticastEntitlement` in that file to true once Apple grants it
  (request: https://developer.apple.com/contact/request/networking-multicast).
  Without it, iOS devices may not pair over broadcast even after the user accepts
  the local-network prompt.
- **Android:** `NetworkDiscovery` acquires a `WifiManager.MulticastLock` while
  listening (many devices drop inbound broadcasts without it) and releases it after.

## Known Issues

- Bullet/Cloud collision: occasionally de-syncs visually (investigate if reproduced)
