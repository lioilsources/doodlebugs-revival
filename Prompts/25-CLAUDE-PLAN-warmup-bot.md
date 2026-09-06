# Warm-up Bot — Implementation Plan

Status (2026-09-06): **implemented** in the same session as the plan -
`Scripts/Bot/BotManager.cs`, `Scripts/Bot/BotBrain.cs`, the seams listed in
§5. Verified by batchmode compile; device verification per §7 is the owner's
next step.

## Context

The Waiting lobby's FLY warm-up is an empty sky: one plane over a rotating
arena until a second device connects. The owner wants an AI plane there that
flies, shoots, loops, recovers when its engine cuts, and turns up in a fresh
unoccupied look every time it appears — a living demo of the game, never a
combatant. It must not join battles, must not start or block one, must not
count as a player anywhere (score, roster, READY), and must not take a shape
away from a human.

Decisions taken with the owner (2026-09-06):

| # | decision |
|---|---|
| D1 | **Host-spawned, networked** `PlaneHolder` (server-owned NetworkObject). |
| D2 | **Bullets do normal damage.** The bot never aims at humans; a stray hit hurts. |
| D3 | **Bot yields on shape.** Never TAKEN in the picker; if a human equips its shape, the bot re-picks mid-air. |
| D4 | **Engine recovery is a scripted reflex.** No learning layer is built (noted as a future extension). |
| D5 | **UI: kill feed only, as "BOT".** No HUD panel, no roster row, no score rows, no haptic. |
| D6 | Exists only in `GamePhase.WaitingForPlayers`; despawned the frame the phase leaves it. |

**One thing the owner must know (found during design):** at HEAD a remote
client can never be in `WaitingForPlayers` — the 2nd connect always flips to
PreBattle (MatchManager.cs:219) and freeze-to-waiting leaves only the host.
So today **only the host device ever sees the warm-up bot** (and its couch
co-op pilots). D1 still holds architecturally (a PlaneHolder *is* a
NetworkObject, and any future "warm-up until READY" lobby gets the bot for
free), but the "clone sees the bot" test does not exist yet.

No AI/bot/scripted-input code exists in the repo (verified).

## 0. Findings that shape the plan

- **Reuse the plane.** Flight, wrap, death, respawn, smoke, explosion, weapon
  and element trail already live in `PlayerController`/`Shooting`/`PlaneStats`.
  So: PlaneHolder + a bot flag + an input override, not a second flight model.
- **Host-owned = flies for free.** `NetworkObjectSpawner.SpawnNewNetworkObject`
  makes the server the owner; on the host `IsOwner` is true so movement
  (`FixedUpdate:552`) and `Shooting.Update:65` run there; `ClientNetworkTransform`
  is owner-authoritative; `MatchManager.ShouldSpawnHiddenInHangar:186` is
  false for host-owned → spawns un-parked.
- **Phases cannot see it.** Waiting→PreBattle, the `remaining < 2` freeze,
  `AllClientsReady` and the lobby cap all iterate `ConnectedClientsIds`.
- **The input seam is four methods** (`IInputProvider`: horizontal = rotation
  with +1 = clockwise = nose down when flying right; vertical = throttle;
  shoot; update). No engine toggle — the engine is emergent: stall at
  `minSpeed 2`, auto-relight in `movePlane:651` when the nose is inside the
  maturity window. Both `PlayerController.GetInputProvider:446` and
  `Shooting.GetShootInput:86` dispatch to human devices; a host-owned plane
  with the default index would **mirror the host's stick and trigger**.
- **Identity is `(OwnerClientId, max(LocalPlayerIndex,0))` everywhere** (HUD
  key, score row, look claim, self-hit, kill credit). Index -1 collapses
  onto host P1's `(0,0)`. The bot gets a reserved index; every index consumer
  keys a dictionary or uses modulo (verified: `PlayerColorManager:119`,
  `CloudManager:564`, `PlayerController:837`, `LocalPlayerManager:150`
  bounds-checks). Index ≥ 0 also switches the owner glow off
  (`PlaneVisualEffects:243`).
- **Numbers.** Rotation is unclamped on a kinematic body. Engine-on loop radius
  `r = 900/(π·rotateSpeed·Handling)` = **5.73 u** for Novice, speed-independent;
  full loop 3.6 s at v=10. Engine-off rotation 800°/s. Novice relight window
  (asset values z∈(-0.91,-0.41)) = heading **-131°..-48°** (0° = flying right,
  -90° = straight down). World: 54 u wide (scene override), Ground kill line
  ≈ -9.16, Space engine-cut line ≈ +17.57 at 16:9 (≈ -6.2/+15.1 on 20:9) —
  read at runtime from the `Ground`/`Space` border colliders, never hardcoded.
