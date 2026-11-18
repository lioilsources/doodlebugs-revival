# Arzenál — zbraně, gadgety, bedny a čitelnost střel (plán implementace)

Status (2026-11-18): **plán, neimplementováno.** Vlastník téhož dne rozhodl
D3 (odhození ano) a D5 (Repair zůstává); ostatní rozhodnutí v §2 mají výchozí
hodnotu. Implementuje Opus po vlnách (§10). Navazuje na `Prompts/28`
(letový model: `BoostSpeed`, výkrut) a `Prompts/30` (karty draftu); první vlna
se dá vydat bez obou.

Zadání vlastníka: výběr zbraní a vizualizace nábojů nejsou dotažené; inspirovat
se hrami jako Worms; říct, co doplnit, co může fungovat a co zahodit; dnes jsou
skoro jen střelné zbraně — přidat jiné typy, boosty apod.

„Vizualizaci nábojů“ čtu dvojím způsobem a plán řeší obojí: **jak střela vypadá
a ohlašuje se za letu** (§6) a **jak HUD ukazuje nabití a počet nábojů** (§7).

ROADMAP: obsah pro fázi 1 (hratelnost sólo), druhý slot z fáze 4 (loadout),
prezentace z fáze 2.

## 0. Zjištění z kódu (stav `e6d025a`)

- **Osm zbraní, jedno sloveso.** Všechno je parametrická varianta téže střely
  vypálené dopředu (`WeaponProfile.cs:75-195`). Jiné sloveso mají jen Bomb
  (pustit) a Mine (nechat číhat); Twin MG a Heavy Flak jsou táž zbraň s jinými
  čísly.
- **Twin MG zabírá místo v draftu** (`DraftPool:237-241`), přestože je to
  druhý stupeň MG z bedny.
- **Bedna se zbraní je v pěti případech z osmi k ničemu.**
  `Shooting.UpgradeWeaponTier:214-224` nedělá nic, když `UpgradesTo == null`
  (Bomb, Sniper, Rocket, Mine a oba druhé stupně). Hráč slyší pickup a nic se
  nestane; bedna tvoří 20 % dropů (`PowerUpManager.TypeWeights`).
- **Druhý slot je hotový od spouště po RPC, ale prázdný**
  (`Shooting.NetWeaponId2`, `ShootServerRpc(..., slot)`, druhá spoušť v
  `TouchControls`). Nic ho neplní.
- **Odhození neexistuje.** `PlayerController.movePlane:756` každý krok přepíše
  rychlost na `transform.right * speed`, takže výbuch umí jen ubrat HP
  (`Bullet.Explode:503-528`). Země zabíjí okamžitě (`OnTriggerEnter2D:1067-1074`),
  ale nikoho do ní nejde strčit — a kdyby šlo, kill by nikdo nedostal: větev
  Ground volá `HandleDeathAndRespawn`, ne `HandleCombatDeath`.
- **Srážka zabije oba** (`:1076-1083`) — žádný kontaktní boj.
- **Power-upy padají jen z mrtvých** (70 %, max 5, `PowerUpManager.cs`), nikdy
  jako událost, o kterou se bojuje. Čtyři z pěti jsou pasivní doplnění; nic se
  neaktivuje tlačítkem.
- **Prezentace:** výstřel nemá záblesk ani zpětný ráz; bomba, mina ani sniper
  se neohlašují; vlastní a cizí střely vypadají stejně; pickup má jen zvuk
  (`PowerUp.PlayPickupFxClientRpc:181-184`) — proti zásadě čtyř kanálů;
  ikony zbraní jsou tmavé siluety na tmavém disku spouště i tmavé kartě.
- **Draft losuje klient** (`GameHUD.BuildWeaponCards:1697-1717`) a server
  přijme jakékoli id menší než `WeaponProfile.Count`
  (`Shooting.ServerSetSelectedWeapon:249`). Jakmile v registru přibudou
  speciály z beden, musí server kontrolovat, že id je draftovatelné.
- **Vstup zná jen „stisknuto“.** Dotyk hlásí stisk každý snímek (hold), desktop
  jen hranu. Nabíjení a odpálení na puštění potřebují „drženo“ ve všech
  providerech.
