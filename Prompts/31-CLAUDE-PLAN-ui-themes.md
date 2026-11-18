# Motivy rozhraní — deset kabátů pro hangár (plán implementace)

Status (2026-11-18): **implementováno** (vlny 0–3 v kódu, výtvarné podklady z
L40S), kompilace ověřena Roslynem, editor ani zařízení ne. Původně: plán;
rozhodnutí v §2 mají výchozí hodnotu.

Odchylky od plánu: filtry arény běží na CPU (blit do malé RenderTexture a
zpět), ne ve shaderech, takže na zařízení nemůže nic selhat při překladu;
Lightfield nemá sklo nad rozmazanou kopií, rozmazané je celé pozadí; pro Aqua
jsou písma Paytone One + Rubik (Fredoka a Lilita One nemají ě č ř ů); písma
mají 1,67 MB. Generátor běžel na L40S (FLUX.1-schnell fp8 a Qwen-Image fp8).
Implementuje Opus podle tohoto dokumentu, mockupu
`Prompts/31-ui-themes-mockup.html` a dvou katalogů v `tools/ui_themes/`.

Zadání vlastníka: dva mockupy hangáru z plánu 30 se líbily. Připravit asi
deset podobných ve stylu her ze 70., 80., 90. let, 2000, 2010, 2020 a 2030,
plus vesmír, džungle a utopické město budoucnosti. Plán pro Opus a prompty
pro schnell a Qwen, aby mohl začít pracovat.

