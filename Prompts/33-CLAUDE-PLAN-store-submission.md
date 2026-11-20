# Podání na App Store a Google Play: opravy a dodělávky (plán implementace)

Status (2026-11-20): **plán.** Podkladem je revize `main` @ `9f64d77`
(v2.11.1) čtením kódu a rozbor posledních buildů z CI. **Žádný nález nebyl
spuštěn v Unity ani na zařízení**; u každého je uvedeno, jak jistý je.
Rozhodnutí v §2 mají výchozí hodnotu, Opus může začít hned. Vlastník
20. 11. odpověděl na R2, R4 a R5; snímky a video udělá na MacBooku.

Zadání vlastníka (2026-11-20): zítra poslat hru na oba obchody; projít kód
a najít možné chyby; říct, co ještě brání vydání; připravit plán dodělávek.

Navazuje na `Prompts/28`–`32` (letový model, arzenál, hangár, motivy, LEAN)
a na `ROADMAP.md` §3. Sólo režim (ROADMAP fáze 1) **není** součástí tohoto
plánu, viz §6.

## 0. Verdikt

- **iOS:** binárka je technicky v pořádku (§1.1). Podání zítra je reálné,
  pokud se dnes udělá P0 z §4 a vlastník stihne průchod na dvou zařízeních.
  Hlavní riziko není technické: recenzent s jedním telefonem uvidí jen
  cvičný let s botem. Doporučení: podat s ručním uvolněním po schválení.
- **Android:** kód verzí 2.10 a 2.11 se pro Android **nikdy nestavěl** a
  pipeline umí jen APK, Play chce AAB. Účet je firemní (R4), uzavřený test
  s 12 testery neplatí a AAB jde do produkce. Podání zítra je reálné, pokud
  WP0 neukáže chyby platformy a WP4 projde. Recenze nové aplikace v Play
  může trvat i týden.
- **Chyby:** dvě vedou do slepé uličky na hlavní cestě (N1, N2), šest
  dalších je vidět při běžném hraní (N3, N4, N6, C1, C2, U2). Všechny P0
  jsou opravy v řádu jednotek až desítek řádků.

## 1. Fakta

### 1.1 Buildy

| Co | Stav |
|---|---|
| iOS 2.11.1 (38), TestFlight | SDK 26.2, min. iOS 15, iPhone + iPad, jen na šířku. Privacy manifest bez sběru dat a bez sledování, `ITSAppUsesNonExemptEncryption = false`, texty pro místní síť a Bluetooth, Bonjour služby. Od Apple jen varování ITMS-90725 (Xcode 27 od dubna 2027, neblokuje). `UIApplicationSceneManifest` v plistu už je. |
| Android, poslední build | 13. 11. z větve `feat/lan-bots` (2.9.3). IL2CPP, jen arm64, `targetSdk` 37, zarovnání 16 KB prošlo. **Dnešní `main` se pro Android nestavěl.** |
| Android oprávnění | INTERNET, Wi-Fi, Bluetooth, NEARBY_WIFI_DEVICES, poloha do API 32, VIBRATE a **`com.android.vending.BILLING`** (z balíčku nákupů). `uses-feature` bluetooth, location a wifi vycházejí jako povinné. |
| Android balíček | jen APK (`BuildScript.BuildAndroid` má `buildAppBundle = false`), podpis přes `ci/sign-apk.sh` (apksigner). Lane pro Play není. |

### 1.2 Podklady pro obchody

- `marketing/appstore.md` popisuje verzi 2.3: „no single-player“, „no
  in-app purchases“, recenzentovi říká, že jedno zařízení zůstane na
  SEARCHING. Nic z toho už neplatí přesně.
- `docs/privacy.html` žije na GitHub Pages (větev `main`, složka `docs`).
  Tvrdí „collects no data“. `Telemetry.Log` jen píše do logu, takže to
  platí, dokud se nezapne analytika.
- Screenshoty, ukázkové video, grafika pro Play (ikona 512, feature graphic
  1024×500) ani texty pro Play v `main` nejsou. Anglické texty pro oba
  obchody existují na větvi `i18n/all-langs` (`fastlane/metadata/…/en-US`).

