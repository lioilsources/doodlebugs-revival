# Screenshoty a video pro obchody (předávka pro MacBook)

Status (2026-11-20): připraveno. Navazuje na `Prompts/33` (WP5). Snímky dělá
vlastník na MacBooku ze **skutečných zařízení**, ne ze simulátoru: hra páruje
přes Multipeer a Nearby, simulátor ukáže jen cvičný let.

## Z jakého buildu

Testovací build z větve `fix/store-submission` (TestFlight / Firebase), nebo
až tag `v2.12.0`. Starší buildy mají zamčené barvy a „Premium · soon“, ty do
obchodu nepatří.

## Co vyfotit (5 snímků, na šířku)

| # | Obrazovka | Na co dát pozor |
|---|---|---|
| 1 | Úvodní obrazovka s tlačítkem PRACTICE | motiv Sky Poster, bez systémových dialogů |
| 2 | Hangár, záložka WEAPON | tři karty, celé popisy, vybraná karta zvednutá |
| 3 | Hangár, záložka PLANE | výrazný tvar (drak, vosa), barvy pod ním |
| 4 | Souboj | dvě letadla blízko, výbuch nebo díra v terénu, mraky |
| 5 | Pódium po vyhraném běhu | dvě zařízení, jinak pódium nevznikne |

Tipy: snímek tlačítky (boční + hlasitost nahoru), stavový řádek iOS skrývá hra
sama. Pětku a čtyřku nejlíp hrát ve dvou, druhé zařízení drží druhý člověk.

## Zařízení a rozměry

| Obchod | Slot | Výsledný rozměr | Zdroj |
|---|---|---|---|
| App Store | iPhone 6,9" | 2868×1320 | libovolný iPhone s Face ID (19,5:9) |
| App Store | iPad 13" | 2732×2048 | iPad, jen když iPad projde testem (plán 33 R5) |
| Google Play | telefon | 1920×1080 | Android telefon; poměr 20:9 se ořízne, viz `--fit pad` |

## Úprava

Snímky ulož mimo repo, např. `~/Desktop/doodlebugs-shots/{iphone,ipad,android}/`,
pak v klonu repa:

```bash
python3 tools/store/fit_screenshots.py ~/Desktop/doodlebugs-shots/iphone/*.PNG --target iphone69
python3 tools/store/fit_screenshots.py ~/Desktop/doodlebugs-shots/ipad/*.PNG --target ipad13
python3 tools/store/fit_screenshots.py ~/Desktop/doodlebugs-shots/android/*.png --target play --fit pad
python3 tools/store/fit_screenshots.py --play-art      # ikona 512 a feature graphic 1024×500
```

Výstup je v `tools/store/out/<slot>/` (gitignorováno). Skript napíše, kolik
procent ořízl; nad 5 % zkontroluj, že tlačítka u okraje zůstala, jinak `--fit pad`.

## Video

- **Pro recenzenta** (povinné kvůli dvěma zařízením): do 2 minut, obě zařízení
  v záběru, spuštění, spárování, READY, kus souboje. Nahrát jako neveřejné
  (YouTube unlisted nebo odkaz na soubor) a odkaz vložit do poznámek
  v `marketing/appstore.md`.
- **App preview** (nepovinné): do 30 s, nahrávání obrazovky z jednoho iPhonu.

## Kam to patří

- App Store Connect: verze 2.12, sekce snímků podle slotů.
- Play Console: Hlavní záznam v obchodě, grafika a snímky telefonu.
- Do repa nic z toho necommitovat; `marketing/*.md` jen odkaz na video.
