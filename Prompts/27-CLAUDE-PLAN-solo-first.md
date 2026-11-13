# Solo-first (v3.0) — Next-steps Plan

Status (2026-11-13): **WP0 PR #58 · WP1 verified on PR #57 (licence-free
compile + checker; real Unity builds blocked by an expired editor licence on
the dev Mac) · WP2 PR #59 · WP3 PR #60 · WP4 PR open (not soaked: needs the editor).** Written against `main` @ `4ce7970` (v2.9.3).
Line numbers below are from that commit; `GameHUD.cs` lines shift by a few
once PR #57 lands, so every seam also names its symbol. The executor updates
this status line after each work package (WP) lands.

Owner's ask (2026-11-12): "prepare a plan for Opus, what next". This file is
that hand-off. It is self-contained for a fresh session that has read
`CLAUDE.md`.

**Goal:** the game is playable alone on one device with no connection, tap
PLAY and be shooting inside 15 s, and that build goes to App Review. This is
ROADMAP Phase 1 plus the two things standing in front of it (an open
507-file PR and the unbuilt half of Phase 0).

## How to work this plan (executor)

1. Read `CLAUDE.md`, `ROADMAP.md` §3 and §4 Phase 0–1, then this file.
   `Prompts/25` explains the existing bot, `Prompts/26` is the house style.
2. Take the first WP in §2 that is not marked done. **One WP = one branch =
   one PR.** Branch names are given per WP.
3. Before every PR: batchmode compile (§12.1) and, for anything touching
   `MatchManager`, `ScoreManager`, `ConnectionManager`, `PlayerController` or
   bots, the two-instance ParrelSync pass (§12.3). Unity GUI must be closed
   for batchmode ("Multiple Unity instances").
4. Open the PR and stop there. **Do not merge, tag, or upload store metadata
   unless the owner says so in that session.** A tag ships to TestFlight and
   Firebase.
5. Every user-visible string goes through `Loc` (after WP1) and into all 45
   catalogues; `python3 tools/l10n/check_catalogues.py` must exit 0.
6. If a seam below does not match the code you find, trust the code, fix the
   plan in the same PR, and say so in the PR body.

## 1. Where we are (facts from the repo, 2026-11-12)

- **Shipped:** v2.9.0–v2.9.3 (touch controls, bullet hitbox, pooled terrain,
  kinematic clouds). CI is green for desktop, iOS (TestFlight) and Android
  (Firebase) on the v2.9.3 tag. Not on the App Store.
- **ROADMAP drift:** "v2.9" was meant to be Phase 0 (analytics, profile,
  tween). What shipped as 2.9.x is the touch-controls slice of Phase 1 and a
  bug-fix pass. Of Phase 0 only a logging stub exists:
  `Scripts/Meta/Telemetry.cs` (22 lines, `Debug.Log`), three call sites
  (`SettingsOverlay.cs:55`, `ControlSettings.cs:52,58`). No `PlayerProfile`,
  no `Tween`, no UGS package in `Packages/manifest.json`, and
  `ProjectSettings.asset:980-985` has an empty `cloudProjectId`
  (`cloudEnabled: 0`), so no UGS project is linked.
- **Open PR #57** `i18n/all-langs` (2026-11-09): `Loc` string table, 45
  catalogues × 202 keys in `Resources/Localization`, iOS `InfoPlist.strings`,
  fastlane store metadata (50 iOS / 46 Android locales). 507 files, mergeable,
  based on current `main`. **Never compiled in Unity** (its own PR body says
  so) and the glyph check was skipped (no fontTools). It rewrites 132 lines
  of `GameHUD.cs`, so any UI work started before it lands will conflict.
- **Solo is still a dead end.** A lone device boots into the Searching
  hangar; on timeout it hosts, opens the Waiting hangar and can only FLY a
  scoreless warm-up next to one harmless bot. `marketing/appstore.md:52-65`
  tells App Review that two devices are required.
- **Stray file:** untracked `Assets/Resources.meta` (third reappearance; the
  performance-test package recreates the empty folder on every headless run).

## 2. Order of work