- **Skript na procedurální WAVy v repu není** (`tools/audio` má jen hudbu);
  ikony generuje `tools/weapons/generate_icons.py` (Pillow, bez GPU).

## 1. Co si vzít z Worms-like her (principy, ne obsah)

| Princip | Co znamená tady |
|---|---|
| Každá zbraň je jiné **sloveso**, ne jiná čísla | pustit, navést, odpálit na dálku, střílet dozadu, nabít, odhodit, uhnout |
| **Očekávání** je víc než zásah | rozbuška, odpočet, pípání, stín dopadu, zaměřovací paprsek |
| Fyzika je pointa: **odhození zabíjí víc než damage** | naše „voda“ je země, vesmír (zhasne motor), mina a cizí bomba |
| **Vzácnost**: málo nábojů, bedna z nebe | zásobovací shoz, o který se perou všichni; speciál na 1–3 rány |
| Terén je munice i úkryt | už máme (krátery, mraky) — zbraně s nimi mají počítat |
| Kolo nesmí vyšumět na čas | náhlá smrt: aréna se zmenšuje |
| Osobnost | jméno, vlastní zvuk, jednořádková hláška v kill feedu |

Nepřebírat: tahovost, inventář desítek položek v mřížce, míření úhlem a silou s
větrem (v reálném čase na telefonu neunesitelné) a cokoli konkrétního — jména,
zbraně, vzhled. Všechny položky níže jsou vlastní návrh pro dogfight.

## 2. Rozhodnutí

| # | Otázka | Výchozí (doporučení) | Kdo |
|---|---|---|---|
| D1 | Struktura výzbroje | **Slot 1 ZBRAŇ + slot 2 GADGET + dočasný SPECIÁL z bedny** (nahradí zbraň na N ran). | vlastník |
| D2 | Twin MG a Heavy Flak | **Z draftu pryč, zůstávají jako Mk II.** Mk II dostane každá zbraň; bedna už nikdy není naprázdno. | doporučeno |
| D3 | Odhození výbuchem | **Ano**, včetně kreditu za kill při nárazu do země (`KillCause.Slammed`). | **rozhodnuto: ano** (vlastník, 2026-11-18) |
| D4 | Výchozí gadget, než bude draft gadgetů (plán 30) | **NITRO všem.** | doporučeno |
| D5 | Pickup Repair | **Zůstává** — postih ovladatelnosti se neruší, jen zjemňuje (plán 28 D3), takže Repair má dál smysl. Kanystr NITRO přibude jako šestý pickup vedle něj. | Repair **rozhodnuto** (vlastník, 2026-11-18); kanystr doporučeno |
| D6 | Sniper → LANCE (nabít držením, pustit) | **Ano**, ve vlně 2. | po testu |
| D7 | Zásobovací shoz | **Ano**, vlna 2: každých 35–50 s, nejvýš jeden, jde sestřelit. | vlastník |
| D8 | Náhlá smrt místo konce na čas | **Vlna 3, volitelně**: posledních 30 s klesá strop vesmíru. | vlastník |
| D9 | Barva vlastních střel | **Bílé jádro stopy u vlastních, barva hráče u cizích.** Vzhled elementu zůstává. | doporučeno |

## 3. Zbraně (slot 1)

HP letadla: 3 štít (regenerace) + 3 trup. Kolo = 3 killy.