### 1.3 Sólo režim leží mimo `main`

PR #57–#63 (lokalizace do 45 jazyků, `PlayerProfile`, bojoví boti, obrazovka
PLAY/PARTY, boti v LAN) jsou otevřené od 13. 11. Podle vlastního předávacího
dokumentu nikdy neběžely v Unity ani na zařízení. Vycházejí z `main` před
PR #64, takže s dnešním `main` kolidují ve 13 souborech: jsou psané proti
starému hangáru v `GameHUD` (na `main` smazáno 1 600 řádků) a proti `UiKit`.
`MatchManager`, `ConnectionManager` a `Bot/*` se od té doby na `main`
nezměnily a slučují se čistě. Port je práce na dny, ne na dnešek.

Jedno zařízení dnes: úvodní obrazovka bez tlačítek (iOS 4 s + rozptyl,
Android na Wi-Fi 10–12 s), pak hangár WAITING FOR PLAYERS s výběrem letadla,
zbraně, gadgetu a motivu, tlačítko FLY a cvičný let bez skóre s botem.

## 2. Rozhodnutí vlastníka

| # | Otázka | Výchozí (doporučení) |
|---|---|---|
| R1 | Rozsah první veřejné verze | Dnešní `main` + opravy: LAN party a cvičný let. Sólo proti botům až další verze (§6). |
| R2 | 38 prémiových barev | **Skrýt** (zůstane 12 volných × 15 tvarů). Odemknout zdarma nedoporučuji: pozdější zamčení by hráčům bralo, co už měli. S tím odpadá i otázka názvů v balíčku Homage. **Potvrzeno vlastníkem 20. 11.** |
| R3 | Balíček nákupů v buildu | Odebrat `com.unity.purchasing` (zmizí oprávnění BILLING a listing „no in-app purchases“ bude pravda). |
| R4 | Typ účtu v Google Play | **Vyřešeno 20. 11.:** vydává se přes firemní účet (organizace). Uzavřený test s 12 testery po 14 dní neplatí, AAB jde do produkce. Interní test jen krátce, kvůli ověření instalace z obchodu. |
| R5 | iPad | **Vlastník 20. 11.:** iPad je, má iOS 17 (hra chce 15+) a na tom zařízení už jedna hra nehrála zvuk, viz §7. Zůstává iPhone + iPad, pokud iPad projde §5 včetně zvuku a rozložení 4:3. Když neprojde nebo se nestihne, první verze jen pro iPhone (`targetDevice: 0`); přidat iPad později jde, ubrat ne. |
| R6 | Číslo verze | Pokračovat v řadě: `2.12.0`. |
| R7 | Uvolnění po schválení | Ruční. Doba recenze je rezerva na druhý průchod zařízeními. |
| R8 | Flight Lab ve store buildu | Ne. Směr LEAN ověřit ve Flight Labu teď na 2.11.1, pak ho ze store buildu vykompilovat (U3). Když se směr do tagu neověří, je LEAN v první verzi ve výchozím stavu vypnutý (`ControlSettings.LeanDefault`); hráč si ho zapne v SETTINGS. |

## 3. Pořadí práce

Jedna větev `fix/store-submission`, jeden PR, commity po balících. Testovací
buildy přes `workflow_dispatch`, tag až po průchodu zařízeními.

| WP | Co | Odhad | Čeká na vlastníka |
|---|---|---|---|
| 0 | Android build dnešního `main` do Firebase, hygiena repa | 0,5 h + build | ne |
| 1 | Síť a průběh zápasu (N1–N6) | 3–4 h | ne |
| 2 | Souboj a svět (C1, C2) | 1–2 h | ne |
| 3 | Rozhraní (U1–U4) a to, co vidí recenzent (W1–W5) | 3–4 h | R2, R3, R8 |
| 4 | AAB pro Google Play | 2–3 h + build | ne |
| 5 | Podklady pro obchody | 2 h + snímky a video | snímky, video (na MacBooku) |
| 6 | Testovací buildy, průchod zařízeními, tag, podání | builds + 1–2 h | ano |
| 7 | P1 opravy, když zbude čas (jinak 2.12.1) | 4–6 h | ne |