| WP | What | Branch | Depends on | Blocked on owner? | Effort |
|---|---|---|---|---|---|
| 0 | Hygiene: stray meta, ignore rule, stale status docs | `chore/resources-meta` | – | no | 0.5 h |
| 1 | Verify and land PR #57 (localisation) | `i18n/all-langs` (existing) | 0 | device language pass, merge | 0.5–1 d |
| 2 | `PlayerProfile` (Phase 0 slice) | `feat/player-profile` | 1 | no | 1 d |
| 3 | Combat-bot identity: scoring, HUD rows, run state, deploy | `feat/combat-bot-identity` | 1 | no | 1.5–2 d |
| 4 | Combat brain and four tiers | `feat/combat-bot-brain` | 3 | playtest feel | 4–5 d |
| 5 | Solo session and Home screen (PLAY / PARTY) | `feat/solo-session` | 2, 3 | device pass | 3 d |
| 6 | Bots in LAN (+ BOT) and the nearby-pilot toast | `feat/lan-bots` | 4, 5 | two-device pass | 2 d |
| 7 | Telemetry backend: UGS Analytics + Cloud Diagnostics | `feat/telemetry-ugs` | 2 | **yes: link UGS project, consent call (S12)** | 1–1.5 d |
| 8 | Submission kit: review notes, listing text ×50, privacy page, docs | `chore/v3-submission` | 5, 7 | **yes: App Store Connect** | 1 d |

WP4 and WP5 do not touch the same files (brain vs. session/UI) and can run
in parallel worktrees or swap order. WP7 can land any time after WP2; it must
land before WP8 because the store text and the privacy label change with it,
and that text exists in ~50 locales, so it should be rewritten once.

Versions: v2.9.4 = WP0+WP1. v2.10.x = TestFlight betas after WP5 and WP6.
**v3.0.0 = the App Store submission build** (after WP8).

## 3. Decisions

Roadmap decisions keep their ROADMAP numbers (D1–D9). This plan's own are
S1–S14. Defaults let the work proceed; the owner overrides before the WP
that uses them.

| # | Decision | Default | Used by | Why |
|---|---|---|---|---|
| S1 | Boot (ROADMAP D1) | **Home screen with PLAY (large) and PARTY (small). No discovery, no socket, no permission prompt until one is tapped.** | WP5 | Rule 2: the iOS Local Network prompt and Android Nearby permissions fire the moment pairing starts. Today that is frame one. |
| S2 | Solo transport | **Scene `UnityTransport` bound to 127.0.0.1, no broadcast, no listener.** PARTY tears the solo host down and runs today's pairing flow unchanged. | WP5 | Smallest change to the netcode that already works; loopback touches no local-network API. |
| S3 | Solo roster | **1 human + 2 bots, free-for-all.** Bots target the nearest plane, so they also fight each other. | WP4, WP5 | Two bots chasing one rookie is a 2-v-1; FFA keeps the sky busy and fair. |
| S4 | Difficulty | **Four tiers (Rookie / Pilot / Ace / Baron). Quick play adapts: start Rookie, up one tier after a round win, down one after a loss, saved in the profile.** | WP4, WP5 | ROADMAP target: a new player wins ≥ 70 % of first rounds. Campaign (Phase 3) will pin tiers per level. |
| S5 | Solo run format | **Same best-of-5 run, hangar draft and upgrades as LAN.** Bots buy no upgrades; a bot's weapon is drawn per round from its tier's pool. No Bomb or Mine for bots in v3.0. | WP3, WP4 | Reuses the whole round flow. Dropped and lurking weapons need their own tactics. |
| S6 | Bot identity | **Host-owned planes, `(OwnerClientId 0, LocalPlayerIndex 90+seat)`, seats 0–7.** Warm-up bot keeps 99. Display name is built on each client from a synced tier + seat, never from `netPlayerName`. | WP3 | ROADMAP Phase 1. A synced name string would leak the host's language onto clients (rule 6). |
| S7 | Run-state key | **Participant id:** humans = clientId, combat bots = `BotParticipantBase + seat`. | WP3 | `_roundWins`/`_runPoints` are keyed by clientId today; a bot win would be credited to the host (§4). |
| S8 | LAN bots | **Host-only "+ BOT" / "− BOT" in Waiting, PreBattle and Intermission hangars, up to 4 participants; tier Pilot.** | WP6 | ROADMAP Phase 1. A tier picker can follow. |
| S9 | Warm-up bot | **Stays, only in the PARTY Waiting lobby.** | WP3 | It is the lobby's screensaver; solo no longer passes through Waiting. |
| S10 | Leaving a solo battle | **Corner HOME button during a solo battle; no pause.** | WP5 | A phone player needs a way out of a 3-minute round. Pause is `Time.timeScale`, Phase 2. |
| S11 | Hit-stop / slow-mo (ROADMAP Phase 1 bullet) | **Deferred to the Phase 2 plan.** `MatchManager.IsSoloSession` is added now so Phase 2 has its guard. | – | Not needed for App Review; it is juice. Move the ROADMAP bullet. |
| S12 | Analytics (ROADMAP D5) and consent | **UGS. No prompt at boot; collection starts after the first finished round; Settings row "ANONYMOUS STATS ON / OFF", default ON.** The legal call for EU players is the owner's. | WP7 | Rule 2 forbids a boot prompt. Whether opt-out is enough is not an engineering decision. |
| S13 | Languages (ROADMAP D7) | **Superseded by PR #57: 45 catalogues, 32 active at runtime** (the 13 RTL/Indic ones fall back to full English via `Loc.NeedsShaping`). Update D7 in ROADMAP. | WP1 | The owner opened that PR. Cost to remember: every new string × 45, every store text change × ~50. |
| S14 | Bot names | **Tier name + seat number ("ROOKIE 1", "ACE 2"), localised.** | WP3 | Tells the player how hard the opponent is; no name list to translate. |

