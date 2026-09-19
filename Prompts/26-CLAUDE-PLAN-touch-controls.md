# Touch Controls — Implementation Plan

Status (2026-09-19): **plan only, no code.** Owner's ask: gyro is hard for
people, keep it as an optional switch in settings; default to an on-screen
joystick bottom-right (up/down = throttle, left/right = turn down/up) and an
on-screen fire trigger bottom-left with a circular charge indicator for
weapons that have a rate; a second trigger ready for a plane that carries two
weapons, same charge behaviour; a picture of the weapon on the trigger.

ROADMAP: Phase 1 (v3.0, solo-first) — it is an App Store prerequisite as much
as a comfort fix: a reviewer with a phone on a desk cannot tilt.

## Decisions

Taken from the ask (owner, 2026-09-19):

| # | decision |
|---|---|
| D1 | **Joystick is the default scheme.** Gyro stays, opt-in from settings. Setting persists across launches. |
| D2 | **Stick axes map 1:1 onto `IInputProvider`.** Y → `GetVerticalInput` (up = speed up). X → `GetHorizontalInput` (+1 = clockwise rotation = nose down while flying right, nose up while flying left — same as gyro and keyboard today). |
| D3 | **Fire is a trigger button, bottom-left, with a radial charge ring** driven by the weapon cooldown. A shot leaves only at full charge. |
| D4 | **A second trigger exists for a second weapon slot** and appears only when the plane has one. Same ring, own icon. |
| D5 | **Every trigger shows its weapon's icon.** |

Proposed — owner confirms or overrides before code:

| # | proposal | default | why |
|---|---|---|---|
| D6 | Trigger semantics | **Hold = auto-fire at the cooldown cadence, tap = one shot.** Desktop/gamepad stay press-per-shot. | 2.5 taps/s on glass (MG) is a cramp; the ring already says when the next shot comes. No interface change: the provider reports "pressed" every frame while held and `Shooting.Update:73` gates on the cooldown. |
| D7 | Joystick base | **Floating**: base appears where the thumb lands inside the right-bottom zone, knob follows within 90 u, base fades on release. | Fixed bases force a look-down; floating works for any grip. Zone = right 40 % × bottom 60 % of the safe area. |
| D8 | HUD collision | **Own detailed panel (bottom-right, `GameHUD.cs:362-366`) moves up above the stick zone on touch schemes** (`anchoredPosition.y` +240 u); opponent rows travel with it. Desktop layout untouched. | The stick zone is exactly where the own panel sits today. |
| D9 | Gyro scheme keeps the triggers | **Yes.** "Touch anywhere to shoot" (`MobileInputProvider.cs:139-146,172-185`) goes away in both schemes. | One firing model, one charge ring; a tap on the stick must never fire. |
| D10 | Second-slot plumbing depth | **Network + firing plumbing now, hangar draft for the second weapon later** (Phase 4 loadout). `Shooting` gets `NetWeaponId2` (-1 = none); a second pick is one draft card away. | The ask says "prepare"; the draft UI is a balance decision, not a controls one. |
| D11 | Denied press (not charged) | **Silent.** No sound, no shake; the ring is the feedback. | Rapid tapping would spam a "denied" tick. |
| D12 | Where the toggle lives | **New `Scripts/UI/SettingsOverlay.cs`, opened by a SETTINGS button in every hangar** (bottom-left column under SCENE). One row today: CONTROLS `JOYSTICK / GYRO` (+ RECENTER when gyro). Phase 2's shake/music/SFX/haptics rows go in the same class. | No settings screen exists; `GameHUD` must not grow (ROADMAP §3.9). |
| D13 | Weapon icons | **96×96 pixel-art PNGs, one per `WeaponType`, procedural Pillow script; fallback = the metal projectile sprite of the weapon's form, tinted.** | Same style and degrade rule as power-up icons and projectile elements. A build without the art still runs. |

## 0. Findings that shape the plan