- **Name trap.** `InitializeOwnerDelayed:302` overwrites `netPlayerName` with
  the device name one frame after spawn — must be guarded for bots.

## 1. Files

New (commit their `.meta` too):

| file | responsibility |
|---|---|
| `Assets/Doodlebugs/Scripts/Bot/BotManager.cs` | Server-only lifecycle singleton created by `GameSetup` next to `MatchManager`. Spawn / despawn / replace the one bot, pick fresh looks, enforce yield, ticked from `MatchManager.Update`. |
| `Assets/Doodlebugs/Scripts/Bot/BotBrain.cs` | `MonoBehaviour, IInputProvider`, added at runtime on the host only. State machine + overrides + burst shooting + human-equivalent input ramp. `[DefaultExecutionOrder(-50)]` so it decides before `PlayerController.FixedUpdate`. |
| `Prompts/25-CLAUDE-PLAN-warmup-bot.md` | This plan in the repo's plan-doc style (status line, decisions, risks). |

Modified: `PlayerController.cs`, `Shooting.cs`, `Skins/PlaneAppearance.cs`,
`Skins/PlaneSkinManager.cs`, `UI/GameHUD.cs`, `GameState/MatchManager.cs`,
`GameState/ScoreManager.cs`, `GameState/GameSetup.cs`, `CLAUDE.md` — exact
lines in §5. No new prefab, no `NetworkPrefabsList` change (the bot is the
already-registered `PlaneHolder` = `NetworkManager.NetworkConfig.PlayerPrefab`).

## 2. Lifecycle — `BotManager`

- Created in `GameSetup.CreateScoreManager()` (GameSetup.cs:74-90) after the
  MatchManager block; `Instance`; every public method starts with
  `if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;`.
- **Tick seam** — `MatchManager.Update:413-418` becomes:
  ```csharp
  if (Phase == GamePhase.WaitingForPlayers) {
      BackgroundManager.Instance?.ServerTickWarmUpRotation();
      BotManager.Instance?.ServerTickWarmUp();
  } else {
      BotManager.Instance?.ServerEnsureDespawned();
  }
  ```
  Polling the phase covers every present and future `Phase = …` site.
- **Spawn** when Waiting and `_bot == null`: after `BotSpawnDelaySeconds = 2`
  (clouds and P1 exist by then; the hangar opens at 0.5 s).
- **Despawn** the tick after the phase leaves Waiting:
  `NetworkObjectDespawner.DespawnNetworkObject(_bot.NetworkObject)`.
- **Bot death** (new `PlayerController.OnServerDeath` event, fired first thing
  in `HandleDeathAndRespawn:600`): mark pending, `_spawnAt = now + 3 s`; next
  tick despawn the old object (after its explosion RPC went out) and spawn a
  **new** one with a **new** look. `CloudManager`'s respawn queue null-checks
  destroyed entries.
- **After a freeze** (`ServerFreezeToWaiting`): the next tick spawns a fresh
  bot with a new look ("nový run").

**Spawn procedure** (synchronous, one call stack):
1. `prefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab`.
2. `pos = CloudManager.Instance?.GetInitialSpawnPosition() ?? (Random(-18,18), 8, 0)`.
3. `go = NetworkObjectSpawner.SpawnNewNetworkObject(prefab, pos, Quaternion.identity)`.
4. `pc.ServerSetBotIdentity()` → `NetIsBot = true`, `netLocalPlayerIndex = BotLocalPlayerIndex`,
   `netPlayerName = "BOT"` (Owner-write; server is the owner on the host).
   Not `SetLocalPlayerIndex` (it would name it "P100" for a frame).
5. `appearance.ServerSetLookUnclaimed(PickFreshLook())`.
6. `pc.PlaneStats.ResetStats()` → 2 s invulnerability so a fallback-position
   spawn cannot instantly midair a human.
7. `brain = go.AddComponent<BotBrain>(); brain.Init(pc); pc.SetInputOverride(brain);`
   — before the first `FixedUpdate`, or a mobile host's tilt would fly it.
8. `pc.OnServerDeath += OnBotDied`.

Identity constants on `PlayerController`: `BotLocalPlayerIndex = 99`,
`BotDisplayName = "BOT"`, `static bool IsBotIdentity(ulong clientId, int idx) => idx == 99`.

## 3. Look selection