| Zbraň | Verdikt | Sloveso | Čísla (dmg / cooldown / zvláštnosti) | Ohlášení | Mk II (bedna) | Vlna |
|---|---|---|---|---|---|---|
| MG | nechat | střílet | 1 / 0,4 | záblesk | TWIN MG (dnešní) | – |
| TWIN MG | **z draftu pryč** | – | – | – | – | 1 |
| FLAK | nechat | brokovnice | 5×1 / 0,8 / dosah krátký, odhození 3 | záblesk | HEAVY FLAK (dnešní) | 1 |
| AERO BOMB | nechat | pustit | 3 / 1,6 / r 3,5, odhození 12 | hvizd, značka dopadu, zaměřovač | CLUSTER: po výbuchu 4 pumičky (1 dmg, r 1,4) | 1–2 |
| ROCKET | nechat | rychle a rovně | 2 / 1,1 / r 1,6, odhození 7 | kouřová stopa | HEAVY: r 2,4, odhození 10 | 1 |
| MINE | nechat | nechat číhat | 3 / 2,5 / r 1,5, odhození 9 | po odjištění pulzující kruh, pípání do 4 u | TRIPLE: tři miny ve vějíři | 1–2 |
| SNIPER → **LANCE** | předělat | **nabít a pustit** | drž 0–0,9 s: 1→3 dmg, síla 1,6→3,2; cooldown 1,3 od výstřelu | při nabíjení paprsek viditelný všem | prostřelí první cíl i mrak | 2 |
| **STINGER** | nová | **střílet dozadu** | 1 / 0,3 / síla 0,9, ven z ocasu | záblesk vzadu | dvojitý | 1 |
| **AIRBURST** | nová | **odpálit na dálku** | 2 / 1,4 / drž = letí, pusť = výbuch r 2,6, odhození 10; sám po 2,2 s | kruh poloměru letí se střelou | r 3,4 | 2 |
| **SEEKER** | nová | **navést** | 2 / 1,8 / r 1,2; max 16 u/s, zatáčí 120 °/s, zámek v kuželu 50° do 25 u, 5 s | oběť: „!“ a tón; střelec: značka zámku | dva najednou | 2 |
| **SKIPPER** | nová, volitelná | odrazit a počkat | 3 / 2,0 / rozbuška 3 s s číslicí, odraz od země | číslice nad granátem | – | 3 |

Protihra je součást návrhu: SEEKER je pomalejší než nitro (17 u/s), zatáčí hůř
než letadlo (130–210 °/s) a **ztrácí zámek, když cíl vletí do mraku**; LANCE se
prozradí paprskem; AIRBURST letí pomalu; STINGER netrefí nic před sebou.
Elementy zůstávají čistě prezentační (Prompts/24 D5).

**Speciály z bedny** (omezené náboje, pak zpět na draftovanou zbraň):

| Speciál | Rány | Efekt | Vlna |
|---|---|---|---|
| BIG ONE | 1 | pomalá bomba: 4 dmg, r 7, odhození 18, siréna při pádu, velký kráter | 2 |
| SWARM | 1 | salva 5 mini-seekerů (1 dmg, r 0,8, 160 °/s, 4 s) ve vějíři 40° | 2 |
| EMP | 1 | puls r 12 kolem střelce: všem ostatním **zhasne motor** a 0,8 s nejde nahodit; raketám vypne tah | 2 |
| CARPET | 1 | šest bomb (2 dmg, r 2,2) po 0,2 s | 3 |
| VORTEX | 1 | 3 s gravitační studna r 8 táhne letadla i střely, pak výbuch r 3 | 3 |
| STORM | 1 | blesk do každého mraku: kdo se v něm schovává, bere 2 | 3 |

EMP je náš nejvlastnější kus: staví na mechanice pádu a restartu motoru,
kterou žádná jiná hra v žánru nemá.

## 4. Gadgety (slot 2) — nestřelné

Druhá spoušť, cooldown na stejném kruhu jako u zbraně, bez nábojů.