## 4. Findings that shape the plan

- **A bot is a host-owned plane, so "is it mine" checks are true for it.**
  `PlayerController.NetIsBot:81` + reserved index 99 (`:87`) keep the warm-up
  bot out of everything. Every `IsBot` test in the repo is one of two kinds,
  and combat bots need them split:
  - *"Not a participant"* (must become warm-up-only so combat bots score):
    `PlayerController.cs:657, 863, 868, 879, 1071, 1081`;
    `ScoreManager.cs:149, 181, 197` (`IsBotIdentity`);
    `MatchManager.cs:320, 877` (deploy loops); `GameHUD.cs:388, 410` (rows),
    `:816` (name).
  - *"Not the local human's plane"* (must stay true for every bot):
    `TouchControls.cs:151`; `GameHUD.cs:1472, 1770, 2856, 2869`;
    `PlayerController.cs:924` (haptic); `PlaneAppearance.cs:60` (look claim);
    `GameHUD.cs:1969` (READY list); and the RPC routers that must never pick
    a bot as carrier: `MatchManager.cs:665`, `ScoreManager.cs:113, 214, 228`.
- **`GameHUD.IsMine` (`:471`) decides detailed vs. compact panel.** If it is
  ownership-based, the host would get a full detailed panel per bot. It must
  return false for any bot.
- **Run state is per device, keyed by clientId** (`MatchManager.cs:114-116`,
  `EndRound:578-580`, `AwardRunPoints:597-639`,
  `HandleMatchEndFromServer:684`, podium `GameHUD.cs:1207, 1219`). All combat
  bots share clientId 0 with the host. Without S7 a bot's round win advances
  the host's run and the podium names the host.
- **Battles start and freeze on connected clients only.**
  `MatchManager.OnNetClientConnected:219` (`ConnectedClientsIds.Count >= 2`),
  `OnNetClientDisconnected:265` (`remaining < 2` → freeze). Both become
  "participants" = clients + combat bots.
- **`BotManager` assumes one bot that must die with the Waiting phase.**
  `MatchManager.Update:417-426` calls `ServerEnsureDespawned()` in every other
  phase. Combat bots live through Battle, Intermission and Podium.
- **Death handling differs.** The warm-up bot is despawned and replaced on
  death (`BotManager.OnBotDied:169`). A combat bot must respawn like a player
  through `HandleDeathAndRespawn` (`PlayerController.cs:665`), and its brain
  must reset when that happens (`OnServerDeath:102` is the hook).
- **`BotBrain` is the right base.** It already flies the real flight model
  through `IInputProvider` with a keyboard-rate input ramp
  (`InputRampPerSecond:33`), has ground/space pull-out overrides
  (`FixedUpdate:170-193`) and stall recovery (`TickRecover:335`). Combat adds
  states on top; the override layer stays the top priority.
  `HumanInLineOfFire:408` scans every `PlayerController` with
  `FindObjectsByType` per physics tick: fine for one bot, not for four.
- **Muzzle speed is knowable.** `Shooting.cs:192-194`: impulse =
  `baseBulletForce (20) × weapon.ForceMultiplier × maturity + planeSpeed`,
  bullet mass 1 (`Bullet.prefab:111`), so that number is units per second.
- **Planes wrap left/right, bullets do not.** `PlayerController.cs:1047-1056`
  (`WrapToPositionClientRpc`); `Bullet.cs` has no wrap. A bot may chase
  through the wrap but must only fire on direct geometry.
- **Respawn invulnerability is synced:** `PlaneStats.IsInvulnerable:97` (2 s).
- **Boot starts pairing on frame one.** `ConnectionManager.Start:114-136`
  goes straight to `StartNativeP2P()` (always on iOS, `DetermineMode:84-96`)
  or `StartDiscovery()`. `GameHUD.Start` opens the Searching hangar (`:184`),
  whose builder returns early with only a title and a version
  (`BuildHangarOverlay:1535-1540`).
- **A host that hears another host yields if its lobby is empty.**
  `ConnectionManager.OnServerFound:208` → `HandleHostConflict:250-279`. A solo
  host has an "empty lobby" by that test. Harmless while solo runs no
  listener (WP5); a trap the day it does (WP6).
