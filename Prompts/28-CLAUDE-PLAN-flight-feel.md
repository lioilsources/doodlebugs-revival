# Letový pocit — dynamická odezva letadla (plán implementace)

Status (2026-11-18): **plán, neimplementováno.** Vlastník téhož dne rozhodl
D3 (postih ovladatelnosti nerušit, zjemnit) a D4 (POINT výchozí); ostatní
rozhodnutí v §2 mají výchozí hodnotu a dá se začít hned.
Implementuje Opus podle tohoto dokumentu. Sourozenecké plány ze stejného
zadání: `Prompts/29` (arzenál) a `Prompts/30` (hangár a vizuál).

Zadání vlastníka: dotykový gamepad „není slick“, letadlo zatáčí pomalu, chybí
dynamická odezva **letadla** — nejde o citlivost páčky.

ROADMAP: fáze 1 (ovládání `[wip]`, Prompts/26) a část fáze 2 (juice). Nic z
fáze 0 není potřeba.

## 0. Zjištění z kódu (stav `e6d025a`)

- **Zatáčení je 3–4× pomalejší, než vypadá z profilů.**
  `PlayerController.rotatePlane:777-819`: `ω = rotateSpeed × (|v| / defaultSpeed)
  × handling × |x|`. Platí profil Novice (`NoviceProfile.asset`: `rotateSpeed`
  50, `maxSpeed` 10; `defaultSpeed` 5, `minSpeed` 2). Tedy 50 °/s při cestovní
  rychlosti, 100 °/s na maximu, 20 °/s u pádové. Obrat o 180° trvá 3,6 s,
  looping 7,2 s. Poloměr je konstantní 5,73 u (`EngineOnTurnRadius:137`),
  looping má průměr 11,5 u z ~25 u viditelné výšky (54 u šířky na 19,5:9);
  letadlo měří 2,56 u.
- **Pilotní „maturity“ je mrtvý systém — všichni létají Novice navždy.**
  `ScoreManager.AddScore:162-166` volá `CheckMaturityUpgrade(stats.Kills)` s
  prahy 10/20 (`PlayerController.cs:172-187`), jenže `StartMatch:84` maže
  statistiky každé kolo a `MatchManager.KillTarget` = 3. Navíc je
  `_maturityLevel` obyčejné pole nastavené jen na serverové instanci, kdežto
  pohyb počítá vlastník. Profily Advanced (100 °/s) a Expert (200 °/s, max 20)
  se nikdy nepoužijí. `fireRateCooldown` v assetech chybí → násobič ROF je 1.
- **Rychlost zatáčení roste s rychlostí letu** (konstantní poloměr). Ubrat
  plyn zatáčku neutáhne, jen zpomalí otáčení; u pádové rychlosti letadlo
  skoro nereaguje. Plyn nemá pro manévr žádnou taktickou hodnotu.
- **Poškození ubírá ovladatelnost.** `PlaneStats.TakeDamage:174-175`: −0,1 za
  bod poškození až na 0,3. Po bombě nebo mině (3 dmg) zatáčí letadlo o 30 %
  pomaleji — prohrávající hráč navíc ztrácí kontrolu.
- **Žádná rotační dynamika, žádná zpětná vazba.** Úhlová rychlost se zapisuje
  přímo (`:806`), při `x == 0` je nula. Sprite se nenaklání (`:813-818` nastaví
  jen kurz), žádné stopy za křídly, žádný zvuk ani haptika manévru. Jediná
  „odezva“, kterou hráč vidí, je samotná (pomalá) rotace.
- **Motor vypnutý = 800 °/s** (`engineOffRotateMultiplier` 16 × 50, bez vlivu
  rychlosti) proti 50 °/s s motorem. Skok 16× mezi dvěma stavy téhož letadla.
