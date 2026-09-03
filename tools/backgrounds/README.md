# Arena backgrounds & foregrounds — SPARK pipeline

Generuje mapy pro `BackgroundManager` na SPARKu (DGX GB10, ComfyUI API na
`192.168.88.66:8188`). Výstupy mají rovnou rozměry, které `Doodlebugs → Sync
Background Profiles` očekává: background 4096×2732, foreground strip 4096 px
široký, výška ≤ 1280 px (= 12.8 wu, zhruba půl obrazovky na telefonu),
terén opřený o spodní hranu, průhledné nebe.

## Větve

| příkaz | model | co dělá |
|---|---|---|
| `bg` | FLUX.1-dev (fp8 cast) → 4x-UltraSharp → 4096×2732 | nové pozadí z promptu, `themes.py` × styl × seed |
| `restyle` | FLUX Kontext (fp8) → 4x-UltraSharp | přemaluje existující fotku z `Assets/…/Background` do stylu |
| `fg` | FLUX.1-dev → 4x-UltraSharp → RMBG-2.0 (alfa na SPARKu) | terénní cutout na bílé; lokálně: solidní zem, neviditelný wrap seam, crop na obsah, cap výšky |
| `sheet` | — | náhledy + `out/index.html` k výběru |
| `apply` | — | zkopíruje výběr do `Assets` pod jménem mapy (`<Name>.png` + `<Name>_fg.png`) |

```bash
python3 tools/backgrounds/spark_backgrounds.py bg                    # 17 témat × 6 stylů
python3 tools/backgrounds/spark_backgrounds.py restyle               # 6 fotek × 6 stylů
python3 tools/backgrounds/spark_backgrounds.py fg --styles painterly,papercut
python3 tools/backgrounds/spark_backgrounds.py fg --from-cache --seam blend   # přegeneruj stage B všem existujícím pásům
python3 tools/backgrounds/spark_backgrounds.py sheet && open tools/backgrounds/out/index.html
python3 tools/backgrounds/spark_backgrounds.py apply jungle__artdeco__s1101 --as Jungle --fg jungle__papercut__s1101
# Unity: Doodlebugs → Sync Background Profiles
```

`--dry-run` jen vypíše prompty. Hotové soubory se přeskakují, takže zabitá
dávka po restartu pokračuje (`--force` je přepíše). Dva joby jsou pořád ve
frontě, GPU nečeká na download.

`--from-cache` bere seznam jobů z nacachovaných `fg_raw/*_a.png` místo
součinu téma × styl × seed — jediný způsob, jak trefit id z dřívějších dávek
s `--seeds 2` nebo reseedem (`city__papercut__s1507`, `jungle__papercut__s1101`).

## Seam: `blend`, ne `fill` (2026-09-04)

**Používej `--seam blend`.** `fill` (FLUX Fill přemaluje 320px pás přes
wrap) vyrábí uprostřed každého pásu zřetelný sloupec cizího obsahu — bílý
flek v poušti, druhá věž v Gothamu, ledová jehla v Arktidě. Fill dostane
kontext jen z okrajů masky, takže si střed vymyslí. `blend` (256px
cross-fade lokálně, GPU dělá jen upscale + RMBG) je čistý na obou koncích:
žádný blob uprostřed a napojení smyčky bez schodu. Je i **4× rychlejší**
(~20 s/job vs ~83 s) — Fill sampler pass odpadá.

Pozor: `blend` mění výšku pásu (jiný roll → jiný „ground row“ v crop logice),
takže po přehození seam módu nečekej stejné rozměry.

## Prompty a styly

Vše je v `themes.py`: `THEMES[<id>]` má `bg` (pozadí — horizont v dolní
třetině, horní dvě třetiny klidné nebe, tam se lítá a je tam HUD) a `fg`
(terénní pás). `STYLES` je landscape-safe podmnožina `kStylePresets` z
Ol1nLLM (stejná id, bloky bez figurálních frází, jinak Flux sází do scén
lidi). `painterly` je domácí styl bez presetu.

Franšízová témata (`gotham`, `dreamhouse`, `villainlair`, `comiccity`,
`twinsuns`, `backyard`, `seafloor`) jsou **homage**: nálada, paleta,
architektura — žádná jména, postavy, loga ani signature rekvizity. Stejné
pravidlo jako v `tools/ads/PROMPTS.md`; reálné IP v App Store buildu je
takedown. Přejmenuj podle chuti, id je jen stem souboru.

## Paměť na SPARKu

Box sdílí 128 GB unified memory s LLM kontejnery (TensorRT-LLM, vLLM,
litellm) a ComfyUI si v LRU cache drží video modely z jiných jobů (~57 GB
RSS). Každá dávka proto začíná `POST /free` a FLUX.1-dev se načítá jako
fp8 (12 GB místo 24). Kontext checkpoint je fp8 už na disku.

Gitignored: `out/` (rendery, náhledy, galerie). Do repa jde jen kód a
vybrané mapy v `Assets`.