| Gadget | Typ | Efekt | Cooldown | Protihra | Vlna |
|---|---|---|---|---|---|
| **NITRO** | pohyb | 1,2 s na `BoostSpeed`; okamžitě nahodí zhaslý motor (ne ve vesmíru) | 7 s | široké zatáčky při boostu | 1 |
| **BARREL ROLL** | obrana | 0,45 s výkrut: střely a odhození procházejí; srážka letadel dál zabíjí | 5 s | načasovat druhou ránu | 1 |
| **THUNDERCLAP** | kontakt | rázová vlna r 3,5: odhození 11 a stočení ±40°, 0 dmg; **smaže cizí střely v dosahu** (miny, rakety, seekery) | 9 s | držet odstup | 1 |
| **RAM** | kontakt | 0,5 s výpad na `BoostSpeed`: při srážce umře jen soupeř, útočník ztratí 1 štít; dva výpady proti sobě = oba | 8 s | uhnout, minout = dlouhý cooldown | 3 |
| **FLIP** | pohyb | okamžitý obrat o 180°, rychlost na `CornerSpeed` | 5 s | předvídatelný | 3 |
| **SMOKE** | terén | tři obláčky za letadlem na 6 s; blokují střely jako mraky | 10 s | vlétnout dovnitř, AIRBURST | 3 |
| **BUBBLE** | obrana | 2 s pohltí veškeré poškození | 12 s | přečkat, odhodit | 3 |
| **DECOY** | klam | balon, na který se chytají seekery a boti; po zásahu práskne (1 dmg, r 2) | 10 s | – | 3 |

## 5. Pickupy, bedny, náhlá smrt

| Pickup | Verdikt |
|---|---|
| Health, Shield | nechat |
| Damage ×1,5 na 10 s | nechat; zviditelnit — větší a zářivější střely, chip s odpočtem už v HUD je |
| Repair | nechat (D5); vrací ovladatelnost na 100 % — po zjemnění postihu jde o 15 %, ne o 70 % |
| **NITRO kanystr** | nový: nabije gadget a dá 1,5 s boost |
| Weapon crate | **Mk II pro každou zbraň** (D2); už na Mk II → +1 štít a nabitý gadget |

Váhy dropu se šesti typy: Health 22, Shield 18, Repair 12, Damage 15,
Weapon 18, Nitro 15 (dnes 25 / 20 / 20 / 15 / 20). Repair klesá, protože
opravuje menší škodu než dřív.

**Zásobovací shoz (D7).** Jen ve fázi Battle: 35–50 s po začátku a po vyřešení
předchozího shozu. Bedna na padáku klesá 2 u/s z horního okraje na náhodném x,
zastaví se v polovině výšky a visí 25 s. Čtyři kanály: šipka na horním okraji
+ velký nápis SUPPLY DROP, siréna, haptika, řádek v kill feedu. Obsah: speciál
60 % (BIG ONE 25, SWARM 20, EMP 15), FULL REPAIR 20 %, Mk II 20 %. **Zásah
střelou bednu odpálí** (2 dmg, r 3, odhození 12) — kdo ji nestihne, může ji
soupeři vyhodit pod rukama.

**Náhlá smrt (D8, vlna 3).** 30 s před limitem se pás vesmíru začne snižovat
0,6 u/s do 55 % výšky. Každý klient posouvá svůj lokální `ArenaBounds.Space`
podle synchronizovaného času kola — žádný nový síťový objekt. Shozy se zastaví.

## 6. Čitelnost střel za letu

| Co | Jak | Kde |
|---|---|---|
| Záblesk a ráz při výstřelu | `EffectAssets.CreateBurst` v barvě elementu, 4 částice, 0,08 s; sprite ucukne o 2 px | `Bullet.OnNetworkSpawn` (poloha spawnu je ústí — žádné RPC navíc) |
| Čí je střela (D9) | jádro stopy bílé pro vlastní, jinak barva z `PlayerColorManager` | `Bullet.ApplyTrail` |
| Poloměr výbuchu | rozpínající se prstenec přesně na `ExplosionRadius` (`UiSprites.Ring` jako world sprite), 0,25 s | `EffectLibrary.SpawnExplosion` |
| Bomba | hvizd klesající výškou; značka dopadu na zemi pod předpokládaným místem | `Bullet.Update`, lokální vizuál |
| Zaměřovač pro střelce | 8 teček balistické křivky pod vlastním letadlem, když nese Bomb/BIG ONE/SKIPPER: `p(t) = p0 + v·t + ½·g·t²` | nový `Effects/AimGuide.cs`, jen vlastník |
| Mina | po `ArmDelay` pulzující kruh spouštěcího poloměru; pípání zrychluje s blížícím se nepřítelem | `Bullet`, `IsArmed` už existuje |
| LANCE | paprsek z `firePoint` po kurzu, sílí s nabitím | `Shooting.NetCharging` + `LineRenderer` |
| SEEKER | oběti „!“ u letadla a tón, střelci rámeček na cíli | `ClientRpc` cílený na oběť |
| AIRBURST | průhledný kruh r 2,6 letí se střelou | `Bullet.ApplyVisual` |
| Potvrzení zásahu | bílé ✕ v místě zásahu jen pro střelce + číslo poškození (modré štít, oranžové trup) | `Bullet.PlayHitFxClientRpc` dostane `shooterClientId`, `damage`, `shieldHit` |
| Pickup | záblesk v barvě typu, plovoucí text („+SHIELD“, „MK II“, „BIG ONE ×1“), Light haptika, puls chipu | `PowerUp.PlayPickupFxClientRpc` |

