# Power-Up & Stats System - Implementation Plan

## Context

Hra momentálně funguje na principu "one-hit kill" - jedno zásah = smrt. Chceme přidat statistiky letadla (štít, zdraví, ovladatelnost, damage) a systém power-upů, které padají ze sestřelených letadel. Cílem je hlubší gameplay s možností recovery a strategickými rozhodnutími.

## Nové statistiky letadla

### PlaneStats.cs (nový NetworkBehaviour, připojený k PlaneHolder prefabu)

Všechny NetworkVariables s `WritePermission.Server` (server je autorita na damage i pickup):

| Stat | Typ | Default | Rozsah | Chování |
|------|-----|---------|--------|---------|
| Shield (štít) | `NetworkVariable<int>` | 3 | 0-3 | Absorbuje damage před zdravím. Pomalu se regeneruje po 5s bez zásahu (1 bod / 3s). |
| Health (zdraví) | `NetworkVariable<int>` | 3 | 0-3 | Odečítá se až po vyčerpání štítu. Smrt při 0. Neregeneruje se. |
| Handling (ovladatelnost) | `NetworkVariable<float>` | 1.0 | 0.3-1.0 | Multiplikátor na rotateSpeed. Klesá o 0.1 za každý bod damage. **Neregeneruje se přirozeně** - jen přes Repair power-up. |
| DamageMultiplier | `NetworkVariable<float>` | 1.0 | 1.0-2.0 | Multiplikátor na damage střely. **Časově omezený** - boost trvá 10s, pak reset na 1.0. |

### Damage boost timer (server-side v PlaneStats.Update)

- Když hráč sebere Damage power-up: `netDamageMultiplier += 0.5` (max 2.0), nastav `_damageBoostTimer = 10f`
- Každý frame na serveru: `_damageBoostTimer -= Time.deltaTime`
- Když timer vyprší: `netDamageMultiplier = 1.0f`
- Pokud hráč sebere další Damage power-up během aktivního boostu: reset timer na 10s, přičti 0.5 (max 2.0)

### Damage flow (server-side)

```
Bullet hits player
→ Bullet.OnTriggerEnter2D() volá damagable.Hit(bulletDamage)
→ PlayerController.Hit(damage):
    → PlaneStats.TakeDamage(damage):
        1. shield -= min(damage, shield), remaining = damage - absorbed
        2. health -= remaining
        3. handling -= 0.1 * celkový_damage, clamp na 0.3
        4. reset shield regen timer
        5. return (health <= 0)
    → pokud mrtvý: ScoreManager.AddDeath, PowerUpManager.SpawnPowerUp, HandleDeathAndRespawn, ResetStats
    → pokud živý: TriggerDamageFlash
```

### Respawn = plný reset všech stats

## Power-Up systém

### PowerUpType.cs (enum)
- `Health` - doplní 2 HP
- `Shield` - doplní 2 štítu
- `Repair` - obnoví handling o 0.3
- `Damage` - zvýší damage multiplikátor o 0.5 (max 2.0), **trvá 10s** pak se vrátí na předchozí hodnotu

### PowerUp.cs (NetworkBehaviour)

- Prefab: NetworkObject + NetworkTransform + Rigidbody2D (gravity=0.3) + CircleCollider2D (trigger) + SpriteRenderer
- Typ uložen v `NetworkVariable<int>` → určuje barvu/sprite
- `OnTriggerEnter2D` (server-only): detekuje Player tag → `PlaneStats.ApplyPowerUp(type)` → pickup FX → Despawn
- Self-destruct po 15s, blikání od 10s
- Tag: "PowerUp"

### PowerUpManager.cs (singleton)

- `SpawnPowerUp(Vector3 deathPos)` - server-only
- Drop chance: 70% (ne každá smrt = power-up)
- Weighted random: Health=30%, Shield=25%, Repair=25%, Damage=20%
- Max 5 aktivních power-upů ve světě
- Spawn jen při combat deaths (bullet, plane collision), NE při ground crash

### Registrace prefabu v NetworkPrefabsList.asset (nutné v Unity Editoru)

## Úpravy existujících souborů