- **Empty `ServerListenAddress` means loopback in this NGO version**
  (`UnityTransport.ListenEndPoint`, package cache). Scene01 has
  `ServerListenAddress: 0.0.0.0` (`Scene01.unity:871`). Solo sets 127.0.0.1 at
  runtime, so PARTY must set 0.0.0.0 back before hosting.
- **iOS swaps the NGO transport** to the native one in
  `NativeBecomeHost:359` / `NativeBecomeClient:375`. After a party session a
  solo start must assign the `UnityTransport` component back.
- **A host leaving its own session gets no reset.**
  `MatchManager.OnNetClientDisconnected:247` calls `HandleLocalSessionEnded`
  only for non-servers. The existing host-yield path works because its state
  is always Waiting. Leaving a solo battle leaves coroutines, scores, run
  tables and bots behind unless they are cleared explicitly.
- **PR #57's scene edit is safe.** It changes `ConnectionData.Address` from a
  baked LAN IP to 127.0.0.1; the host listens on `ServerListenAddress`
  (0.0.0.0, untouched) and a client overwrites `Address` from discovery
  (`ConnectionManager.ConnectToHost:232`). Verified by reading, not by running.
- **Store text contradicts the goal in 50 locales.** PR #57's
  `fastlane/metadata/ios/en-US/description.txt` says "local-multiplayer",
  "No ads, no in-app purchases, no data collection" and "needs a second
  device". Solo changes the first and last, WP7 the middle.

## 5. WP0 — Hygiene

- Delete `Assets/Resources.meta` and the empty `Assets/Resources/` folder.
  Add `/[Aa]ssets/Resources.meta` to `.gitignore` right after the
  `PerformanceTestRun*` rules (~line 95). The rule beats the
  `!/[Aa]ssets/**/*.meta` re-include on line 18 and leaves
  `Assets/Doodlebugs/Resources.meta` tracked (checked in the 2026-11-12
  review). Drop the rule if real assets ever go into top-level
  `Assets/Resources`.
- `Assets/_Recovery/0.unity` is a tracked Unity crash-recovery scene (PR #57
  even edits it). Propose `git rm --cached` + ignore in the PR body; do it
  only if the owner agrees.
- `ROADMAP.md` §0 and `CLAUDE.md` "Product Direction" both describe v2.8.2
  ("no localisation", "UI strings hardcoded", "no profile"). Refresh them
  **after WP1 merges**, in the WP2 PR, so they are rewritten once.

## 6. WP1 — Land PR #57

The PR is the owner's; the job is to make it safe to merge, not to redesign
it. Push fixes as extra commits on `i18n/all-langs`.

1. **Compile for both mobile targets.** `#if UNITY_IOS` code
   (`iOSPostProcessBuild.cs`, +59 lines; `Plugins/iOS/DoodlebugsLocale.m`)
   only exists under the iOS target. The honest gate is the real builds:
   `./ci/unity-build.sh android <scratch>/a.apk` and
   `./ci/unity-build.sh ios <scratch>/ios`.
2. **Run the checker with the glyph check on:** a venv with `fonttools`, then
   `python3 tools/l10n/check_catalogues.py`. Fix what it reports
   (`--fix-font` maintains the `@font=system` lines).
3. **Read the C# diff** (16 files, ~460 lines) for:
   - strings still concatenated or interpolated around `Loc.T` results;
   - `ToUpperInvariant()` on localised text (`GameHUD.OnConnectionStatus:208`
     on main) → `Loc.Upper`;
   - status strings that originate in `ConnectionManager.SetStatus` and
     `NativeLocalCoopManager.OnStatus` (do they send keys or English?);
   - fixed-size buttons (180×68 hangar column, weapon cards) with long
     German / Russian / Finnish labels, and system-font fallback metrics for
     CJK;
   - the commit "Move LAN address and Apple team ID out of tracked files":
     confirm `release-ios.yml`, `fastlane/Fastfile` and
     `iOSPostProcessBuild` still resolve the team id on the Mac Mini runner.
     A broken lookup here only shows up on the next tag.
4. **Editor pass:** force a few languages through `Loc.SetLanguage` (cs, de,
   ja, ar) and screenshot boot, Waiting hangar, weapon cards, settings,
   results, podium. `ar` must be entirely English (`NeedsShaping`), never
   mixed.
5. **Owner:** device language pass on a phone, two-device pairing regression,
   merge, tag v2.9.4.

Do **not** upload the fastlane listing yet: WP8 rewrites its description.

After the merge, the rule for every later WP: new string → key in `en.txt`,
then all 44 other catalogues, checker green.

## 7. WP2 — PlayerProfile

`Scripts/Meta/PlayerProfile.cs`, static facade over one JSON file.

