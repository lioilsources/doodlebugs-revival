# Bug-fix pass (v2.9.x) — findings, priorities, plan

Status (2026-10-23): **P0 implemented** on branch
`fix/bullet-hitbox-fg-heal-warmup-arena` (this session; compile-checked with
Roslyn against the Unity reference DLLs, NOT run in the editor - the Mac in
use has no Rosetta 2, so batchmode refuses to start). P1-P3 are proposals
with the owner's call in §3. No release per fix: the branch merges to main
and devices take the next tag from TestFlight.

## 1. What was reported, what it actually was

| # | Reported | Root cause | Fix (P0, done) |
|---|---|---|---|
| 1 | Warm-up (FLY) flight is interrupted by "hitches" | `BackgroundManager.ServerTickWarmUpRotation` changed the arena every 45 s **during** the flight; `ForegroundScroller.SetForeground` rebuilds ~1300 tile GameObjects synchronously (plus `GetPixels32` on a 2048 px strip) - one long frame | Timer removed. Arena changes only on FLY out of the Waiting hangar (`ServerOnWarmUpFlyOut`, first FLY keeps the arena drawn at host start), so the rebuild lands on the transition frame. `MatchManager` no longer ticks the rotation; `Scene01` lost the stale `warmUpRotateSeconds` field. |
| 2 | One shot bites "almost half" the foreground | `Bullet.ApplyArtScale` scales the bullet object ~3.5x so a 32 px tracer is 1.1 units long, but the prefab's `BoxCollider2D` (5.66 x 2.19 local, calibrated for the legacy 1289 px texture at scale 0.09) scaled with it → **hitbox 19 x 7.5 world units**, a third of the screen. The box overlapped ~20 x 8 tiles at once; every one of them fired its own 2x2 bite + collapse. Doubled by PR #42 ("Double the projectile sizes"). Second half: the wrap-time heal was a no-op - debris tiles were `Destroy`ed at the end of their flight, so holes were permanent and a 3-minute round ground the terrain away. | `Bullet.FitHitbox`: box = opaque pixel rect of the sprite (cached per sprite, floor 0.25 units). Legacy sprite path restores the prefab box. `ForegroundTile.ReturnHome`: debris parks back under its copy disabled and heals on the next wrap; `ForegroundTileGrid.Generation` guards against a copy rebuilt meanwhile. `_falling` counter no longer leaks when a tile is destroyed mid-flight. |
| 3 | Bullets stop on clouds "in empty air", unclear what they hit | Same 19-unit hitbox: the bullet "touched" the cloud polygon ~10 units before its sprite did. Plane hits had the same aim-assist. | Same hitbox fix. Bullet body set to Continuous collision (sniper moves ~1 unit per 20 ms step, same as a tile / a thin cloud lobe). |

## 2. Other defects found while reading (not fixed here)

Ordered by player impact. "Effort" is a rough size for one PR.

| Pri | Where | Defect | Proposed fix | Effort |
|---|---|---|---|---|
| P1 | `ForegroundScroller.SetForeground` / `BuildTiles` | The rebuild itself: `GetPixels32` on the whole 2048 x ~650 strip, `Sprite.Create` + 3 components per tile, ~1300 tiles, all in one frame. Now hidden at FLY and behind the intermission overlay, but it still stalls the results/hangar UI for a frame or two on a phone, and every camera-aspect change (rotation) triggers it. | (a) Cache the opaque-cell mask per strip (one scan per sprite, not per copy per round). (b) Pool the tile GameObjects per copy: an arena change re-sprites and re-positions existing tiles instead of Destroy/Instantiate. (c) If still visible, build copies across frames behind the opaque overlay. Measure with the Phase 0 perf budget. | M |
| P1 | `Cloud.FixedUpdate` + `Cloud.prefab` | Clouds are **Dynamic** `Rigidbody2D` moved by writing `transform.position` every step. Works, but it is a teleport per step for Box2D (no contact continuity) and the one remaining candidate for the "cloud collision de-syncs visually" note. | Kinematic body + `MovePosition`; clients already get kinematic from `NetworkRigidbody2D`. Then re-test with ParrelSync; if a residual offset remains it is NetworkTransform interpolation - draw the despawn FX at the server position only (already the case for the spark). | S |
| P1 | `ForegroundTile.OnTriggerEnter2D` | One bullet overlapping 2-4 tiles in the contact step fires 2-4 overlapping 2x2 bites (a 3x3 hole) - the "few blocks" rule is only approximately met even with the fixed hitbox. | First tile wins: `Bullet` gets a `ClaimTerrainBite()` that returns true once; tiles call it before biting. | S |
| P2 | `ForegroundTile.MaxFalling` | Only `Topple` counts toward the 80-rubble cap; `LaunchDebris` (bullet bites, craters) does not, so a Heavy Flak crater can still put 100+ tumbling sprites up at once on a phone. | Count both paths; past the cap deactivate instead of flying. | S |
| P2 | `Bullet.HandleContact`, `Cloud`, `CloudManager`, `PlayerController`, `BoundaryManager` | Hardcoded names: `other.gameObject.name == "Space"`, `GameObject.Find("Left"/"Right"/"Ground")` in five scripts - violates the "no hardcoded collision strings" rule and is a silent no-op if an object is renamed. | One `ArenaBounds` scene singleton exposing the four colliders; a `Space`/`Boundary` tag check in `Bullet`. | S |
| P2 | `Cloud.cs` | `Debug.Log` on every Awake/Start/Initialize/wrap - a dozen lines per cloud per wrap, all round long, on mobile. | Gate behind a `[SerializeField] bool verbose` or delete. | XS |
| P2 | `PlayerController.Shutdown` | `// TODO` - menu-scene load is commented out; the ShutdownClientRpc path leaves clients on a dead scene. | Decide with Phase 1 (PLAY button / boot flow); until then show the Searching hangar after shutdown. | S |
| P3 | `BackgroundManager.RequestSetSceneOrderServerRpc` | A playlist edit while waiting re-rolls the arena immediately - fine, but now also resets `_warmUpArenaFlown`, so the next FLY keeps it (intended) - document in the picker if the host expects a change on FLY. | None; note only. | - |