- **The interface is already the stick.** `IInputProvider.cs:4-25`: horizontal =
  rotation (-1..1), vertical = throttle (-1..1), shoot = "pressed this frame",
  update. `PlayerController.HandleMovement:689-707` reads both axes every
  FixedUpdate; `movePlane:743-747` integrates throttle at `throttleRate` 5 u/s².
  No engine toggle, no flip button — the whole flight model is those two axes.
- **On a phone there is exactly one provider path.** `LocalPlayerManager`
  destroys itself on mobile (`LocalPlayerManager.cs:32-33`), so
  `PlayerController.GetInputProvider:510-526` returns `_inputOverride` (bot)
  or `InputManager.Instance.InputProvider`. `InputManager.InitializeInputProvider:100-147`
  builds a `MobileInputProvider` and prefers a gamepad when one is present;
  `CheckInputDeviceSwitch:198-214` flips back to touch on `primaryTouch.press`.
  The touch scheme plugs in as **the** mobile provider; gyro becomes a source
  inside it, not a sibling.
- **Shooting is press-gated by cooldown, on the owner.** `Shooting.Update:59-81`:
  `shootPressed && Time.time >= _lastFireTime + fireRateCooldown` → `_lastFireTime = now`
  → `ShootServerRpc`. `fireRateCooldown:38` = `CurrentWeapon.Cooldown × maturity ÷ run-upgrade`.
  So the charge ring is local, exact and needs no netcode:
  `charge = (now - _lastFireTime) / fireRateCooldown`. Cooldowns
  (`WeaponProfile.cs:77-171`): MG 0.4, Twin MG 0.45, Flak 0.8, Heavy Flak 0.85,
  Aero Bomb 1.6, Sniper 1.3, Rocket 1.1, Mine 2.5 s.
- **One weapon slot today.** `Shooting.NetWeaponId:16` (server-write) +
  `_selectedWeaponId:20`; crate tier-up `UpgradeWeaponTier:187`, death reset
  `ResetWeaponToSelected:200`, hangar pick `ServerSetSelectedWeapon:207` /
  `RequestSelectWeaponServerRpc:219`. `ShootServerRpc:97-98` takes no weapon —
  the server reads `CurrentWeapon:106`. A second slot = a second NetworkVariable,
  a slot index on the RPC, and a per-slot `_lastFireTime`.
- **The canvas is Screen Space Overlay, scaled 1920×1080 match 0.5**
  (`Scene01.unity:1515-1520,1535`). iPhone 12 mini (2340×1080) → scale 1.104 →
  **2119×978 canvas units**; the numbers below are canvas units. The scene's
  `EventSystem` runs `InputSystemUIInputModule` (`Scene01.unity:179-181`), so
  uGUI pointer handlers get one `pointerId` per finger — multi-touch (stick +
  trigger at once) works through `IPointerDown/Drag/UpHandler` with no raw
  touch polling. `Screen.safeArea` is used nowhere yet.
- **Bottom corners are taken by the HUD, not by gameplay.** Own detailed panel:
  bottom-right, anchored (1,0), pos (-60, 20), 420×700 (`GameHUD.cs:362-366`).
  Kill feed: top-left (`:712-716`). Timer: top-centre (`:2870-2873`). Hangar
  corner button: top-right (`:1602-1606`). Hangar buttons SKIN (60,40) and
  SCENE (60,116), 180×68, bottom-left (`:2125-2129`, `:2205-2209`) — hangar
  only, so no clash with the triggers, which are hidden in the hangar.
- **Overlay state is scattered.** `GameHUD.IsHangarOpen:1414` exists; results,
  podium, skin and scene pickers are private fields (`:124,131,1344,2192`).
  The controls need one `IsAnyOverlayOpen` to hide behind every overlay.
