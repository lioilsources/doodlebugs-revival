# Hangár a vizuální směr — výběr zbraně, letadla, nátěru a arén (plán implementace)

Status (2026-11-18): **plán, neimplementováno.** Vlastník téhož dne rozhodl
D1: **směr A — Sky Poster**; ostatní rozhodnutí v §2 mají výchozí hodnotu.
Implementuje Opus podle tohoto dokumentu a
mockupu `Prompts/30-hangar-mockup.html`. Sourozenecké plány: `Prompts/28`
(let), `Prompts/29` (arzenál — dodá gadgety a nové ikony).

Zadání vlastníka: UX výběru zbraní, scény, lodě a skinu je na telefonu málo
sexy; je to pixel-art, ale možná to chce jiný styl; panely pro změnu zbraně
mezi koly jsou malinké a špatně čitelné. Navrhnout změnu a směr.

ROADMAP: zásada §3.9 (GameHUD dál neroste), fáze 0 (tween util), fáze 2 (pohyb
v UI), fáze 4 (loadout).

**Mockup je specifikace rozměrů.** V `Prompts/30-hangar-mockup.html` platí
uvnitř rámečků 1 CSS px = 1 jednotka canvasu; třídy `.a …` popisují směr A,
`.o …` dnešní stav přepsaný z kódu. Obrázky (base64) jsou až od řádku 405 —
pro čtení stačí prvních 400 řádků.

## 0. Zjištění (změřeno z kódu, stav `e6d025a`)

- **Canvas je navržený pro monitor.** `Scene01` má `CanvasScaler` 1920×1080,
  match 0,5. iPhone 12 mini tak dostane 2119×978 jednotek a jedna jednotka je
  **0,37 pt**. Hangár píše velikostmi 8–14 (`GameHUD.cs:1747-1755`, `:1828-1833`,
  `:2701-2709`), tedy **3–5 pt**; Apple uvádí minimum 11 pt.
- **Karta zbraně je 240×120 jednotek = 88×44 pt** (`BuildWeaponCards:1719-1720`),
  karta upgradu 185×95 = 68×35 pt. Tři karty zabírají 36 % šířky a 12 % výšky —
  výběr, kvůli kterému hangár existuje, má 4 % plochy displeje.
- **Karta zbraně nemá ikonu**, jen tři řádky textu, přestože ikony existují
  (`WeaponProfile.LoadIcon`). Tvoje letadlo je v hangáru vidět jen jako 36j.
  náhled v seznamu hráčů.
- **Nejlepší výtvarno hry je schované.** Hangár je černá plocha s 82–100 %
  krytím (`BuildHangarOverlay:1496-1499`) přes malované oblohy.
- **Tlačítka vlevo dole mají 180×68 jednotek = 66×25 pt** (PLANE, SCENES,
  SETTINGS) — pod 44 pt na výšku. Na hostu navíc SETTINGS na y 192–260
  **překrývá první dva řádky seznamu hráčů**, které začínají na y 200
  (`BuildReadyList:1901`, `BuildSettingsButton:2166-2172`; vyčteno z kódu, na
  zařízení neověřeno).
- **Výběr letadla:** 15 tvarů v řadě 192×220 a 50 nátěrů v mřížce 150×148 s
  popisky velikosti 8 (2,9 pt) (`BuildSkinPickerOverlay:2558-2628`). Výběr arén:
  řádky 740×120 s úchytem 64 j. (23 pt).
- **Press Start 2P** má čtvercové glyfy a je čitelné až od velkých velikostí;
  používá se na všechno včetně vět.
- **Všechno je pozicované absolutně od středu obrazovky**, bez bezpečné zóny
  (tu řeší jen `TouchControls`), bez přechodů; overlay se při každém otevření
  ničí a staví znovu (`CloseHangar:1442-1465`).
- **Hangár a výběry jsou ~1 560 řádků z 2 956** v `GameHUD.cs` (`:1323-2886`).
  Pět režimů (Searching, Waiting, PreBattle, LateJoin, Intermission) sdílí
  jeden stavitel s větvením (`:1477-1591`).
- **Premium nátěry ukazují cenu produktu, který ve storu neexistuje**
  (`BuildSkinCards:2704-2718`, `PlaceholderPrice`) a tap volá nákup, který
  selže — riziko pro App Review.

## 1. Směr

