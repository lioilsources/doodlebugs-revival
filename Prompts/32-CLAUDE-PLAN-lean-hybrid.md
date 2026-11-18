# LEAN — hybridní ovládání: páka řídí, pohyb telefonu přidává (plán implementace)

Status (2026-11-18): **plán, neimplementováno.** Vlastník téhož dne potvrdil
D3 (bonus 15 %), D5 (zapnuto od začátku) a D7 (vypínač hostitele až ve vlně
2). Ostatní rozhodnutí v §2 mají výchozí hodnotu, Opus může začít hned. Čísla v §3 pocházejí ze simulace
`tools/flight/lean_sim.py`, na zařízení ověřená nejsou.

Zadání vlastníka: ke gyru a joysticku přidat třetí, hybridní režim. Hlavní
je joystick; naklánění telefonu pomáhá o kousek za hranici toho, co joystick
dokáže sám. Hráč tím získá výhodu, ale musí se hýbat a dělat s telefonem
legrační tanečky. Jak by to fungovalo, jaká jsou rizika, co není domyšlené.

Navazuje na `Prompts/28` (letový model v2, schémata POINT a RATE) a
`Prompts/26` (dotykové ovládání). Základem je větev
`feat/flight-arsenal-hangar` (PR #64).

ROADMAP: fáze 1 (ovládání), fáze 2 (juice). Zásady §3: solo-first, čtyři
kanály, měřitelnost.

## 0. Zjištění z kódu

- **Zatáčení má jedno místo.** `PlayerController.rotatePlane(x)` (`:834`)
  počítá `target = x * FlightModel.TurnRateAt(P, speed, handling) * snapMul`
  a `_omega` k němu dojíždí (`StepOmega`, náběh 0,07 s). Bonus je jeden další
  násobitel na tomhle řádku.
- **Strop zatáčení dnes:** ARCADE 210 °/s při rohové rychlosti 6, poloměr
  1,64 j., půlobrat 0,86 s. Snap ho na 0,18 s zvedne 1,5×.
- **Pohyb je autoritativní u vlastníka** (`ClientNetworkTransform`). Bonus se
  počítá na zařízení hráče a po síti se nic nového posílat nemusí. Proto musí
  být čísla bonusu stejná na všech zařízeních: patří do `FlightPreset`.
- **Senzor už ve hře je, ale jiný.** Schéma GYRO čte `GravitySensor`
  (`MobileInputProvider`), tedy polohu vůči gravitaci, s kalibrací neutrálu.
  Běží jen ve schématu GYRO (`TouchInputProvider.SetScheme`).
- **GYRO řídí stejným pohybem**, jaký by měl LEAN: otáčení telefonu jako
  volantem (`gravity.x`). LEAN proto dává smysl jen nad pákou.
- **Orientace je zamčená** na LandscapeLeft (`defaultScreenOrientation: 3`,
  jediné povolené otočení). Obraz se při tanci nepřeklopí.
- `InputSystem.inputsettings.asset`: `m_CompensateForScreenOrientation: 1`,
  osy senzorů jsou tedy v souřadnicích obrazovky a osa z míří z displeje.
- `PlaneFlightVisuals` si úhlovou rychlost odvozuje ze změny kurzu i u cizích
  letadel (`LateUpdate`, `:146-150`). Efekt „točí víc, než obálka dovolí“ jde
  tedy ukázat všem bez netcode.
- Haptika Light a Medium je zatím prázdná (`SfxManager.Haptic`, `:195-198`).

## 1. Jak to funguje

**Páka řídí všechno. Pohyb telefonu nic neřídí, jen přidává.**

1. Hráč zatáčí pákou jako dnes (POINT nebo RATE).
2. Když přitom telefon **pootočí stejným směrem, jakým se točí letadlo na
   obrazovce** (jako volantem, kolem osy trčící z displeje), naplní se
   ukazatel LEAN.
3. Plný ukazatel přidá **+15 % rychlosti zatáčení** nad obálku. Jen ve
   směru, kam už zatáčí páka, a úměrně jejímu vychýlení. Bez páky se neděje
   nic.
4. Ukazatel **sám vytéká**: zhruba za 3 s je prázdný, i když hráč telefon
   drží nakloněný. Odměňuje se pohyb, ne poloha.

Co z toho plyne pro hraní (simulace v §3):

- **Jeden švih o 30° unese celý půlobrat.** To je základní tah: páka + švih.
- **Nůžky jsou tanec.** Každá změna směru chce švih na druhou stranu, takže
  hráč v souboji kývá telefonem zleva doprava.
- **Držet telefon nakloněný nepomůže.** Kdo si najde pohodlnou šikmou
  polohu, nedostane nic.
- **Dlouhá zatáčka jedním směrem** bonus udrží jen tomu, kdo telefon dál
  pomalu přetáčí (12 °/s), tedy dokud mu stačí zápěstí. Za 8 s je telefon na
  výšku.
- Otáčení na kancelářské židli s telefonem naplocho se počítá také. Je to
  omezené stropem a je to přesně ten druh legrace, o který jde.

Pohyb obrazovky navíc sčítá dojem: letadlo se točí na displeji a displej se
točí v ruce stejným směrem, takže hráč vidí, že „pomáhá“.

**Proč pohyb a ne poloha.** Poloha vůči gravitaci (jako u GYRO) potřebuje
kalibraci neutrálu, selže vleže a s telefonem naplocho a hráč si v ní najde
statickou pózu. Rychlost otáčení z gyroskopu s vytékáním nepotřebuje
kalibraci, funguje v každé poloze, snese drift senzoru a k pohybu nutí sama
od sebe.

## 2. Rozhodnutí

| # | Otázka | Výchozí (doporučeno) | Vlastník |
|---|---|---|---|
| D1 | Co náklon přidává | Jen rychlost zatáčení nad obálku, jen ve směru páky. Sám neřídí, plyn neovlivňuje. | Olin |
| D2 | Co se měří | **Pohyb** (rychlost otáčení, vytéká za ~3 s). Varianta „poloha“ je popsaná v §1 a zamítnutá. | Olin |
| D3 | Velikost | **+15 %** v ARCADE (CLASSIC+ 10 %, ACE 15 %). Se snapem dohromady nejvýš 1,8×. | Olin, **rozhodnuto 18. 11.** |
| D4 | Jak se zapíná | Přepínač **LEAN** pod řádkem schémat, pro POINT a RATE. GYRO ho nemá. Žádné čtvrté schéma. | Olin |
| D5 | Výchozí stav | **Zapnuto**, kde je gyroskop. Bonus jen přidává, nikomu neuškodí, a hráč ho objeví sám. | Olin, **rozhodnuto 18. 11.** |
| D6 | Gamepad, klávesnice, boti | Bonus nemají. Na PC by „náklon“ bylo jen držené tlačítko. Boti bez něj zůstanou stejně těžcí. | Olin |
| D7 | Vypnutí pro celou partii | Host může LEAN zakázat všem. Přijde ve vlně 2; do té doby ho vypíná každý jen u sebe. | Olin, **rozhodnuto 18. 11.** |
| D8 | Cena v letu | Žádná. Ztráta rychlosti v zatáčce se nemění. Cenou je pohyb a šikmá obrazovka. | Olin |
| D9 | Kde žijí čísla | `FlightPreset` (pravidlo hry). Flight Lab je mění jen v lobby, jako zbytek presetu. | Olin |
| D10 | „Přesný švih“ zesílí snap | vlna 2 | Olin |
| D11 | Ocenění BEST DANCER po kole | vlna 2 | Olin |
| D12 | Jemné míření náklonem | Ne teď. Je to jiný směr (přesnost místo síly), viz §7. | Olin |

## 3. Čísla

`python3 tools/flight/lean_sim.py` (τ 2,0 s, mrtvá zóna 6°, plný náklon 24°,
strop 36°, bonus 15 %):

| Pohyb ruky | Průměrný náklon | Co to znamená |
|---|---|---|
| jeden švih 30° do půlobratu | 0,88 | základní tah funguje |
| jeden švih 40° | 0,92 | víc už moc nepřidá |
| kývání ±30° s každou změnou směru | 0,65 | odměňovaný styl |
| telefon držený šikmo, zatáčka o 3 s později | 0,00 | póza nedává nic |
| „pumpování“ v jedné dlouhé zatáčce | 0,12 | malá výhoda, ne díra |
| plynulé přetáčení 20 °/s | 0,95 | jde, dokud stačí zápěstí |
| drift senzoru 1 °/s, chvění při chůzi | 0,00 | šum nedává nic |

Plný náklon v ARCADE: 210 → 241 °/s, poloměr 1,64 → 1,42 j., půlobrat
0,86 → 0,75 s. Pilot s plným náklonem získá na stejně dobrého bez něj 180°
za 5,7 s. V kole do tří sestřelů je to znát, ale nerozhoduje to samo.

`--check` hlídá šest pravidel návrhu (švih unese půlobrat, póza a šum nedají
nic, pumpování zůstane malé). Každé přeladění jím musí projít.

## 4. Architektura a švy

Tok: `Gyroscope` → `LeanSensor` (zařízení) → `IInputProvider.GetLean()` →
`PlayerController.rotatePlane` → násobitel z `LeanModel`.

1. **`Flight/LeanModel.cs`** (nový), čisté funkce jako `FlightModel`, 1 : 1
   se simulátorem: `Step(swing, rateDeg, dt, p)`, `Lean01(swing, p)`,
   `Multiplier(p, lean, x)`.
2. **`FlightPreset`** (`Flight/FlightTuning.cs`): nová pole `LeanBonus`,
   `LeanTau`, `LeanDeadDeg`, `LeanFullDeg`, `LeanCapDeg`. Hodnoty: CLASSIC+
   0,10 / ARCADE 0,15 / ACE 0,15; τ 2,0; 6; 24; 36. `FlightModel.Describe`
   přidá řádek s rychlostí zatáčení při plném náklonu.
3. **`Input/LeanSensor.cs`** (nový), obyčejná třída jako
   `MobileInputProvider`:
   - zdroj `UnityEngine.InputSystem.Gyroscope.current`, osa z, převod na
     °/s se znaménkem „+ = po směru hodin z pohledu hráče“ (shodně s `_omega`);
   - záloha bez gyroskopu: úhel `atan2(gravity.x, -gravity.y)` z
     `GravitySensor`, derivovaný v čase; platí jen, když telefon neleží
     naplocho (`|gravity.z| < 0,85`), jinak 0;
   - bez obou senzorů `Available == false`;
   - `SetEnabled`, `Update(dt)`, `Reset()`, `Swing`, `Lean` (−1..1).
4. **`IInputProvider`**: nová výchozí metoda `float GetLean() => 0f;`.
   Gamepad, klávesnice a `BotBrain` ji nepřepisují.
5. **`TouchInputProvider`**: vlastní `LeanSensor`. `GetLean()` vrací hodnotu,
   když je `ControlSettings.Lean` a schéma není Gyro. `UpdateInput()` senzor
   krokuje, `ReleaseAll()` a `SetScheme()` ho nulují, senzor běží jen se
   zapnutým LEAN.
6. **`Meta/ControlSettings.cs`**: `Lean` (bool), klíč `settings.lean`,
   událost `LeanChanged`, `Telemetry.Log("input_lean_set", …)`.
7. **`PlayerController`**: `HandleMovement` přečte `GetLean()` a předá ho
   `rotatePlane(x, lean)`. V `rotatePlane`, jen při běžícím motoru:
   `target = x * TurnRateAt(...) * Mathf.Min(snapMul * leanMul, 1.8f)`.
   Nová vlastnost `Lean01` pro HUD. `TurnBleed` se nemění (`Clamp01` v něm
   zůstává, viz D8).
8. **`UI/TouchControls.cs`**: oblouk LEAN na prstenci páky (`BuildStick`,
   `:207`), `Image` typu Radial360 stejně jako prstenec spouště. Plní se
   `|lean|`, při plném náklonu změní barvu na `UiTheme.Highlight`. Vidět je
   jen se zapnutým LEAN.
9. **`UI/SettingsOverlay.cs`**: pod řádek schémat (`:88-104`) přepínač LEAN
   ON / OFF s jednou větou nápovědy. U GYRO a bez senzoru je šedý s důvodem.
10. **`Effects/PlaneFlightVisuals.cs`**: když letadlo točí déle než 0,25 s
    nad `TurnRateAt × 1,04`, stopy od konců křídel zesvětlají a prodlouží se.
    Počítá se z pozorovaného kurzu, takže to vidí i soupeři.
11. **`UI/FlightLabOverlay.cs`**: pole `LeanBonus`, `LeanTau`, `LeanFullDeg`
    do seznamu (`:19-21`) a živý řádek „gyro z / swing / lean“ pro kontrolu
    znaménka na zařízení.
12. **Zvuk:** `lean_full` (krátké cvaknutí při dosažení plného náklonu) přes
    `tools/audio/generate_sfx.py`.

`GameHUD.cs` se nemění. Žádná nová síťová proměnná ve vlně 1.

## 5. Zpětná vazba a naučení

| Kanál | Co |
|---|---|
| obraz | oblouk LEAN u páky; světlejší a delší stopy za křídly, viditelné i soupeřům |
| zvuk | `lean_full` při naplnění |
| haptika | `HapticKind.Light` při naplnění (dnes prázdná, ozve se, až bude nativní plugin) |
| text | poprvé v životě instalace banner `LEAN! +15 % TURN`, když hráč náklon poprvé naplní |

Žádný tutoriál. Hráč, který se při zatáčení instinktivně nakloní, si bonus
objeví sám a banner mu řekne, co se stalo. Nápověda v nastavení: „Otoč
telefon stejným směrem, jakým se točí letadlo.“

## 6. Rizika

| Riziko | Dopad | Ošetření |
|---|---|---|
| Kdo se hýbat nemůže nebo nechce (autobus, postel, zdraví, tablet) | v PvP o 15 % horší zatáčka | bonus je malý a zastropovaný; host ho umí vypnout (D7); sólo postup na něm nikdy nesmí záviset |
| Smíšená partie telefon + gamepad | telefon má výhodu, kterou pad nemá | D6 je vědomé rozhodnutí; padu zůstává přesnější páka |
| Soupeř nechápe, proč ho někdo přetočil | působí to jako lag nebo podvod | efekt na letadle viditelný všem (§4 bod 10) |
| Pád telefonu, bolest zápěstí, rána sousedovi na gauči | úraz, rozbitý telefon | plný náklon už při 24°, žádné třesení ani prudká gesta, strop 36° nic dalšího nepřidá |
| Šikmá obrazovka zhoršuje míření | horší střelba při náklonu | to je zamýšlená cena; hlídat, že nepřeváží zábavu |
| Palec při pohybu sjede z páky | ztráta řízení | páka je plovoucí a drží se dotyku; ověřit na zařízení |
| Různé senzory, chybějící gyroskop, opačné znaménko | bonus nefunguje nebo jde obráceně | záloha přes gravitaci, `Available`, živý řádek ve Flight Labu, test na iOS i Androidu |
| Falešný náklon v autě nebo za chůze | náhodný bonus | jen přidává a jen ve směru páky; šum v simulaci dává 0 |
| Zneužití | přetáčení telefonu, židle, držák | strop 15 %; pumpování dává 0,12; v LAN mezi kamarády přijatelné |
| POINT se s vyšší rychlostí rozkmitá | letadlo přestřeluje kurz | zisk je `PointGainDeg`; ověřit, případně přidat do Flight Labu |
| Čísla se na zařízeních liší | každý letí podle jiných pravidel | pole v `FlightPreset`, Lab jen v lobby |
| 15 % je moc nebo málo | LEAN je povinnost, nebo ho nikdo nepoužívá | měření v §8, ladění ve Flight Labu |

## 7. Co zadání neřeklo

1. **Co přesně je „za hranice joysticku“.** Návrh volí rychlost zatáčení a
   nic jiného. Kdyby náklon i řídil, je to schéma GYRO s pákou navíc a hráč
   se pere se dvěma vstupy.
2. **Poloha, nebo pohyb.** Slovo „naklánění“ svádí k poloze. K tanci nutí
   jen pohyb, proto ukazatel vytéká.
3. **Kdo z toho vypadne.** Gamepad, klávesnice, boti, tablety a lidé, kteří
   se hýbat nemohou. Proto strop, vypínač hostitele a pravidlo, že sólo
   postup na LEAN nezávisí.
4. **Čitelnost pro ostatní.** Výhoda, kterou soupeř nevidí, vypadá jako
   chyba hry.
5. **Jak se to hráč dozví.** Bez ukazatele a první hlášky mechaniku nikdo
   neobjeví nebo nepochopí.
6. **GYRO a LEAN se vylučují.** Oba používají stejný pohyb.
7. **Síla, nebo přesnost.** Běžný hybrid ve střílečkách je opačný: páka dělá
   hrubý pohyb a gyro jemné domíření. To by pomohlo míření, ale k tanci
   nenutí. Zůstává jako pozdější experiment (D12).
8. **Sociální odměna.** Tanec je vtipný hlavně pro okolí. Ocenění BEST
   DANCER po kole ho zviditelní (D11).

## 8. Měření

- `input_lean_set {on, source: settings | default}`.
- Na konci kola `lean_round {lean_pct, full_count, scheme, kills, deaths}`:
  podíl času v zatáčce s náklonem nad 0,5 a počet naplnění.
- Otázky: kolik hráčů LEAN nechá zapnutý; jestli ti s `lean_pct` nad 20 %
  mají lepší poměr sestřelů a o kolik. Rozdíl nad zhruba 25 % znamená bonus
  snížit.

## 9. Ověření

Bez zařízení:

1. `python3 tools/flight/lean_sim.py --check` → kód 0.
2. Kompilace C# přes Roslyn proti DLL Unity.
3. `LeanModel` dává pro vstupy ze simulátoru stejná čísla (stejné vzorce,
   stejné konstanty).

Na zařízení (iPhone, Android s gyroskopem, Android bez něj, iPad):

- Živý řádek ve Flight Labu: otočení po směru hodin dává kladný `swing` na
  iOS i Androidu.
- Švih 30° s pákou naplno: oblouk se naplní, zazní `lean_full`, stopy
  zesvětlají. Bez páky se letadlo nepohne.
- Telefon držený šikmo 5 s: oblouk prázdný.
- Vleže na boku a s telefonem naplocho na stole: gyroskop funguje stejně;
  záložní cesta naplocho dává 0 a nepadá.
- GYRO: přepínač LEAN šedý, senzor náklonu vypnutý.
- POINT i RATE: žádné přestřelování kurzu při plném náklonu.
- Dvě zařízení, jedno s LEAN: druhé vidí světlejší stopy, pozice se
  nerozchází.
- Minimalizace a návrat do hry: ukazatel začíná od nuly.
- 10 minut hraní: žádný znatelný vliv na baterii ani teplotu.

## 10. Pořadí

- **Vlna 1 — mechanika.** Body 1–12 z §4, telemetrie, simulátor. LEAN jde
  vypnout jen na zařízení.
- **Vlna 2 — partie.** Vypínač hostitele (`MatchManager`, serverem psaná
  `NetworkVariable<bool>`, čte ji `rotatePlane`), přesný švih (švih do
  0,15 s od cvaknutí páky zvedne `SnapBoost` z 1,5 na 1,75), BEST DANCER
  (sekundy náklonu jedním `ServerRpc` na konci kola, řádek ve výsledcích).
- **Vlna 3 — podle dat.** Ladění bonusu, případně experiment s jemným
  mířením (D12).