Navazuje na `Prompts/30` (hangár, směr Sky Poster, `MenuCanvas`, kit
`UI/Kit`). **Základem je větev `feat/flight-arsenal-hangar` (PR #64)**; bez ní
tenhle plán nemá na čem stát.

ROADMAP: fáze 2 (juice), fáze 4 (meta: motivy jsou kosmetika, kterou jde
později odemykat), zásady §3 (solo-first, čtyři kanály, měřitelnost, jeden
jazyk na locale, `GameHUD` neroste).

## 0. Co už existuje (hotovo v tomto kroku)

| Soubor | Co to je |
|---|---|
| `Prompts/31-ui-themes-mockup.html` | Deset rámečků 866 × 400, v každém tatáž obrazovka (výběr zbraně mezi koly). Uvnitř rámečku 1 CSS px = 1 jednotka `MenuCanvas`. Každý motiv je jeden blok CSS proměnných (`.t-70`, `.t-80`, …) a pár pravidel pro tvar. Obrázky (base64) jsou až na konci souboru. |
| `tools/ui_themes/tokens.py` | Paleta, písma, velikosti, tvar a přepínače stylu všech 11 motivů (Sky Poster + 10). Spuštěním počítá kontrast WCAG pro každý pár text/plocha; teď **11 motivů, 0 chyb**. Je to zrcadlo budoucího `UiThemeCatalog.cs`. |
| `tools/ui_themes/prompts.py` | Katalog promptů: 42 úloh, 148 obrázků (104 schnell, 44 Qwen-Image, odhadem 110 minut Qwenu). Spuštěním vypíše úlohy a zkontroluje pravidla promptů. |

Mockup je specifikace vzhledu, `tokens.py` specifikace čísel. Když se liší,
platí `tokens.py` (prošel bránou kontrastu).

## 1. Motivy

Rozložení a velikosti jsou u všech stejné jako u Sky Posteru: nejmenší text
12, nejmenší dotykový cíl 48. Motiv mění barvy, písmo, tvar panelů, ukazatele,
odznak ikony a úpravu pozadí. Letadla a ikony zbraní zůstávají pixelové.

| id | klíč | jméno | panel | ukazatel | odznak | úprava arény | vlna |
|---|---|---|---|---|---|---|---|
| 0 | `skyposter` | SKY POSTER | zaoblený, inkoustový obrys, tvrdý stín | plný | disk | žádná | hotovo |
| 1 | `phosphor` | PHOSPHOR | jen obrys, záře | svítící čára | kroužek | jedna barva fosforu + řádkování | 1 |
| 2 | `coinop` | COIN-OP | ostré rohy, tvrdý stín šikmo | bloky | čtverec | 5 barevných stupňů + řádkování | 1 |
| 3 | `chrome` | CHROME | ocelový rám s nýty, tmavý displej uvnitř | LED zelená → červená | plíšek | vinětace | 3 |
| 4 | `aqua` | AQUA | sklo, lesklý pruh | lesklá trubička | lesklá koule | odlesky | 2 |
| 5 | `flat` | FLAT | bílá karta, měkký stín podle výšky | tenká linka | dlouhý stín | odstíny šedi pod barvou | 1 |
| 6 | `clay` | CLAY | nafouknutý, bez obrysu, vnitřní stíny | tlustá pilulka | zaoblený čtverec | rozmazání | 2 |
| 7 | `lightfield` | LIGHTFIELD | průsvitné sklo, vlasová hrana | paprsek s bodem | světelný kruh | rozmazání + polární záře | 2 |
| 8 | `cockpit` | COCKPIT | zkosené rohy, tyrkysová linka | šikmé segmenty | šestiúhelník | mřížka | 1 |
| 9 | `canopy` | CANOPY | prkna, pergamen uvnitř | bambus | disk | liány přes horní okraj | 3 |
| 10 | `solaria` | SOLARIA | keramika, horní oblouk, zlatá linka | lístky | dvojitý kroužek | světlý závoj | 3 |

Id se jen přidávají a po vydání se nemění (ukládají se a jdou do telemetrie),
index v poli = id, stejně jako u `WeaponType`.

**Nálada doby, žádná konkrétní hra.** Žádná cizí loga, postavy, názvy her,
studií ani konzolí, v kódu ani v promptech. Stejné pravidlo jako u arén
(`tools/backgrounds/README.md`).

## 2. Rozhodnutí

| # | Otázka | Výchozí (doporučeno) | Vlastník |
|---|---|---|---|
| D1 | Komu motiv patří? | **Zařízení.** Každý hráč má svůj, po síti se nic neposílá. | Olin |
| D2 | Výchozí motiv | **Sky Poster zůstává.** Deset nových přibude v nastavení. | Olin |
| D3 | Cena | **Všechny zdarma.** `UiThemeDef.Premium` existuje, ale nic nezamyká, dokud nevznikne obchod (fáze 4). | Olin |
| D4 | Kde se motiv projeví | Vlny 1–3: obrazovky na `MenuCanvas` (hangár, výběr letadla a arény, nastavení, banner, Flight Lab). HUD v boji, dotykové ovládání a výsledky až ve vlně 4. | Olin |
| D5 | Pozadí hangáru | Před bojem a mezi koly **zůstává vidět příští aréna**, motiv ji jen upraví. Vlastní ilustraci motivu dostanou neprůhledné režimy Searching a Waiting. | Olin |
| D6 | Jména motivů | Anglická vlastní jména, nepřekládají se. | Olin |
| D7 | Co znamená „qwen“ | **Qwen-Image v ComfyUI**, použitý jen na nápis hry. Kdyby šlo o LLM `qwen36`, mění se jen pole `model` v `prompts.py`. | Olin, potvrdit |
| D8 | Písma a jazyky | Písmo motivu jen pro jazyky psané latinkou. Ostatní abecedy berou písmo jazyka (`Loc.PickFont` z PR #57). TTF se oříznou na latinku a latin-ext. | Olin |
| D9 | Nové arény Jungle a Utopia | Ano, ve vlně 4 stávající pipeline `tools/backgrounds`. Motiv na aréně nezávisí. | Olin |
| D10 | Zvuky tlačítek podle motivu | Ano, ve vlně 4, generované `tools/audio/generate_sfx.py`. | Olin |
| D11 | Motiv podle arény (AUTO) | Teď ne. | Olin |
| D12 | Pořadí slučování | PR #64 → tento plán. PR #57–#63 sahají na `SettingsOverlay` a `UiKit`; kdo jde druhý, řeší konflikt. | Olin |

## 3. Architektura

Dnes je `UiTheme` statická třída s `static readonly Color` a `const`
velikostmi, čtená na 134 místech (`HangarScreen` 48, `DraftCard` 14,
`FlightLabOverlay` 14, `Ui` 14, `PlaneTab` 12, `SettingsOverlay` 12, …).
Rozhraní se staví jednou za běhu z kódu.

**Zásada: volající se nemění.** `UiTheme.Ink` zůstane platný výraz, jen
přestane být konstantou.

1. **`UI/Kit/UiThemeDef.cs`** (nový): datová třída, pole 1 : 1 podle
   `tokens.py`. Barvy, čtyři písma, tabulka velikostí, proklad, velikost
   písmen, `PanelStyle`, poloměry, obrys, `ShadowSpec` (Hard / Soft / Glow +
   posun + barva), pohyb vybrané karty, `BarStyle`, `BadgeStyle`,
   `BackdropFilter`, `OverlayKind`, cesty k assetům, `Premium`.
2. **`UI/Kit/UiThemeCatalog.cs`** (nový): pole 11 definic, `Get(id)`,
   `Validate()` volaná z `GameSetup` vedle `WeaponProfile.Validate` (index ==
   id, písma se načetla, assety existují).
3. **`UI/Kit/UiTheme.cs`**: fasáda. `public static UiThemeDef Active`,
   `Ink => Active.Ink` atd. Konstanty `SizeTitle`, `RadiusCard`, `Outline`…
   se stanou vlastnostmi. Dvě místa je používají jako výchozí hodnotu
   parametru (`Ui.Card(... radius = UiTheme.RadiusCard)`, `Ui.Pill(... size =
   UiTheme.SizeSmall)`): tam dát `-1` / `0` s významem „podle motivu“.
   `MinText` a `MinTouch` zůstávají konstanty, motiv je nesmí snížit.
4. **`Meta/ThemeSettings.cs`** (nový), podle vzoru `ControlSettings`: klíč
   PlayerPrefs `settings.uiTheme`, událost `Changed`,
   `Telemetry.Log("ui_theme_set", ("theme", key), ("source", …))`, při startu
   `LogBoot`. Až přistane `PlayerProfile` (PR #59), klíč převezme.
5. **Přepnutí = přestavba.** `UiTheme.Apply(def)` nastaví `Active`, načte
   assety nového motivu, vyvolá `Changed`. Obrazovky se na `Changed` zničí a
   postaví znovu: `HangarScreen.Rebuild()` (nový; zapamatuje režim, zbývající
   čas a záložku, zavolá `Build` a `Open`), `SettingsOverlay`, `BannerOverlay`,
   `FlightLabOverlay`; `ArenaSheet` a karty se staví při otevření samy.
   Přepíná se jen v nastavení, takže přestavba za pár milisekund stačí a
   žádný widget si nemusí pamatovat, jak se přebarvit.
6. **Pořadí při startu** (`GameSetup`): `ThemeSettings.Boot()` →
   `MenuCanvas.Create()` → obrazovky. Uložený motiv platí od prvního snímku.
   Načítá se jen aktivní motiv; assety předchozího uvolní
   `Resources.UnloadUnusedAssets()`.

### 3.1 Panely (`UI/Kit/UiPanels.cs`, nový)

`Ui.Card` dostane volitelný parametr `UiRole role = UiRole.Card` (Card,
Button, Tab, Cta, Slot, Sheet, Pill) a stavbu předá motivu. Dnešní tři
`Image` (Shadow, Outline, Face) zůstávají pro `Rounded`; ostatní styly:

| `PanelStyle` | Z čeho | Poznámka |
|---|---|---|
| Rounded | `UiSprites.RoundedRect`, jako dnes | Sky Poster, Flat (obrys 0, měkký stín) |
| Outline | nový `UiSprites.RoundedOutline(width)` + průhledná výplň | Phosphor; záře = `UiSprites.SoftRect` pod panelem |
| Square | poloměr 0, stín posunutý v x i y | Coin-Op |
| Chamfer | nový `UiSprites.Chamfer(cut)`, 9-slice | Cockpit; obrys = stejný sprite o 2 j. větší |
| Gloss | nový `UiSprites.RoundedGradient(stops)` | Aqua; pruh karty má dělený přechod (2 × 2 zastávky) |
| Glass | materiál `Glass` nad rozmazanou kopií pozadí (§3.4) | Lightfield |
| Clay / Bevel / Plank / Arch | 9-slice z `Resources/Sprites/UiThemes/<klíč>/panel_<role>.png` | skládá `compose_frames.py` (§7), ne model |

Chrome a Canopy mají v kartě vnitřní plochu (displej, pergamen): `DraftCard`
ji postaví, když `Active.CardInset > 0`. Text karty pak leží na `Paper`,
pruh a sloty na `Frame`. Solaria má oblouk: 9-slice s horním okrajem 64 j.,
pruh je vycentrovaný a vysoký 50.

Měkký stín a záře: `UiSprites.SoftRect(radius, blur)` generovaný za běhu,
jeden `Image` pod panelem. Žádný `Mask`, žádné efekty přes celou obrazovku.

### 3.2 Ukazatele, odznaky, ikony

- `Ui.Bar` deleguje na `BarStyle`. Segmentové styly (Blocks, Led, Slant,
  Bamboo, Leaves) postaví nejvýš 12 dílků jako samostatné `Image` a podle
  hodnoty je zapínají. Led má pevné barvy podle pozice dílku (6 zelených, 3
  žluté, 1 červený), ne podle hodnoty.
- `Ui.Badge` deleguje na `BadgeStyle` (Disc, Ring, Square, Plate, Orb,
  LongShadow, Squircle, Halo, Hex, Ringed).
- **Ikony bez disku.** `weapon_*.png` mají dnes disk zapečený
  (`generate_icons.py:render`) a `DraftCard` kreslí sprite přímo. Přidat do
  generátoru `--glyphs`, který uloží jen glyf 24 × 24 zvětšený 4× jako
  `weapon_<key>_glyph.png`, a `WeaponProfile.LoadGlyph`. Karty a sloty v
  hangáru pak kreslí `Ui.Badge(glyf)`. Spouště v boji (`TouchControls`) dál
  berou původní odznak, dokud nepřijde vlna 4.
- Phosphor obarvuje letadlo i glyfy stejným materiálem jako arénu.

### 3.3 Text (`UI/Kit/UiTextFx.cs`, nový)

- `UnityEngine.UI.Text` neumí proklad. `UiLetterSpacing : BaseMeshEffect`
  posune vrcholy znaků; `Ui.Text` ho přidá, když má role v motivu proklad
  jiný než 0. `supportRichText` zůstává vypnuté, takže počítání znaků je
  přímočaré.
- Role textu: `Ui.Text` dostane volitelný `UiText role` (Title, CardTitle,
  Tab, Label, Body, Small, Cta, Name). Bez něj se role odvodí z předaného
  písma (`UiTheme.Display` → CardTitle). Velikost se pak bere z tabulky
  motivu, protože každé písmo běží jinak široké (Press Start 2P má název
  karty 13, Barlow 25).
- `TextCase`: Phosphor chce nadpisy malými písmeny (Major Mono Display je
  tak kreslí jako verzálky). Použít `ToLowerInvariant` jen na roli Display.
- Popis na kartě: úzké karty a široká písma (Phosphor, Coin-Op, Canopy)
  lámou popis na tři řádky. `DraftCard` dnes dává popisu pevných 30 j.;
  zapnout `resizeTextForBestFit` s minimem 12 a u těchto tří motivů zmenšit
  odznak na 44 (viz mockup).
- Přechod v nadpisu (Coin-Op, Chrome): dvoubarevný svislý přechod umí
  `UiTextGradient : BaseMeshEffect`. Tři ostré pruhy z mockupu Coin-Opu
  takhle nejdou; nadpis HANGAR dostane dvoubarevný přechod a plné provedení
  nese jen obrázkový nápis hry na úvodní obrazovce.

### 3.4 Pozadí (`UI/Kit/UiBackdrop.cs`, nový)

Dnes `HangarScreen.Build` staví `_sky` a dva závoje (`UiTheme.SkyTop`,
`SkyBottom`). Nově to vlastní `UiBackdrop`:

- **Závoj:** dvě barvy s alfou z motivu. Světlé motivy (Clay, Solaria) mají
  závoj nahoře nejméně 0,55, jinak tmavý text na tmavé aréně zmizí; hlídá to
  brána v `tokens.py`.
- **Filtr arény:** `Mono(barva)`, `Posterize(n)`, `Grayscale` jsou tři malé
  UI shadery v `Resources/Shaders/UiThemes/` (načtené `Resources.Load<Shader>`,
  aby se dostaly do buildu), materiál na `_sky`. Když se shader nenačte nebo
  není podporovaný, aréna se ukáže bez filtru.
- **Rozmazání:** jednou při otevření a při změně arény se pozadí i se
  závojem vykreslí do `RenderTexture` 1/8 rozlišení (dva kroky zmenšení).
  Clay ji ukáže místo arény. Lightfield ji dá skleněným panelům: shader
  `Glass` čte rozmazanou texturu podle pozice na obrazovce a alfu bere ze
  spritu panelu. Žádný `GrabPass`.
- **Překryv:** řádkování, mřížka, fazety, odlesky a liány jsou dlaždicové
  nebo jednotlivé sprity nad závojem, `raycastTarget = false`.
- **Vlastní ilustrace motivu** (`backdrop.jpg`, 1344 × 768): jen v režimech
  Searching a Waiting. Úvodní nápis DOODLEBUGS se z textu (`BuildBoot`,
  velikost 72) změní na `wordmark.png`; když chybí, zůstane text písmem
  motivu.

### 3.5 Výběr motivu (`UI/ThemeSheet.cs`, nový)

V `SettingsOverlay` přibude řádek THEME se jménem aktivního motivu. Otevře
list přes celou šířku (vzor `ArenaSheet`): 11 karet 124 × 104 ve dvou
řadách, vodorovně posuvné. Každá karta je **živý náhled postavený tokeny
toho motivu** (pruh, plocha, dva ukazatele, jméno), žádné obrázky. Tap motiv
hned použije.

Čtyři kanály přepnutí: krátké prolnutí přes barvu `Ink` (120 ms ven, přestavba,
160 ms dovnitř), zvuk `SfxManager.PlayNamed("theme")`, haptika Light, text
`BannerOverlay.Show("THEME · CHROME")`.

## 4. Co mockup ukazuje a Unity udělá jinak

| V mockupu | V Unity |
|---|---|
| Lightfield: krajní karty natočené v perspektivě | `MenuCanvas` je Overlay bez perspektivy. Krajní karty budou o 3 % menší a o něco průhlednější. Nenahánět. |
| Lightfield: rozmazání jen pod panely | Stejný dojem přes shader `Glass` a rozmazanou kopii (§3.4). |
| Phosphor: záře textu | `Shadow` s barvou fosforu a nulovým posunem je slabý; přijatelné. Záři panelů dělá `SoftRect`. |
| Coin-Op: třípruhový nadpis | Dvoubarevný přechod; pruhy jen v obrázkovém nápisu. |
| Canopy, Chrome: textury kreslené přechody v CSS | Skutečné textury ze schnellu složené do 9-slice. Vypadat to bude lépe než mockup, ne stejně. |
| Jungle a Utopia v pozadí | Kreslí je skript na stránce. Arény ve hře nejsou (D9). |

## 5. Písma

Seznam souborů je v `tokens.py` (`FONTS`): 32 řezů z 19 rodin, všechny pod
OFL (Press Start 2P už v repu je). Všechny umí latinku s rozšířením, ověřeno
na Google Fonts 18. 11. Share Tech Mono a Carter One neuměly českou diakritiku, proto je
nahradily IBM Plex Mono a Titan One.

- **Unity neumí proměnná písma.** 10 rodin Google dodává jen jako proměnný
  TTF (`static=False` v `FONTS`). `tools/ui_themes/fetch_fonts.py` (nový) je
  stáhne z repa `google/fonts`, vyřízne statický řez
  (`fonttools varLib.instancer wght=…`) a ořízne na latinku a latin-ext
  (`pyftsubset`). Do `Resources/Fonts/` jdou hotové TTF a vedle nich text
  licence každé rodiny, stejně jako dnes u Barlow.
- **Rozpočet:** po oříznutí do 1,5 MB za všech 32 řezů.
- Každé písmo má jinou účaří. `UiThemeDef.FontTweak` (posun y a měřítko na
  roli) je připravené pole; hodnoty se doladí na zařízení.

## 6. Prompty pro schnell a Qwen-Image

Všechno je v `tools/ui_themes/prompts.py`; `--prompts` vypíše složený prompt
každé úlohy, `--json` seznam pro stroj.

| Druh | Model | Velikost | Kolik | Zpracování |
|---|---|---|---|---|
| `backdrop` | schnell | 1344 × 768 | 11 × 4 seedy | beze změny, JPG q90 |
| `tile` | schnell | 1024 × 1024 | 9 × 3 | udělat bezešvé, zmenšit na 256 |
| `ornament`, `strip` | schnell | 1024 × 1024, 1344 × 768 | 9 × 3 | bílé pozadí → BEN2/RMBG + bílý klíč, oříznout |
| `glow` | schnell | 1024 × 1024 | 2 × 3 | černé pozadí → alfa z jasu |
| `wordmark` | Qwen-Image | 1664 × 928 | 11 × 4 | `cutout` nebo `luma`, oříznout, vejít do 1024 × 320 |

Pravidla, která skript sám hlídá:

- **Prompty pro schnell jsou jen kladné.** FLUX při cfg 1 přitahuje i to,
  co prompt popírá. Co na obrázku být nemá, je v poli `negative` a v ComfyUI
  jde přes `NAGuidance` (`spark_backgrounds.flux_graph(..., negative=…)`).
- **Text nese jen nápis hry.** Slovo `DOODLEBUGS` je v promptu jednou a v
  uvozovkách. Žádný jiný řetězec rozhraní se do textury nepeče.
- Rozměry pro schnell se vejdou do obálky NIM (strany 768–1344 po 64), takže
  stejná úloha běží v ComfyUI i přes doplněk `flux-schnell`. NIM neumí
  negativy; jeho výstup je koncept.

**Qwen-Image:** názvy souborů modelu v `prompts.py` jsou výchozí sada
ComfyUI a **na SPARKu nejsou ověřené**. Před první dávkou je porovnat s
`/object_info` (chybějící vstup ComfyUI tiše vyřadí a ohlásí úspěch). Když
Qwen nebude k dispozici nebo slovo zkomolí, nápis vykreslí Pillow z písma
motivu; `compose_frames.py` to má umět jako záložní cestu, pravopis je pak
jistý.

## 7. Nástroje (`tools/ui_themes/`)

| Skript | Stav | Co dělá |
|---|---|---|
| `tokens.py` | hotovo | tokeny + brána kontrastu |
| `prompts.py` | hotovo | katalog promptů + validace |
| `generate_theme_art.py` | napsat | `render` / `post` / `sheet` / `apply`, stejný tvar jako `tools/weapons/generate_projectiles.py`. Klienta ComfyUI, `run_jobs`, `white_key` a RMBG bere z `tools/backgrounds/spark_backgrounds.py`. Graf pro schnell: `UNETLoader flux1-schnell.safetensors`, `DualCLIPLoader` (clip_l + t5xxl_fp16, typ flux), `ae.safetensors`, 4 kroky, cfg 1, euler/simple. Výstupy idempotentní, jedna úloha ve frontě. |
| `compose_frames.py` | napsat | Pillow, bez GPU. Z dlaždic a tokenů složí 9-slice panely (Clay, Chrome, Canopy, Solaria): maska, okraj, zkosení nebo vnitřní stín, textura, ozdoby v rozích. Zapíše `.meta` s `spriteBorder` (rozšířit `tools/planes/unity_meta.py`). Brána: okraje symetrické, střed bezešvý. |
| `fetch_fonts.py` | napsat | §5 |
| `README.md` | napsat | česky, krátce |

SPARK jen přes `sparkctl` (lease nebo `exec`), heartbeat a `done` s metrikou
`{"images":{"ok":N,"failed":M}}`. Qwen-Image a schnell běží v profilu
`comfy`; 44 obrázků Qwenu je přes hodinu, dávku pustit přes `sparkctl exec`.

## 8. Soubory

**Nové:** `UI/Kit/UiThemeDef.cs`, `UiThemeCatalog.cs`, `UiPanels.cs`,
`UiTextFx.cs`, `UiBackdrop.cs`; `UI/ThemeSheet.cs`; `Meta/ThemeSettings.cs`;
`Resources/Shaders/UiThemes/{Mono,Posterize,Grayscale,Glass}.shader`;
`Resources/Fonts/*.ttf` + licence; `Resources/Sprites/UiThemes/<klíč>/*`;
`Resources/Sprites/Weapons/*_glyph.png`; tři skripty a README v
`tools/ui_themes/`. Ke každému novému assetu `.meta`.

**Měněné:** `UI/Kit/UiTheme.cs` (fasáda), `Ui.cs` (role, delegace),
`UiSprites.cs` (obrys, zkosení, měkký obdélník, přechod), `UiAudit.cs`;
`UI/HangarScreen.cs` (`Rebuild`, `UiBackdrop`, úvodní nápis),
`UI/Hangar/DraftCard.cs`, `PlaneTab.cs`, `ArenaSheet.cs`;
`UI/SettingsOverlay.cs` (řádek THEME), `BannerOverlay.cs`,
`FlightLabOverlay.cs` (vývojářské tlačítko „další motiv“);
`GameState/GameSetup.cs` (pořadí startu, `Validate`);
`Weapons/WeaponProfile.cs` (`LoadGlyph`);
`tools/weapons/generate_icons.py` (`--glyphs`).

`GameHUD.cs` se v tomto plánu nemění.

## 9. Pořadí

Každá vlna jde vydat samostatně.

- **Vlna 0 — základ bez viditelné změny.** `UiThemeDef`, katalog s jediným
  motivem Sky Poster, fasáda, `ThemeSettings`, `Rebuild`, role v `Ui.Card` a
  `Ui.Text`, `ThemeSheet`. Hotovo, když hra vypadá na pixel stejně jako
  build 36.
- **Vlna 1 — motivy jen z kódu:** Phosphor, Coin-Op, Flat, Cockpit. Nové
  styly panelů, ukazatelů a odznaků, glyfy bez disku, proklad, tři shadery,
  písma.
- **Vlna 2 — přechody a sklo:** Aqua, Clay, Lightfield. Přechodové sprity,
  měkké stíny, rozmazaná kopie pozadí, `compose_frames.py` pro Clay.
- **Vlna 3 — textury:** Chrome, Canopy, Solaria.
- **Výtvarná linka běží souběžně od začátku** a na kódu nezávisí:
  `generate_theme_art.py`, pozadí a nápisy pro všech 11 motivů, pak dlaždice
  a ozdoby. Pořadí: nejdřív `render` pozadí (schnell, minuty), pak dávka
  nápisů (Qwen, přes hodinu), `sheet` k výběru vlastníkem, `apply`.
- **Vlna 4 — volitelně:** HUD a ovládání podle motivu, zvuky, arény Jungle a
  Utopia, odemykání.

## 10. Měření

- `ui_theme_set {theme, source: settings | boot | default}`.
- Vlastnost relace `ui_theme` u startu, aby šlo řezat retenci podle motivu.
- KPI: podíl hráčů, kteří motiv aspoň jednou změní; rozložení motivů po 7
  dnech. Motiv, který si nikdo nenechá, nepatří do placené nabídky.

## 11. Ověření

Unity na vývojovém Macu neběží, takže před zařízením:

1. `python3 tools/ui_themes/tokens.py` → 0 chyb.
2. `python3 tools/ui_themes/prompts.py` → kód 0.
3. Kompilace C# přes Roslyn proti DLL Unity (stejně jako u plánů 28–30).
4. `UiThemeCatalog.Validate()` nic nehlásí v logu.
5. Build iOS v CI: v logu žádná chyba shaderu.

Na zařízení (iPhone mini, iPad, malý Android), pro každý motiv:

- Všech pět režimů hangáru, čtyři záložky, výběr arény, nastavení. Nic
  nepřetéká, `UiAudit` nehlásí text pod 12 ani cíl pod 48.
- Přepnutí motivu uprostřed odpočtu před bojem: čas běží dál, vybraná
  zbraň a READY zůstanou.
- Start s uloženým motivem: první snímek už je v něm, studený start →
  první výstřel pořád pod 15 s.
- Tmavá aréna (Orbit) a světlá (MistyPeaks) pod každým motivem: nadpis a
  štítky čitelné.
- Lightfield a Clay: plynulost hangáru stejná jako u Sky Posteru.
- Dvě zařízení v LAN s různými motivy: hra se chová stejně.
- Velikost IPA vzroste nejvýš o 10 MB.

## 12. Rizika

| Riziko | Dopad | Ošetření |
|---|---|---|
| Shadery nejde přeložit lokálně | chyba se ukáže až v CI | malé shadery, záložní cesta bez filtru, vlna 1 je má jen tři |
| 134 míst čte `UiTheme` | regres vzhledu Sky Posteru | vlna 0 je samostatná a nesmí změnit ani pixel |
| Různá šířka a účaří písem | přetékání, text mimo střed | tabulka velikostí na motiv, `FontTweak`, best-fit u popisu |
| 9-slice z generovaných textur | švy, nesouměrné rohy | rámy skládá Pillow z dlaždic, model kreslí jen materiál |
| Qwen-Image zkomolí slovo nebo nepoběží | chybí nápis | 4 seedy, výběr vlastníkem, záloha z TTF |
| Kontrast na cizí aréně | nečitelný nadpis | brána počítá s nejtmavší i nejsvětlejší arénou; nový motiv musí projít |
| Konflikt s PR #57–#63 | ruční slučování `SettingsOverlay`, `UiKit` | D12; `HomeScreen` z #62 přejde na kit až po sloučení |
| Jazyky mimo latinku | písmo motivu nemá znaky | D8, `Loc.PickFont` |
| Velikost buildu | delší stahování | pozadí 1344 × 768 bez zvětšování, oříznutá písma, limit 10 MB |
| Podoba s konkrétní hrou | zamítnutí v App Review | pravidlo v §1, kontrola promptů i výsledků při `sheet` |