| | A — Sky Poster | B — Hi-bit Arcade | C — Squadron Ledger |
|---|---|---|---|
| Myšlenka | pixel-art patří **světu a hračkám** (letadla, střely, ikony); rozhraní je tučné, čisté a světlé, pozadí je obloha příští arény | vše pixelové, jen konečně velké: ostré rámečky, neon, řádkování | papír, plech, šablonové písmo, kokardy |
| Čitelnost na telefonu | nejlepší | lepší než dnes, horší než A | dobrá |
| Využije malované oblohy | ano, jsou pozadím | skoro ne | ne |
| Cena | střední (4 TTF, generované panely) | nízká | vysoká (textury z GPU pipeline) |
| Riziko | dva výtvarné jazyky vedle sebe — drží je pravidlo „hračka vs. krabice“ | „sexy“ efekt malý | draci, talíře a plazma se do WWI nevejdou |

**Doporučení: A.** Stejné rozložení a velikosti platí i pro B, takže rozhodnutí
D1 mění jen `UiTheme` a písma, ne strukturu — dá se přepnout i později.

## 2. Rozhodnutí

| # | Otázka | Výchozí (doporučení) | Kdo |
|---|---|---|---|
| D1 | Směr | **A — Sky Poster** | **rozhodnuto: A** (vlastník, 2026-11-18, podle mockupu) |
| D2 | Písma | **Barlow Condensed** (ExtraBold Italic titulky, Bold štítky) + **Barlow** (Medium/SemiBold text); OFL, mají českou diakritiku. Press Start 2P zůstává v letovém HUD do jeho vlastní úpravy. | vlastník |
| D3 | Členění | Záložky **WEAPON · GADGET · UPGRADES · PLANE**; tvar a nátěr na jedné záložce; arény pod pilulkou NEXT (jen host); nastavení pod ozubeným kolem. | doporučeno |
| D4 | Premium nátěry, dokud IAP neběží | **Bez ceny, štítek „soon“, tap nic nekupuje.** | vlastník (App Review) |
| D5 | Pořadí arén na telefonu | **Jen zapnout/vypnout**, bez přetahování; pořadí = katalog. | vlastník |
| D6 | Pozadí v režimu LateJoin | **Živá bitva pod 82% závojem** (jako dnes); ostatní režimy obloha arény. | doporučeno |
| D7 | Letový HUD (panely, kill feed, výsledky, pódium) | **Mimo tento plán**; samostatný krok se stejným kitem, výsledky s fází 2. | vlastník |
| D8 | GADGET před plánem 29 | Záložka se ukáže, až existuje `GadgetProfile`; do té doby tři záložky. | doporučeno |

## 3. Layoutový systém

**MenuCanvas** — nový kořenový canvas, vytvořený za běhu v `GameSetup` (žádná
změna scény): `Canvas` (Screen Space Overlay, `sortingOrder` 10) +
`CanvasScaler` (Scale With Screen Size, reference **800×400**,
`ScreenMatchMode.Expand`) + `GraphicRaycaster`. Scénový `EventSystem` obslouží
oba canvasy.

| Zařízení | MenuCanvas | 1 jednotka |
|---|---|---|
| iPhone 12 mini (812×375 pt) | 866×400 | 0,94 pt |
| iPhone Pro Max (932×430 pt) | 867×400 | 1,08 pt |
| 16:9 (desktop, Android) | 800×450 | – |
| iPad 4:3 | 800×600 | ~1,3 pt |

Navrhuje se pro jádro 800×400; šířka navíc roztahuje obsah, výška navíc (iPad)
zvětšuje scénu s letadlem a přidá druhou řadu nátěrů. Vše pod kořenem
`SafeArea` (stejný výpočet jako `TouchControls.ApplySafeArea:186-198`, přesunout
do `UiKit`). Letový HUD a `TouchControls` zůstávají na původním canvasu.

**Škála písma** (jednotky ≈ body na telefonu; **minimum 12**):

| Role | Velikost | Řez |
|---|---|---|
| Titul obrazovky, CTA | 30 | Condensed ExtraBold Italic |
| Název na kartě / jméno letadla | 25–26 | Condensed ExtraBold Italic |
| Záložky, sloty, názvy tvarů | 17–18 | Condensed Bold |
| Štítky verzálkami (pás karty, lišty, stav) | 12–13, proklad 0,08 em | Condensed Bold |
| Text a popisy | 12–13 | Barlow Medium / SemiBold |

**Dotykové cíle:** nejméně 48×48 jednotek (45 pt). **Mřížka:** okraj 16,
mezera 8–12. **Tvary:** poloměr 14 (karty), 12 (tlačítka, záložky), 16 (CTA),
pilulky = půl výšky; kontura 3; tvrdý stín posunutý o 4–6 dolů, bez rozmazání.

