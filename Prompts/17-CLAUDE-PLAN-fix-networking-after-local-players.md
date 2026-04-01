# Plán: Revize systému lokálních hráčů (Couch Co-op)

## Pravidla dle uživatele

1. Lokální hráči vznikají **jen na desktopu**
2. Zjednodušení: lokální hráči vznikají **jen na hostu**
3. Mobilní zařízení: **žádní lokální hráči** - jen host nebo client
4. Analyzovat náročnost: desktop client se 4 lokálními hráči připojený k hostu se 4 lokálními hráči

---

## Současný stav architektury

### Co funguje dobře
- `LocalPlayerManager` správně vytváří `HybridInputProvider[4]` pro 4 hráče
- `PlayerController` rozlišuje `IsLocalPlayer` vs síťový hráč
- `ConnectionManager` používá `ConnectionApproval` pro kontrolu spawnu

### Identifikované problémy

1. **LocalPlayerManager běží i na clientech** - ale spawn selže (není server)
2. **Duplikace P1** - `ConnectionApproval` nastaveno až v `OnDiscoveryTimeout()`, ne při startu
3. **Timing race conditions** - P1 se povolí v `Start()`, ale síť ještě neběží
4. **Mobilní zařízení** - `LocalPlayerManager` se stále inicializuje (zbytečně)

---

## Fáze 1: Zjednodušení (jen host má lokální hráče)

### Změny v `LocalPlayerManager.cs`

| Změna | Popis |
|-------|-------|
| Přidat early-return | Pokud není desktop NEBO není host, singleton se neaktivuje |
| Odstranit spawn retry | Spawn jen po úspěšném `StartHost()` |
| Čistší lifecycle | Žádné povolování P1 v `Start()` |

```csharp
// V Awake():
if (IsMobilePlatform())
{
    Destroy(gameObject);
    return;
}

// V Start():
// NEPOVOLOVAT P1 automaticky - počkat na síť

// Nová metoda volaná z ConnectionManager:
public void OnHostStarted()
{
    if (!NetworkManager.Singleton.IsHost) return;
    EnablePlayer(0); // P1 se zapne až teď
}
```

### Změny v `ConnectionManager.cs`

| Změna | Popis |
|-------|-------|
| `ConnectionApproval = true` | Nastavit PŘED `StartHost()` |
| Volat `LocalPlayerManager.OnHostStarted()` | Po úspěšném startu hostu |
| Zjednodušit `ApproveConnection` | Host nikdy negeneruje auto-spawn |

---

## Rozhodnutí uživatele

- ✅ **Max 4 hráči celkem** - host může mít až 4 lokální hráče, client má 1 síťového hráče
- ❌ **Fáze 2 není potřeba** - žádná podpora pro 4+4 hráčů
- 📋 **Zatím jen plánování** - implementace později

### Cílový model

```
HOST (ClientId=0) - Desktop
├── P1 (LocalPlayerIndex=0) - vlastněn hostem, input z HybridInputProvider[0]
├── P2 (LocalPlayerIndex=1) - vlastněn hostem, input z HybridInputProvider[1]
├── P3 (LocalPlayerIndex=2) - vlastněn hostem, input z HybridInputProvider[2]
└── P4 (LocalPlayerIndex=3) - vlastněn hostem, input z HybridInputProvider[3]

CLIENT (ClientId=1) - Desktop nebo Mobile
└── Player (LocalPlayerIndex=-1) - vlastněn clientem, auto-spawn, input z InputManager
```

---

## Soubory k úpravě (Fáze 1)

| Soubor | Změny |
|--------|-------|
| `LocalPlayerManager.cs` | Early-return pro mobile, `OnHostStarted()`, odstranit auto-enable P1 |
| `ConnectionManager.cs` | Přesunout `ConnectionApproval = true` před `StartHost()`, volat `OnHostStarted()` |

---

## Ověření (Fáze 1)

1. Spustit jako host na desktopu
2. P1 by se měl automaticky vytvořit po `StartHost()`
3. Klávesy 2-4 přidají P2-P4
4. Spustit jako client - připojí se jako 1 síťový hráč
5. Na mobilu žádný `LocalPlayerManager` neběží

---

## TODO pro implementaci

- [ ] Refaktorovat `LocalPlayerManager` - early-return pro mobile
- [ ] Přidat `OnHostStarted()` metodu
- [ ] Upravit `ConnectionManager` - `ConnectionApproval` před `StartHost()`
- [ ] Otestovat host flow
- [ ] Otestovat client flow
- [ ] Otestovat mobile flow