## 4. Balíky

Priorita: **P0** = před podáním, **P1** = před uvolněním nebo v 2.12.1,
**P2** = později. Jistota: **čteno** = potvrzeno v kódu na obou koncích,
**odhad** = mechanismus potvrzen, výsledek závisí na běhu.

### WP0 — Android build a hygiena

1. Hned na začátku `gh workflow run release-android.yml --ref main`. Android
   verzi nového hangáru, motivů (filtry přes `ReadPixels`), LEAN a RECOVER
   nikdo neviděl; výsledek ukáže, co přibude do P0.
2. Cherry-pick z větve `i18n/all-langs`: `60d2bee` (Team ID a LAN adresa ven
   ze sledovaných souborů) a `d306858` (LAN adresa ve scéně → loopback). Obojí
   je dnes ve veřejném repu. Z `88becd6` jen pravidlo `.gitignore` pro
   `Assets/Resources.meta`.

### WP1 — Síť a průběh zápasu

| ID | P | Jistota | Co se stane | Kde |
|---|---|---|---|---|
| N1 | P0 | čteno | iPhone jako klient se nedozví, že hostitel skončil nebo že mu systém na pozadí zrušil spojení. Zůstane v „bitvě“ se zamrzlými letadly, pomůže jen ukončení aplikace. | `Network/NativeLocalTransport.cs` `HandlePeerDisconnected` |
| N2 | P0 | čteno | Android na mobilních datech: po odmítnutí oprávnění Nearby zůstane úvodní obrazovka navždy, není na ní žádné tlačítko. Stejně končí větev „backend unavailable“. | `Network/NativeLocalCoopManager.cs` `Begin` |
| N3 | P0 | čteno | Hostitel odejde uprostřed bitvy. Druhé zařízení se stane hostitelem, čas kola mu běží dál a po 3 minutách v čekárně vyhlásí ROUND OVER; fáze se už nevrátí do čekání a bot se neobjeví. | `GameState/MatchManager.cs` `Update`, `HandleLocalSessionEnded` |
| N4 | P0 | čteno | Soupeř odejde během 10 s pódia. Hostiteli zůstane RUN OVER přes arénu, ovládání je skryté, `IsRunOver` se nevynuluje. | `UI/GameHUD.cs` `OnHangarOpened`, `MatchManager.HandlePhaseFromServer` |
| N5 | P0 | odhad | Zmrazení do čekárny se posílá přes první nalezené letadlo serveru; může to být už despawnuté letadlo odcházejícího hráče a RPC se tiše zahodí. Hostitel pak sedí v hangáru, kde READY nic nedělá. | `MatchManager.BroadcastThroughServerPlayer`, smyčky `Sync*` v `ScoreManager` |
| N6 | P0 | čteno | Zařízení hostuje podruhé v jednom běhu aplikace (po N1/N3 běžné): žádné mraky, nikdo se nerespawnuje, letadlo pod obrazovkou hlásí smrt každý fyzikální krok. | `CloudManager.cs` `_networkedCloudsSpawned`, `_clouds` |

Opravy:

- **N1:** na klientovi, když `peerId == _hostPeerId`, zařadit
  `NetworkEvent.Disconnect` pro `ServerClientId`. Dál už funguje dnešní
  `ConnectionManager.OnClientDisconnected` → „Host disconnected“ →
  `NativeRestartDiscovery`.
- **N2:** `NativeLocalCoopManager` dostane událost „nativní cesta není
  k dispozici“ (oprávnění odmítnuto, backend chybí). `ConnectionManager` na
  ni přepne `_mode` na `Lan` a zavolá cestu `OnDiscoveryTimeout()` (hostitel
  na UDP, bez Nearby). Text stavu neutrální, bez výzvy do Nastavení. K tomu
  W2: tlačítko na úvodní obrazovce jako obecná úniková cesta.
