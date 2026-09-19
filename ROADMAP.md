# Roadmap — Doodlebugs Revival

Z LAN party hry na titul, který na App Store obstojí vedle Candy Crush Saga,
Brawl Stars a Subway Surfers. Tento dokument je produktový směr; každá fáze
dostane před implementací vlastní plán v `Prompts/NN-CLAUDE-PLAN-*.md`
(konvence repa) a po vydání se zde aktualizuje řádek se stavem.

Stav řádků: `[ ]` neplánováno v detailu · `[plan]` má plán v Prompts ·
`[wip]` rozpracováno · `[done vX.Y]` vydáno.

---

## 0. Kde jsme (2026-09-11)

Fakta z repa, ne z paměti:

- **Verze** v2.8.2 (tag 2026-09-06). CI staví desktop, Android (Firebase App
  Distribution) i iOS (TestFlight) z každého `v*.*.*` tagu. **Na App Store hra
  není** — listing kit je připravený v `marketing/appstore.md`, v katalogu
  `ol1n.now` jsou pole `appstore/playstore/testflight` prázdná.
- **Herní smyčka:** LAN-only. Boot = „SEARCHING FOR GAME…“, dokud se nenajde
  druhé zařízení. Sólo hráč vidí jen warm-up bota (Prompts/25) a nikdy
  nezačne bitvu. `marketing/appstore.md` pro App Review výslovně píše, že
  jsou potřeba dvě zařízení — to je riziko zamítnutí i nulová retence
  osamělého hráče.
- **Obsah:** 15 tvarů letadel × 50 liverií, 8 zbraní, 4 run-upgrady, 5 arén
  (Metropolis, MistyPeaks, DuneSea, Volcano, Orbit), 6 elementů projektilů.
  Vše kromě 38 „premium“ skinů je zdarma; IAP produkty ve storech neexistují,
  paywall je záměrně neaktivní (`Skins/IAPManager.cs`).
- **Persistence:** jediný `PlayerPrefs` klíč (vlastněné skin bundly). Žádný
  profil hráče, žádný postup, žádné statistiky přes sezení.
- **Měření:** žádná analytika, žádný crash reporting, žádné Game Center,
  žádný review prompt, žádné notifikace. Privacy label „Data Not Collected“.
- **UI:** celé generované v kódu (`UI/GameHUD.cs`, 2 923 řádků, uGUI, font
  Press Start 2P), texty natvrdo anglicky. Žádné tweeny, žádný screen shake,
  haptika = jediné `Handheld.Vibrate()` při vlastní smrti.
- **Obsahová pipeline** je silná stránka: arény, tvary, skiny, projektily,
  efekty i SFX se generují skripty v `tools/` (FLUX na SPARKu). Nový obsah
  je levný — to je konkurenční výhoda pro live-ops.
- **Tempo:** nárazové (73 commitů v týdnu 31. 8.–6. 9., pak týdny klidu).
  Odhady níže jsou v „soustředěných týdnech“, ne v kalendáři.

---

## 1. Cíl a měřítka

**North star:** hráč otevře appku kdekoli (v autobuse, sám, bez WiFi) a do
15 sekund střílí; po každém kole má důvod hrát ještě jedno a zítra se vrátit.
LAN party zůstává jako to, co hru odlišuje, ne jako podmínka hraní.

Cílové metriky (benchmark casual/mid-core na iOS, měřeno od fáze 0):

| Metrika | Dnes | Cíl v3.x |
|---|---|---|
| Čas od studeného startu k prvnímu výstřelu (sólo) | ∞ | < 15 s |
| D1 / D7 / D30 retence | neměřeno | 35 % / 15 % / 6 % |
| Délka sezení / sezení denně | neměřeno | 6–8 min / 3+ |
| Crash-free sessions | neměřeno | ≥ 99,5 % |
| Hodnocení na App Store | – | ≥ 4,5 ★ |
| Konverze na platícího | 0 (nelze platit) | 2–3 % |
| Frame time na iPhone 11 / Pixel 5 | „mobile stutter“ opraven v 2.8.2 | < 16 ms p95 |

---

## 2. Co „pocit Candy Crush“ znamená pro dogfight