```
shapes = PlaneModelCatalog.Available.Where(id => !IsModelTakenByAnyone(id) && id != _lastModelId)
if empty: drop the "!= last" filter; if still empty: { BaseModelId }   // unreachable below 28 humans
model = random(shapes)
skins  = PlaneSkinCatalog.All.Where(s => !s.IsPremium && s.Id != _lastSkinId)   // 12 free liveries; never advertise unbuyable ones
skin   = random(skins); remember both as _last*
```
Applied via new server-only `PlaneAppearance.ServerSetLookUnclaimed(model, skin)`
which writes `NetModelId/NetSkinId` **without** `TryClaim`. The bot therefore
never appears in `PlaneSkinManager.Claims`: never TAKEN, never a roster
thumbnail. `PlaneAppearance.ServerEnsureClaim:57` must return early for bots
(the coroutine yields one frame; `NetIsBot` is set synchronously after
`Spawn()`, so the flag is visible in time).

**Yield (D3)**: every tick, `if (IsModelTakenByAnyone(bot.NetModelId))` →
`ServerSetLookUnclaimed(PickFreshLook())`. A human's pick succeeds server-side
(nothing to collide with); the bot swaps within a frame; `_lastModelId` keeps
it from bouncing back. `IsModelTakenByAnyone(modelId)` = new one-loop overload
on `PlaneSkinManager` next to `IsModelTaken:141`.

## 4. The brain — `BotBrain`

**Accessors added to `PlayerController`** (one-liners):
`HeadingDegrees` (= `Atan2(right.y, right.x)` in degrees), `IsInSpace`,
`MaxFlightSpeed`, `MinFlightSpeed`, `EngineRestartHeadingWindow`
(= `2·asin(engineRestartMin/Max)` in degrees), `EngineOnTurnRadius`
(= `900/(π·rotateSpeed·Handling)`). `IsEngineOff` and `Speed` exist.

**World bounds** read once in `Init()`: `groundTop = Find("Ground").bounds.max.y`,
`spaceBottom = Find("Space").bounds.min.y`. All altitudes below derive from
these and the live radius `r`.

**Steering primitive** (sign derived from `rotatePlane:731-751`: x>0 →
clockwise → heading decreases):
```csharp
float SteerTo(float targetDeg, float gainDeg) =>
    -Mathf.Clamp(Mathf.DeltaAngle(player.HeadingDegrees, targetDeg) / gainDeg, -1f, 1f);
float NoseUpInput  => -Mathf.Sign(transform.right.x);       // -1 flying right, +1 flying left
float LevelHeading => transform.right.x >= 0f ? 0f : 180f;
```

**Fairness ramp**: outputs are `MoveTowards`-ramped at `2/s` = the keyboard
provider's `smoothingSpeed` (DesktopInputProvider.cs:15), so the bot never
out-turns a human. `UpdateInput()` is a no-op; decisions run in `FixedUpdate`.

**Tick order**: hangar guard → sample (heading, y, v, engineOff, inSpace, r)
→ engine-off rising edge ⇒ RECOVER → overrides → state body → ramp → shoot.

| state | entry | behaviour | exit |
|---|---|---|---|
| **CRUISE** | default | Altitude band `[groundTop+6, spaceBottom−5]`; target = band centre + Perlin wander (±40 % of half-band); `pitch = clamp((yT−y)·6°/u, ±30°)` + Perlin heading wander ±15°; `x = SteerTo(base ± pitch, 30°)`. Throttle toward `vT = 7 ± 1.5` (slow Perlin); stall guard `v<4 ∨ pitch>20°` ⇒ +1. | timers |
| **LOOP** | every 10–20 s; engine on; `v ≥ 7`; fit: loop-up needs `y + 2r + 2 < spaceBottom`, loop-down needs `y − 2r − 2.5 > groundTop`; pick the side with room, skip if none | `x = ±NoseUpInput` full, throttle +1 (throttle 5/s swamps climb drag 1/s, so the circle stays 2r tall). Accumulate `Σ DeltaAngle(prev, heading)`. | `|Σ| ≥ 360` → CRUISE |
| **REVERSE** | every 8–16 s | LOOP code to 180°; direction by altitude; fit uses `r`. Flying left = upside-down sprite, like humans. | `|Σ| ≥ 180` → CRUISE, base flipped |
| **ZOOM_CLIMB** (the deliberate stall that shows the recovery) | every 30–50 s; engine on; `y ∈ [groundTop+12, spaceBottom−8]` | nose to `base ± 75°`, throttle −1 → speed bleeds ~6/s, cut in ~1 s after ~3.5 u of climb | engine off → RECOVER; 4 s cap → CRUISE |
| **RECOVER** (D4 reflex) | engine-off rising edge, any cause | **A, engine off:** if `IsInSpace`: `SteerTo(−90°, 20°)` and wait (relight is impossible until below `spaceBottom`; pointing down exits fastest). Else `SteerTo(−90°, 20°)` — dead centre of the −131..−48 window, throttle +1 pre-armed. At 800°/s under the 2/s ramp the nose is in the window in ≈ 0.45 s; `movePlane:651` relights. **B, relit:** hold heading with throttle +1 for 0.4 s (dive 3 + throttle 5 = +8 u/s), then pull-out `SteerTo(LevelHeading, 30°)`, throttle +1. | `|right.y| < 0.25 ∧ v ≥ 4` → CRUISE, base = LevelHeading; 6 s abort → CRUISE |
| **AVOID_GROUND** (override) | engine on, descending, `y < groundTop + r·(1−cos φ) + 2.5` (φ = dive angle; the pull-out bottoms out `r(1−cos φ)` lower: 5.73 u from vertical, 2.4 u from 55°) | `SteerTo(LevelHeading, 30°)` full, throttle +1 | `right.y ≥ 0` |
| **AVOID_SPACE** (override) | climbing, `y > spaceBottom − 4` | nose to `LevelHeading`, throttle +1 | `right.y ≤ 0` |