- **No settings, no profile, no telemetry yet.** Phase 0 (`PlayerProfile`,
  `Telemetry`) is `[ ]`. `PlayerPrefs` is the only persistence in the repo
  (`IAPManager`). The scheme lives in `PlayerPrefs` until `PlayerProfile`
  lands, under a key the profile migration will read.
- **Icon fallbacks exist.** `Resources/Sprites/Projectiles/metal/{tracer,pellet,bomb,bolt,rocket,mine}.png`
  map to the six sprite forms (`ElementProfile.SpriteForm`); power-up icons set
  the style (`Resources/Sprites/PowerUps/*.png`, 96×96, point filter).
- **Bot unaffected.** `BotBrain : IInputProvider` (`BotBrain.cs:25`); a default
  interface method for the slot-aware shoot keeps every provider compiling.

## 1. Files

New (commit `.meta` too):

| file | responsibility |
|---|---|
| `Scripts/Input/TouchInputProvider.cs` | `IInputProvider` for phones. Axes from the on-screen stick (Joystick scheme) or from `MobileInputProvider` gravity (Gyro scheme); shoot per slot from the triggers; hold = pressed every frame (D6). Owns no UI — `TouchControls` writes into it. |
| `Scripts/UI/TouchControls.cs` | Runtime-built uGUI: safe-area container, stick zone + base + knob, trigger buttons with ring + icon. Pointer handlers per element. Visibility rule (§4). Reads `Shooting` for charge/icon. |
| `Scripts/UI/SettingsOverlay.cs` | Overlay in its own class: CONTROLS row (JOYSTICK / GYRO, RECENTER), CLOSE. Opened from the hangar button. Phase 2 rows go here. |
| `Scripts/Meta/ControlSettings.cs` | `enum ControlScheme { Joystick, Gyro }`, load/save (`PlayerPrefs` key `settings.controlScheme`), `Changed` event, default Joystick. |
| `Scripts/UI/UiSprites.cs` | Runtime-generated ring / disc / knob sprites (annulus with AA), cached static — same idea as `EffectAssets` particle shapes. |
| `Resources/Sprites/Weapons/weapon_<key>.png` ×8 | Trigger icons (D13). Keys = `WeaponType` names lower-case: `mg, twinmg, flak, heavyflak, bomb, sniper, rocket, mine`. |
| `tools/weapons/generate_icons.py` | Pillow-only generator (no GPU): 96×96 silhouettes on transparent, 2-colour + outline in the power-up palette; `--apply` writes PNGs + `.meta` via `tools/planes/unity_meta.py` (`sprite` kind, point filter). |
| `Prompts/26-CLAUDE-PLAN-touch-controls.md` | This plan. |

Modified: `Input/IInputProvider.cs`, `Input/InputManager.cs`,
`Input/MobileInputProvider.cs`, `Shooting.cs`, `UI/GameHUD.cs`,
`GameState/GameSetup.cs`, `Weapons/WeaponProfile.cs`, `CLAUDE.md`,
`ROADMAP.md`. Exact seams in §6. No scene change, no prefab change, no
`NetworkPrefabsList` change.

## 2. Scheme and settings

- `ControlSettings.Scheme` (static). Read once at boot; `Set(scheme)` saves
  and raises `Changed`. `InputManager` listens and rebuilds the mobile
  provider's axis source; `TouchControls` listens and shows/hides the stick.
- Desktop and gamepad: untouched. When `InputManager.IsUsingGamepad` the
  controls hide (a controller on an iPad is a better stick).
- Gyro scheme: `MobileInputProvider` keeps its gravity pipeline
  (`:97-137`) and `Recenter:70`; its touch-to-shoot goes (D9). Triggers stay.
- The 15-second rule holds: no prompt, no setup — Joystick by default, the
  toggle is a hangar side-door.

## 3. Stick

