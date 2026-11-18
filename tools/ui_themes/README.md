# Motivy rozhraní — tokeny a výtvarné podklady

Podklady k plánu `Prompts/31-CLAUDE-PLAN-ui-themes.md`: deset motivů hangáru
(70. až 30. léta, vesmír, džungle, utopie) plus výchozí Sky Poster. Vzhled
ukazuje `Prompts/31-ui-themes-mockup.html`.

## Co tu je

| Soubor | K čemu |
|---|---|
| `tokens.py` | Paleta, písma, velikosti, tvar a přepínače stylu každého motivu. Zrcadlo budoucího `Scripts/UI/Kit/UiThemeCatalog.cs`: klíče a id musí sedět. |
| `prompts.py` | Katalog promptů pro FLUX schnell (vše bez textu) a Qwen-Image (jen nápis hry). |

```bash
python3 tools/ui_themes/tokens.py            # brána kontrastu, kód 1 při chybě
python3 tools/ui_themes/tokens.py -v         # všechny páry s poměrem
python3 tools/ui_themes/prompts.py           # seznam úloh + kontrola pravidel
python3 tools/ui_themes/prompts.py --theme canopy --prompts
python3 tools/ui_themes/prompts.py --json    # seznam úloh pro generátor
```

Oba skripty běží bez sítě a bez GPU, stačí systémový Python.

## Brána kontrastu

Unity na vývojovém Macu neběží, proto se čitelnost hlídá tady. Pro každý
motiv se počítá poměr WCAG: text karty a popis na ploše karty, štítek na
pruhu, nápis na hlavním tlačítku, aktivní záložka, text a čísla ve
skleněných štítcích a nadpis přímo na pozadí. Sklo a závoj se skládají přes
nejtmavší a nejsvětlejší arénu (Orbit, MistyPeaks).

Nový motiv nebo změna barvy musí bránou projít dřív, než se propíše do C#.

## Pravidla promptů

- Prompt pro schnell je jen kladný. FLUX při cfg 1 přitahuje i to, co prompt
  popírá; co na obrázku být nemá, patří do `negative` (v ComfyUI přes
  `NAGuidance`).
- Text nese jen nápis `DOODLEBUGS`. Žádný jiný řetězec rozhraní se do
  textury nepeče.
- Nálada doby nebo prostředí, nikdy konkrétní hra: žádná jména, loga,
  postavy ani typické rekvizity.
- Rozměry pro schnell drží obálku NIM (strany 768–1344 po 64).

## Co chybí

`generate_theme_art.py`, `compose_frames.py` a `fetch_fonts.py` popisuje plán
v §5 a §7. SPARK jen přes `sparkctl`.