- **N3:** kontrolu časového limitu podmínit `Phase == GamePhase.Battle`.
  V `HandleLocalSessionEnded` zavolat `ScoreManager.ResetMatch()` (existuje,
  nikdo ji nevolá).
- **N4:** v `GameHUD.OnHangarOpened` zničit pódium stejně jako
  `OnSessionReset`; ve větvi Waiting v `HandlePhaseFromServer` nastavit
  `IsRunOver = false`.
- **N5:** přeskakovat `!player.IsSpawned`; vlastní stav hostitele při
  zmrazení nastavit přímo, ne přes RPC. Ověřit ve dvou instancích: klient
  odejde v bitvě, o přestávce a při odpočtu.
- **N6:** při startu serveru vynulovat příznak, `_clouds`, cooldowny a
  frontu; `RequestRespawn` bez živého mraku použije `GetFallbackPosition`.

### WP2 — Souboj a svět

| ID | P | Jistota | Co se stane | Kde |
|---|---|---|---|---|
| C1 | P0 | čteno | Střely bez životnosti (MG, Twin MG, Lance, Stinger a jejich Mk II), které odletí horem, nikdy nezmizí: pás Space ignorují a nad ním nic není. Každá zůstane síťovým objektem s částicovou stopou na všech zařízeních a přes kola se sčítají. MG je výchozí zbraň. | `Bullet.cs` (kontakt se Space, jediný časový despawn), `Shooting.cs` `SetLifetime` |
| C2 | P0 | čteno, četnost výpočtem | Na telefonech 19,5:9 jako hostitel padne asi čtvrtina respawnů do pásu Space: horní mrak je v y 15, pás začíná v 15,39. Letadlo se objeví s vypnutým motorem a do 3 s je na zemi. Zhruba 2 % respawnů navíc padne za boční stěnu. | `CloudManager.cs` `SpawnPlayerAtCloud`, `GetInitialSpawnPosition` |

Opravy:

- **C1:** vždy nastavit životnost (`BulletLifetime > 0 ? BulletLifetime :
  6 s`) a v `Bullet.Update` na serveru despawn nad `Space.bounds.max.y + 30`.
- **C2:** omezit bod respawnu: `y ≤ Space.bounds.min.y − 2`,
  `x` mezi `Left.bounds.max.x + 2` a `Right.bounds.min.x − 2`
  (`ArenaBounds`). Totéž pro první nasazení a bota.

### WP3 — Rozhraní a to, co vidí recenzent

| ID | P | Jistota | Co se stane | Kde |
|---|---|---|---|---|
| U1 | P0 | čteno | Záložka PLANE je první, co nový hráč vidí: štítek „Premium · soon“ a 38 z 50 barev se zámkem, který nic nedělá. `IAPManager` při startu žádá obchod o čtyři produkty, které neexistují; obnovení nákupů chybí. | `UI/Hangar/PlaneTab.cs` `BuildLayout`, `BuildPaints`; `GameState/GameSetup.cs:100`; `Skins/IAPManager.cs` |
| U2 | P0 | čteno | Náhledy letadel zmizí po změně motivu na záložce PLANE nebo po druhém klepnutí na PLANE: starý `PlaneTab` se ničí až na konci snímku a jeho `OnDestroy` smaže kompozity, které už ukazuje nový. | `PlaneTab.OnDestroy`, `PlaneModelCatalog.TrimComposites`, `HangarScreen.SwitchTab` |
| U3 | P0 | čteno | Flight Lab (5 klepnutí na SETTINGS) je dostupný i ve vydaném buildu. | `UI/SettingsOverlay.cs` `TitleTap` |
| U4 | P0 | výpočtem | Popisy zbraní a gadgetů jsou na telefonu uříznuté uprostřed věty: pole je asi 62 j. široké a 2 řádky vysoké, 11 z 12 popisů potřebuje 3–7 řádků. Na motivech CHROME, CLAY a SOLARIA se vejde jeden řádek. | `UI/Hangar/DraftCard.cs:144-150` |