Recovery altitude budget (why ZOOM_CLIMB needs `groundTop+12`): ≈ 1 u falls
during the 0.45 s rotate, ≤ 1.5 u in the speed build, ≤ r = 5.73 u in the
pull-out, + 2.5 margin ≈ 11 u. Known quirk: `EngineOn():699` sets
`speed = |velocity|`, which right after a nose-up stall is < 2, so the engine
re-cuts for a few ticks until gravity pushes |v| past 2 — the bot just keeps
its nose in the window; resolves in < 0.3 s.

**SHOOT**: bursts of 0.4–1.2 s every 2–6 s, only in CRUISE/LOOP/REVERSE with
the engine on and > 1 s after spawn; `GetShootInput()` returns
`_bursting && !Suppressed()` (holding true is safe: `Shooting.Update:73` is
cooldown-gated). Non-aggression is structural (no state reads a human's
position for steering) plus a tunable suppression: any non-bot, non-hangar
plane within `FriendlyFireConeDegrees = 20` and `FriendlyFireRange = 15 u`
ahead mutes the trigger; 0 disables.

Future extension (not built, D4): an epsilon-greedy bandit over 3–4 recovery
headings with reward `1/timeToRelight` and a crash penalty, owned by
BotManager for the session; PlayerPrefs persistence is one line. Documented in
Prompts/25 as "next step if the reflex reads as too mechanical".

## 5. Pollution filter — exact sites

`NetIsBot` = server-write `NetworkVariable<bool>` on `PlayerController`,
`IsBot` accessor; synced so late joiners' HUDs skip it from frame one.

`PlayerController.cs`
- `:59` add `NetIsBot`/`IsBot`, the identity consts, `ServerSetBotIdentity()`,
  `public event Action OnServerDeath`, `SetInputOverride(IInputProvider)`.
- `:302` `netPlayerName.Value = IsBot ? BotDisplayName : GetDeviceDisplayName()`.
- `:446` `GetInputProvider` → `public`, first line `if (_inputOverride != null) return _inputOverride;`.
- `:592` (OOB), `:796` (combat), `:1001` (Ground): `if (!IsBot) AddDeath(...)`.
- `:800-803` `if (credited && !IsBotIdentity(_lastAttacker…)) AddScore(...)` — keep `credited` so the feed prints "BOT > name".
- `:600` first line of `HandleDeathAndRespawn`: `OnServerDeath?.Invoke();`
- `:810` `if (!IsBot) PowerUpManager.SpawnPowerUp(...)` — bot deaths do not litter crates.
- `:854` `if (IsOwner && !IsBot) SfxManager.Haptic();`
- `:1010` `if (!IsBot) AddPlaneCollision(...)` (the human side counts on its own object).

`Shooting.cs:86-109` → `return playerController != null && (playerController.GetInputProvider()?.GetShootInput() ?? false);` (also removes the duplicated dispatch).

`Skins/PlaneAppearance.cs`
- `:57` `if (!IsServer || PlaneSkinManager.Instance == null || _playerController.IsBot) return;`
- new `ServerSetLookUnclaimed(int model, int skin)` with a WHY comment.

