# Doodlebugs Revival – Stabilization and Improvements

## Goals

- Fix network inconsistencies (projectiles, hits, respawn).
- Simplify and clarify key scripts.
- Improve UX and dev productivity.

## Phase 0 — Quick Fixes (no design changes)

- Suppress per-frame logs in `Assets/Doodlebugs/Scripts/PlayerController.cs` (keep Debug.DrawRay, wrap `Debug.Log` under `#if UNITY_EDITOR`).
- Align class and file name in `Assets/Doodlebugs/Scripts/PlayerDebug.cs` (class is `PlaneDebug` → either rename class to `PlayerDebug`, or file to `PlaneDebug.cs`).
- Remove unused usings (e.g., `UnityEngine.UIElements` in `NetworkManagerUI.cs`).

## Phase 1 — Projectiles and Collisions (main source of desync)

- Set up bullet as a full `NetworkObject`:
  - In `Assets/Doodlebugs/Scripts/Shooting.cs` change flow: instead of `AddForceClientRpc()` instantiate and spawn bullet on server in `ShootServerRpc(position, rotation)` and apply force on server.
  - Choose server-authoritative position synchronization (add `NetworkTransform` to Bullet prefab, simulate physics on server).
- In `Assets/Doodlebugs/Scripts/Bullet.cs` process collisions only on server:
  - Start with `if (!IsServer) return;` in `OnTriggerEnter2D`.
  - On hit call `IDamagable.Hit(damage)` on target (server only), send explosion effect via `ClientRpc`.
  - Use `NetworkObject.Despawn()` instead of `Destroy(gameObject)`.

## Phase 2 — Player Damage and Respawn

- In `Assets/Doodlebugs/Scripts/PlayerController.cs` unify hit flow:
  - `Hit(int damage)` performs respawn/effect on server and informs clients via `ClientRpc` (avoid `GameObject.Find`, use serialized boundary references or play area coordinates in `NetworkVariables`).
  - Replace name-based checks (`"Left"`, `"Right"`, `"Space"`) with tags or dedicated `EdgeTrigger` scripts that call server (e.g., `TeleportPlayerServerRpc(side)`), server performs teleport and sync.

## Phase 3 — Enemy/Bird Spawning

- In `Assets/Doodlebugs/Scripts/GameManager.cs` in `SpawnBirdServerRpc` use `NetworkObjectSpawner.SpawnNewNetworkObject(birdPrefab, position)` and ensure `birdPrefab` is in `NetworkManager` → Network Prefabs.

## Phase 4 — Movement and Stability

- In `PlayerController` simplify movement:
  - Unify "forward" vs `transform.right` (currently conflict: `Vector3.left` in `Update()` vs `transform.right` in movement).
  - Use `Mathf.MoveTowards`/`SmoothDamp` for speed instead of manual `duration` + `SmoothStep`.
  - Limit `rb.angularVelocity` and introduce angle clamp to prevent extremes at low speed.

## Phase 5 — UI/UX

- `Assets/Doodlebugs/Scripts/NetworkManagerUI.cs`: add basic state (Host/Client/Disconnected), possibly short error messages for failed connections.

## Phase 6 — Cleanup and Tooling

- Add `CONTRIBUTING.md` (build/run, ParrelSync usage, code style) and `asmdef` for `Assets/Doodlebugs/Scripts` (faster compilation).
- Clarify `NetworkObjectSpawner` server check even outside `UNITY_EDITOR` (log warning/early return).

## Phase 7 — Tests (basic)

- PlayMode test: shoot → hit player → consistent respawn across clients.
- EditMode test: `DevMath.CalculateDirection`.

## Key Changes (brief examples)

- `Shooting.cs` – move instantiation to server:
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

- `Bullet.cs` – collision only on server + despawn:
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

- `PlayerController.cs` – avoid `GameObject.Find` in RPC, use serialized boundary points or server teleport RPC with position.

### To-dos

- [ ] Suppress per-frame Debug.Log in PlayerController
- [ ] Align PlayerDebug.cs and PlaneDebug class
- [ ] Move Bullet spawn to server in Shooting.cs
- [ ] Process bullet collisions only on server and despawn
- [ ] Unify Hit/respawn flow in PlayerController via server and RPC
- [ ] Replace name-based collisions with tags/EdgeTrigger and server teleport
- [ ] GameManager.SpawnBirdServerRpc use NetworkObjectSpawner
- [ ] Simplify and stabilize movement/rotation in PlayerController
- [ ] Extend NetworkManagerUI with state and error messages
- [ ] Extend server-checks in NetworkObjectSpawner outside editor
- [ ] Add CONTRIBUTING.md and asmdef for Scripts
- [ ] Add basic PlayMode/EditMode tests
