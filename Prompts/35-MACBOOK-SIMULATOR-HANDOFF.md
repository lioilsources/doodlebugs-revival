# 35 — Handoff na MacBook: simulátory pro obchody a review

Stav: připraveno 2026-11-22 na Mac Mini (M2). Pokračuje se na MacBooku. Ten je
na stejné LAN a má víc paměti, takže utáhne dva až tři simulátory současně.
Mac Mini s 16 GB měl při jednom iPadu Pro 13" pomalé snímky a iPad
nestihl doběhnout.

## Výchozí stav (2.12.0)

- Tag `v2.12.0` je na TestFlightu (build 142) a ve Firebase. Rozeslání skupině
  `alfa` proběhlo ručně, další buildy už půjdou automaticky (PR #69).
- Snímky App Store jsou ze simulátoru v `fastlane/screenshots/en-US/`
  (iPhone 6.9" a iPad 13", po 6). Metadata jsou v `fastlane/metadata/en-US/`.
- Nahrání do ASC dělá workflow `store-media-ios.yml` (lane `ios_store_media`).
  První běh spadl, protože API klíč v secrets je jen pro čtení. Výměnu
  secrets řeší session **Store** s Olinovým OK.
- Google Play: appku založila session Store. Listing a grafika jsou ve větvi
  `store/play-assets` (`fastlane/metadata/android/en-US/`). Nahrání a review
  dělá Store s Olinovým OK.
- Postup snímání ze simulátoru: `Prompts/34-SCREENSHOTS_HANDOFF.md`, sekce
  „Automaticky ze simulátoru (2.12)“. Skripty jsou v `tools/store/`.

## Co chybí (úkoly pro MacBook)

1. **iPad sada znovu a celá.** Na M2 iPad doběhl jen 12 snímků souboje z 60
   (`TIMEOUT` 420 s). Spusť
   `SKIP_UNITY=1 DEVICE="iPad Pro 13-inch (M5)" TIMEOUT=900 tools/store/shoot_simulator.sh`,
   vyber lepší souboj s explozí a nahraď `ipad13_01_*` a `ipad13_04_*`.
2. **Dva hráči proti sobě.** Snímek pódia a kill feed se dvěma jmény ze
   simulátoru nevzniknou, protože director hraje sám proti botovi.
   - Dva simulátory na jednom Macu sdílí síť hostitele, takže by se měly
     najít. iOS páruje přes MultipeerConnectivity, ne přes UDP broadcast
     (viz paměť „iOS pairing“). Nejdřív ověř, že se dva simulátory spárují
     ručně.
   - Director potřebuje roli klienta. Navržený šev: `simctl launch` předá
     aplikaci každou proměnnou `SIMCTL_CHILD_X` jako `X`. Ať
     `StoreShotDirector` čte `STORESHOTS_ROLE` (`host`/`client`):
     - Klient nevolá `HostNow()`, čeká na discovery a v hangáru dá READY.
     - Host čeká na druhého hráče a projde celý run až na pódium.
     - Podle `MatchManager` je to 3 výhry kol po 3 kill. Bot na obou
       stranách přes `SetInputOverride` funguje stejně jako teď.
   - Nový build potřebuje Unity 6000.3.13f1. Buď ho nainstaluj na MacBook,
     nebo požádej session na M2 (Doodle), ať build se změnou vyrobí.
3. **Video pro App Review (povinné v praxi).** Apple chce vidět párování dvou
   zařízení, jinak hrozí zamítnutí podle 2.1 (hra pro dva, reviewer má jeden
   telefon).
   - Natoč `xcrun simctl io <udid> recordVideo --codec h264 out.mp4` na obou
     simulátorech současně: spuštění, spárování a jedno kolo, pod 2 min.
   - Obě videa slož vedle sebe: `ffmpeg … -filter_complex hstack`.
   - Nahraj je jako neveřejné (unlisted) a odkaz vlož do
     `marketing/appstore.md`, sekce „App Review notes“. Řádek s videem byl
     v 2.12 odstraněn, protože video nebylo.
   - Odkaz předej session Store do App Review info.
4. **App preview video (volitelné).** Stejný záznam zkrácený na 15–30 s pro
   6.9" iPhone na šířku. Rozměry a limity ověř v aktuální nápovědě ASC.

## Jak dostat build na MacBook

- **Bez Unity:** hotový simulátorový build 2.12.0 s directorem leží na M2
  v repu jako `run/doodlebugs-2.12.0-storeshots-sim.zip` (78 MB,
  gitignorováno). Zkopíruj ho po LAN (adresa M2 je v `infra.md`
  v DevConventions) a rozbal:
  `ditto -x -k doodlebugs-2.12.0-storeshots-sim.zip Builds/iOS-shots-dd/Build/Products/Release-iphonesimulator/`
  Pak funguje `SKIP_UNITY=1`. Build je arm64, pro Apple Silicon.
- **S Unity:** `tools/store/shoot_simulator.sh` bez `SKIP_UNITY` vyexportuje
  a postaví vše sám (`BuildScript.BuildIOSStoreShots`). První import projektu
  trvá dlouho.

## Pasti (narazilo se na ně na M2)

- Simulátor nesmí dostat x86_64 binárku. `BuildIOSStoreShots` nastavuje
  `simulatorSdkArchitecture = ARM64`, jinak instalace hlásí „Needs to Be
  Updated“.
- Když se `simctl install` zasekne, ukonči `simctl` a `CoreSimulatorService`
  a simulátor nabootuj ručně.
- Modul ScreenCapture je v projektu vypnutý. Director snímá přes
  `Texture2D.ReadPixels` v `WaitForEndOfFrame`.
- Simulátor nemá výřez ani dotykové ovládání. Na snímcích to nevadí, ve videu
  pro reviewera to řekni v poznámce.
- Google Play chce poměr nejvýš 2:1, `fit_screenshots.py` řeže na 2640×1320.
- Billboard „Cloud Nine Gum“ na snímcích je fiktivní herní art, není to
  cizí značka.

## Výstup

- Nové snímky dej do `fastlane/screenshots/en-US/` (stejné názvy a pořadí)
  a Play verze přes `tools/store/fit_screenshots.py`.
- Změny directoru a snímky jsou jeden PR, video a odkaz druhý PR.
- Merge je na Olinovi. Nahrání do obchodů dělá Store.