Not bugs, already on the roadmap: single device never leaves "SEARCHING"
(Phase 1), hardcoded English HUD strings (Phase 6), no analytics (Phase 0).

## 3. Decisions

| # | Question | Default taken / proposal | Owner |
|---|---|---|---|
| D1 | Hitbox = full sprite bounds or opaque pixels? | **Opaque pixels** (tracer stroke is 3 of 16 rows; bounds would still be 5x too tall), floor 0.25 units so a hairline cannot skip a plane. | done |
| D2 | Warm-up arena: every FLY or every N-th? | **Every FLY after the first.** The first keeps the arena drawn at host start so the playlist is not skipped. | done |
| D3 | Healed holes: keep the "disabled until next wrap" rule for debris that lands after its copy already wrapped? | Yes - one extra cycle for a late tile is invisible; the alternative (re-enable on landing) would pop a tile into view. | done |
| D4 | Order of P1 items | Rebuild cost (perf budget, Phase 0) → cloud body → first-tile-wins. | owner |
| D5 | Bundle P2 into one "code-rules" PR? | Yes, they are all small and touch the same five files. | owner |

## 4. Files touched (P0)

- `Scripts/Bullet.cs` - `_box` + `FitHitbox` / `RestoreLegacyHitbox` / `OpaqueRect`
- `Prefabs/Bullet.prefab` - `m_CollisionDetection: 1`
- `Scripts/ForegroundTile.cs` - `ReturnHome`, `OnDestroy`, `_countedFalling`
- `Scripts/ForegroundTileGrid.cs` - `Generation`, `BeginBuild`
- `Scripts/ForegroundScroller.cs` - `grid.BeginBuild()` in `BuildTiles`
- `Scripts/BackgroundManager.cs` - `ServerOnWarmUpFlyOut`, `ServerResetWarmUpArena`; timer removed
- `Scripts/GameState/MatchManager.cs` - no rotation tick; renamed reset calls
- `Scripts/UI/GameHUD.cs` - `HideHangarForWarmup` → `ServerOnWarmUpFlyOut`
- `Scenes/Scene01.unity` - `warmUpRotateSeconds` removed
- `CLAUDE.md` - foreground heal, hitbox, warm-up rotation, known issues

## 5. Verification (owner, editor + two devices)

1. Open Scene01, Play → Start Host. FLY. Fly ~2 minutes: the arena must
   not change. HANGAR → FLY: the arena changes (hitch on the transition
   only). HANGAR → FLY again: next playlist entry.
2. Shoot the terrain with MG: a 2x2 hole (3x3 at most on a corner), never a
   trench. Keep shooting one spot for a minute, then wait for the copy to
   wrap off-screen: holes are back.
3. Shoot a cloud edge: the bullet stops where the sprite meets the solid
   cloud (overlay in `tools/clouds/out/collider_overlay.png`), not before.
4. ParrelSync client: same holes, same cloud stops, no plane hit from more
   than ~0.5 units away. Watch the console for "falling" cap behaviour on a
   Heavy Flak crater.
5. Select a bullet in the hierarchy mid-flight: `BoxCollider2D` size x
   localScale ≈ 1.1 x 0.25 (tracer), ≈ 0.5 x 0.5 (pellet).
6. Phone: no frame drop during the warm-up flight (Profiler or the Phase 0
   budget overlay).

## 6. Risks

- The hitbox is now ~35x smaller in area: hit rate drops noticeably. That is
  the intended game (one shared 50 x 50 px plane box), but if MG feels
  useless, raise `MinHitboxWorld` or add a per-form padding in
  `ElementProfile` - not the prefab box.
- Continuous collision on bullets costs a little per step; with <30 bullets
  alive it is noise, but keep an eye on the Phase 0 budget.
- Debris now survives its flight and re-parents: a copy that is destroyed
  while a tile is flying is handled (generation / null parent → Destroy),
  but `DisableForeground` mid-flight on a scene teardown relies on Unity's
  null check of `_homeParent`.