Candy Crush je vzor pro *strukturu sezení a juice*, Brawl Stars (3minutové
PvP zápasy s boty, trophy road, postavy = naše letadla) je bližší vzor pro
*metu*. Rozbor pilířů:

| Pilíř | Candy Crush / Brawl Stars | Doodlebugs dnes | Mezera |
|---|---|---|---|
| Okamžitá hra | Ikona → level → hraju za 10 s | Boot → čekání na druhé zařízení | **Sólo režim proti botům** (fáze 1) |
| Krátké kolo s jasným cílem | Level 2–3 min, výhra/prohra, hvězdy | Kolo = 3 killy / 3 min — sedí | Hvězdy, cíl na obrazovce, variabilita cílů (fáze 3) |
| Juice | Každý tap má animaci, zvuk, částice; „Sugar Crush“ finále | Exploze, kouř, 8-bit SFX; UI bez animací | Tween, shake, hit-stop, callouty, výsledková sekvence, haptická taxonomie (fáze 2) |
| Viditelný postup | Saga mapa, světy, hvězdy | Best-of-5 run, po podiu reset | Kampaň 5 světů × 10 levelů + mapa (fáze 3) |
| Sbírka a meta | Postavy, skiny, trophy road | 15 × 50 × 8 obsahu, ale vše hned k dispozici | Měna, odemykání, sbírka s % (fáze 4) |
| Denní návyk | Denní odměny, mise, streak, notifikace | nic | Kalendář, 3 denní mise, lokální notifikace (fáze 5) |
| Sociální důkaz | Leaderboardy, přátelé, sdílení | LAN = nejsilnější sociální prvek, ale offline | Game Center, review prompt, share card (fáze 6) |
| Monetizace | IAP + reklamy, bez pay-to-win | Neaktivní IAP, listing slibuje „no IAP“ | Kosmetické bundly + starter pack, rozhodnutí o reklamách (fáze 4) |
| Data | Vše měřeno, A/B, remote config | nic | UGS Analytics + Cloud Diagnostics (fáze 0) |
| Store presence | Ikona, screenshoty s popisky, video, lokalizace, in-app events | Listing kit, ikona z `tools/icon` | Screenshoty, preview video, cs+en lokalizace UI (fáze 6) |

---

## 3. Zásady pro veškerou další práci

Platí od teď pro každý plán i PR (zrcadleno v CLAUDE.md):

1. **Solo-first.** Každá obrazovka a každý režim musí dávat smysl s jedním
   zařízením a nulovým připojením. LAN je bonus, který se objeví, nikdy
   podmínka.
2. **Pravidlo 15 sekund.** Studený start → první výstřel pod 15 s, bez
   účtu, bez nastavení, bez povolení (síťové a notifikační prompty až po
   prvním úspěchu, ne při bootu).
3. **Čtyři kanály na každou událost.** Kill, smrt, zásah, pickup, konec
   kola, konec runu, nákup: vždy vizuál + zvuk + haptika + číslo/text v UI.
   Nová událost bez všech čtyř kanálů není hotová.
4. **Bez pay-to-win.** Platí se jen za kosmetiku a čas (skiny, starter pack).
   V LAN partě je všechno herně relevantní k dispozici všem.
5. **Bez login wall, bez energie/životů** (rozhodnutí D3 — přehodnotit jen
   s daty).
6. **Jeden jazyk na locale.** Buď je UI kompletně lokalizované, nebo je
   celé anglicky. Smíšený jazyk = zamítnutí App Review (poučení z Lexify
   2.2.2, 2026-09-03).
7. **Měřitelné.** Každá fáze definuje event(y), kterými se pozná, zda
   fungovala. Bez eventu v analytice se feature nepovažuje za dodanou.
8. **`Time.timeScale` se v LAN nesahá.** Hit-stop a slow-mo jen v sólo hře
   (host je jediná simulace); v síťové hře by rozhodil klienty.
9. **GameHUD dál neroste.** Nové obrazovky (mapa, obchod, sbírka, denní
   odměny) jdou do vlastních tříd v `Scripts/UI/`; sdílený tween a layout
   util, ne copy-paste.