### PlayerController.cs
- Přidat referenci na `PlaneStats` (GetComponent v OnNetworkSpawn)
- **Hit()**: nahradit instant-kill za PlaneStats.TakeDamage pipeline
- **rotatePlane()**: násobit rotateSpeed * planeStats.Handling
- **HandleDeathAndRespawn()**: přidat planeStats.ResetStats()
- **OnTriggerEnter2D()**: odebrat duplicitní bullet handling (Bullet.cs to řeší přes IDamagable), přidat PowerUp spawn u player collision
- **Přidat invulnerability timer** (2s po respawnu): `_invulnerabilityTimer`, check v Hit(), odpočet v FixedUpdate, blikání přes PlaneVisualEffects

### Bullet.cs
- Přidat `NetworkVariable<int> _damage` (default 1, server-write)
- Přidat `SetDamage(int)` metodu
- Změnit `damagable.Hit(1)` na `damagable.Hit(_damage.Value)`

### Shooting.cs
- **Fire rate cooldown**: 0.3s mezi výstřely (Novice=0.4, Advanced=0.3, Expert=0.25)
- V `ShootServerRpc`: číst PlaneStats.DamageMultiplier a nastavit `bullet.SetDamage()`

### PlaneVisualEffects.cs
- Invulnerability blink efekt (rychlé blikání sprite)
- Smoke/tint při nízké ovladatelnosti (subscribe na netHandling.OnValueChanged)
- Damage boost vizuální indikátor (záře/tint po dobu trvání boostu)

### GameHUD.cs
- Přidat shield bar (modrý) a health bar (červený) ke každému hráči
- Indikátor handling stavu (wrench ikona, červená při nízké ovladatelnosti)
- Ikona aktivního damage boostu s odpočtem (10s timer)
- **Žádné world-space bary** - stats jen v HUD panelu

## Speciální pravidla

| Situace | Damage | Power-up drop? |
|---------|--------|----------------|
| Zásah střelou | 1 × shooterDamageMultiplier | Ano (při smrti) |
| Srážka letadel | 999 (instant kill oba) | Ano |
| Náraz do země | instant kill | Ne |
| Out of bounds | instant kill | Ne |

## Implementační fáze

### Fáze 1: Stats základ (bez power-upů)
1. `PowerUpType.cs` - enum
2. `PlaneStats.cs` - NetworkVariables, TakeDamage, ResetStats, shield regen
3. `PlayerController.cs` - integrace PlaneStats, Hit() pipeline, handling factor, invulnerability, fix duplicitního bullet handling
4. `Bullet.cs` - variable damage
5. `Shooting.cs` - fire rate cooldown, damage multiplier

**Test**: Letadla přežijí více zásahů. Štít se regeneruje. Ovladatelnost klesá. Cooldown na střelbu.

### Fáze 2: Power-upy
6. `PowerUp.cs` - pickup logika, lifetime, vizuál
7. `PowerUpManager.cs` - spawn logika, type selection
8. `PlayerController.cs` - spawn power-up při smrti
9. Vytvořit PowerUp prefab + registrace v NetworkPrefabsList

**Test**: Smrt dropne power-up. Prolétnutí power-upu obnoví stats.

### Fáze 3: Vizuální feedback
10. `PlaneVisualEffects.cs` - blink, smoke, health bary
11. `GameHUD.cs` - stat bary v UI

## Moje doporučení pro balance

1. **Fire rate cooldown** (0.3s) - momentálně neomezená střelba, spray-and-pray je příliš silný
2. **Invulnerability po respawnu** (2s) - prevence spawn-campingu
3. **Handling se NEREGENERUJE přirozeně** - jen přes Repair power-up
4. **Damage boost časově omezený** (10s) - nutí hráče být agresivní během boostu
5. **Power-up lifetime 15s** - prevence zahlcení obrazovky
6. **Srážka letadel = instant kill** - zachovat dramatický moment, žádné stats nepomůžou
7. **Stats jen v HUD** - žádné world-space health bary, čistá herní plocha
8. **Budoucí rozšíření**: Speed power-up, fire rate power-up, speciální typy střel - odložit do pozdější fáze

## Verifikace

- Testovat s ParrelSync (host + client): ověřit synchronizaci stats přes síť
- Bullet hit: ověřit shield → health pipeline, handling degradation
- Respawn: ověřit plný reset stats + invulnerability blink
- Power-up: spawn na pozici smrti, pád gravitací, pickup, despawn po 15s
- Fire rate: ověřit cooldown mezi výstřely
- Edge cases: self-hit ochrana, couch co-op (localPlayerIndex)
