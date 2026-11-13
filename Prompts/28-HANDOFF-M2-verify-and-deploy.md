# Handoff — verify the solo-first stack on the M2 and ship a test build

**Status: open, 2026-11-13.** For the Claude Code session on the Mac Mini M2,
which is the self-hosted `macos-unity` CI runner with a working Unity licence.
The dev Mac cannot run Unity right now (batchmode exits 198: "No valid Unity
Editor license"), so nothing in `Prompts/27-CLAUDE-PLAN-solo-first.md`
WP2–WP6 has been run in an editor yet.

## What is on the table

A stack of open PRs. Each one's base is the previous branch, and
`feat/lan-bots` contains all of them:

| PR | Branch | WP |
|---|---|---|
| #58 | `chore/resources-meta` | WP0 hygiene (base `main`) |
| #57 | `i18n/all-langs` | WP1 owner's localisation PR (45 languages) |
| #59 | `feat/player-profile` | WP2 PlayerProfile JSON |
| #60 | `feat/combat-bot-identity` | WP3 combat bots as participants |
| #61 | `feat/combat-bot-brain` | WP4 brain, four tiers, editor soak |
| #62 | `feat/solo-session` | WP5 Home screen (PLAY / PARTY), solo on loopback |
| #63 | `feat/lan-bots` | WP6 + BOT / − BOT, nearby-pilot toast |

**What has been verified:** a licence-free Roslyn compile of
Assembly-CSharp for Android, iOS and macOS (method bodies bound), plus the
editor assembly. `tools/l10n/check_catalogues.py` reports 0 errors with
fontTools installed.

**What has not been verified:** no Unity import, no scene, no IL2CPP, no play
mode, no ParrelSync, no soak, no device.

## Rules

- **Do not merge any PR or push a tag.** A `v*.*.*` tag ships every platform
  and creates a GitHub release. The test build goes out through
  `workflow_dispatch` on the branch instead (step 4).
- A fix goes on the branch that introduced the code. Then merge it up the
  stack (`git merge` into each later branch, push each). Never rebase: the PRs
  are already open.
- A new UI string means a key in `en.txt` and in all 44 other catalogues, and
  the checker must stay green.
- The runner shares this machine. Don't run a local Unity build while a
  `Release iOS` or `Release Android` job is in progress
  (`gh run list --limit 5`).

## Steps

### 1. Check out and open

```bash
git fetch origin && git switch feat/lan-bots && git pull
```

Open the project in Unity 6000.3.13f1, either from a separate clone or from
the dev checkout, but not from the runner's `_work` folder. Wait for the
import to finish. Gate: the Console shows zero compile errors.

Check that `Assets/Resources.meta` is not showing as a new file; WP0
ignores it.

### 2. Real builds of both mobile targets

```bash
./ci/unity-build.sh android /tmp/out/doodlebugs.apk
./ci/unity-build.sh ios /tmp/out/ios
```

Gate: both succeed. If one fails, fix it on the right branch (see Rules),
then run it again.

### 3. Editor checks (Scene01, play mode)

Work through `Prompts/27` §12 items 3–6. In short:

1. **Boot:** the Home screen appears with no network activity, meaning no
   discovery log lines before you tap anything.
2. **PLAY:** the battle starts against 2 bots within 15 s. Kills, deaths and
   round end score correctly, and the bot names read "ROOKIE 1" and so on.
   HOME returns to the Home screen and PLAY works again.
3. **Soak:** run `Doodlebugs → Debug → Bot Soak: Start (all tiers, x4)` for
   at least 50 rounds, then `Stop and Report`. The gates are in `Prompts/27`
   §9:
   - ground and out-of-bounds deaths are at most 15 % for Pilot and above;
   - each tier beats the tier below it in more than 60 % of rounds;
   - no more than one `Recover` timeout per 10 minutes;
   - under 0.3 ms per physics tick for 4 bots, and no per-tick allocations.

   Write the CSV numbers into PR #61. If a gate fails, tune `Bot/BotTier.cs`
   on `feat/combat-bot-brain`.
4. **ParrelSync, LAN regression:**
   - Set up a host and a clone: PARTY on both, then
     PreBattle → READY → battle → results → hangar → next round.
   - Late join mid-battle.
   - The client leaves: play freezes back to Waiting and the warm-up bot
     returns.
   - The host quits: the client goes back to Home.
5. **LAN bots:** in the host's hangar, press + BOT twice. The bot rows must
   be identical on both instances. Then press − BOT. The participant cap is 4.

Report the result of each check as a comment on the PR it belongs to.

### 4. Ship a test build (TestFlight and Firebase), no tag

The mobile workflows have a `workflow_dispatch` trigger. They build the
chosen ref and run the same upload lanes as a tag push: `fastlane ios
ios_beta` (TestFlight) and the Firebase App Distribution lane. A dispatch
skips the GitHub-release step, which runs on tags only.

```bash
gh workflow run release-ios.yml --ref feat/lan-bots
gh workflow run release-android.yml --ref feat/lan-bots
gh run list --limit 4   # watch; iOS takes ~50–130 min, Android ~90
```

The version comes from `bundleVersion` (2.9.3). The build number comes from
`GITHUB_RUN_NUMBER`, so TestFlight lists it as 2.9.3 (N+1) next to the
tagged build.

Gate: both runs are green. A build that is uploaded but not yet distributed
in Firebase prints a warning; check the testers group.

Only dispatch after steps 1–3 pass. A red step means no upload.

### 5. Device pass (the owner, with the TestFlight / Firebase build)

Use `Prompts/27` §10 "done when":

- PLAY to the first shot in under 15 s.
- A whole run works in airplane mode.
- No system prompt appears before PARTY. On iOS the local-network prompt
  only appears on PARTY.
- PARTY works across two devices.
- The p95 frame time is no worse than v2.9.3.

## When done

Write one summary comment on PR #58 (the stack root): what passed, what
failed, the run URLs of the dispatched builds, and the soak numbers. Update
the status line of `Prompts/27`. The owner decides about merging (in the
order #58, #57, then #59 → #63), the version bump and the tag.

## Known, not fixed

`settings.hint_stick` describes the stick axes backwards compared with the
real mapping (up = turn left, right = throttle up). This is pre-existing and
also on `main`.

## Not started

WP7 (UGS Analytics) and WP8 (submission kit) are waiting on the owner. WP7
needs a UGS project link and the EU consent decision (S12). WP8 needs store
text in 50 languages, the privacy details and App Store Connect.