- **Zone**: right 40 % of width × bottom 60 % of height of the safe-area
  rect, transparent `Image` with `raycastTarget = true`. `OnPointerDown`
  captures `pointerId`, places the base (Ø 260) at the touch, knob (Ø 110)
  on top. `OnDrag` moves the knob to `clamp(delta, 90)`; `OnPointerUp` (or
  `OnPointerExit` with a lost id) zeroes everything and fades the base
  (0.15 s, plain coroutine — no `Tween` util yet).
- **Response**: `v = delta / 90`, radial dead zone 0.12, then the same
  smooth dead-zone + expo curve as gyro (`MobileInputProvider.ApplyResponse:153-161`,
  made `internal static` with parameters) with expo 1.3 — fine control near
  centre, full authority at the rim. Axes are independent (no circular
  normalisation): full throttle and full turn at once must be possible.
- **Smoothing**: none on the stick (the thumb is the filter); the plane's
  `throttleRate` already integrates throttle.
- **Occlusion**: the plane can fly under the thumb. Nothing to do here —
  arenas are 54 u wide and the stick zone is under the terrain line for the
  most part; noted in risks, measured on device.

## 4. Triggers, charge ring, icons

- **Layout** (canvas units, from the safe-area's bottom-left): primary
  trigger centre (150, 150), Ø 200; secondary (150, 380), Ø 170 — stacked so
  the left thumb rests on the primary and reaches up for the secondary. Both
  above the home-indicator inset.
- **Anatomy** per trigger: disc (dark, α 0.55) → ring track (Ø 200, width
  14, α 0.35) → ring fill (`Image.type = Filled`, `Radial360`, origin Top,
  clockwise, `fillAmount = charge`) → weapon icon (110×110, point-filtered
  sprite) → optional tier chip for a crate-boosted weapon (text "II",
  `PixelFont`, size 12).
- **Charge**: `Shooting.GetCharge01(slot)`; ring fill 0→1 while charging in
  the element colour dimmed; at 1 the ring snaps to full white and the disc
  does one scale punch (1.0→1.12→1.0, 0.12 s, coroutine like
  `GameHUD.pulseCoroutine`). Pressing below 1: nothing (D11). Holding: the
  provider reports pressed every frame, `Shooting.Update:73` fires each time
  the gate opens, the ring restarts — the cadence is visible.
- **Icon**: `WeaponProfile.IconSpriteName` (new field, default
  `weapon_<key>`), loaded via `Resources.Load<Sprite>("Sprites/Weapons/…")`;
  fallback `Sprites/Projectiles/metal/<form>` tinted with the weapon's
  `ProjectileTint`; last resort the weapon's `DisplayName` initials in
  `PixelFont`. Refreshed from `Shooting.NetWeaponId.OnValueChanged` (crate
  tier-up, death reset, hangar pick) and `NetWeaponId2.OnValueChanged`.
- **Second trigger**: built when `Shooting.SlotCount == 2` (`NetWeaponId2 >= 0`),
  destroyed when it drops back to 1; no gap in the layout while absent.
- **Visibility** (evaluated every frame in `TouchControls.Update`):
  `InputManager.IsMobile() && !InputManager.IsUsingGamepad && plane != null
  && !plane.InHangar && !GameHUD.Instance.IsAnyOverlayOpen`, where `plane` =
  `NetworkManager.Singleton.LocalClient.PlayerObject`'s `PlayerController`.
  Hidden → every pointer released, stick zeroed, `held[]` cleared. Shown in
  the FLY warm-up (plane deployed, no overlay), hidden in every hangar,
  results, podium, pickers, settings.
- **No haptic on fire.** ROADMAP §3.3 lists kill/death/hit/pickup/round/run/
  purchase; a shot every 0.4 s would drain the motor and the battery.

## 5. Second weapon slot — what "prepared" means

In scope now:

- `Shooting.NetWeaponId2` (server-write, default -1), `SlotCount`,
  `GetWeapon(slot)`, `_lastFireTime[2]`, `fireRateCooldown(slot)`,
  `GetCharge01(slot)`, `ShootServerRpc(..., int slot)` with the server
  resolving `GetWeapon(slot)` (out-of-range → slot 0), `ResetWeaponToSelected`
  resetting both, `ServerSetSelectedWeapon(weaponId, slot = 0)`.
- `IInputProvider.GetShootInput(int slot)` as a default interface method
  (`slot == 0 && GetShootInput()`), overridden by `TouchInputProvider`.
  `GamepadInputProvider` can map `rightTrigger` to slot 1 later in one line.
- The crate tier-up (`UpgradeWeaponTier:187`) stays on slot 0.

Out of scope (Phase 4 loadout): the hangar draft offering a second pick,
balance (two weapons vs one), the compact HUD row showing two weapons.

## 6. Exact seams

| file:line | change |
|---|---|
| `IInputProvider.cs:19` | add `bool GetShootInput(int slot) => slot == 0 && GetShootInput();` (C# 8 DIM; every existing provider compiles untouched). |
| `InputManager.cs:14,28` | `mobileProvider` becomes `TouchInputProvider`; keep a `MobileInputProvider gyro` inside it; `MobileProvider` getter returns the gyro source (for RECENTER). |
| `InputManager.cs:112-129` | mobile branch: `mobileProvider = new TouchInputProvider(gyro, ControlSettings.Scheme)`; subscribe `ControlSettings.Changed → mobileProvider.SetScheme`. |
| `InputManager.cs:155-158` | when a gamepad is active, still `gyro.UpdateInput()` only if scheme = Gyro (unchanged intent). |
| `MobileInputProvider.cs:139-146,172-185` | delete touch-to-shoot; `GetShootInput` returns false. `ApplyResponse:153` → `internal static float ApplyResponse(float v, float deadZone, float max, float expo)`. |
| `Shooting.cs:16-22` | add `NetWeaponId2` (-1), `SlotCount`, `GetWeapon(int slot)`; `CurrentWeapon` = `GetWeapon(0)`. |
| `Shooting.cs:38,44` | `fireRateCooldown(int slot)`, `_lastFireTime` → `float[2]`; add `public float GetCharge01(int slot)`. |
| `Shooting.cs:71-80` | loop `for slot in 0..SlotCount`: `provider.GetShootInput(slot)` + per-slot gate → `ShootServerRpc(pos, rot, speed, localIdx, slot)`. |
| `Shooting.cs:97-106` | RPC signature gains `int slot`; `var weapon = GetWeapon(slot)`. |
| `Shooting.cs:200-213` | reset both slots; `ServerSetSelectedWeapon(int weaponId, int slot = 0)`. |
| `WeaponProfile.cs:61-64` | add `public string IconSpriteName;` next to `ProjectileSpriteName`; set per weapon in the registry (`:74-171`). |
| `GameHUD.cs:1414` | add `public bool IsAnyOverlayOpen` = hangar ∨ results ∨ podium ∨ skin picker ∨ scene picker ∨ `SettingsOverlay.IsOpen`. |
| `GameHUD.cs:362-366` | if `InputManager.IsMobile()`: `anchoredPosition = (-60, 260)` (D8). |
| `GameHUD.cs:2201` (next to `CreateSceneButton`) | `CreateSettingsButton()` — same 180×68 style at (60, 192), `onClick → SettingsOverlay.Instance.Open()`. ~15 lines; the overlay is not in `GameHUD`. |
| `GameSetup.cs:103-114` | after `GameHUD.CreateHUD(canvas)`: `TouchControls.Create(canvas)` and `SettingsOverlay.Create(canvas)` (both no-ops on desktop except the overlay). |
| `CLAUDE.md` | after implementation: Input section (schemes, provider tree, where the setting lives, icon assets + generator). |
| `ROADMAP.md` | Phase 1 bullet `[plan]` → `[wip]` → `[done v3.0]`; Phase 0 taxonomy gains `input_scheme_set`. |

## 7. Analytics (ROADMAP §3.7)

Events: `input_scheme_set {scheme, source: default|settings}` at boot and on
change; `settings_open`. `Telemetry` does not exist until Phase 0, so both go
through a single `Telemetry.Log(name, params)` stub that `Debug.Log`s with a
`[Telemetry]` prefix — the Phase 0 class replaces the body, not the call sites.
KPI once UGS is live: share of sessions on each scheme, `round_end` kills per
scheme.

## 8. Verification

1. Batchmode compile (`-quit`), then Editor with `InputManager.forceMobileInput`
   + Device Simulator (iPhone 12 mini profile): stick appears at the thumb,
   knob clamps at 90 u, HUD panel sits above the zone, safe-area insets hold.
2. Device (TestFlight, per no-cable rule): stick and trigger **at the same
   time** — turn while firing; two fingers never swap roles; lifting the
   stick finger zeroes both axes within a frame.
3. Ring: with MG the ring completes in 0.4 s and a held trigger fires at
   2.5 Hz; with Mine (2.5 s) a tap during charge does nothing; after a FIRE
   RATE run-upgrade the ring is visibly faster (`fireRateCooldown` includes
   `runRofMultiplier`).
4. Icon follows the weapon: hangar pick → icon; crate tier-up → next tier's
   icon + "II" chip; death → back.
5. Second trigger: temporarily set `NetWeaponId2 = Rocket` on the host via
   `PlayerDebug` → second trigger appears, fires rockets on its own cooldown,
   primary unaffected; set back to -1 → it disappears.
6. Settings: GYRO → stick gone, tilt flies, triggers fire; RECENTER re-captures
   the hold angle; kill the app → scheme restored on next boot (PlayerPrefs).
7. Nothing leaks through: stick zone does not react under the results overlay,
   hangar buttons (SKIN/SCENE/SETTINGS/READY) still tap, HANGAR corner button
   still tappable during the warm-up with the stick on screen.
8. Gamepad on iPad (MFi): controls hide when the pad becomes active, return on
   the first touch (`InputManager.cs:198-214`).
9. Perf: `TouchControls.Update` is a handful of comparisons; ring fill is one
   `Image` property; no per-frame allocation (profile once on device).
10. 15-second rule: cold start → FLY → first shot with no setting touched.

## 9. Risks

- **iOS bottom-edge system gesture.** Triggers sit 150 u above the bottom
  edge; a swipe starting on the edge itself opens the home indicator gesture
  and cancels the touch. Mitigation: `PlayerSettings.iOS.deferSystemGesturesMode`
  = bottom edge (`iOSPostProcess` already exists for entitlements) — verify
  the setting is on; keep the triggers' hit area off the last 60 u.
- **Thumb over the HUD.** D8 moves the own panel up; opponent rows may now
  overlap the timer on very short screens — check 16:10 iPad in the simulator.
- **Cooldown drift.** `_lastFireTime` is owner-side; the server does not
  re-gate. Same as today — not made worse, noted so nobody "fixes" the ring by
  moving the clock to the server.
- **Two-finger edge cases.** A finger sliding from the stick zone onto a
  trigger must not fire it: triggers act on `OnPointerDown` only, never on
  enter. A trigger finger sliding off: `OnPointerUp` still arrives for that
  `pointerId` — held cleared.
- **Gyro users lose tap-to-fire.** Intentional (D9); the trigger is where the
  ring is. Mention in the release note.
- **Icon art quality.** Procedural silhouettes may read poorly at 110 u on a
  phone; the FLUX path in `tools/weapons/` is the upgrade if so — the fallback
  chain means the build never waits on art.

## 10. Effort

~2–3 days: provider + stick + triggers 1 d, `Shooting` slots + ring + icons
0.5–1 d, settings overlay + persistence 0.5 d, device tuning (dead zone,
sizes, D8 offset) 0.5 d. Ships in v3.0 with Phase 1, or as v2.9.x on its own —
it needs nothing from Phase 0.