| ID | P | Co doplnit | Kde |
|---|---|---|---|
| W1 | P0 | Skrýt prémiové barvy a filtr (R2); `IAPManager` nevytvářet, soubor zabalit do `#if DOODLEBUGS_IAP`; odebrat balíček nákupů (R3). | viz U1, `Packages/manifest.json` |
| W2 | P0 | Tlačítko **PRACTICE** na úvodní obrazovce: přeskočí hledání a rovnou hostuje. Hostitel se dál inzeruje, párování to nebrání. | `HangarScreen.BuildBoot`, nové `ConnectionManager.HostNow()` |
| W3 | P0 | Text „Searching nearby (mobile data)...“ se ukazuje na každém iPhonu i na Wi-Fi → „Searching for nearby players...“. | `NativeLocalCoopManager.cs:97` |
| W4 | P1 | `uses-feature` bluetooth, location a wifi s `required="false"`, jinak Play odfiltruje zařízení bez nich. | `Assets/Plugins/Android/AndroidManifest.xml` |
| W5 | P1 | `Application.targetFrameRate = 60` nastavit při startu, ne až při spawnu letadla (úvod a hangár běží na 30). | `PlayerController.cs:291` → `GameSetup` |

Opravy:

- **U2:** mazat kompozity, jen když žádný `PlaneTab` nežije (statický
  čítač), nebo mazání přesunout do `HangarScreen.Close`. Klepnutí na už
  aktivní záložku nic nepřestavuje.
- **U3:** `FlightLabOverlay` a `TitleTap` za `#if DOODLEBUGS_LAB`;
  `BuildScript` symbol přidá pro buildy z `workflow_dispatch`, pro tag ne.
- **U4:** popis na plnou šířku karty pod řádkem s ikonou a názvem, tři
  řádky; texty v `WeaponProfile` a `GadgetProfile` zkrátit, aby se vešly při
  kartě 140 j. Rozměry kontrolovat proti `Prompts/30-hangar-mockup.html`.
- **W2:** `HostNow()` platí jen ve stavu Searching. LAN: zastavit
  naslouchání a zavolat `OnDiscoveryTimeout()`. Nativní cesta: zrušit okno
  hledání a `BecomeHost()`; před udělením oprávnění použít cestu z N2.
  Telemetrie `boot_practice`.

### WP4 — AAB pro Google Play

- `BuildScript`: výstup končící `.aab` zapne `buildAppBundle`.
  `ci/unity-build.sh` dnes příponu přepisuje na `.apk`, povolit obě.
- `ci/sign-aab.sh`: AAB podepisuje `jarsigner`, ne `apksigner`. Klíč je
  PKCS#12, které Java nepřečte, proto stejný postup jako `sign-apk.sh`
  (OpenSSL 3 → PEM), z PEM dočasné úložiště čitelné pro Javu, podpis,
  `jarsigner -verify`.
- `release-android.yml`: druhý výstup `doodlebugs-android-aab` jako artefakt
  (tag a ruční spuštění). APK pro Firebase zůstává.
- První nahrání do Play Console je ruční (API funguje až potom). Zvolit
  Play App Signing s klíčem od Googlu; náš klíč zůstane jako upload key.
  Lane `supply` až pro další verze. Účet je firemní (R4): AAB nejdřív do
  interního testu, po ověření instalace povýšit do produkce.

### WP5 — Podklady pro obchody

- `marketing/appstore.md` přepsat na 2.12, přidat `marketing/playstore.md`.
  Základ textů vzít z `fastlane/metadata/{ios,android}/en-US` na větvi
  `i18n/all-langs`; první verze jen anglicky (zásada „jeden jazyk“).
- Poznámky pro recenzenta (anglicky, upravit podle výsledné podoby):

  > Local multiplayer dogfight for 2–8 nearby devices. No accounts, no
  > servers, no purchases, no ads.
  > ONE DEVICE: launch, tap PRACTICE, pick a plane, tap FLY. You fly a
  > practice round against a training drone (right stick steers, left
  > buttons fire).
  > TWO DEVICES: launch on both and accept the Local Network prompt. They
  > pair automatically in about 10 seconds. Tap READY on both. First to 3
  > kills wins the round, best of 5.
  > Local Network and Bluetooth are used only to find nearby players.
  > Demo video of two devices pairing and playing: (link)