## 7. HUD: nabití a náboje

- Spoušť 1: ikona zbraně, kruh cooldownu (hotovo), **tečky nábojů** pod
  spouští u speciálu, šipka „MK II“ u vylepšené zbraně, u LANCE kruh ukazuje
  nabití místo cooldownu.
- Spoušť 2: ikona gadgetu a tentýž kruh (`GetCharge01(1)`).
- **Ikony jako odznaky.** Dnešní tmavé siluety nahradit stylem power-upů:
  plný barevný disk, bílý glyf, tmavá kontura. Barva podle kategorie — zbraň
  oranžová, gadget tyrkysová, speciál purpurová. `generate_icons.py --apply`
  přegeneruje všech osm a přidá nové; klíče = jména enumů malými písmeny.
- Data pro kartu draftu (kreslí plán 30): `Verb` (krátký štítek),
  `PreviewKind` (tvar dráhy pro animovaný náhled) a tři lišty 0–1
  (DMG, RATE, RANGE) — vše pole `WeaponProfile` / `GadgetProfile`.

## 8. Technika

**Kanál odhození** (D3), autoritou zůstává vlastník letadla:

```
// server
PlayerController.ServerApplyImpulse(Vector2 impulse, float spinDeg, ulong attackerClient, int attackerLocalIdx)
    if (planeStats.IsInvulnerable || planeStats.IsDodging) return;
    SetLastAttacker(...);  ApplyImpulseClientRpc(impulse, spinDeg);
// vlastník
_externalVelocity += impulse;  _spinKick += spinDeg;
// movePlane, motor běží:
rb.linearVelocity = transform.right * speed + _externalVelocity;
_externalVelocity = Vector2.MoveTowards(_externalVelocity, Vector2.zero, ImpulseDecay * dt);   // 14 u/s²
// motor zhaslý: rb.linearVelocity += impulse (rychlost se tam integruje)
// rotatePlane: _spinKick se rozpustí do kurzu během 0,2 s
```

Impuls 11 posune letadlo o `v²/2a` ≈ 4,3 u; 18 o 11,6 u. `Bullet.Explode` dá
každému letadlu do `ExplosionRadius × 1,6` impuls od středu s lineárním útlumem
(`WeaponProfile.Knockback`), i když už nedostane poškození.

**Kredit za náraz do země:** ve větvi Ground (`PlayerController.cs:1067-1074`)
— je-li platný poslední útočník v `KillAttributionWindow` (4 s), připsat kill
jako v `HandleCombatDeath` s `KillCause.Slammed`.