- **Páčka:** dráha 90 canvas u ≈ 5 mm na iPhonu 12 mini, mrtvá zóna 0,12, expo
  1,3 (`TouchControls.cs:29-33`). Řídí **rychlost otáčení** (nahoru = proti
  směru hodin, `OnStickAxes:248-260`), takže „nos nahoru“ je nahoře jen při
  letu doprava; při letu doleva se význam obrací. Plyn sdílí tentýž palec (osa
  X, `throttleRate` 5 u/s², `movePlane:742-745`) se stejnou mrtvou zónou, takže
  každý šikmý pohyb při zatáčení mění rychlost.
- **Latence není problém.** uGUI drag → provider → `FixedUpdate` 50 Hz →
  kinematické tělo s interpolací ≈ 40–60 ms. Tady nic neladit.
- **Bot létá stejným modelem** přes `IInputProvider` a výběry z piké plánuje
  podle `EngineOnTurnRadius` (`BotBrain.cs:160-178`); má vlastní konstanty
  rychlostí (`CruiseSpeed` 7, `LoopMinSpeed` 7, `StallGuardSpeed` 4).
- **Pohyb je autoritou vlastníka** (`ClientNetworkTransform`, `netSpeed` aj.
  owner-write). Ladicí konstanty proto musí být v buildu stejné pro všechny —
  nikdy uživatelské nastavení v LAN.

## 1. Cíle (měřitelné)

| Veličina | Dnes | Cíl (preset ARCADE) |
|---|---|---|
| Obrat o 180° z cestovní rychlosti | 3,6 s | ≤ 1,0 s |
| Looping (čas / průměr) | 7,2 s / 11,5 u | ≤ 2,0 s / ≤ 4,5 u |
| Plná výchylka → viditelná reakce | jen rotace | náklon spritu do 1 snímku, stopy od křídel |
| Plyn jako rozhodnutí | žádný vliv na poloměr | pomalu = těsně (r 1,6 u), rychle = široce (r 5,7 u) |
| Jemné míření | plná páčka = 50 °/s | půl páčky ≈ 50 °/s (stejná přesnost jako dnes) |
| Pády motoru za minutu | neměřeno | ne víc než dnes (`stall` event) |

## 2. Rozhodnutí

| # | Otázka | Výchozí (doporučení) | Kdo |
|---|---|---|---|
| D1 | Výchozí preset letu | **ARCADE** (§3). CLASSIC+ a ACE zůstávají v kódu pro Flight Lab. | vlastník, po 10 min ve Flight Labu |
| D2 | Co s maturity | **Vypnout z letu i střelby.** `FlightTuning` je jediný zdroj čísel; třídy a scéna zůstávají (žádná změna scény), jen se nečtou. | doporučeno |
| D3 | Postih ovladatelnosti za poškození | **Nerušit, zjemnit.** Počítá se jen poškození trupu (zásah do štítu nic neubere): −5 % za bod, nejhůř 85 % (dnes −10 % za každý bod včetně štítu, nejhůř 30 %). Pickup Repair zůstává a vrací 100 %. V ARCADE tak nejhorší případ dělá 178 °/s. | **rozhodnuto** (vlastník, 2026-11-18); čísla jsou návrh k doladění |
| D4 | Nové schéma POINT (páčka ukazuje směr letu) | **Přidat jako třetí schéma a nastavit jako výchozí pro nové instalace.** Uložená volba se nemění. RATE (dnešní) i GYRO zůstávají. | **rozhodnuto: ano** (vlastník, 2026-11-18) — mění D1 z Prompts/26 |
| D5 | Snap (krátké přetažení otáčení při švihu páčkou) | **Zapnuto** v ARCADE: ×1,5 na 0,18 s, cooldown 0,8 s. | po testu |
| D6 | Náklon a automatické srovnání spritu „koly nahoru“ | **Ano**, čistě vizuální na všech klientech. | doporučeno |
| D7 | Flight Lab v release buildu | **Ano, skrytý** (5× tap na titulek SETTINGS); přepisy platí jen ve fázi `WaitingForPlayers` (warm-up), v bitvě se vždy použije zkompilovaný preset. | doporučeno |