- `docs/privacy.html`: doplnit Android (Nearby Connections), nové datum.
  Věta „collects no data“ zůstává.
- Skript `tools/store/fit_screenshots.py` (Pillow): snímky z telefonu
  přepočítat na 2868×1320 pro App Store a 16:9 pro Play; feature graphic
  1024×500 a ikona 512 z existující grafiky (`tools/icon`, podklady motivu
  Sky Poster).
- Snímky a video dělá vlastník na MacBooku. Opus k tomu připraví
  `Prompts/34-SCREENSHOTS_HANDOFF.md`: co vyfotit (úvod, hangár se zbraněmi,
  výběr letadla, souboj, pódium), z jakého zařízení, cílové rozměry, kam
  soubory uložit a jak pustit `fit_screenshots.py`. Video ze dvou zařízení
  do 2 minut.
- iPad (pokud zůstane, R5): snímky z iPadu 4:3 se bez ořezu zvětší na
  2732×2048 pro 13" slot v App Store Connect.

Formuláře v konzolích vyplňuje vlastník:

| Kde | Co |
|---|---|
| App Store Connect | texty, snímky, věkové hodnocení (mírné kreslené násilí), soukromí „Data Not Collected“, cena zdarma, status obchodníka pro EU, poznámky a video, výběr buildu, ruční uvolnění |
| Play Console | založit aplikaci, texty a grafika, dotazník hodnocení obsahu, cílová skupina 13+, Data safety „nic se nesbírá ani nesdílí“, bez reklam, přístup bez přihlášení, nahrát AAB do interního testu a po ověření instalace povýšit do produkce |

### WP6 — Buildy, průchod zařízeními, podání

1. Kompilace (Roslyn proti Unity DLL, nebo CI) po každém balíku.
2. `gh workflow run release-ios.yml --ref fix/store-submission`, totéž pro
   Android. Tyhle buildy mají Flight Lab.
3. Průchod podle §5. Chyba = oprava na větvi a nový build.
4. Vlastník: merge, `bundleVersion` 2.12.0, tag `v2.12.0`. Tag pošle desktop,
   TestFlight, Firebase i AAB.
5. Vlastník: build z TestFlightu podat k recenzi; AAB nahrát do Play.
6. Po schválení doplnit `appstore` a `playstore` v katalogu ol1n.now,
   `ROADMAP.md` §0 a stavové odstavce v `CLAUDE.md`.

### WP7 — P1, když zbude čas