---

## 4. Fáze

### Fáze 0 — Základna a data (v2.9) `[ ]` · ~1–2 týdny

**Cíl:** umět měřit a ukládat, než se začne cokoli stavět. Beta na TestFlight
v externí skupině.

**Práce**
- Unity Gaming Services: propojit projekt (stejně to potřebuje IAP
  validace). Zapnout **Analytics** (`com.unity.services.analytics`) a
  **Cloud Diagnostics** (crash + exception). Anonymní, bez ATT promptu.
- Event taxonomie (`Scripts/Meta/Telemetry.cs`, jediné místo, kde se eventy
  posílají): `session_start`, `boot_mode {solo|lan}`, `round_start`,
  `round_end {result, kills, deaths, duration, weapon, arena}`,
  `lan_pair {peers}`, `input_scheme_set {scheme, source}` (Prompts/26),
  později `level_*`, `iap_*`, `daily_*`, `mission_*`.
- **PlayerProfile** (`Scripts/Meta/PlayerProfile.cs`): versionovaný JSON v
  `Application.persistentDataPath`, atomický zápis (tmp + rename), migrace
  podle `schemaVersion`. Zatím jen statistiky (kills, deaths, rounds,
  rounds won, playtime) a nastavení. Nahradí i `IAPManager` PlayerPrefs cache.
- **Tween util** (`Scripts/UI/Tween.cs`): coroutine-based, easing OutBack /
  OutCubic / OutElastic, `Scale`, `Fade`, `Move`, `Punch`, sekvence se
  staggerem. Bez balíčků třetích stran (repo je záměrně drží mimo; PrimeTween
  je náhradní varianta, pokud vlastní util nestačí výkonem).
- Výkonový rozpočet zapsat do CLAUDE.md a měřit na iPhone 11 / Pixel 5:
  p95 frame < 16 ms, boot do hangáru < 3 s, IPA < 150 MB.
- Privacy: `docs/privacy.html` a `marketing/appstore.md` přepsat na „Product
  Interaction + Crash Data, not linked to you, no tracking“. Věta „no data
  collection“ padá.

**Hotovo když:** eventy z TestFlight buildu jsou vidět v UGS dashboardu;
`profile.json` přežije restart i update; crash-free ≥ 99 % na betě.

---

### Fáze 1 — Solo-first: bitva proti botům (v3.0) `[ ]` · ~3 týdny

**Cíl:** hra se dá hrát sama. Odstranit jediný největší blok retence i
App Review.

**Hráč cítí:** otevřu, tapnu PLAY, za pár sekund lítám proti soupeřům, kteří
se snaží mě sestřelit. Když se vedle objeví kamarád, hra mi ho nabídne, ale
nevytrhne mě z kola.

**Práce**
- **Dotykové ovládání** `[plan]` (Prompts/26): on-screen joystick vpravo
  dole (Y = plyn, X = zatáčení, stejné osy jako `IInputProvider`), spouště
  vlevo dole s kruhovým nabíjením podle cooldownu zbraně a ikonou zbraně,
  druhá spoušť připravená pro druhý slot (`Shooting.NetWeaponId2`, hangárový
  draft až ve fázi 4). Gyro je pro lidi těžké → zůstává volitelně; výchozí
  schéma = joystick. Přepínač v nové `UI/SettingsOverlay.cs` (domov i pro
  přepínače z fáze 2), persistence přes `PlayerPrefs` do doby `PlayerProfile`.
  App Review na stole bez naklánění je vedlejší, ale reálný důvod.
- **Bojový bot.** Rozšířit `Bot/BotBrain.cs` (dnes ne-agresivní warm-up)
  o stavy PURSUE (lead-pursuit s rychlostí střely z `WeaponProfile`),
  EVADE (člověk v kuželu za mnou → zlom/loop), DISENGAGE (nízké HP → k
  power-upu), a o obtížnostní profily: **Rookie / Pilot / Ace / Baron**
  (reakční zpoždění, chyba míření ve stupních, délka dávky, agresivita,
  fairness ramp zůstává = nikdy neotočí rychleji než člověk). Warm-up
  chování zůstane jako tier „Warmup“.