## 3. Letový model v2

Jediný zdroj čísel: `Scripts/Flight/FlightTuning.cs` (statická třída, tři
presety, `Active`). Matematika jako čisté statické funkce v
`Scripts/Flight/FlightModel.cs` (bez `MonoBehaviour`), aby šla ověřit mimo Unity.

| Parametr | Dnes (Novice) | CLASSIC+ | **ARCADE** | ACE |
|---|---|---|---|---|
| `MinSpeed` (pád) | 2 | 2,5 | 3 | 3,5 |
| `CornerSpeed` | – | 5 | 6 | 7 |
| `CruiseSpeed` (spawn, respawn) | 5 | 6 | 7,5 | 9 |
| `MaxSpeed` | 10 | 10 | 13 | 16 |
| `BoostSpeed` (plán 29, nitro) | – | 13 | 17 | 20 |
| `TurnRate` špička, °/s | 50 při v=5 | 150 | 210 | 260 |
| `EnvAtMin` / `EnvAtMax` | ∝ v | 0,75 / 0,70 | 0,70 / 0,62 | 0,65 / 0,55 |
| `TurnAttack` / `TurnRelease`, s | 0 / 0 | 0,05 / 0,04 | 0,07 / 0,05 | 0,08 / 0,05 |
| `SnapBoost` / `SnapSeconds` / `SnapCooldown` | – | vyp. | 1,5 / 0,18 / 0,8 | 1,6 / 0,20 / 0,6 |
| `TurnDrag`, 1/s | – | 0,5 | 0,9 | 1,1 |
| `ThrottleRate`, u/s² | 5 | 7 | 9 | 11 |
| `ClimbDrag` / `DiveBoost` | 1 / 3 | 1 / 3 | 1,4 / 3,5 | 1,6 / 4 |
| `StallTurnRate`, °/s | 800 | 320 | 320 | 320 |
| gravitace bez motoru, okno restartu | Novice | beze změny | beze změny | beze změny |

**Obálka zatáčení** — nejvyšší rychlost otáčení je na `CornerSpeed`, k oběma
krajům klesá:

```
Envelope(v) = v <= Corner ? Lerp(EnvAtMin, 1, InverseLerp(Min, Corner, v))
                          : Lerp(1, EnvAtMax, InverseLerp(Corner, Max, v))
targetOmega = x × TurnRate × Envelope(v) × snapMul          // °/s, x ∈ −1..1
tau         = |targetOmega| > |omega| ? TurnAttack : TurnRelease
omega       = Lerp(omega, targetOmega, 1 − exp(−dt / tau))
rb.angularVelocity = −omega                                  // +x = po směru hodin, jako dnes
```

Co z toho vyjde pro ARCADE (kontrolní tabulka pro `FlightModel.Describe()`):

| v, u/s | ω, °/s | poloměr, u | 180° | looping |
|---|---|---|---|---|
| 3 (u pádu) | 147 | 1,17 | 1,22 s | 2,45 s |
| 6 (corner) | 210 | 1,64 | 0,86 s | 1,71 s |
| 7,5 (cruise) | 193 | 2,23 | 0,93 s | 1,87 s |
| 13 (max) | 130 | 5,72 | 1,38 s | 2,76 s |

**Snap.** Když `|x|` dosáhne 0,85 do 0,10 s od neutrálu (`|x| < 0,2`) nebo od
opačné výchylky (`|x| ≥ 0,5` s opačným znaménkem) a cooldown uplynul:
`snapMul = SnapBoost` na `SnapSeconds`, pak 1. Vyvolá `OnSnapTurn` (efekty §5).
Bot ho nespustí — jeho vstup má rampu 2/s (`BotBrain.InputRampPerSecond`).

