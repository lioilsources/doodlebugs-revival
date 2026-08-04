# Pseudo-ads: prompty a pravidla

## Právní pravidla (platí pro každý nový inzerát)

1. Jen **celé vymyšlené názvy**. Žádné reálné značky ani „o písmenko vedle"
   parodie (Coca-Corla, McDoodle's…) — confusing similarity je přesně to, co
   žaloby vyhrává.
2. Žádná reálná loga, trade dress (charakteristické barevné kombinace, tvary
   lahví, maskoti), žádné reálné osoby.
3. Žádný tabák, alkohol, gambling — drží nízký App Store / Play rating.
4. Fonty: Press Start 2P i Alfa Slab One jsou OFL (licence vedle .ttf) — OK.
5. Do kreditů: **"All in-game brands are fictional."**
6. Před přidáním nového názvu: rychlý web search, jestli identický název
   neexistuje jako známá známka v herním/potravinovém segmentu.
7. **Vyřešené dluhy** (nechávám zapsané jako varování, co hlídat u nových map):
   `Pulparindo_fg.png` byla fotka reálného obalu (De la Rosa) → nahrazeno
   vlastním DESERT DEW billboardem; `Smart_wings.png` byla fotka letadla
   s logem Smartwings → nahrazeno vygenerovaným DOODLE AIR. **Každý nový
   background/foreground z fotky projdi na loga a trade dress dřív, než se
   dostane do buildu.**

## Pipeline

Pořadí je závazné — každý krok přepisuje výstup předchozího:

```bash
cd tools/ads
python3 generate_ad_signs.py         # pixel cedule + mantinelové panely → sprites/
python3 generate_ad_props.py         # plechovky, lahve, telefon, auto, diamant… → sprites/
$COMFY_PY generate_print_ads.py      # SDXL malby + typografie PŘEPÍŠOU ad_*.png
python3 generate_sunny_beach_fg.py --apply       # nový strip pro pláž
python3 generate_pulparindo_replacement.py --apply  # mega billboard místo obalu
python3 compose_foreground_ads.py --apply        # zapeče vše do 4 stripů v Assets
# Unity: Doodlebugs → Sync Background Profiles
```

`$COMFY_PY` = `/Volumes/YOTTA/Documents/ComfyUI/.venv/bin/python3` (má torch+MPS).
Bez `--apply` jde všechno jen do `tools/ads/out/` jako preview.

Gitignored (regenerovatelné): `sprites/` (vstupy kompozice), `art/` (cache
SDXL maleb), `base/` (originály stripů), `out/` (náhledy). Do repa jde jen
kód, brands.json, fonty a hotové stripy v Assets.

**Velké rendery na SPARKu**: `spark_generate.py` volá ComfyUI API na
`192.168.88.66:8188` (DGX GB10) — enqueue `/prompt` → poll `/history/{id}` →
download `/view`, vzor podle `Ol1nLLM/backend/comfyui`. Použito na 4096×2732
DOODLE AIR background; Mini by takové rozlišení neutáhl.

Vlastnosti zděděné z foreground stacku zdarma: nekonečný scroll, letadla
létají ZA cedulemi, kulky je rozbíjejí po 100×100px dlaždicích
(ForegroundTile), AoE zbraně v nich dělají krátery. Nic se nesíťuje — stripy
jsou lokální vizuál, deterministické díky seedům v brands.json.

Originály stripů se při prvním běhu archivují do `tools/ads/base/` a každá
kompozice startuje z nich (idempotentní; re-run = stejný výsledek).

## AI-gen prompty

Print vizuály už generuje `generate_print_ads.py` automaticky (prompty per
značka jsou přímo v něm, ART_PROMPTS). Templaty níže jsou pro ruční práci
v Midjourney/DALL·E nebo pro nové značky — výstup ulož jako
`tools/ads/sprites/ad_<id>.png` v rozměrech z brands.json a kompozitor ho
vsadí beze změny.

**Proč se text sází zvlášť:** difuzní modely neumí pravopis. Malba se
generuje s „no text" + negative promptem na písmo, titulky pak dosadí Pillow
(Alfa Slab One). Stejná dělba jako výtvarník + sazeč v tiskárně.

```
Vintage 1920s hand-painted tin advertising sign, flat frontal view,
bold headline "<NAME>", smaller slogan "<SLOGAN>",
<PALETTE HINT> muted aged palette, distressed enamel texture with rust
specks at the corners, simple geometric art-deco border,
painterly, crisp silhouette, isolated on transparent background,
no real brand logos, no photographs, no watermarks, no people
```

Per-brand palette hinty:

| id | PALETTE HINT |
|---|---|
| doodle_cola | deep brick red panel, cream lettering, mustard accents |
| cloud_nine_gum | slate blue panel, cream lettering, pale sky-blue accents |
| piston_pete | near-black panel, mustard lettering, brick red accents |
| ace_academy | olive drab panel, cream lettering, mustard accents |
| turbulence_mutual | cream panel, navy lettering, brick red accents |
| goggles_sons | walnut brown panel, cream lettering, mustard accents |
| dirigible_express | slate blue panel, cream lettering, brass accents |
| hangar_hotel | mustard panel, near-black lettering, brick red accents |
| mountain_mule | walnut brown panel, cream lettering, mustard accents |
| sierra_sarsaparilla | slate blue panel, cream lettering, ice-blue accents |
| desert_dew | mustard panel, brick red lettering, cream accents |
| el_sombrero | brick red panel, cream lettering, brass accents |
| seagull_ice | cream panel, navy lettering, brick red accents |
| punctual_watches | near-black panel, mustard lettering, cream accents |
| propwash_flakes | slate blue panel, cream lettering, pale blue accents |

Neonové značky (griffon_motors, talkbox, glint_diamonds) mají vlastní template:

```
1920s Times Square rooftop NEON sign at night, dark steel panel with a border
of marquee light bulbs, glowing neon tube lettering "<NAME>" in <TUBE COLOR>,
smaller tube slogan "<SLOGAN>" in <SECOND COLOR>, soft neon glow halo,
flat frontal view, isolated on transparent background, no real brand logos,
no photographs, no people
```

| id | TUBE COLOR / SECOND COLOR |
|---|---|
| griffon_motors | electric cyan / hot pink |
| talkbox | hot pink / warm amber |
| glint_diamonds | ice white-blue / electric cyan |

Negativní kontrola po generaci: žádný output nesmí obsahovat rozpoznatelné
reálné logo/typografii (AI je ráda „inspirovaná" — zkontroluj hlavně cola/soda
motivy proti Coca-Cola script fontu a Pepsi globe).
