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

## Výtvarné podklady a písma

```bash
ssh -N -L 18188:127.0.0.1:8188 tcloud     # L40S, profil D, okno přes qctl (požádat L40S session)
COMFY=http://127.0.0.1:18188 python3 tools/ui_themes/generate_theme_art.py render
python3 tools/ui_themes/generate_theme_art.py post     # klíčování, bezešvé dlaždice, bez GPU
python3 tools/ui_themes/generate_theme_art.py sheet    # out/sheets/<motiv>.jpg
python3 tools/ui_themes/generate_theme_art.py apply    # picks.json (nebo seed 0) -> Assets
.venv/bin/python tools/ui_themes/fetch_fonts.py        # statické řezy, latinka + latin-ext
```

`picks.json` = vybraný seed na asset (`"canopy.backdrop": 2`, `-1` = nepoužít).
Na L40S jen FLUX.1-schnell a Qwen-Image (Apache), nikdy FLUX-dev.
`compose_frames.py` (9-slice rámy z dlaždic) zatím není: rámy kreslí kód.