- **Více botů jako soupeři.** `BotManager` spravuje N botů; bojový bot je
  plnohodnotný soupeř: má řádek ve skóre, kompaktní panel v HUD, respawn
  jako hráč, může sebrat power-up. Identita `(0, 90+i)` vedle rezervované
  99 pro warm-up; filtr z Prompts/25 se rozdělí na `IsWarmupBot`
  (neviditelný) vs `IsCombatBot` (viditelný, bez READY a claimů).
- **Fázový automat.** `MatchManager` dnes startuje bitvu při ≥ 2 klientech;
  podmínka se změní na „lidé + bojoví boti ≥ 2“. Sólo = host bez klientů.
- **Boot flow.** Searching hangár dostane hned velké **PLAY** (sólo) a menší
  **PARTY** (dnešní auto-pairing). Discovery běží dál na pozadí; nalezený
  peer = nenápadný toast „Pilot nablízku — JOIN“, nikdy modální.
- **Boti v LAN.** V hangáru „+ BOT“ do 4 účastníků (2 zařízení + 2 boti je
  o hodně lepší zápas). Boti jsou host-owned, síťově stejné jako warm-up bot.
- Hit-stop (0,05 s) a krátké slow-mo na posledním killu jen v sólo (zásada 8).
- `marketing/appstore.md`: přepsat App Review notes (jedno zařízení stačí),
  odstranit větu o LAN-only. **Podat na App Store.**

**Hotovo když:** studený start → první výstřel < 15 s bez druhého zařízení;
nový hráč vyhraje ≥ 70 % prvních kol proti Rookie a Ace vyhrává ~50 % proti
zkušenému hráči (z `round_end`); App Review projde.

---

### Fáze 2 — Juice (v3.1) `[ ]` · ~2 týdny

**Cíl:** každý tap a každý zásah má váhu. Toto je „Candy Crush feel“ v
nejužším smyslu.

**Práce**
- **UI pohyb.** Všechny overlaye scale-in OutBack 0,25 s; tlačítka
  squash při stisku; karty (zbraně, tvary, skiny) přijíždějí se staggerem;
  READY/PLAY pulzují („breathing“). Tween z fáze 0.
- **Kamera a zásahy.** `CameraShake` na kameře z `Camera/`: lehký (zásah),
  střední (kill), těžký (vlastní smrt). Bílý flash spritu při zásahu (už je
  damage flash — sjednotit). Plovoucí čísla „+100“ nad vrakem.
- **Callouty.** „FIRST BLOOD“, „DOUBLE KILL“, „ACE“ (3 v řadě bez smrti),
  „REVENGE“, „CLOSE CALL“ — přes kill feed, velký text s punch tweenem a
  vlastním stingem.
- **Výsledková sekvence** (Sugar Crush moment): zmrazení posledního killu →
  banner ROUND WON/LOST → hvězdy se plní jedna po druhé (3 stoupající tóny)
  → bonus za čas tiká nahoru s rychlým klikáním → mince letí do počítadla
  (příprava na fázi 4) → NEXT pulzuje. Podium: konfety, fanfára už existuje.
- **Haptická taxonomie.** Nativní plugin `Plugins/iOS/Haptics.mm`
  (`UIImpactFeedbackGenerator` light/medium/heavy, `UINotificationFeedback`
  success) a Android `VibrationEffect` přes `AndroidJavaObject` (modul
  `androidjni` už je povinný). `SfxManager.Haptic()` dostane parametr typu;
  UI tap light, kill medium, vlastní smrt heavy, výhra success.
- **Audio vrstvy.** Kill = impact + exploze + sting; hudební stingery na
  konec kola; battle bed zesiluje při 2/3 killů do cíle (`MusicManager`
  má crossfade, přidat intenzitu).
- **Splash → title.** Logo bounce, přelet letadla, PLAY dýchá. Boot zůstává
  pod 3 s.
- Nastavení: přepínač „Omezit pohyb“ (vypne shake), posuvníky hudba/SFX,
  haptika on/off.

**Hotovo když:** checklist „4 kanály“ prošel u všech událostí (tabulka v
plánu fáze); žádný overlay se neobjeví bez animace; p95 frame time se
nezhoršil.