**Barvy** (`UiTheme`): Ink `#0D1730`, Paper `#F2F6FB`, PaperDim `#D5DEE9`,
TextDim `#33425E`; kategorie — zbraň `#FF8A1F`, gadget `#22C7DB`, speciál
`#E0489A`, zvýraznění `#FFD23F`, potvrzení `#3DDC68`; sklo nad oblohou
`rgba(13,23,48,0.74)`; závoj oblohy shora 0,30 → dole 0,92.

## 4. Obrazovky (rozměry v jednotkách 866×400, podle mockupu)

**Kostra hangáru** — jedna pro všech pět režimů:

- **Pozadí:** sprite oblohy aktuálního profilu (`BackgroundManager` — při
  mezikole už ukazuje arénu dalšího kola) přes `AspectRatioFitter`
  (Envelope) + svislý gradient. Plně kryje živou arénu (rebuild terénu zůstává
  schovaný), kromě LateJoin (D6).
- **Horní lišta** (y 6, výška 48): titul, pilulky „ROUND 2 OF 5“ a
  „NEXT · VOLCANO“ (host: tap otevře arény), vpravo nastavení 48×48.
- **Sloupec s letadlem** (x 16, šířka 254, y 60 až 76 nad spodkem): sprite
  204×204 s bodovým filtrem, natočený −7°, lehce se houpe; jméno tvaru +
  odznak elementu; dva sloty 50 vysoké (WEAPON, GADGET) — tap skočí na záložku.
- **Záložky** (x 286, y 58, výška 48, min. šířka 104, mezera 8); UPGRADES nese
  žlutý počet bodů k utracení.
- **Obsah** (x 286 až 16 od pravého okraje, y 122–320).
- **Spodní lišta** (výška 54, 12 nad spodkem): pilulky pilotů (náhled 32 v
  kroužku barvy hráče, jméno, PICKING / READY) a vpravo CTA 204×54 s odpočtem.
  CTA podle režimu: READY, FLY, JOIN (fáze 1 přidá PLAY).

| Režim | Titul | Záložky | CTA | Pozn. |
|---|---|---|---|---|
| Searching | DOODLEBUGS (velký) | žádné | – | stav hledání, verze; fáze 1 sem dá PLAY |
| Waiting | HANGAR | všechny | FLY | první otevření bez nároku na vzhled → záložka PLANE |
| PreBattle | HANGAR | všechny | READY + odpočet | |
| LateJoin | BATTLE IN PROGRESS | WEAPON, PLANE | JOIN + odpočet | živá bitva v pozadí |
| Intermission | HANGAR | všechny | READY + odpočet | UPGRADES s body |

**Karta nabídky** (`DraftCard`, tři vedle sebe, každá ~180×198): pás 28
(sloveso vlevo, KEEP vpravo) v barvě kategorie · odznak s ikonou 52 · název 25
· popis 12, nejvýš dva řádky · proužek 28 s **náčrtem dráhy střely** podle
`PreviewKind` (tečky, které se postupně rozsvěcují) · tři lišty DMG / RATE /
RANGE. Vybraná karta: zdvih o 8, náklon −1,2°, hlubší stín, zelená fajfka v
pásu. Po READY se karty zamknou (jako dnes).

- **WEAPON / GADGET:** tři nabídky (ponechat + dvě náhodné). Losování přesunout
  z `GameHUD.BuildWeaponCards` do statické `DraftOffers.Roll(current, pool, n)`.
- **UPGRADES:** čtyři karty téže anatomie; místo lišt stupeň 0–2 jako tečky a
  cena; v záhlaví „RUN POINTS 2“.
- **PLANE:** nahoře vodorovný pás tvarů se zarážkami — karta 128×116 (sprite
  68, název 18, element 12), vlastní žlutě, zabraný šedě se jménem pilota; pod
  ním PAINT: filtry (All / Free / Premium · soon) a řada nátěrů 48×48. Každý
  tap jde hned na server jako dnes; letadlo vlevo ukáže výsledek.
- **Arény (host):** list přes obsah — dlaždice 180×100 s náhledem oblohy a
  terénu (`BuildScenePreview` převzít), tap zapne/vypne, aspoň jedna zůstává.
- **Tlačítko HANGAR při warm-upu:** pilulka 110×48 vpravo nahoře (dnes 66×16 pt).
- **Nastavení:** `SettingsOverlay` přestěhovat na MenuCanvas a postavit z kitu.

## 5. Pohyb a čtyři kanály

`UiTween` (coroutiny, neškálovaný čas, žádný balíček třetí strany) — tím se
doručí tween util z fáze 0: `Scale`, `Fade`, `Move`, `Punch`, `Stagger`,
easing OutCubic a OutBack.