**Energie.** Tvrdá zatáčka nad `CornerSpeed` ubírá rychlost, nikdy pod ni —
zatáčka se při tahu sama utahuje a žádný nový způsob, jak spadnout, nevzniká:

```
k      = Clamp01(|omega| / (TurnRate × Envelope(speed)))
over   = speed − CornerSpeed
if (over > 0) speed −= TurnDrag × over × k² × dt
speed += throttle × ThrottleRate × dt           // stoupání/klesání beze změny tvaru
speed  = Clamp(speed, MinSpeed, MaxSpeed × planeStats.SpeedMultiplier)
```

**Motor vypnutý:** `omega_target = x × StallTurnRate` (bez obálky, bez snapu),
gravitace a okno restartu zůstávají na hodnotách Novice jako konstanty
`FlightTuning`. Mechanika pádu a restartu se nemění.

**Ovladatelnost (D3):** `planeStats.Handling` dál násobí rychlost otáčení
(`targetOmega × Handling`, s motorem i bez), jen v rozsahu 0,85–1,0. Na snap,
obálku ani rychlost letu nesahá.

## 4. Ovládání na dotyku

Tři schémata v `ControlScheme` (hodnoty se ukládají — jen přidávat):
`Joystick = 0` (v UI nově **RATE**), `Gyro = 1`, `Pointer = 2` (**POINT**).

**POINT (nové, D4).** Páčka neudává rychlost otáčení, ale **kam má letadlo
letět** v souřadnicích obrazovky. Letadlo se tam stočí kratší cestou na hraně
svých možností. Švih palcem na opačnou stranu = obrat naplno; nic se neobrací
podle směru letu; looping = palec opíše kruh.

```
// TouchInputProvider: s = surový vektor páčky (−1..1 po osách), m = Clamp01(|s|)
m < PointDeadZone (0,22)  → žádný záměr: x = 0, cílová rychlost = CruiseSpeed
headingDeg = Atan2(s.y, s.x);  push = InverseLerp(0,22, 1, m)

// PlayerController.HandleMovement
delta = DeltaAngle(HeadingDegrees, headingDeg)               // + = proti směru hodin
if (|delta| > 165) delta = |delta| × znaménko poslední zatáčky   // bez kmitání na 180°;
                                                             // z klidu oblouk, který vede vzhůru
x = −Clamp(delta / PointGainDeg (22), −1, 1)

// „chytrý plyn“: do zatáčky letadlo samo přibrzdí na corner speed, na rovince zrychlí
straight = Lerp(CruiseSpeed, MaxSpeed, push)
target   = Lerp(straight, CornerSpeed, InverseLerp(20, 70, |delta|))
throttle = Clamp((target − speed) / 1,5, −1, 1)              // do stejného integrátoru jako RATE
```

Šev: `IInputProvider` dostane výchozí metodu
`bool TryGetHeadingIntent(out float headingDeg, out float push) => false;`
(stejný vzor jako `GetShootInput(int slot)`); přepíše ji jen
`TouchInputProvider` ve schématu Pointer. Klávesnice, gamepad, gyro i bot
zůstávají na osách.

**RATE (dnešní páčka)** — jen přeladit na trojnásobnou rychlost otáčení:
expo zatáčení 1,3 → **1,8** (půl páčky = 23 % = ~49 °/s, tedy dnešní plná
rychlost), dráha 90 → **110 u**, mrtvá zóna zatáčení 0,12 → 0,10, mrtvá zóna
plynu **0,28** (zatáčení už nepřelévá do rychlosti).

**Nastavení:** `SettingsOverlay` — tři volby POINT / RATE / GYRO, nápověda
jednou větou ke každé. Telemetrie `input_scheme_set` už existuje.

## 5. Zpětná vazba (čtyři kanály)

