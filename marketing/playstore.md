# Google Play — Doodlebugs Revival 2.12

Podklady pro Play Console (plán `Prompts/33`). Účet je firemní: AAB jde
z interního testu rovnou do produkce, uzavřený test s 12 testery neplatí.
Texty anglicky, první verze jen v angličtině.

## App name (30)
Doodlebugs Revival

## Short description (80)
Local-multiplayer biplane dogfights. No accounts, no ads, no purchases.

## Full description (4000)
Grab a friend, sit in the same room, and dogfight.

Doodlebugs Revival is a local-multiplayer biplane arcade shooter. Android
phones on the same Wi-Fi find each other on their own, and without Wi-Fi
they pair over Bluetooth: no accounts, no servers, no setup. Launch the
game on both, they pair in seconds, and the sky fills with tracer fire.
Alone? Tap PRACTICE and fly against a training drone.

- FAST ROUNDS: first to 3 kills or the 3-minute bell. Best-of-5 runs with a podium for the champion.
- THE HANGAR: before every round, draft a weapon (machine guns, flak, bombs, rockets, mines, homing seekers), pick a gadget (nitro, barrel roll, thunderclap) and spend run points on shield, hull, engine and fire-rate upgrades.
- 15 PLANES, 12 PAINTS: from the classic biplane to a dragon, a wasp and a paper plane. Every shape fires its own element - fire, lightning, venom, plasma.
- DESTRUCTIBLE GROUND: five arenas from a city skyline to a volcano. Shoot the terrain apart tile by tile and hide in the clouds.
- SUPPLY DROPS: shoot a crate out of the sky for a special weapon.
- STALL AND RECOVER: let the engine die, flip, relight it - or double-tap the stick and the plane does it for you.
- GAMEPADS: plug in a controller and the touch controls step aside.

No ads, no in-app purchases, no data collection. Just planes.

Playing together needs a second Android device nearby. Android phones and
iPhones do not play with each other.

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
  communication, no purchases, no location sharing. Expect PEGI 7 / ESRB E10+.
- Target audience: 13 and over (not designed for children; avoids the
  Families policy).
- Data safety: no data collected, no data shared. Permissions (Bluetooth,
  nearby Wi-Fi devices, location on Android 12 and older) are used only to
  find nearby players; nothing leaves the device.
- Government apps, financial features, health: no.

## Release
1. Create the app (game, free), fill the sections above.
2. Testing -> Internal testing: upload `doodlebugs-signed.aab` from the
   `doodlebugs-android-aab` artefact of the tag, add yourself as tester,
   install from the Play link once.
3. Enrol in Play App Signing (Google-generated key); our key stays the
   upload key.
4. Promote the release to Production, countries: all. First review of a new
   app can take up to a week.