| Kdy | Pohyb | Zvuk | Haptika |
|---|---|---|---|
| Otevření | pozadí fade 0,15 s; lišty přijedou o 12 j.; karty zprava po 60 ms (OutBack 0,28 s) | – | – |
| Přepnutí záložky | prolnutí obsahu 0,12 s | tik | Light |
| Výběr karty | punch 1,06, pak zdvih a pohupování ±3 j. / 2,6 s | potvrzení | Light |
| Koupě upgradu | tečka stupně naskočí s punch, body odtikají | potvrzení | Medium |
| READY | CTA squash, pilulka pilota zezelená | potvrzení | Medium |
| Posledních 5 s | odpočet tiká s punch | tik | – |

Haptika přes `SfxManager.Haptic(HapticKind)` z plánu 28 (do fáze 2 no-op).
`Time.timeScale` se nesahá.

## 6. Architektura a migrace

Nové soubory (commitnout i `.meta`):

| Soubor | Role |
|---|---|
| `Scripts/UI/Kit/MenuCanvas.cs` | vytvoří canvas a kořen `SafeArea`; `MenuCanvas.Root`. |
| `Scripts/UI/Kit/UiTheme.cs` | barvy, velikosti, písma — jediné místo (přepnutí A/B). |
| `Scripts/UI/Kit/UiTween.cs` | tweeny. |
| `Scripts/UI/Kit/UiAudit.cs` | vývojová kontrola: `Text` pod 12 nebo `Button` pod 48 j. → varování s cestou v hierarchii. |
| `Scripts/UI/Kit/UiStrings.cs` | všechny texty hangáru jako konstanty (anglicky) — příprava na lokalizaci fáze 6. |
| `Scripts/UI/UiSprites.cs` (rozšířit) | `RoundedRect(radius)` jako 9-slice generovaný ve 4× rozlišení (`pixelsPerUnitMultiplier` 4), `VerticalGradient`. |
| `Scripts/UI/UiKit.cs` (rozšířit) | `Panel3` (stín + kontura + líc ze tří `Image`), `Pill`, `IconBadge`, `StatBar`, `TabBar`, `PressButton`, styly textu z `UiTheme`, `SafeArea`. |
| `Scripts/UI/HangarScreen.cs` | kostra, režimy, odběr událostí `MatchManager`, `IsOpen`. |
| `Scripts/UI/Hangar/{LoadoutTab,UpgradesTab,PlaneTab,ArenaSheet,DraftCard}.cs` | obsah záložek. |
| `Scripts/Weapons/DraftOffers.cs` | losování nabídek. |
| `Resources/Fonts/Barlow*.ttf` + `Barlow-OFL.txt` | čtyři řezy; `.meta` = kopie `PressStart2P.ttf.meta` s novým GUID. |
| `tools/ui/generate_ui_icons.py` + `Resources/Sprites/Ui/ui_*.png` | bílé glyfy 64×64 (ozubené kolo, fajfka, zámek, šipka); Pillow, `.meta` přes `tools/planes/unity_meta.py`. |

Švy:

| Soubor:řádek | Změna |
|---|---|
| `GameSetup.cs:103-122` | po `GameHUD.CreateHUD`: `MenuCanvas.Create()`, `HangarScreen.Create()`; `SettingsOverlay.Create` nad MenuCanvas. |
| `GameHUD.cs:234-292` | odběry `OnHangarOpened`, `OnClientReadyChanged`, `OnRunStateChanged`, `OnSessionReset` přejdou do `HangarScreen`; `OnHangarTick` odebírají obě třídy (pódium zůstává v HUD); `OnMatchStarted` zavírá hangár v `HangarScreen`. |
| `GameHUD.cs:1323-2886` | **smazat** hangár, výběr letadla a arén (v tomtéž PR, který dosáhne parity — žádný dvojí kód v mainu). Zůstávají panely, kill feed, časomíra, výsledky, pódium. |
| `GameHUD.cs:1418-1428` | `IsHangarOpen` → `HangarScreen.IsOpen`; `IsAnyOverlayOpen` = hangár ∨ výsledky ∨ pódium ∨ nastavení. Volající (`TouchControls:135`, `ConnectionUI:102`) beze změny. |
| `GameHUD.cs:1392-1398` | `OnLocalPlayerDeployed` přepošle do `HangarScreen` (`PlayerController:321`). |
| `GameHUD.cs:1602-1614` | FLY: `ServerOnWarmUpFlyOut` volá `HangarScreen`. |
| `BackgroundManager.cs:56, 407-410` | veřejná `CurrentProfile` (z `_backgroundIndex`) + událost změny pro pozadí. |
| `UI/SettingsOverlay.cs` | postavit z kitu, rodič MenuCanvas. |
| `CLAUDE.md`, `ROADMAP.md` | sekce UI (MenuCanvas, kit, škála, minimum 12 / 48); fáze 0 tween `[done]`. |