| Událost | Vizuál | Zvuk | Haptika | UI |
|---|---|---|---|---|
| Zatáčka | náklon spritu: `scale.y = Lerp(1, 0,55, |ω|/220)`, vyhlazení 0,08 s | – | – | – |
| Tvrdá zatáčka (`|ω|` > 70 % max) | dvě stopy od konců křídel (`EffectAssets.CreateTrailSystem`, world space) | – | – | – |
| Snap | záblesk stop + krátký squash | `sfx_whoosh` | Light | – |
| Let „koly nahoru“ > 0,25 s bez tvrdé zatáčky | půlvýkrut 0,22 s: `scale.y` přes nulu, v půlce přepnout `flipY` | tichý `sfx_whoosh` | – | – |
| Blíží se pád (`speed < MinSpeed + 1` při stoupání) | chvění spritu ±2° | tik à 0,25 s | Light | blikání segmentů rychlosti |
| Motor zhasl | obláček, vrtule stojí | `sfx_stall` | Medium | segmenty rychlosti rudé |
| Motor naskočil | výfuk dozadu | `sfx_relight` | Light | – |

- Vše běží v `PlaneVisualEffects.LateUpdate` na **každém** klientovi z toho, co
  už se synchronizuje: kurz z transformu (ω = `DeltaAngle` mezi snímky,
  vyhlazená), `netEngineOff`, `netSpeed`. Žádná nová síťová proměnná.
- Výkrut přes `SpriteRenderer.flipY` a kladné měřítko — ne záporný scale
  (shader `Custom/ColorReplace` nemusí mít vypnutý culling). Stejně i
  `GlowOutline` (jeho základ je 1,1). `FirePoint` leží na ose, střelba se
  nemění. `WreckEffect.Spawn` převezme `flipY` i měřítko.
- `movePlane:718` čte `plane.transform.rotation.z` pro okno restartu — rotace
  vizuálního dítěte se proto **nemění**, jen jeho `localScale` a `flipY`.
- `SfxManager.Haptic(HapticKind kind)`: nové API; do nativní haptiky z fáze 2
  je Light/Medium no-op a Heavy = dnešní `Handheld.Vibrate()`. Volání zůstanou.
- Nové WAVy procedurálně ve stylu ostatních, mono 44,1 kHz 16 bit. Skript na
  ně v repu není (`tools/audio` má jen hudbu) — přidat
  `tools/audio/generate_sfx.py` (čistý Python); sdílí ho plán 29.
- Smyčka motoru se nevrací (odstraněna záměrně, `48929c8`).

## 6. Flight Lab (ladění na zařízení)

Důvod: pocit se nedá rozhodnout na papíře a build jde na telefon jen s tagem
(bez kabelu). `Scripts/UI/FlightLabOverlay.cs`, vlastní třída nad `UiKit`:

- Otevření: 5× tap na titulek SETTINGS. Tlačítka presetů CLASSIC+ / ARCADE /
  ACE; ± krokování deseti parametrů z §3 a čtyř z §4; RESET; **COPY** — JSON
  aktuálních hodnot do schránky (`GUIUtility.systemCopyBuffer`), aby je vlastník
  poslal zpět a staly se novým presetem.
- Přepisy žijí v `PlayerPrefs` (`flightlab.*`) a `FlightTuning.Active` je
  použije **jen když `MatchManager` hlásí `WaitingForPlayers`** (D7). Při
  přechodu do bitvy se vrací zkompilovaný preset a HUD to oznámí jedním řádkem.
- `FlightModel.Describe()` při startu vypíše do logu tabulku z §3 pro aktivní
  preset (kontrola čísel v `Player.log` i bez editoru).

## 7. Soubory a švy

Nové (commitnout i `.meta`): `Scripts/Flight/FlightTuning.cs`,
`Scripts/Flight/FlightModel.cs`, `Scripts/UI/FlightLabOverlay.cs`,
`Resources/Sfx/sfx_{whoosh,stall,relight}.wav`.

