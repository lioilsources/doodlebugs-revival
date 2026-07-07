# CLAUDE.md

Instructions for Claude Code when working in this repository.

## Project

2D multiplayer arcade air combat game. Unity 6000.2.9f1. WWI biplanes. Physics-based movement, one-hit kill, WiFi/couch multiplayer.

## Tech Stack

- Unity 6000.2.9f1
- Netcode for GameObjects 1.14.1
- Unity Input System 1.7.0
- Platforms: Desktop (Win/macOS), iOS, Android

## Key Commands

**Run the game:**
1. Open in Unity 6000.2.9f1
2. Scene: `Assets/Doodlebugs/Scenes/Scene01.unity`
3. Play → Start Host

**Multiplayer testing:** Unity menu → ParrelSync → Clone Manager → open clone → Start Client

**Logs (macOS):** `~/Library/Company/Project/Player.log`

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