- `Application.persistentDataPath/profile.json`; `JsonUtility`; atomic save
  (write `profile.json.tmp`, then replace); `schemaVersion = 1`; a
  `Migrate(from)` switch even though there is only one version.
- Fields: `stats { kills, deaths, rounds, roundsWon, soloRounds, lanRounds,
  playSeconds }`, `settings { controlScheme, analyticsOptOut }`,
  `solo { tier, lastResults }`, `firstSuccessAt` (first finished round;
  gates analytics start and, later, permission prompts).
- Load once at boot (must not cost the 15-second budget: one small file,
  synchronous is fine). Save on round end, on settings change and on
  `OnApplicationPause(true)`.
- Unreadable file → rename to `profile.json.bad`, start fresh,
  `Telemetry.Log("profile_reset")`. Never crash on a bad profile.
- One-time import of `PlayerPrefs` `settings.controlScheme`
  (`ControlSettings.cs`); `ControlSettings` then reads and writes the profile.
  Leave `IAPManager`'s PlayerPrefs cache alone until Phase 4.
- `Scripts/Meta/ProfileRecorder.cs`: subscribes to `MatchManager.OnMatchEnded`
  on the local device and adds the local planes' rows from the replicated
  `ScoreManager.AllStats` to the profile. No server involvement, works for
  LAN clients too.
- Same PR: the ROADMAP §0 / CLAUDE.md status refresh from WP0.

Done when: stats survive an app restart and a reinstall-over (update), and a
hand-corrupted file produces a fresh profile plus a `.bad` copy.

## 8. WP3 — Combat-bot identity (plumbing, no AI yet)

Ship this with the **existing** brain flying the combat bots (flag the
friendly-fire mute off for them). They will be poor fighters, which is fine:
this WP proves rows, scores, run state and round flow before any tactics
exist. A debug entry point (`PlayerDebug` or an editor menu) spawns N combat
bots in the Waiting lobby and starts a battle.

Changes:

- `PlayerController`: `CombatBotIndexBase = 90`, `MaxCombatBots = 8`;
  `IsWarmupBot`, `IsCombatBot`; static `IsWarmupBotIdentity(idx)`,
  `IsCombatBotIdentity(idx)` replacing `IsBotIdentity:92`;
  `NetBotTier` (`NetworkVariable<byte>`, server-write);
  `ServerSetBotIdentity(int seatOrWarmup, byte tier)`; a static registry
  `PlayerController.All` maintained in `OnNetworkSpawn:261` /
  `OnNetworkDespawn:449` for the brain's sensing.
- Re-point the "not a participant" tests listed in §4 to the warm-up-only
  forms. Leave the "not the local human's plane" tests on `IsBot`.
- `GameHUD.IsMine:471` → false for any bot. Combat bots then get the compact
  opponent row for free. `ResolvePlayerName:814` and the entry's name come
  from `Loc` (`bot.tier.<n>` + seat), never from `netPlayerName`.
- `MatchManager`: `ParticipantId(clientId, localIdx)`; use it for
  `_roundWins`, `_runPoints`, `_clientRoundWins` (`EndRound:578`,
  `AwardRunPoints:613-637`, `HandleMatchEndFromServer:684`,
  `HandleRoundWinsFromServer:768`, snapshot `:490`). Skip `SyncRunStateTo`
  for bot participants. Podium name lookup takes a participant id.
- `MatchManager`: `ParticipantCount` (clients + `BotManager.CombatCount`) at
  `:219` and `:265`. Deploy loops `:318-322` and `:875-879` skip only the
  warm-up bot.
- `BotManager`: keep the warm-up path as is; add a combat roster
  (`ServerSetCombatRoster(count, tier)`, `ServerDespawnCombat()`,
  `CombatCount`). `ServerEnsureDespawned:97` affects the warm-up bot only.
  Combat bots: no `OnBotDied` replacement; unclaimed looks, distinct shapes,
  and the existing yield-on-human-pick (`ServerTickWarmUp:82-93`) applied to
  every bot in every phase. Per round: weapon from the tier pool via
  `Shooting.ServerSetSelectedWeapon`.
- Freeze-to-waiting (`ServerFreezeToWaiting:345`) and `ServerResetRun:918`
  must leave no combat bot behind when participants drop below 2.

Check while there: `PlayerColorManager.GetColorIndex` with index 90+ (it
keys on a string id, so it should just assign the next colour);
`ScoreManager.FindPlayerByClientAndIndex:280` resolves `(0, 90)`;
`CheckMaturityUpgrade` on a bot (decide: tier owns skill, so skip maturity
for bots).

Done when (editor, then ParrelSync): bots have compact rows on host and
client; a bot kill moves its own row and the kill feed names it; a bot
reaching 3 kills ends the round with its tier name on the results screen;
three bot round wins give a podium that names the bot, not the host; the
host's READY alone starts the next round; a late joiner sees the same rows.