| Soubor:řádek | Změna |
|---|---|
| `PlayerController.cs:16-39` | pole `base*` a vlastnosti `Profile…` pryč; čte se `FlightTuning.Active`. `maxSpeed` dál násobí `planeStats.SpeedMultiplier`. |
| `PlayerController.cs:129-138` | `EngineRestartHeadingWindow` z konstant `FlightTuning`; `EngineOnTurnRadius` → `TurnRadiusAt(float speed)` včetně `Handling` (starou vlastnost ponechat jako `TurnRadiusAt(Speed)`). |
| `PlayerController.cs:156-187` | `CheckMaturityUpgrade`, `OnMaturityChanged`, `MaturityLevel` označit `[Obsolete]`, tělo prázdné (D2). |
| `PlayerController.cs:687-705` | `HandleMovement`: nejdřív `provider.TryGetHeadingIntent`, jinak osy; výpočet `x` a `throttle` podle §4. |
| `PlayerController.cs:707-758` | `movePlane`: konstanty z `FlightTuning`, `TurnDrag`, clamp; `defaultSpeed` → `CruiseSpeed` (i `:343, :367, :980`). |
| `PlayerController.cs:777-819` | `rotatePlane`: obálka, attack/release, snap, `StallTurnRate`; `leftPoint/rightPoint` se už nečtou (pole nechat — jsou v prefabu). Událost `OnSnapTurn`. |
| `PlaneStats.cs:15-21, 162-175` | D3: `MinHandling` 0,3 → **0,85**, `HandlingLossPerDamage` 0,1 → **0,05** a násobí se jen tím, co prošlo do trupu (`remaining` po odečtení štítu, ne `damage`). `ApplyPowerUp(Repair)` beze změny (+0,3 → strop 1,0). HUD chip s procenty zůstává. |
| `Shooting.cs:41-56` | `maturityRofMultiplier`, `maturityForceMultiplier` → 1 (D2). |
| `ScoreManager.cs:161-166` | volání `CheckMaturityUpgrade` pryč. |
| `IInputProvider.cs` | `TryGetHeadingIntent` jako výchozí metoda. |
| `TouchInputProvider.cs` | drží surový vektor páčky; schéma Pointer → `TryGetHeadingIntent`; RATE → dnešní osy. |
| `TouchControls.cs:29-33, 248-260` | nové konstanty RATE; do provideru jde surový vektor i křivkované osy; mrtvá zóna po osách. |
| `Meta/ControlSettings.cs` | `Pointer = 2`; výchozí pro neuložené = Pointer (D4). |
| `UI/SettingsOverlay.cs` | tři volby schématu, skrytý vstup do Flight Labu. |
| `PlaneVisualEffects.cs` | `LateUpdate`: náklon, výkrut, stopy, chvění; odběr `netEngineOff` přes nový `PlayerController.OnEngineStateChanged`; větve `HandleLevelChange`/Expert sparkle pryč. |
| `Effects/WreckEffect.cs` | převzít `flipY` a `localScale` spritu. |
| `Bot/BotBrain.cs:16-46, 160` | `r = _player.TurnRadiusAt(v)`; konstanty rychlostí odvodit z `FlightTuning` (`CruiseSpeed`, `LoopMinSpeed` = cruise, `StallGuardSpeed` = Min + 1,5, `RecoverMinSpeed` = Min + 1). |
| `Audio/SfxManager.cs` | `HapticKind`, `PlayWhoosh/Stall/Relight`. |
| `GameSetup.cs:117-121` | `FlightLabOverlay.Create(canvas)`. |
| `CLAUDE.md`, `ROADMAP.md` | sekce Input/Flight; fáze 1 odkaz na tento plán. |

Beze změny scény, prefabu i `NetworkPrefabsList`. `PilotMaturityManager` a tři
assety ve scéně zůstávají (jinak chybějící skript ve `Scene01`); jen se nečtou.

