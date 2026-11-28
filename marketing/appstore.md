# App Store — Doodlebugs Revival 2.14

Podklady pro App Store Connect (plán `Prompts/33`, 2.14 = placené balíčky
nátěrů). Texty pro všechny lokality jsou ve `fastlane/metadata/ios/<locale>`
(lane `ios_store_media` je nahrává), tady je anglický zdroj.
Google Play: `marketing/playstore.md`.

## Name (30)
Doodlebugs Revival

## Subtitle (30)
Couch WWI biplane dogfights

## Promotional text (170)
Fight combat bots alone or grab a friend in the same room and dogfight. Pick a plane, draft a weapon, first to three kills wins. No accounts, no ads.

## Description
Take on the bots alone, or grab a friend, sit in the same room and dogfight.

Doodlebugs Revival is a WWI biplane arcade shooter. Tap PLAY and you are in a round against combat bots within seconds: offline, anywhere, no account, no setup. When friends are around, tap PARTY: nearby iPhones and iPads find each other on their own over Wi-Fi or Bluetooth, no servers needed, and the sky fills with tracer fire.

- SOLO AGAINST BOTS: bots that pursue, evade and dogfight back. Play on the train, on a plane, anywhere.
- FAST ROUNDS: first to 3 kills or the 3-minute bell. Best-of-5 runs with a podium for the champion.
- THE HANGAR: before every round, draft a weapon (machine guns, flak, bombs, rockets, mines, homing seekers), pick a gadget (nitro, barrel roll, thunderclap) and spend run points on shield, hull, engine and fire-rate upgrades.
- 15 PLANES, 50 PAINTS: from the classic biplane to a dragon, a wasp and a paper plane. Every shape fires its own element - fire, lightning, venom, plasma. 12 paints are free; four optional paint packs add 38 more.
- DESTRUCTIBLE GROUND: five arenas from a city skyline to a volcano. Shoot the terrain apart tile by tile and hide in the clouds.
- SUPPLY DROPS: shoot a crate out of the sky for a special weapon.
- STALL AND RECOVER: let the engine die, flip, relight it - or double-tap the stick and the plane does it for you.
- PICK A LOOK: ten hangar themes, from poster art to arcade neon.

No ads, no data collection. Just planes. The optional paint packs are cosmetic only: every plane, weapon and gadget is free for everyone, and nothing you buy changes a fight.

Playing together needs a second iPhone or iPad nearby. iPhones and Android phones do not play with each other.

## Keywords (100)
biplane,dogfight,local multiplayer,offline,bots,couch,arcade,plane,shooter,retro,pixel,party,versus

## URLs
- Support URL: https://github.com/lioilsources/doodlebugs-revival
- Marketing URL: https://olin.now/doodlebugs
- Privacy Policy URL: https://lioilsources.github.io/doodlebugs-revival/privacy.html

## In-app purchases (non-consumable, 2.14)
Create each product in App Store Connect (Monetization -> In-App Purchases)
and in Play Console (Monetize -> Products -> In-app products) with exactly
these ids (`Skins/IAPManager.cs`). Prices are fixed tiers: a pack $0.99, a
bundle of more than two packs $1.99. Nothing else.

| Product id | Type | Price | Display name | Description (store, max 45 / 80) |
|---|---|---|---|---|
| `skins_camo_pack` | Non-consumable | $0.99 | Camo Pack | 9 camouflage paints for every plane. |
| `skins_metallic_pack` | Non-consumable | $0.99 | Metallic Pack | 9 metallic paints for every plane. |
| `skins_cosmic_pack` | Non-consumable | $0.99 | Cosmic Pack | 9 cosmic paints for every plane. |
| `skins_homage_pack` | Non-consumable | $0.99 | Homage Pack | 11 tribute paints for every plane. |
| `skins_all_packs` | Non-consumable | $1.99 | All 4 Packs | All 38 premium paints in one pack. |

Cosmetic only: no paint changes a fight, every plane, weapon and gadget is
free, and in a LAN party nothing gameplay-relevant is paid (ROADMAP §3).
Apple wants a review screenshot per product (the PLANE tab with the BUY
cards) and the products attached to the 2.14 version before submitting.

## Privacy labels
Data Not Collected. No analytics, no ads SDK, no accounts. Peer names in
MultipeerConnectivity never leave the devices. Purchases go through Apple
only; the game never initialises Unity Gaming Services, so Unity IAP has no
project to report to. Check before submitting: Unity IAP 5.5 ships a
`PrivacyInfo.xcprivacy` that declares purchase history, user/device ids and
usage data "linked, not tracking" (data Unity collects when UGS is linked).
Xcode's privacy report will list it; the label stays "Data Not Collected"
only while no UGS project is linked (`ProjectSettings` has no
`cloudProjectId`). `IAP_DISABLE_IDFA` keeps AdSupport out of the iOS build.

## Age rating
Cartoon or Fantasy Violence: Infrequent/Mild. Multiplayer without
free-form communication (no chat). No web access, no ads, not Made for
Kids. In-App Purchases: yes (shown by the store). Expect 9+.

## Price and availability
Free, all territories. EU trader status: fill in under Business before
submitting (required for the EU storefronts).

## Release
Manual release after approval (plan 33 R7).

## App Review notes (paste as is)

```
WWI biplane dogfight. Solo against bots, or local multiplayer for 2-8
nearby devices. No accounts, no servers, no ads.

ONE DEVICE: launch, tap PLAY, then FLY. You fly a round against combat
bots (right thumb: the stick steers, left thumb: the round buttons fire).

IN-APP PURCHASES: four optional paint packs and one bundle of all four,
all non-consumable and cosmetic only. Hangar -> PLANE tab: locked paints
show a green BUY card with the price; RESTORE PURCHASES is in the same
row as the PAINT title.

TWO DEVICES: launch on both, tap PARTY and accept the Local Network
prompt. They pair automatically in about 10 seconds. Tap READY on both.
First to 3 kills wins the round, best of 5.

Local Network and Bluetooth are used only to find nearby players.
```

## Screenshots (landscape)
- iPhone 6.9": 2868×1320 (required set).
- iPad 13": 2732×2048, only if the iPad passes the device check (plan 33
  R5); otherwise the build is iPhone-only.
- Five shots: Home with PLAY / PARTY, hangar on the WEAPON tab, PLANE
  tab, dogfight with an explosion, podium.
- What to capture and how to resize: `Prompts/34-SCREENSHOTS_HANDOFF.md`.

## App preview video (optional)
Two devices pairing and one round, under 30 s for the store, under 2 min
for the review notes.