## 9. WP4 — Combat brain and tiers

New `Scripts/Bot/BotTier.cs` (parameter struct + the four presets + a
`Warmup` preset that reproduces today's behaviour) and `Scripts/Bot/BotSenses.cs`
(target and threat queries over `PlayerController.All`, wrap-aware deltas,
intercept point). `BotBrain` gains states; keep each file under ~400 lines.

**States added:** `Pursue`, `Evade`, `Disengage`. Priority each tick:
ground/space override → `Recover` → `Evade` → `Disengage` → `Pursue` →
`Cruise` (no target). Loops, reversals and the show-off stall stay only
where the tier says so.

- **Target:** nearest deployed, non-invulnerable plane that is not self;
  re-evaluated every second with hysteresis (switch only if 30 % closer).
  Humans and bots are equal targets (S3).
- **Pursue:** intercept point `P = target.pos + target.vel × (dist /
  muzzleSpeed) × tier.lead`, one iteration. Steer with the existing
  `SteerTo`. Per-burst aim error (degrees, re-rolled each burst) is added to
  the desired heading. Throttle full beyond half the engage range, then match
  the target's speed. Closer than ~3 u and closing → break away; a midair
  kills both.
- **Fire:** inside the tier's cone and engage range, on direct geometry only
  (no shots across the wrap), never at an invulnerable plane, never in the
  first second after its own spawn (existing rule), and for explosive weapons
  never closer than `ExplosionRadius` + margin.
- **Evade:** an enemy within ~12 u behind me with me inside its ~25° forward
  cone. After the tier's reaction delay and with the tier's probability,
  break with the existing turn machinery (`TryPickTurnDirection` /
  `TickTurn`).
- **Disengage (Ace, Baron):** at 1 HP steer for the nearest health, repair or
  shield crate if one exists; otherwise fly evasively.
- **Fairness, non-negotiable:** `InputRampPerSecond` stays 2 for every tier.
  Skill is reaction time, aim error, lead and discipline, never turn rate.
  No reading of human input, no teleports, no velocity writes.

Starting numbers (tune in the soak and with the owner's playtest):

| | Rookie | Pilot | Ace | Baron |
|---|---|---|---|---|
| Reaction delay (s) | 0.8 | 0.45 | 0.25 | 0.15 |
| Aim error (deg) | 14 | 8 | 4 | 2 |
| Fire cone (deg) | 12 | 8 | 5 | 4 |
| Lead factor | 0 | 0.5 | 0.9 | 1.0 |
| Engage range (u) | 14 | 20 | 26 | 30 |
| Burst (s) | 0.3–0.6 | 0.4–0.9 | 0.5–1.2 | 0.6–1.4 |
| Gap between bursts (s) | 2.5–5 | 1.5–3 | 0.8–1.8 | 0.5–1.2 |
| Evade probability | 0 | 0.4 | 0.75 | 0.9 |
| Disengage at 1 HP | no | no | yes | yes |
| Show aerobatics | yes | sometimes | no | no |
| Weapon pool | MG | MG, Flak | + Twin MG, Heavy Flak, Sniper | + Rocket |

**Soak harness (editor only):** `Doodlebugs → Debug → Bot Soak`: 4 bots, no
human, rounds back to back at raised `Time.timeScale` (editor, solo host
only), a CSV of per-tier kills, deaths and death causes. Gates for the PR:

- ground + out-of-bounds deaths ≤ 15 % of a bot's deaths at Pilot and above;
- over 50 rounds a tier beats the tier below it in > 60 % of rounds;
- no bot stuck in `Recover` timeouts (the existing 6 s warning) more than
  once per 10 minutes;
- profiler: 4 bots add < 0.3 ms per physics tick on the editor host, zero
  per-tick allocations.

Human-facing gates (owner playtest; telemetry later): a first-time player
beats two Rookies most rounds; Ace is a fight for the owner.

## 10. WP5 — Solo session and Home screen

**Home screen** = new `Scripts/UI/HomeScreen.cs` on `UiKit` (GameHUD does
not grow; `HangarMode.Searching` delegates to it and `IsAnyOverlayOpen:1422`
includes `HomeScreen.IsOpen`). Opaque, title, version, **PLAY**, **PARTY**,
SETTINGS. PARTY switches the same screen to today's status line
("SEARCHING FOR GAME…") with a CANCEL; PLAY stays available there and cancels
the search. `OnSessionReset` (`GameHUD.cs:1400`) returns here.

**`ConnectionManager`:**

- `Start:114-136` subscribes to events and stops. No pairing at boot.
- `BeginParty()` = today's body (`DetermineMode` → `StartNativeP2P` /
  `StartDiscovery`). Before hosting it restores `ServerListenAddress =
  "0.0.0.0"` and, on LAN, the `UnityTransport` as the NGO transport.
- `BeginSolo()` mirrors `OnDiscoveryTimeout:291-329` without
  `StartBroadcast` / `StartListening`: assign `UnityTransport` to
  `NetworkConfig.NetworkTransport`, `SetConnectionData("127.0.0.1", port,
  "127.0.0.1")`, `ConnectionApprovalCallback`, **arm solo on `MatchManager`
  first**, `StartHost()`, `SetRole(Host)`, `SelectRandomBackground()`,
  `LocalPlayerManager.Instance?.OnHostStarted()` (desktop couch seats).
- `ReturnHome()`: `MatchManager.ServerAbortSession()`, NGO `Shutdown()`,
  stop any discovery or native session, raise the session-reset event.
  Starting PARTY or JOIN from solo is `ReturnHome()` followed by the party
  flow after NGO's asynchronous shutdown (same 0.5–0.7 s defer the yield
  path uses, `:278`).