---

### Fáze 3 — Kampaň a saga mapa (v3.2) `[ ]` · ~4 týdny

**Cíl:** viditelná cesta. Hráč vždy ví, co je další level, a proč se vrátit.

**Práce**
- **`LevelDef` ScriptableObject** (`Scripts/Campaign/`): svět (aréna
  profile), typ cíle, roster botů (počet, tier, tvary), omezení loadoutu,
  pravidla hvězd (výhra / bez smrti / pod čas), odměny (měna, first-clear
  unlock). Typy cílů pro variabilitu (Candy Crush má jelly/ingredients/
  order — my máme):
  - **Dogfight** N killů (dnešní kolo),
  - **Survive** T sekund proti přesile,
  - **Demolition** zničit X % terénu (destruktivní foreground už umí),
  - **Hunt** sestřel 3 vosy / 2 draky (element targety),
  - **Weapon-locked** vyhraj jen s bombami / minami,
  - **Rings** prolétni trasu (bez boje, učí letový model),
  - **Boss** jeden Baron s unikátním tvarem a 5 HP.
- **5 světů = 5 arén**, 10 levelů každý = 50 levelů; boss na 10. levelu
  světa (dragon/Fire → unicorn/Lightning → wasp/Venom → goose/Air →
  spacecraft/Plasma, tj. existující elementové tvary). Obtížnost pilovitě:
  těžký level každý ~5., po něm oddechový.
- **Mapa** (`Scripts/UI/MapScreen.cs`, ne v GameHUD): horizontální cesta
  (hra je landscape) s uzly, hvězdami, zámkem dalšího světa, pulzujícím
  aktuálním levelem; pozadí světa = existující background s parallaxem.
- **FTUE = levely 1–3.** L1 Rings (řízení + plyn), L2 sestřel 3 balóny
  (střelba), L3 jeden Rookie s 1 HP (první kill zaručený). Nápovědy
  kontextové (šipka „nakloň pro zatáčku“), žádné textové stěny. First-clear
  L3 = skin zdarma a první pohled do sbírky.
- **Retry bez tření.** SHOT DOWN → RETRY hned (bez životů, D3). Nabídka
  „jiná zbraň?“ po 2 prohrách v řadě.
- Postup v `PlayerProfile`; LAN zápasy připisují killy/statistiky lokálně
  z replikované skóre tabulky (žádný server).
- Eventy: `level_start`, `level_end {result, stars, duration, deaths,
  retries}`, `tutorial_step`.

**Hotovo když:** 50 levelů hratelných; medián času k první výhře < 3 min;
funnel L1→L5 ≥ 60 %; D1 ≥ 30 %.

---

### Fáze 4 — Meta ekonomika, sbírka, obchod (v3.3) `[ ]` · ~3 týdny

**Cíl:** obsah, který už existuje, se stane odměnou.

**Práce**
- **Jedna měna „Scrap“** (soft): za levely, hvězdy, denní odměny, LAN
  výhry. Žádná druhá „hard“ měna v první verzi — IAP prodává přímo bundly.
- **Model odemykání (D2):** tvary letadel se odemykají postupem světy,
  skiny za Scrap nebo IAP bundly (4 existující), zbraně v kampani postupně,
  **v LAN partě je vše herně relevantní k dispozici** (zásada 4). Premium
  skiny jsou jediná kosmetika, kterou nelze vyhrát.
- **Sbírka** (`Scripts/UI/CollectionScreen.cs`): tvary 15, skiny 50, zbraně
  8, % dokončení, badge NEW, náhled combo = existující picker karty.
- **IAP naostro:** vlastník založí produkty v App Store Connect + Play
  Console (ids v `IAPManager.SkinBundles`), reálné ceny místo placeholderu,
  Restore Purchases, UGS receipt validace. **Starter pack** ($1,99: skin +
  Scrap) nabídnutý jednou po výhře 5. levelu, ne dřív.
- **Loadout před levelem:** zbraň + 1 spotřební booster (Shield start /
  Damage / Repair — reuse `PowerUpType`), boostery za Scrap.