| ID | Jistota | Co se stane | Oprava |
|---|---|---|---|
| N7 | čteno | Discovery nenese verzi hry. Po první aktualizaci se klient jiné verze připojuje dokola k hostiteli, který ho odmítne, a sám hostovat nezačne. **Do první veřejné verze je to levné, pak už ne.** | verze do dat discovery i nativní inzerce, jinou verzi ignorovat; po N neúspěších hostitele vynechat; hledání restartovat i z `OnClientStopped` |
| N8 | čteno | Třetí zařízení připojené o přestávce nedostane hangár a blokuje READY ostatním až 120 s. | ve snapshotu otevřít hangár Intermission se zbývajícím časem |
| N9 | čteno | Dva Androidy na mobilních datech: při dvou hostitelích se porovnává id zařízení s náhodným id endpointu, výsledek není zrcadlový. | předávat `getEndpointName()` |
| N10 | čteno | Po ztrátě hostitele nový hostitel zdědí cizí nárok na tvar, jeho vlastní ukazuje TAKEN. | `_claims.Clear()` v `OnNetworkSpawn` na serveru |
| C3 | čteno | Strop arény je pás 2 j. Letadlo ho při rychlém stoupání proletí a 3–6 s letí nad obrazovkou. | collider Space protáhnout vysoko nahoru; ověřit balony a mraky |
| C4 | odhad | Tři a více zařízení: vzdálená letadla se při startu bitvy odparkují na jednom místě a srazí se. | srážku ignorovat při nezranitelnosti; pozice při spawnu |
| C5 | čteno | Vzdálený hráč může za jednu událost zemřít vícekrát (pád po sestřelu, opakované hlášení pod mapou). | příznak „respawn čeká“ na serveru |
| C6 | čteno | Speciál ze shozu se sám odpálí, když hráč při sebrání drží spoušť. | po změně zbraně na speciál chtít nový stisk |
| C7 | odhad | Na klientech běží efekty při výstřelu dřív, než dorazí typ zbraně: miny bez kruhu dosahu, špatný zvuk. | `PlaySpawnFx` až po změně zbraně |
| C8 | čteno | Stopa střely se staví 2–5× na výstřel; každá mina hledá hráče každý snímek. | stavět jednou; posunout `_nextBeepAt` před návratem |
| U5 | výpočtem | UPGRADES: čtyři karty po 120 j. se nevejdou, čtvrtá je z půlky mimo. | vodorovný posuv jako `PlaneTab.Strip` |
| U6 | výpočtem | Názvy ve slotech přetékají (BARREL ROLL, THUNDERCLAP). | best-fit do 12 |
| U7 | odhad | Banner má pevnou velikost 44 a výšku 60: na AQUA a CLAY se nejspíš nevykreslí, na COIN-OP přeteče obrazovku. | `Ui.Text` s měřítkem motivu, best-fit, overflow |
| U8 | čteno | Nastavení a výběr motivu se nezavřou, když začne bitva; letadlo letí bez ovládání, dokud hráč nezavře. | zavřít v `HangarScreen.Close` |

## 5. Ověření na zařízeních

Jedno zařízení (iPhone i Android):

- Studený start → PRACTICE → hangár → FLY, první výstřel do 15 s.
- Na záložce PLANE žádné zámky ani „soon“; změna motivu náhledy nesmaže.
- Popisy zbraní a gadgetů jsou celé.
- Deset respawnů za sebou: nikdy se nezačíná s vypnutým motorem.
- Minuta střelby z MG nahoru, pak kontrola plynulosti.
- Vypnutý motor → dvojí klepnutí na páku → letadlo se srovná.
- LEAN: ve Flight Labu (testovací build) kladné hodnoty při otočení po směru
  hodin; jinak přepnout výchozí `LeanGyroSign` před tagem.
- HANGAR ↔ FLY pětkrát; minuta na pozadí v hangáru i za letu.
- Čerstvá instalace s odmítnutým oprávněním (místní síť, na Androidu na
  mobilních datech Nearby): hra se dá hrát sama.

iPad s iOS 17 (R5):

- Zvuk: efekty i hudba hrají v hangáru i v bitvě. Před testem vypnout tichý
  režim (zvonek v Ovládacím centru); hra ho respektuje, protože Unity bez
  `muteOtherAudioSources` používá zvukovou kategorii Ambient.
- Rozložení 4:3 (plátno 800×600): hangár, všechny záložky, HUD a pódium bez
  překryvů a bez prázdných pruhů.
- Plynulost v bitvě na mapě Volcano (nejvíc dlaždic).
- iPad jako hostitel i jako klient proti iPhonu.

Dvě zařízení:

- Spárování, READY, tři kola, pódium, nový běh.
- Klient ukončí aplikaci v bitvě, o přestávce a na pódiu: hostitel skončí
  v použitelném hangáru a může letět.
- **Hostitel** ukončí aplikaci v bitvě: klient se do několika sekund vrátí
  k hledání, hostuje, má mraky, respawnuje se a po 3 minutách nepřijde
  ROUND OVER.
- Klient na 10 s na pozadí a zpět.
- iPhone s Androidem se nespárují (iOS jede přes Multipeer); patří to do
  popisu v obchodě, ne mezi chyby.

## 6. Mimo tento plán

