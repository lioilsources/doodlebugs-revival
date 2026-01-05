> zajimalo by me, jake parametry mam k dispozici, abych customizoval
- pohyb letadla
- zataceni
- zvysovani rychlosti
- automaticka zmena rychlosti
- zataceni v pripade vypnuteho motoru
- kdy se vypina motor
- kdy se zapina motor
- rychlost strely
- rychlost padu pri vypnutem motoru 

claude answer:
Zde je přehled všech parametrů, které můžeš customizovat:

  PlayerController.cs

  | Parametr            | Řádek | Výchozí hodnota | Popis                                      |
  |---------------------|-------|-----------------|--------------------------------------------|
  | rotateSpeed         | 14    | 200f            | Základní rychlost zatáčení                 |
  | defaultSpeed        | 16    | 5f              | Počáteční rychlost letadla                 |
  | maxSpeed            | 17    | 20f             | Maximální rychlost                         |
  | minSpeed            | 18    | 2f              | Minimální rychlost (pod ní se vypne motor) |
  | climbDrag           | 19    | 1f              | Jak rychle klesá rychlost při stoupání     |
  | diveBoost           | 20    | 3f              | Jak rychle roste rychlost při klesání      |
  | maxGravity          | 21    | 0.5f            | Maximální gravitace při vypnutém motoru    |
  | gravityIncreaseRate | 22    | 0.35f           | Jak rychle gravitace narůstá               |

  Kdy se motor vypíná/zapíná:

  - Vypnutí motoru: Když speed <= minSpeed (řádek 201) nebo při vstupu do zóny "Space"
  - Zapnutí motoru: Při střemhlavém letu (rotation mezi -0.8 a -0.6, řádky 174-178)

  Zatáčení při vypnutém motoru:

  - Řádek 248-249: rotateSpeed * 4f - zatáčení je 4x rychlejší než normálně

  Shooting.cs

  | Parametr    | Řádek | Výchozí hodnota | Popis                |
  |-------------|-------|-----------------|----------------------|
  | bulletForce | 12    | 20f             | Základní síla střely |

  Celková rychlost střely = bulletForce + planeSpeed (řádek 54)

> chtel bych definovat 3 levely maturity pro ovladani a pohyb letadla:
- expert: tak jak je ted, zvys rychlost strely 2x
- advanced: sniz rychlost otaceni o polovinu, sniz gravitaci o polovinu, sniz gravityIncreaseRate o polovinu, zvys 
rychlost strely 1x, nahozeni motoru o 10 stupnu vetsi rozsah na obe strany
- novice: sniz maximalni rychlost o polovinu, sniz rychlost otaceni 1x, sniz gravitaci 1x, sniz gravityIncreaseRate 1x,
 zachovej rychlost strely, nahozeni motoru o 25 stupnu vetsi rozsah na obe strany

Vymysli, jak snadno testovat vsechny 3 levely (tzn z editoru muzu nastavit, jaky profil bude vychozi)
- pozdeji se budou levely automaticky prepinat po dosazeni poctu zasahu: novice - default; advanced - po 10ti zasazich;
 expert = po 20ti zasazich 

 claude changes:
 Hodnoty profilů

  | Parametr              | Expert       | Advanced       | Novice         |
  |-----------------------|--------------|----------------|----------------|
  | rotateSpeed           | 200          | 100            | 50             |
  | maxSpeed              | 20           | 20             | 10             |
  | maxGravity            | 0.5          | 0.25           | 0.125          |
  | gravityIncreaseRate   | 0.35         | 0.175          | 0.0875         |
  | bulletForceMultiplier | 2x           | 1x             | 1x             |
  | engineRestart rozsah  | -0.8 to -0.6 | -0.85 to -0.53 | -0.91 to -0.41 |