Pravidla stavby: obrazovka se staví jednou a pak jen ukazuje, skrývá a
obnovuje; `PlaneTab` se staví líně při prvním otevření (15 kompozitů tvarů);
`PlaneModelCatalog.TrimComposites` při zavření zůstává. Ocasní akcent letadla
v UI: `Image.material` se shaderem `Custom/ColorReplace` jen u hrdiny (není v
masce); v rolovacích pásech zůstává akcent červený jako dnes. Všechno je
legacy `Text` s dynamickým písmem — TextMeshPro by chtěl SDF atlas z editoru.

## 7. Měření

`hangar_open {mode}`, `hangar_tab {tab}`, `ready_ms` (otevření → READY),
`plane_pick {model}`, `skin_pick {skin}`, `arena_toggle`; `weapon_pick` a
`gadget_pick` z plánu 29. KPI: medián `ready_ms` pod 12 s; přes 80 % kol
odstartuje tlačítkem, ne vypršením; podíl hráčů, kteří aspoň jednou změní
letadlo.

## 8. Ověření

1. Kompilace přes Roslyn `csc` (postup z Prompts/27); editor headless neběží,
   proto žádná změna scény ani prefabu.
2. `UiAudit` po postavení každé obrazovky: nula varování.
3. Parita režimů — projít tabulku v §4 řádek po řádku: Searching při startu,
   Waiting + FLY + návrat pilulkou, PreBattle s druhým zařízením, LateJoin +
   JOIN, Intermission s body, pódium a reset runu, odpojení hosta (zpět na
   Searching).
4. Dvě zařízení: READY druhého hráče přebarví jeho pilulku; zabraný tvar
   zešedne do 0,2 s; host mění arény, klient pilulku NEXT nevidí jako tlačítko.
5. Zařízení: iPhone mini, Pro Max, iPad (800×600 — letadlo větší, dvě řady
   nátěrů), Android 16:9; výřez a Dynamic Island nic neuřezávají.
6. Čitelnost: z 40 cm přečíst popis zbraně bez přiblížení; výběr zbraně a
   READY jedním palcem pravé ruky.
7. Pravidlo 15 sekund drží: start → Searching → FLY bez nového kroku.
8. Výkon: otevření hangáru bez snímku delšího než 33 ms na iPhonu 11;
   pohupování karet nealokuje.

## 9. Rizika

- **Výměna 1 560 řádků naráz.** Proto tabulka režimů jako kontrolní seznam a
  mazání starého kódu až v PR s paritou.
- **Dva výtvarné jazyky.** Drží je jedno pravidlo: co létá a střílí, je pixel;
  co drží prst, je plochý tvar s konturou. Pixelové sprity se v UI nikdy
  nevyhlazují (`FilterMode.Point`), rozhraní nikdy nepixeluje.
- **Výsledky a pódium zůstanou chvíli ve starém stylu** (D7) — viditelný šev
  mezi koncem kola a hangárem do fáze 2.
- **Písma.** Ověřit českou diakritiku ve všech čtyřech řezech (`UiAudit`
  vykreslí „Příliš žluťoučký kůň“); licenční soubor OFL do repa.
- **iPad a desktop.** Jádro 800×400 se roztahuje; ověřit krok 5.
- **Staré ikony zbraní** jsou tmavé siluety; na oranžovém odznaku čitelné
  jsou, plnou kvalitu dodá plán 29 (odznaky).

## 10. Rozsah a pořadí

1. **Kit + kostra + loadout** (≈ 4 dny): MenuCanvas, `UiTheme`, `UiTween`,
   `UiAudit`, písma, `HangarScreen` se všemi režimy, WEAPON a UPGRADES,
   seznam pilotů, CTA. Záložka PLANE dočasně otevírá starý výběr.
2. **PLANE + arény + nastavení + úklid** (≈ 3 dny): `PlaneTab`, `ArenaSheet`,
   `SettingsOverlay` z kitu, smazání hangáru z `GameHUD`, GADGET, jakmile
   existuje plán 29.

Doporučené pořadí všech tří plánů: **28 PR 1** (rychlá výhra v ovládání) →
**30 PR 1** → **29 vlna 1** → 28 PR 2 → 30 PR 2 → 29 vlna 2.