| Soubor | Změna |
|---|---|
| `Weapons/WeaponProfile.cs` | `WeaponType` jen přidávat: `Stinger = 8, Airburst = 9, Seeker = 10, Bomblet = 11, BigOne = 12, MiniSeeker = 13, Emp = 14, …`. Nová pole: `Verb`, `PreviewKind`, `Knockback`, `FireAngleOffset`, `MaxSpeed`, `HomingTurnRate`, `HomingConeDeg`, `HomingRange`, `RemoteDetonate`, `ChargeSeconds`, `ChargeDamage`, `SplitInto`, `SplitCount`, `IsDraftable`, `SpecialAmmo`. `UpgradesTo` u všech; `DraftPool` bez TwinMG. |
| `Weapons/GadgetProfile.cs` (nový) | `GadgetType` + registr: jméno, popis, `Cooldown`, `Duration`, ikona, `Verb`. |
| `Shooting.cs` | `NetGadgetId` (−1 = žádný; `SlotCount` = 2, když je gadget), `NetSpecialAmmo`, `NetCharging`. `SpawnBullet:185-195`: impuls = `dir × síla + vpřed × rychlost letadla` (dnes se rychlost přičítá ke směru střely — pro střelbu dozadu špatně). `ShootServerRpc`: slot 1 s gadgetem → `ServerUseGadget`. Server ověří `IsDraftable` v `ServerSetSelectedWeapon:249`. `UpgradeWeaponTier`: náhradní odměna, když není vyšší stupeň. Dobíjení: `GetCharge01` nad obecným `SlotCooldown(slot)`. |
| `Input/IInputProvider.cs` + providery | `bool GetShootHeld(int slot)` jako výchozí metoda (= `GetShootInput(slot)`); Desktop a Gamepad ji přepíšou na `isPressed`. Potřebují ji LANCE a AIRBURST (vlna 2). |
| `Bullet.cs` | `FixedUpdate`: navádění (zámek při startu, `Physics2D.OverlapPoint` na mrak = ztráta zámku), strop `MaxSpeed`; rozpad na `SplitInto`; `ServerDetonate()`; impulsy v `Explode`; vizuály z §6. |
| `PlayerController.cs` | kanál odhození; `BeginBoost(seconds)` (strop rychlosti = `BoostSpeed`, okamžité nahození motoru); větev Ground s kreditem; pravidlo RAM ve větvi Player (vlna 3); `ForceEngineOffClientRpc` pro EMP. |
| `PlaneStats.cs` | `NetDodging` (výkrut), `NetBubble`; `TakeDamage` je respektuje. `ApplyPowerUp`: `Nitro`, `Special`. |
| `PowerUpType.cs`, `PowerUp.cs` | přidat `Nitro = 5`, `Special = 6`, `FullRepair = 7`; `NetPayload` (id speciálu); režim shozu (padák, klesání, `IDamagable.Hit` → výbuch). Ikony mimo pole v prefabu načítat z `Resources/Sprites/PowerUps/powerup_<key>` — **prefab se nemění**. |
| `PowerUpManager.cs` | `TypeWeights` pro šest typů podle §5 (Repair zůstává); `ServerSpawnSupplyDrop()`. |
| `GameState/MatchManager.cs` | časovač shozu ve fázi Battle; náhlá smrt (vlna 3). |
| `KillCause.cs` | přidat `Slammed = 4`, `Rammed = 5`; řádky v `GameHUD.PushKillFeed`. |
| `UI/TouchControls.cs` | ikona a jméno slotu přes `Shooting.GetSlotIcon(slot)`; tečky nábojů; šipka Mk II. |
| `Bot/BotBrain.cs` | bez změny ve vlně 1 (slot 1 nemačká); gadgety a nové zbraně pro boty až s bojovým botem fáze 1. |
| `tools/weapons/generate_icons.py` | styl odznaku, nové klíče (zbraně, gadgety, speciály, `powerup_nitro`, bedna s padákem). |
| `tools/audio/generate_sfx.py` (nový) | procedurální 8-bit WAVy: `whistle, beep, lock, siren, emp, nitro, roll, clap, pickup_*`. Čistý Python, bez GPU. |

Pravidla, která platí dál: střely jsou `NetworkObject` přes spawn na serveru,
ids v enumech se jen přidávají, žádný nový síťový prefab (shoz je `PowerUp`,
pumička je `Bullet`), žádná změna scény ani prefabu. Kolizní matici Bullet ×
PowerUp ověřit v `ProjectSettings/Physics2DSettings.asset` — bez ní střela
bednu netrefí.

## 9. Měření a vyvážení

Eventy: `weapon_pick {weapon, offered}`, `gadget_pick`, `gadget_use {gadget}`,
`pickup {type}`, `supply_drop {payload, outcome: taken|shot|expired}`,
`kill {weapon, cause}`; `round_end` dostane zbraň a gadget.