- **Sólo proti botům** (PR #58–#63): přenést na dnešní `main`. Jádro
  (`MatchManager`, `ConnectionManager`, `Bot/*`) se slučuje čistě; obrazovky
  (`HomeScreen`, `SoloControls`, `LanBotControls`, úpravy `GameHUD`) je
  potřeba napsat znovu proti `HangarScreen` a `Ui`. Plány na té větvi mají
  čísla 27 a 28, která jsou na `main` obsazená; při portu přečíslovat.
  Tlačítko PRACTICE z W2 se pak stane tlačítkem PLAY.
- **Lokalizace** (PR #57): nový hangár, motivy a nastavení mají asi sto
  řetězců, které v katalozích chybí.
- **Analytika** (ROADMAP fáze 0): změní text o soukromí i štítky v obchodech.
- **P2 z revize:** text HUD v bitvě má na telefonu 4–6 pt; HUD každý snímek
  hledá hráče a třídí; zařízení s různým poměrem stran se neshodnou na výšce
  arény (iPad s iPhonem až 7 j.); zásah do výkrutu dá falešné potvrzení;
  kill se může připsat útočníkovi z minulého života; „ROUND 7 OF 5“ při
  třech hráčích; ztmavení pod dialogy jen v safe area; soupiska ukáže jen
  čtyři piloty; log každé 2 s za letu.
- **Xcode 27** do dubna 2027.

## 7. Rizika

- **Recenze Apple kvůli dvěma zařízením.** Zmírnění: PRACTICE, poznámky,
  video. Když přesto vrátí, odpovědí je port sólo režimu, ne další úpravy
  textů.
- **Nic z plánu neběželo.** Průchod zařízeními může přidat práci; proto
  ruční uvolnění a rezerva.
- **Zásahy do sítě těsně před vydáním** (N1–N6). Bez průchodu na dvou
  zařízeních s finálním buildem nepodávat.
- **Odebrání balíčku nákupů** může rozbít build (lock soubor, link.xml).
  Záloha: balíček nechat, jen ho neinicializovat a oprávnění BILLING
  odebrat v manifestu.
- **Podpis AAB** s nestandardním PKCS#12. Záloha: nový upload key jen pro
  Play (aplikace tam ještě není, nic se tím nerozbije).
- **První Android build nového kódu** může ukázat chyby platformy; proto je
  WP0 první.
- **Recenze nové aplikace v Play** může trvat i týden; den vydání na
  Androidu neurčujeme my.
- **Zvuk na iPadu s iOS 17.** Ve hře Kiran (Flutter) byl tenhle iPad němý,
  protože CoreAudio před iOS 18.4 neotevře Ogg Opus. Stejnou cestou to tady
  nehrozí: všech 53 klipů importuje Unity jako Vorbis a dekóduje je samo,
  bez CoreAudio. Na zařízení neověřeno, proto je zvuk v §5.

## 8. Soubory

| Soubor | Balík |
|---|---|
| `Scripts/Network/NativeLocalTransport.cs`, `NativeLocalCoopManager.cs`, `ConnectionManager.cs` | 1, 3 |
| `Scripts/GameState/MatchManager.cs`, `ScoreManager.cs`, `GameSetup.cs` | 1, 3 |
| `Scripts/CloudManager.cs`, `Bullet.cs`, `Shooting.cs` | 1, 2 |
| `Scripts/UI/GameHUD.cs`, `HangarScreen.cs`, `SettingsOverlay.cs`, `FlightLabOverlay.cs` | 1, 3 |
| `Scripts/UI/Hangar/PlaneTab.cs`, `DraftCard.cs`; `Scripts/Skins/IAPManager.cs`, `PlaneModelCatalog.cs` | 3 |
| `Scripts/Weapons/WeaponProfile.cs`, `GadgetProfile.cs` (texty) | 3 |
| `Packages/manifest.json`, `Assets/Plugins/Android/AndroidManifest.xml` | 3 |
| `Assets/Doodlebugs/Editor/BuildScript.cs`, `ci/unity-build.sh`, `ci/sign-aab.sh` (nový), `.github/workflows/release-android.yml` | 4 |
| `marketing/appstore.md`, `marketing/playstore.md` (nový), `docs/privacy.html`, `tools/store/` (nový) | 5 |
| `ROADMAP.md`, `CLAUDE.md` | 6 |