## 8. Měření (ROADMAP §3.7)

`flight_preset {name, lab_override}` při startu; `stall {cause: climb|space|speed}`;
`relight {seconds}`; `snap_turn`; `round_end` (fáze 0) dostane `scheme`.
KPI: pády motoru za minutu podle schématu, killy za minutu podle schématu,
čas k prvnímu killu ve warm-upu, podíl relací na každém schématu.

## 9. Ověření

1. Kompilace přes Roslyn `csc` proti Unity DLL (postup z Prompts/27; editor
   tady headless neběží — **import prefabu a scény se neověřuje**, proto se
   jich plán nedotýká).
2. `FlightModel.Describe()` v logu = tabulka z §3 na dvě platné číslice.
3. Zařízení (TestFlight), warm-up proti botovi: obrat 180° do 1 s; looping se
   vejde nad terén na Volcano (nejvyšší terén); půl páčky v RATE míří stejně
   jemně jako dřív; šikmý palec v RATE nemění rychlost.
4. POINT: švih na opačnou stranu = obrat se snapem a stopami; palec na středu =
   rovný let cestovní rychlostí; palec dolů s vypnutým motorem = nos do okna
   restartu a motor naskočí.
5. Vizuál na druhém zařízení (klient): náklon a výkrut cizího letadla běží
   plynule (30 Hz tick, interpolace) a střely vylétají z osy.
6. Bot: neskončí v zemi častěji než před změnou (20 min warm-upu, počet
   `KillCause.Ground` u bota) a nikdy nespustí snap.
7. D3: tři zásahy MG do plného štítu ovladatelnost nezmění; bomba do letadla
   bez štítu (3 body do trupu) ji srazí na 85 % a níž už nejde; Repair vrátí
   100 % a chip v HUD to ukáže.
8. Flight Lab: změna v lobby se projeví hned; po příchodu druhého hráče platí
   preset; COPY vloží JSON do schránky.
9. Desktop a gamepad: klávesnice dostane snap při každém stisku z neutrálu
   (cooldown 0,8 s); gamepad se chová jako RATE.

## 10. Rizika

- **Příliš ostré pro nováčka.** Proto presety a Flight Lab; půl páčky drží
  dnešní rychlost. Kdyby ARCADE bylo moc, CLASSIC+ je o 30 % mírnější.
- **POINT a přesné míření.** Úhlová přesnost palce je ±5°; proporcionální
  pásmo 22° to tlumí. Kdo chce přesnost, má RATE. Rozhoduje telemetrie killů
  podle schématu.
- **Vzdálená letadla při 200 °/s.** 30 Hz tick = 7° na tick, interpolace to
  vyhladí; kdyby cukala, zvednout `TickRate` až po měření, ne předem.
- **Zásahy.** Server testuje kolize proti poloze od vlastníka; rychlejší
  manévry zvětšují chybu o zlomek jednotky na LAN. Sledovat, neřešit předem.
- **Sólo vs LAN ladění.** Flight Lab nesmí prosáknout do bitvy (D7) — ověřit
  krokem 8.
- **Mrtvý kód maturity** zůstává ve scéně; smazat ho jde až s editorem.

## 11. Rozsah a pořadí

Dva PR, každý vydatelný sám:

1. **Model + Lab** (≈ 2 dny): `FlightTuning`, `FlightModel`, přepis
   `PlayerController`, D2 + D3, bot, Flight Lab, přeladěná RATE páčka.
   Samo o sobě řeší „pomalu zatáčí“.
2. **POINT + zpětná vazba** (≈ 2 dny): `TryGetHeadingIntent`, schéma v
   nastavení, náklon/výkrut/stopy, zvuky, `HapticKind`, eventy.

Mimo rozsah: nitro a ostatní gadgety (plán 29), kamera a shake (fáze 2),
nativní haptika (fáze 2), bojový bot (fáze 1).