Mantinely: žádná zbraň nad 35 % ani pod 5 % výběrů; `Slammed` aspoň 10 % killů
(jinak odhození nefunguje, zvednout impulsy); medián kola 60–120 s; shoz
sebraný ve ≥ 60 % případů. Vše herní je zdarma a v LAN dostupné všem (ROADMAP
§3.4).

## 10. Vlny

| Vlna | Obsah | Odhad |
|---|---|---|
| **1 — základ** | kanál odhození + `Slammed`; slot gadgetu; NITRO (všem, D4), BARREL ROLL, THUNDERCLAP v registru; Mk II pro všechny a bedna nikdy naprázdno; STINGER; NITRO kanystr jako nový pickup; záblesk, barva střel, prstenec výbuchu, čtyři kanály pickupu, zásahové ✕; ikony-odznaky; SFX skript | ≈ 5 dní |
| **2 — slovesa** | `GetShootHeld`; LANCE, AIRBURST, SEEKER; shoz + BIG ONE, SWARM, EMP + tečky nábojů; značka bomby, kruh a pípání miny, varování zámku, zaměřovač; draft gadgetů (s plánem 30) | ≈ 5 dní |
| **3 — volitelné** | RAM, FLIP, SMOKE, BUBBLE, DECOY; CLUSTER, TRIPLE, SKIPPER; CARPET, VORTEX, STORM; náhlá smrt | po datech z vlny 2 |

Jedna vlna = jeden PR.

## 11. Ověření

1. Kompilace přes Roslyn `csc` (postup z Prompts/27); editor headless neběží.
2. Odhození: THUNDERCLAP u země pošle soupeře do terénu → kill feed
   „A SLAMMED B“, skóre +1; neporanitelné letadlo po respawnu se nehne.
3. Bedna: s Rocket → HEAVY ROCKET do smrti; druhá bedna → +1 štít. Žádná
   zbraň nezůstane bez efektu.
4. STINGER: střela vyletí z ocasu a při 13 u/s letí dozadu, ne dopředu.
5. NITRO: zhaslý motor naskočí; ve vesmíru ne; kruh na druhé spoušti 7 s.
6. BARREL ROLL: střela během 0,45 s projde; srážka dvou letadel zabije oba.
7. SEEKER: cíl v mraku = raketa letí rovně; nitro jí uteče.
8. EMP: všem v r 12 zhasne motor, střelci ne; restart až po 0,8 s.
9. Shoz: sebrání dá speciál s tečkami nábojů; po poslední ráně se vrátí
   draftovaná zbraň; zásah střelou bednu odpálí.
10. Klient i host vidí totéž: záblesky, prstence, značky (ParrelSync / dvě
    zařízení). Pozdní příchozí vidí rozpracovanou minu s kruhem.
11. Výkon: SWARM + CLUSTER naráz = +9 síťových objektů; na telefonu bez
    propadu pod 16 ms p95.

## 12. Rizika

- **Odhození stojí na důvěře ve vlastníka** (ten si impuls aplikuje sám).
  Stejný model jako pohyb dnes; pro LAN partu přijatelné, pro online (fáze 7) ne.
- **Chaos.** Čtyři hráči × gadgety × shoz. Mantinel: nejvýš jeden shoz, jeden
  speciál na hráče, cooldowny ≥ 5 s; ladit podle `gadget_use` za minutu.
- **Hold vs. stisk.** LANCE a AIRBURST mění význam držení spouště; na desktopu
  druhý stisk = odpálení. Ověřit na klávesnici i gamepadu.
- **Boti.** Do bojového bota nové zbraně neumí; ve warm-upu to nevadí, pro
  sólo režim je to práce navíc (fáze 1).
- **Mraky a respawn.** SMOKE vyrábí mraky, na kterých `CloudManager` respawnuje
  — proto až vlna 3 a s příznakem „není místo respawnu“.
- **Rozsah.** Vlna 1 sama odpovídá na zadání („jiné typy, boosty“); vlny 2 a 3
  se dají škrtat po položkách bez přepisu základu.
