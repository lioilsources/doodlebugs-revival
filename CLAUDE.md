# CLAUDE.md

Instructions for Claude Code when working in this repository.

## Project

2D multiplayer arcade air combat game. Unity 6000.2.9f1. WWI biplanes. Physics-based movement, one-hit kill, WiFi/couch multiplayer.

## Tech Stack

- Unity 6000.2.9f1
- Netcode for GameObjects 1.14.1
- Unity Input System 1.7.0
- Platforms: Desktop (Win/macOS), iOS, Android

## Key Commands

**Run the game:**
1. Open in Unity 6000.2.9f1
2. Scene: `Assets/Doodlebugs/Scenes/Scene01.unity`
3. Play → Start Host

**Multiplayer testing:** Unity menu → ParrelSync → Clone Manager → open clone → Start Client

**Logs (macOS):** `~/Library/Company/Project/Player.log`

## Architecture

**Networking:** Host/Client topology. Client authority for movement, server authority for collisions and damage.

```
Input (Owner) → ShootServerRpc → SpawnBulletClientRpc → all clients instantiate
```

**Ownership pattern:**
```csharp
void Update() {
    if (!IsOwner) return;
    // input only
}
```

**Server authority pattern:**
```csharp
void OnTriggerEnter2D(Collider2D col) {
    if (!IsServer) return;
    // validate collision
}
```

**Spawning/despawning network objects:**
```csharp
NetworkObjectSpawner.SpawnNewNetworkObject(prefab, pos, rot);  // server only
NetworkObjectDespawner.DespawnNetworkObject(networkObject);
```

All network prefabs must be registered in `Assets/Doodlebugs/Prefabs/NetworkPrefabsList.asset`.

## Key Files

| File | Purpose |
|---|---|
| `Scripts/PlayerController.cs` | Movement, collision handling |
| `Scripts/Shooting.cs` | Bullet spawning (ServerRpc → ClientRpc) |
| `Scripts/Bullet.cs` | Projectile, hit detection |
| `Scripts/GameState/GameSetup.cs` | Game flow, singleton access |
| `Scripts/Network/ConnectionManager.cs` | Host/client connect flow |
| `Scripts/Input/` | Multi-platform input (desktop/gamepad/gyro/touch) |
| `Scripts/Camera/` | Dynamic screen size, boundaries |
| `Scripts/ForegroundScroller.cs` | Infinite parallax scroll (two sprite copies, loops left) |
| `Scripts/ForegroundTile.cs` | Single destructible tile — disappears on bullet hit |
| `Scripts/ForegroundSpriteGenerator.cs` | Generates placeholder foreground sprite at runtime from alpha |

## Code Rules

- Always `if (!IsOwner) return;` before input handling
- Always `if (!IsServer) return;` before collision/damage logic
- Use `NetworkObjectSpawner` / `NetworkObjectDespawner` — never `Instantiate`/`Destroy` directly on networked objects
- No `SendMessage()` — use direct calls or events
- No hardcoded collision strings — prefer tags or layers
- Test multiplayer changes with ParrelSync before committing

## Foreground / Parallax Destruction Layer

- Foreground scrolls infinitely left via `ForegroundScroller` (two sprite copies swapped)
- Each background has its own foreground sprite; tiles are 100×100 px
- Planes fly **behind** the foreground (render order), bullets collide with it
- On bullet hit: `ForegroundTile` destroys itself (local, non-networked — visual only)
- Colliders are `BoxCollider2D` auto-generated per tile from sprite alpha
- `ForegroundSpriteGenerator` creates a runtime silhouette if no sprite is assigned

## Known Issues

- Bullet/Cloud collision de-sync: sometimes explodes on one screen but not the other (local `Destroy` on `Bullet.cs`)
- Legacy `SendMessage()` still used in boundary collision (PlaygroundLeft/Right)