**`MatchManager`:**

- `ServerArmSolo(botCount, tier)`; `IsSoloSession` (server, armed, one
  connected client).
- `OnServerStarted:193-200`: when armed, do not open the Waiting hangar;
  wait for the host plane and `PlaneSkinManager.IsSpawned` (same readiness as
  `BotManager.WorldReady:105`), set the combat roster, call
  `ServerStartBattle()`. No PreBattle countdown, no first-launch skin picker:
  the first round uses MG and the current look. The draft appears at the
  first intermission.
- `ServerAbortSession()`: stop `_serverRoundFlowCoroutine` and
  `_preBattleCoroutine`, clear ready set, late-join deadlines and the three
  run tables, despawn every bot, `ScoreManager.ResetMatch()`, disarm solo.
- After each solo round: adjust `PlayerProfile.solo.tier` (S4) and apply it
  to the roster before the next round.

**In battle:** corner HOME button for solo only (S10), reusing the corner
button the warm-up already has (`ShowHangarCornerButton`, `GameHUD.cs:1617`).
The intermission hangar in solo relabels READY to NEXT ROUND and offers HOME.

Four channels for the new events: PLAY and PARTY presses (visual press state,
SFX, haptic, label change); round start in solo (existing start cue).

Done when:

- cold start → PLAY → first shot < 15 s on an iPhone with nothing touched in
  between (measured, see §11 `time_to_first_shot`);
- airplane mode, Wi-Fi and Bluetooth off: full run to the podium, HOME, PLAY
  again, no leftovers (no ghost rows, timers or bots);
- a fresh install shows **no** system permission prompt before PARTY is
  tapped;
- PARTY then behaves exactly as v2.9.x on two devices (iOS ↔ iOS,
  Android ↔ desktop);
- desktop: PLAY with a second couch seat gives 2 humans + bots.

## 11. WP6, WP7, WP8 in brief

**WP6 — Bots in LAN and the toast.** "+ BOT / − BOT" for the host in the
Waiting, PreBattle and Intermission hangars (cap: 4 participants; a human
joining beyond that removes a bot at the next round start). In Waiting, the
first bot makes participants ≥ 2 and starts the PreBattle countdown, which
replaces the warm-up bot. READY list shows bots as ready. Then the
**nearby-pilot toast** on platforms where passive listening costs no prompt
(desktop, Android on Wi-Fi) or after `firstSuccessAt` elsewhere: a solo
session listens, and `ConnectionManager.OnServerFound:208` branches on solo
to raise `OnNearbyHost` instead of `HandleHostConflict`. The toast is a
non-modal chip ("PILOT NEARBY · JOIN"); tapping it runs
`ReturnHome()` → connect. Never automatic.

**WP7 — Telemetry backend.** Owner links a UGS project first (Editor →
Services; fills `cloudProjectId`). Then add the Analytics and Cloud
Diagnostics packages at the versions current for Unity 6000.3 (read the
package docs; do not trust remembered API shapes), replace the body of
`Telemetry.Log`, keep every call site. Start collection per S12. Events (all
through `Telemetry`):

| Event | Fields | Added in |
|---|---|---|
| `session_start` | version, platform, language | WP7 |
| `boot_mode` | `solo` / `party` | WP5 |
| `time_to_first_shot` | seconds since process start, mode | WP5 |
| `round_start` | mode, bots, tier, arena, weapon | WP5 |
| `round_end` | result (`won`/`lost`/`quit`), kills, deaths, duration, weapon, arena, mode, tier | WP5 |
| `solo_tier_change` | from, to | WP5 |
| `bot_added` / `bot_removed` | count, phase | WP6 |
| `lan_pair` | peers, transport | WP6 |
| `nearby_toast` | shown / joined / ignored | WP6 |
| `profile_reset` | reason | WP2 |

