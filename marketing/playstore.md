# Google Play — Doodlebugs Revival 2.14

Podklady pro Play Console (plán `Prompts/33`, 2.14 = placené balíčky
nátěrů). Účet je firemní: AAB jde vždy do produkční stopy (jako koncept,
dokud není vše v pořádku), testovací stopy se nepoužívají. Texty pro
všechny lokality jsou ve `fastlane/metadata/android/<locale>`, tady je
anglický zdroj.

## App name (30)
Doodlebugs Revival

## Short description (80)
WWI biplane dogfights against bots or friends nearby. No accounts, no ads.

## Full description (4000)
Take on the bots alone, or grab a friend, sit in the same room and dogfight.

Doodlebugs Revival is a WWI biplane arcade shooter. Tap PLAY and you are in a round against combat bots within seconds: offline, anywhere, no account, no setup. When friends are around, tap PARTY: nearby Android phones and tablets find each other on their own over Wi-Fi or Bluetooth, no servers needed, and the sky fills with tracer fire.

- SOLO AGAINST BOTS: bots that pursue, evade and dogfight back. Play on the train, on a plane, anywhere.
- FAST ROUNDS: first to 3 kills or the 3-minute bell. Best-of-5 runs with a podium for the champion.
- THE HANGAR: before every round, draft a weapon (machine guns, flak, bombs, rockets, mines, homing seekers), pick a gadget (nitro, barrel roll, thunderclap) and spend run points on shield, hull, engine and fire-rate upgrades.
- 15 PLANES, 50 PAINTS: from the classic biplane to a dragon, a wasp and a paper plane. Every shape fires its own element - fire, lightning, venom, plasma. 12 paints are free; four optional paint packs add 38 more.
- DESTRUCTIBLE GROUND: five arenas from a city skyline to a volcano. Shoot the terrain apart tile by tile and hide in the clouds.
- SUPPLY DROPS: shoot a crate out of the sky for a special weapon.
- STALL AND RECOVER: let the engine die, flip, relight it - or double-tap the stick and the plane does it for you.
- PICK A LOOK: ten hangar themes, from poster art to arcade neon.

No ads, no data collection. Just planes. The optional paint packs are cosmetic only: every plane, weapon and gadget is free for everyone, and nothing you buy changes a fight.

Playing together needs a second Android device nearby. Android phones and iPhones do not play with each other.

## In-app purchases (non-consumable, 2.14)
Create each product in Play Console (Monetize -> Products -> In-app products) with exactly
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
In Play the products must be active before the AAB reaches review; the
BILLING permission comes with the build.

## Graphics
- Icon 512×512 PNG, feature graphic 1024×500: `tools/store/fit_screenshots.py --play-art`.
- Phone screenshots: 2-8, 16:9 landscape (1920×1080).
- 7" and 10" tablet screenshots: optional; skip unless an Android tablet is at hand.

## Store settings
- Category: Games / Arcade. Tags: Arcade, Action, Multiplayer.
- Contact e-mail: business@olin.now. Website: https://olin.now/doodlebugs.
- Privacy policy: https://lioilsources.github.io/doodlebugs-revival/privacy.html

## App content (questionnaires)
- Privacy policy: the URL above.
- Ads: no ads.
- App access: all functionality is available without special access.
- Content rating (IARC): violence - cartoon or fantasy, mild; no user
  communication, digital purchases: yes (cosmetic paint packs), no location
  sharing. Expect PEGI 7 / ESRB E10+ with "In-Game Purchases".
- Target audience: 13 and over (not designed for children; avoids the
  Families policy).
- Data safety: no data collected, no data shared. Purchases are processed
  by Google Play; the game itself sends no purchase data anywhere. Permissions (Bluetooth,
  nearby Wi-Fi devices, location on Android 12 and older) are used only to
  find nearby players; nothing leaves the device.
- Government apps, financial features, health: no.

## Release
1. Create the app (game, free), fill the sections above.
2. Monetize -> Products -> In-app products: create the five products above
   and activate them.
3. Production: upload `doodlebugs-signed.aab` from the
   `doodlebugs-android-aab` artefact of the tag as a draft release; send it
   to review only when everything is in order. Never a testing track.
4. Play App Signing (Google-generated key); our key stays the upload key.
   First review of a new app can take up to a week.