`Skins/PlaneSkinManager.cs` — after `:148` `public bool IsModelTakenByAnyone(int modelId)`.

`UI/GameHUD.cs`
- `:382-385`, `:399` `if (player.IsBot) continue;` — no panel ⇒ no lazy `GetStats` row ⇒ clean `AllStats` for `EndRound`/`AwardRunPoints`, no roster row, READY denominator untouched.
- `:808` `if (PlayerController.IsBotIdentity(clientId, localPlayerIndex)) return BotDisplayName;`
- `:1454` `if (p.IsOwner && !p.IsBot) return p;` — picker/JOIN/READY/upgrades must target a human.
- `:1744`, `:2816`, `:2829` `if (!p.IsOwner || p.IsBot) continue;`
- `:1939` roster `LooksOf` fallback: skip bot appearances.

`GameState/MatchManager.cs`
- `:415-418` tick/despawn branch (§2).
- `:320`, `:864` `if (player.IsBot) continue;` before `SafeDeploy` (defensive).
- `:655` `if (player.IsServer && !player.IsBot)` — never route phase RPCs through the object that despawns on the very connect that triggers them.

`GameState/ScoreManager.cs`
- `:113`, `:211`, `:225` carrier loops `&& !player.IsBot`.
- `:147`, `:178`, `:193` first line `if (IsBotIdentity(clientId, idx)) return;` — defense in depth against a forgotten path creating `"0_99"`.

`GameState/GameSetup.cs:~90` create `BotManager`. `CLAUDE.md`: "Warm-up bot" bullet under Match Flow.

No change: `Bullet.cs` (`IsShootersOwnPlane` works with `(0,99)`; bot bullets hit
humans per D2 and never the bot), `LocalPlayerManager.cs`, `CloudManager.cs`,
`PlaneVisualEffects.cs`, `ConnectionManager.cs`.

## 6. Network

- Owner = host = server: `FixedUpdate`/`Shooting.Update` run on the host, the
  owner-authoritative transform replicates, Owner-write flight variables are
  legal. Identical to the host's own P1.
- `ShootServerRpc` is invoked locally, `SenderClientId = 0`, bullets carry `(0, 99)`.
- `NetIsBot`, index, look are server-write NetworkVariables set synchronously
  after `Spawn()` → same send batch as the create message; late joiners get
  them in the sync payload.
- `BotBrain` exists only on the host (`AddComponent` server-side); clients
  render the bot from transform + variables. Bandwidth = one couch pilot.

## 7. Verification

Batchmode compile first (`Unity -batchmode -nographics -quit -projectPath …`,
zero `error CS`). Then on the host (log prefixes `[BotManager]`, `[BotBrain]`):

1. Host alone, FLY: within 2 s a plane named BOT with a non-base shape and a free skin; no HUD panel, no glow; own panel unchanged.
2. PLANE picker: bot's shape not TAKEN; equip it → bot swaps within a frame (`Yielded shape`), human keeps the pick.
3. A loop within ~20 s, ≈ 11.5 u tall; `LOOP done in 3.x s`.
4. Within ~50 s a zoom climb → engine off → nose drops → relight → pull-out; `RECOVER relit in 0.4–0.7 s`. Temporarily set the space margin to 0 to force a Space cut and see the fall/relight below `spaceBottom`.
5. Shoot the bot: feed "name > BOT", own K unchanged, no `0_99` row, no crate, no haptic on a phone host; 3 s later a new bot with a **different** shape and skin.
6. Get shot by the bot: feed "BOT > name"; midair: "name >< BOT", only the human's C increments.
7. Clone connects → `Despawned` on the host at once; PreBattle roster shows 2 rows, never BOT; battle deploys 2 planes. Clone quits → freeze to Waiting → bot returns after 2 s with a new look; run state intact.
8. Grep the host log: no BOT spawn outside Waiting.
9. Android/iOS host build: the bot does not mirror the phone's tilt.

## 8. Risks / open

- **Host-only visibility today** (see Context). Not a bug in this plan; a lobby that keeps players in Waiting until READY would make the bot a shared sight.
- Yield swaps the sprite mid-flight; deferring to the next respawn is a one-liner if it reads badly.
- Friendly-fire cone (20° / 15 u) needs a feel pass; 0 disables.
- Tall phone hosts (20:9 ⇒ 21 u of sky) skip loops more often — all bands are live, verify on device.
- Damage lowers Handling and inflates `r` (5.73/H); fit checks use the live value, so a battered bot loops rarely. Acceptable.
- The bot may collect crates (weapon tier up) — kept as warm-up fun; one line to disable if unwanted.