- **Reklamy (D4):** doporučení *žádné interstitial nikdy*; rewarded video
  („dvojnásobná odměna“) jen pokud data po fázi 5 ukážou potřebu — mění
  privacy label a přidává ATT prompt.
- Eventy: `iap_view`, `iap_purchase`, `unlock`, `scrap_earn/spend`.

**Hotovo když:** konverze a ARPDAU se měří; audit „nic v LAN není za
peníze“ prošel; store listing už neslibuje „no IAP“.

---

### Fáze 5 — Denní návyk a notifikace (v3.4) `[ ]` · ~2 týdny

**Cíl:** důvod otevřít appku zítra.

**Práce**
- **Denní odměna:** 7denní kalendář s eskalací, streak (Scrap → booster →
  skin 7. den), reset o půlnoci lokálního času.
- **Denní mise ×3** ze šablon („3 killy s Flak“, „vyhraj na Volcano“,
  „level bez smrti“, „2 LAN kola“), týdenní „Ace challenge“.
- **Lokální notifikace** (`com.unity.mobile.notifications`): „Denní bedna
  je připravená“, „Streak v ohrožení“ (večer). Oprávnění se žádá až po
  prvním vyzvednutí denní odměny, ne při bootu.
- **Live-ops kadence:** 1 nová aréna nebo 5 skinů měsíčně z existujících
  pipelines v `tools/`; sezónní víkendy jako App Store In-App Events.
- **Cloud save:** UGS Cloud Save s anonymní autentizací (cross-platform,
  bez účtu), fallback lokální profil. Řeší reinstall a nový telefon.
- Eventy: `daily_claim {day, streak}`, `mission_complete`, `notif_open`.

**Hotovo když:** D7 ≥ 15 %; ≥ 40 % DAU si vyzvedne denní odměnu.

---

### Fáze 6 — Sociální vrstva a store presence (v3.5, průběžně od fáze 1) `[ ]`

**Práce**
- **Game Center / Play Games:** tichý login, leaderboardy (hvězdy kampaně,
  killy celkem, nejlepší čas levelu), ~20 achievementů (první kill, ACE,
  100 killů, všechny světy, všechny tvary, LAN 4 hráči…).
- **Review prompt:** `UnityEngine.iOS.Device.RequestStoreReview()` po
  dokončení 2. světa nebo 3 LAN výhrách v řadě, max 1× za 90 dní, nikdy po
  prohře.
- **Share card:** PNG (level, hvězdy, letadlo, skóre) do nativního share
  sheetu.
- **Lokalizace:** tabulka stringů (`Scripts/UI/Strings.cs` nebo Unity
  Localization), všechny natvrdo psané texty v `GameHUD` → klíče. Start
  **cs + en kompletně** (zásada 6), pak de/fr/es/ja/ko/zh-Hans/pt-BR.
- **Store assety:** ikona (`tools/icon`), 5 screenshotů s popisky na
  6,9" a 13", 20s preview video (přelet → kill → výsledková sekvence →
  mapa), lokalizovaný subtitle/keywords, test variant ikony přes Product
  Page Optimization.
- **Release train:** 2 týdny, každé vydání má viditelné „What's New“;
  CHANGELOG.md buď oživit, nebo formálně nahradit GitHub Releases (dnes
  končí 31. 3. 2026 a neodpovídá tagům).

**Hotovo když:** listing má video + lokalizované screenshoty; review prompt
generuje hodnocení; ≥ 4,5 ★ po 100 hodnoceních.

---

### Fáze 7 — Online multiplayer (v4.0, 2027) `[ ]` · rozhodnutí D6

Unity Relay + Lobby (UGS): „hraj s kamarádem přes kód“ na stejném NGO
host-authority modelu. Matchmaking až po datech. Velká položka; startuje
jen když D30 a poptávka („kde je online?“ v recenzích) ukážou, že LAN +
boti nestačí. Trust model z `IAPManager` (host peer nic nevaliduje) je pro
kosmetiku přijatelný, pro ranked by potřeboval backend.

---

## 5. Časová osa

Při nárazovém tempu; každá fáze je samostatně vydatelná.

