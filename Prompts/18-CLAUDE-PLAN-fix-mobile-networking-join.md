# Plán: Oprava síťových problémů lokálních hráčů

## Identifikované problémy

| # | Problém | Příčina | Priorita |
|---|---------|---------|----------|
| 1 | Mobile host nevytvoří hráče | `CreatePlayerObject = false` pro host, ale na mobilu není LocalPlayerManager | VYSOKÁ |
| 2 | MAX_PLAYERS = 4 je zastaralé | Má být 7 (4 lokální + 3 remote) | STŘEDNÍ |
| 3 | Mobile client: "Host disconnected" po pár sekundách | Pravděpodobně timeout/keepalive problém | VYSOKÁ |

---

## Problém 1: Mobile host nevytvoří hráče

### Současný stav
```csharp
// ConnectionManager.ApproveConnection()
bool isHostConnection = currentPlayers == 0 && NetworkManager.Singleton.IsServer;
response.CreatePlayerObject = !isHostConnection;  // false pro host
```

Na desktopu: `LocalPlayerManager.OnHostStarted()` spawne P1
Na mobilu: `LocalPlayerManager` neexistuje → nikdo nespawne hráče

### Řešení
Zkontrolovat, zda `LocalPlayerManager.Instance` existuje. Pokud ne (mobile), nechat NetworkManager auto-spawn.

```csharp
bool isHostConnection = currentPlayers == 0 && NetworkManager.Singleton.IsServer;
bool hasLocalPlayerManager = LocalPlayerManager.Instance != null;
// Desktop host: LocalPlayerManager spawne hráče
// Mobile host: NetworkManager auto-spawn
response.CreatePlayerObject = !isHostConnection || !hasLocalPlayerManager;
```

---

## Problém 2: MAX_PLAYERS

### Scénář
- Host desktop: až 4 lokální hráči
- Každý další client: 1 hráč (bez ohledu na platformu)
- Maximum: 4 + 1 + 1 + 1 = **7 hráčů**

### Řešení
```csharp
private const int MAX_PLAYERS = 7;
```

---

## Problém 3: Mobile client "Host disconnected"

### Symptomy
- Mobile se připojí jako client k desktop hostu
- Po několika sekundách se zobrazí "Host disconnected"
- Desktop host stále běží

### Možné příčiny
1. **Network timeout** - Unity Netcode má výchozí timeout ~10s
2. **Keepalive packets** - client neposílá heartbeat
3. **iOS background restrictions** - aplikace přechází do úsporného režimu
4. **NAT/Firewall** - blokuje UDP pakety po iniciálním handshake

### Diagnostika
Přidat logging do `OnClientDisconnected`:
- Zjistit `clientId` který se odpojil
- Zkontrolovat `NetworkManager.Singleton.DisconnectReason`

### Potenciální řešení
1. **Zvýšit timeout** v UnityTransport settings
2. **Zkontrolovat HeartbeatTimeout** - výchozí může být příliš krátký
3. **Debugovat na mobilním zařízení** - sledovat konzoli na mobilu

---

## Soubory k úpravě

| Soubor | Změna |
|--------|-------|
| `ConnectionManager.cs` | Fix `CreatePlayerObject` pro mobile host, změnit MAX_PLAYERS na 7 |
| `ConnectionManager.cs` | Přidat diagnostiku do `OnClientDisconnected` |

---

## Implementační kroky

### Krok 1: Fix mobile host (okamžitý)
```csharp
// V ApproveConnection():
bool isHostConnection = currentPlayers == 0 && NetworkManager.Singleton.IsServer;
bool hasLocalPlayerManager = LocalPlayerManager.Instance != null;
response.CreatePlayerObject = !isHostConnection || !hasLocalPlayerManager;
```

### Krok 2: Změnit MAX_PLAYERS
```csharp
private const int MAX_PLAYERS = 7;
```

### Krok 3: Diagnostika disconnect problému
```csharp
// V OnClientDisconnected():
Debug.Log($"[ConnectionManager] Client {clientId} disconnected, LocalClientId={NetworkManager.Singleton.LocalClientId}");
if (NetworkManager.Singleton.DisconnectReason != null)
{
    Debug.Log($"[ConnectionManager] Disconnect reason: {NetworkManager.Singleton.DisconnectReason}");
}
```

---

## Ověření

1. **Mobile host test:**
   - Spustit hru na mobilu jako host
   - Hráč by se měl automaticky vytvořit (auto-spawn)

2. **MAX_PLAYERS test:**
   - Host s 4 lokálními hráči
   - 3 další clienti by se měli připojit

3. **Disconnect diagnostika:**
   - Připojit mobile client k desktop hostu
   - Sledovat logy když dojde k odpojení
   - Analyzovat důvod odpojení

---

## Poznámky

- Problém 3 může vyžadovat další investigaci po přidání diagnostiky
- Unity Netcode má `UnityTransport` s konfigurovatelným `HeartbeatTimeoutMS` a `ConnectTimeoutMS`
- iOS má agresivní správu baterie - může uspávat síťová vlákna
