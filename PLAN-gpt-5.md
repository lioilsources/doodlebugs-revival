# Doodlebugs Revival – Stabilizace a vylepšení

## Cíle

- Opravit síťové nekonzistence (projektily, zásahy, respawn).
- Zjednodušit a zpřehlednit klíčové skripty.
- Zlepšit UX a dev produktivitu.

## Fáze 0 — Rychlé opravy (bez změny designu)

- Utlumit per-frame logy v `Assets/Doodlebugs/Scripts/PlayerController.cs` (Debug.DrawRay ponechat, `Debug.Log` obalit pod `#if UNITY_EDITOR`).
- Sladit název třídy a souboru v `Assets/Doodlebugs/Scripts/PlayerDebug.cs` (třída je `PlaneDebug` → buď přejmenovat třídu na `PlayerDebug`, nebo soubor na `PlaneDebug.cs`).
- Odstranit nepoužité usingy (např. `UnityEngine.UIElements` v `NetworkManagerUI.cs`).

## Fáze 1 — Projektily a kolize (hlavní zdroj desync)

- Nastavit střelu jako plnohodnotný `NetworkObject`:
  - V `Assets/Doodlebugs/Scripts/Shooting.cs` změnit tok: místo `AddForceClientRpc()` instanciovat a spawnovat střelu na serveru ve `ShootServerRpc(position, rotation)` a aplikovat sílu na serveru.
  - Volit server-authoritativní synchronizaci pozice (přidat `NetworkTransform` na Bullet prefab, simulovat fyziku na serveru).
- V `Assets/Doodlebugs/Scripts/Bullet.cs` zpracovávat kolize pouze na serveru:
  - Začít `if (!IsServer) return;` v `OnTriggerEnter2D`.
  - Na zásah volat `IDamagable.Hit(damage)` na cíli (jen server), efekt výbuchu rozeslat přes `ClientRpc`.
  - Místo `Destroy(gameObject)` používat `NetworkObject.Despawn()`.

## Fáze 2 — Poškození a respawn hráče

- V `Assets/Doodlebugs/Scripts/PlayerController.cs` sjednotit zásahový tok:
  - `Hit(int damage)` provede na serveru respawn/efekt a skrze `ClientRpc` informuje klienty (vyhnout se `GameObject.Find`, používat serializované reference na hranice nebo koordináty hrací plochy v `NetworkVariables`).
  - Nahradit kontroly jmény (`"Left"`, `"Right"`, `"Space"`) za tagy nebo dedikované `EdgeTrigger` skripty, které volají server (např. `TeleportPlayerServerRpc(side)`), server provede teleport a sync.

## Fáze 3 — Spawnování nepřátel/ptáků

- V `Assets/Doodlebugs/Scripts/GameManager.cs` v `SpawnBirdServerRpc` použít `NetworkObjectSpawner.SpawnNewNetworkObject(birdPrefab, position)` a zajistit, že `birdPrefab` je v `NetworkManager` → Network Prefabs.

## Fáze 4 — Pohyb a stabilita

- V `PlayerController` zjednodušit pohyb:
  - Sjednotit „forward“ vs `transform.right` (aktuálně rozpor: `Vector3.left` v `Update()` vs `transform.right` v pohybu).
  - Použít `Mathf.MoveTowards`/`SmoothDamp` pro rychlost místo ručního `duration` + `SmoothStep`.
  - Omezit `rb.angularVelocity` a zavést clamp úhlů, aby nedocházelo k extrémům při nízké rychlosti.

## Fáze 5 — UI/UX

- `Assets/Doodlebugs/Scripts/NetworkManagerUI.cs`: přidat základní stav (Host/Client/Disconnected), případně krátké chybové hlášky pro neúspěšné připojení.

## Fáze 6 — Úklid a tooling

- Přidat `CONTRIBUTING.md` (build/run, ParrelSync usage, styl kódu) a `asmdef` pro `Assets/Doodlebugs/Scripts` (rychlejší kompilace).
- Zpřehlednit `NetworkObjectSpawner` kontrolu serveru i mimo `UNITY_EDITOR` (log warning/early return).

## Fáze 7 — Testy (základní)

- PlayMode test: vystřel → zásah hráče → konzistentní respawn napříč klienty.
- EditMode test: `DevMath.CalculateDirection`.

## Klíčové úpravy (stručné ukázky)

- `Shooting.cs` – přesun instanciace na server:
```csharp
[ServerRpc]
void ShootServerRpc(Vector3 pos, Quaternion rot)
{
    var go = Instantiate(bulletPrefab, pos, rot);
    var netObj = go.GetComponent<NetworkObject>();
    netObj.Spawn(true);
    go.GetComponent<Rigidbody2D>().AddForce(rot * Vector3.right * bulletForce, ForceMode2D.Impulse);
}
```

- `Bullet.cs` – kolize jen na serveru + despawn:
```csharp
void OnTriggerEnter2D(Collider2D other)
{
    if (!IsServer) return;
    var dmg = other.GetComponent<IDamagable>();
    if (dmg != null) dmg.Hit(1);
    DespawnWithFxClientRpc(transform.position);
    NetworkObject.Despawn();
}
```

- `PlayerController.cs` – vyhnout se `GameObject.Find` v RPC, použít serializované hraniční body nebo serverový teleport RPC s pozicí.

### To-dos

- [ ] Utlumit per-frame Debug.Log v PlayerController
- [ ] Sladit PlayerDebug.cs a třídu PlaneDebug
- [ ] Přesunout spawn Bullet na server ve Shooting.cs
- [ ] Zpracovat kolize střely jen na serveru a despawnovat
- [ ] Sjednotit Hit/respawn tok v PlayerController přes server a RPC
- [ ] Nahradit jmenné kolize za tagy/EdgeTrigger a server teleport
- [ ] GameManager.SpawnBirdServerRpc používat NetworkObjectSpawner
- [ ] Zjednodušit a stabilizovat pohyb/rotaci v PlayerController
- [ ] Rozšířit NetworkManagerUI o stav a chybová hlášení
- [ ] Rozšířit server-checky v NetworkObjectSpawner i mimo editor
- [ ] Přidat CONTRIBUTING.md a asmdef pro Scripts
- [ ] Přidat základní PlayMode/EditMode testy