| Fáze | Verze | Orientačně | Milník |
|---|---|---|---|
| 0 Základna a data | v2.9 | září 2026 | TestFlight externí beta s analytikou |
| 1 Sólo proti botům | v3.0 | říjen 2026 | **Podání na App Store** |
| 2 Juice | v3.1 | listopad 2026 | Sekvence výsledků, haptika |
| 3 Kampaň a mapa | v3.2 | prosinec 2026 – leden 2027 | 50 levelů, FTUE |
| 4 Meta a obchod | v3.3 | únor 2027 | IAP naostro, sbírka |
| 5 Denní návyk | v3.4 | březen 2027 | Denní odměny, notifikace, cloud save |
| 6 Sociální a store | v3.5 | průběžně od v3.0 | Game Center, lokalizace, video |
| 7 Online | v4.0 | Q2 2027+ | jen podle dat |

---

## 6. Otevřená rozhodnutí

| # | Otázka | Doporučení |
|---|---|---|
| D1 | Boot: PLAY (sólo) hned, nebo dnešní auto-search? | PLAY primární, discovery na pozadí, JOIN jako toast. |
| D2 | Co se zamyká? | V LAN nic herního; v kampani tvary postupem, skiny za Scrap/IAP, zbraně postupně. |
| D3 | Životy/energie jako v Candy Crush? | Ne při startu. Retry okamžitý. Přehodnotit jen s D7/ARPDAU daty. |
| D4 | Reklamy? | Žádné interstitial. Rewarded video nejdřív po fázi 5 a jen podle dat. |
| D5 | Analytika: UGS vs Firebase? | UGS (stejný projekt pro IAP validaci, Cloud Save i Diagnostics; jeden SDK). |
| D6 | Online multiplayer? | Odložit; rozhodnout podle D30 a recenzí po v3.4. |
| D7 | Jazyky při startu? | cs + en úplně. Nic napůl. |
| D8 | Rozsah kampaně? | 50 levelů (5 × 10); 30 jako minimum, pokud dojde čas. |
| D9 | Cena skin bundlů | Business call vlastníka ($2,99/$3,99 jsou placeholdery). |

---

## 7. Co záměrně neděláme

- Login wall, povinný účet, sběr identity.
- Energie/životy, timery na odemykání, loot boxy s náhodou za peníze.
- Interstitial reklamy, reklamy uprostřed kola.
- Pay-to-win: statistiky, zbraně ani upgrady za peníze.
- Herní obsah dostupný jen přes LAN.
- Chat / free-form komunikace (drží rating 9+ a odpadá moderace).
- Portrétní režim (dogfight je landscape).

---

## 8. Rizika

- **App Review se dvěma zařízeními** — řeší fáze 1 (sólo). Nepodávat před ní.
- **Privacy label** se změní s analytikou (fáze 0) a znovu s reklamami (D4).
  Každá změna = update `docs/privacy.html` + dotazník v App Store Connect.
- **GameHUD monolit** — mapa, obchod a sbírka se do něj nesmí přidávat;
  před fází 3 vyčlenit sdílené UI utility (zásada 9).
- **`Time.timeScale` v LAN** — hit-stop jen sólo (zásada 8).
- **Boti a férovost** — AI, která míří lépe než člověk, zabíjí retenci;
  fairness ramp a chyba míření jsou povinné parametry tieru.
- **Nárazové tempo** — každá fáze musí být vydatelná samostatně; nic
  „napůl“ nesmí ležet v mainu (feature flag nebo větev).
- **Balance bez serveru** — remote config (UGS Remote Config) pro čísla
  botů a odměn, ať se ladí bez vydání.

---

## 9. Jak s dokumentem pracovat

1. Před fází: `Prompts/NN-CLAUDE-PLAN-<fáze>.md` s rozhodnutími, soubory,
   verifikací (styl Prompts/25). Zde změnit `[ ]` → `[plan]`.
2. Během: `[wip]`; hotové eventy/KPI průběžně kontrolovat v UGS.
3. Po vydání: `[done vX.Y]`, doplnit naměřené hodnoty do tabulky v §1 a
   aktualizovat CLAUDE.md (sekce Product Direction).