Until WP7 lands these are log lines, which is fine for development and not
fine for the store build (rule 7).

**WP8 — Submission kit.** `marketing/appstore.md`: App Review notes say one
device is enough (PLAY → bots), LAN is optional and how to see it; drop "no
single-player". Listing description: lead with solo dogfights, keep LAN as
the party trick; remove "no data collection" and the "needs a second device"
note; "no in-app purchases" stays true until Phase 4. Re-translate the
changed paragraphs in every `fastlane/metadata` locale and rerun the
checker. `docs/privacy.html` and the privacy label: "Product Interaction +
Crash Data, not linked to you, no tracking". Screenshots of solo play.
ROADMAP: Phase 1 `[done v3.0]`, D1/D5/D7 resolved, §0 and §5 dates; CLAUDE.md:
Match Flow, bot and boot sections rewritten for the new flow. Then the owner
submits.

## 12. Verification

1. **Compile:** batchmode `-quit` with `-buildTarget` for the platform under
   test; for WP1, WP5 and WP8 the real `./ci/unity-build.sh` builds for both
   mobile targets.
2. **Catalogues:** `python3 tools/l10n/check_catalogues.py` exits 0.
3. **LAN regression (ParrelSync, every netcode WP):** host + clone pair,
   PreBattle → READY → battle → results → hangar → next round; late join
   mid-battle; client leaves → freeze to Waiting → warm-up bot returns;
   host quits → client back on Home.
4. **Solo (WP5):** the "done when" list in §10, on a phone.
5. **Bots (WP3, WP4):** the lists in §8 and the soak gates in §9.
6. **Mixed (WP6):** 2 devices + 2 bots; add and remove a bot in each hangar
   mode; bot rows identical on both devices; a third device late-joins.
7. **Perf:** p95 frame time on the iPhone test device not worse than v2.9.3
   with 3 planes in the air.
8. **Device lab and local deploy commands** are in the project memory notes
   (`doodlebugs-device-lab`, `doodlebugs-release-procedure`).

## 13. Risks

- **A filter flipped the wrong way** (§4's two lists) gives a phone that
  buzzes for bot deaths, touch controls that steer a bot, or a host HUD with
  three detailed panels. Walk every `IsBot` site in the WP3 PR description.
- **Solo host yanked into someone else's lobby** by `HandleHostConflict` once
  WP6 adds a listener. The solo branch must come before the conflict check.
- **Stale server state after leaving solo** (coroutines restarting a round
  on a dead session, run tables carried into a party). `ServerAbortSession`
  must be the only way out and must be idempotent.
- **Loopback port clash:** two instances on one machine (ParrelSync) both
  binding 127.0.0.1:7777 in solo. Check whether this UTP version accepts
  port 0 for a host; otherwise fall back to the next free port.
- **Listen address left on loopback** after solo makes a PARTY host
  unreachable with no error. Covered by the two-device pass in §10.
- **Bots that feel unfair** kill retention faster than bots that are dumb.
  When in doubt ship easier; S4's ladder raises difficulty by itself.
- **Bots that crash into the ground** read as broken. The soak gate exists
  for this; do not ship a tier that fails it.
- **Translation load:** WP3–WP6 add roughly 15–20 keys × 45 catalogues, WP8
  rewrites store text × ~50. Machine translation, unreviewed, like the rest
  of PR #57.
- **Analytics and EU consent** (S12) is a legal question the owner answers;
  WP7 must not guess.
- **App Review may still open PARTY** on one device and see a search screen.
  It must show a clear status and a way back (CANCEL), and the review notes
  must say so.
- **`GameHUD.cs` is 2 956 lines.** Home screen, toast and bot controls go in
  their own classes; touching GameHUD is limited to delegation and filters.

## 14. Owner checklist

- Confirm or override S1–S14 (silence = defaults).
- WP1: device language pass, merge PR #57, tag v2.9.4.
- WP4: play Rookie → Baron once the brain PR is up; say which tiers feel
  wrong and how.
- WP5/WP6: phone solo pass, two-device party pass, tag v2.10.x betas.
- WP7: create and link the UGS project; decide the consent model.
- WP8: App Store Connect record, privacy questionnaire, screenshots, submit.
- Optional: a native reader for the languages that matter most
  commercially (de, fr, es, ja, ko, zh-Hans, pt-BR).

## 15. Out of scope

Tween utility, camera shake, hit-stop, callouts, haptic taxonomy (Phase 2).
Campaign, level objectives, FTUE levels (Phase 3). Currency, unlocks, real
IAP (Phase 4). Online play (Phase 7). A shaping text renderer for RTL and
Indic scripts. Bot use of Bomb and Mine. Pause.
