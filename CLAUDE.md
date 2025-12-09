# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Doodlebugs Revival is a 2D multiplayer arcade air combat game built with Unity 2022.3.4f1 and Unity Netcode for GameObjects. Players control WWI-era biplanes in a physics-based flying shooter with network synchronization.

## Development Commands

### Running the Game
1. Open the project in Unity 2022.3.4f1
2. Load scene: `Assets/Doodlebugs/Scenes/SampleScene.unity`
3. Press Play in the Unity Editor
4. Click "Start Host" (first player) or "Start Client" (additional players)

### Multiplayer Testing
- Use **ParrelSync** to clone the project and run multiple editor instances simultaneously
- Each instance can run as host/client without rebuilding
- Access via: Unity menu → ParrelSync → Clone Manager

### Debugging
- Player logs (macOS): `~/Library/Company/Project/Player.log`
- Use `Debug.Log()` for console output
- Use `Debug.DrawRay()` for visual debugging in Scene view
- Monitor network traffic via Unity Multiplayer Tools window

### IDE Setup
- Generate .csproj files: Unity → Preferences → External Tools → Regenerate project files
- VSCode configured in `.vscode/` directory
- Recommended extensions: Unity debugger, C# support

## Architecture

### Networking Model

**Host/Client topology** using Unity Netcode for GameObjects v1.5.2:
- **Mixed Authority**: Client controls input/movement, server validates collisions/damage
- **RPC Pattern**:
  - `[ServerRpc]` - Client → Server (e.g., shoot request, spawn request)
  - `[ClientRpc]` - Server → All Clients (e.g., visual effects, physics sync)
- **Ownership Checks**: Always check `IsOwner` before processing input, `IsServer` before validation

### Network Flow Example
```csharp
// Client detects input
ShootServerRpc()  // Client → Server

// Server validates and broadcasts
AddForceClientRpc()  // Server → All Clients

// All clients execute
Instantiate bullet + apply force
```

### Key Systems

**1. Player Control** (`PlayerController.cs`)
- Physics-based movement: constant forward velocity (`transform.right * speed`) + rotational steering
- Dual rotation system: body uses physics angular velocity, sprite visual rotation controlled separately
- Engine state machine: speed < 2 = engine off (gravity pulls down), speed maintained = engine on
- Server-authoritative collision detection in `OnTriggerEnter2D()`

**2. Shooting System** (`Shooting.cs`)
- Owner-only input handling (checks `IsOwner`)
- ServerRpc validates shoot request
- ClientRpc broadcasts bullet spawn to all clients
- Each client instantiates and applies force locally

**3. Network Object Lifecycle**
- Use `NetworkObjectSpawner.SpawnNewNetworkObject(prefab, position, rotation)` for server-side spawning
- Use `NetworkObjectDespawner.DespawnNetworkObject(networkObject)` for cleanup
- All network prefabs must be registered in `Assets/Doodlebugs/Prefabs/NetworkPrefabsList.asset`

**4. Physics & Collision**
- 2D Rigidbody-based physics with trigger colliders
- Collision types:
  - Player-Bullet: respawn with explosion
  - Player-Player: respawn with explosion
  - Player-Space region: disable engine, start falling
  - Player-Left/Right boundaries: wrap to opposite side
- Pattern: Always `if (!IsServer) return;` at start of `OnTriggerEnter2D()` for server authority

### Critical Classes

**PlayerController.cs** - Main player plane control
- Line 10: TODO annotations for Unity Editor
- `rotatePlane()`: Uses cross-product math with control points for steering angle
- `movePlane()`: Applies velocity, manages engine state transitions
- `OnTriggerEnter2D()`: Server-authoritative collision handling with hardcoded string/tag checks

**Shooting.cs** - Bullet spawning and shooting
- Owner-only input in `Update()`
- Network flow: `ShootServerRpc()` → `AddForceClientRpc()`
- Debug: S key spawns test birds via `SpawnBirdServerRpc()`

**GameManager.cs** - Singleton network manager
- Extends `SingletonNetwork<T> : NetworkBehaviour`
- Holds prefab references for dynamic spawning
- Access via `GameManager.Instance`

**NetworkObjectSpawner.cs** - Network spawning helper
- Static utility class with overloaded spawn methods
- Always call from server-only code
- Pattern: Instantiate → Get NetworkObject → Call Spawn(destroyWithScene)

**Bullet.cs** - Projectile behavior
- Hit detection in `OnTriggerEnter2D()`
- Currently uses local Destroy (potential de-sync issue noted in README)
- Tracks shooter and target client IDs (not fully implemented)

### Code Conventions

**Ownership Pattern:**
```csharp
void FixedUpdate() {
    if (!IsOwner) return;  // Only owner processes input
    // Input handling here
}
```

**Server Authority Pattern:**
```csharp
void OnTriggerEnter2D(Collider2D collider) {
    if (!IsServer) return;  // Only server validates
    // Collision logic here
}
```

**RPC Naming:**
- Always suffix with `ServerRpc` or `ClientRpc`
- ServerRpc for client-to-server calls
- ClientRpc for server-to-all-clients broadcasts

**Collision Detection:**
- Uses hardcoded string matching: `collider.name == "Space"`
- Uses tag comparison: `collider.gameObject.CompareTag("Bullet")`
- Consider refactoring to use layers or scriptable objects for type safety

## Directory Structure

```
Assets/
├── Doodlebugs/                    # Main game code
│   ├── Scripts/                   # Core game logic
│   │   ├── PlayerController.cs    # Player plane control & collision
│   │   ├── Shooting.cs            # Bullet spawning & shooting
│   │   ├── Bullet.cs              # Projectile behavior
│   │   ├── GameManager.cs         # Singleton network manager
│   │   ├── NetworkObjectSpawner.cs # Spawning helper
│   │   └── NetworkObjectDespawner.cs # Despawning helper
│   ├── Prefabs/                   # Network prefabs
│   │   ├── PlaneHolder.prefab     # Player aircraft
│   │   ├── Bullet.prefab          # Projectile
│   │   ├── NetworkPrefabsList.asset # Netcode registry
│   │   └── explosion.prefab       # Hit effect
│   └── Scenes/
│       └── SampleScene.unity      # Main gameplay scene
├── Bird.cs                        # Bird AI logic
├── Cloud.cs                       # Environmental hazard
├── CameraBehaviour.cs             # Camera following
└── [3rd party assets]             # 2DWarMachines, Warped Caves, TextMesh Pro
```

## Known Issues & Patterns

**Known Issues (from README):**
1. Bullet/Cloud collision de-sync: Sometimes bullet explodes on one screen but not another
2. Legacy `SendMessage()` pattern used in boundary collision (PlaygroundLeft/Right)
3. Commented-out features: PowerUp logic, exit game handling

**Anti-Patterns to Avoid:**
- `SendMessage()` - slow and breaks refactoring; use direct method calls or events
- Hardcoded collision string matching - use layers or scriptable objects instead
- Local Destroy on networked objects - use NetworkObjectDespawner

**Best Practices:**
- Always check `IsOwner` before input handling
- Always check `IsServer` before state validation
- Use NetworkObjectSpawner/Despawner helpers
- Register all network prefabs in NetworkPrefabsList.asset
- Test with ParrelSync to catch synchronization issues early

## Package Dependencies

Key packages (from Packages/manifest.json):
- `com.unity.netcode.gameobjects` (1.5.2) - Core multiplayer
- `com.unity.multiplayer.tools` (1.1.0) - Network debugging
- `com.veriorpies.parrelsync` - Editor cloning for testing
- `com.unity.2d.pixel-perfect` (5.0.3) - Retro rendering

## Game Mechanics

**Movement Controls:**
- Arrow keys/WASD: Rotate plane left/right
- Spacebar: Fire bullet
- S key (debug): Spawn test bird

**Physics Behavior:**
- Constant forward velocity with rotational steering
- Engine state affects gravity: engine off = falling, engine on = stable flight
- Speed management via smooth transitions
- Boundary wrapping on left/right edges
- Space region disables engine

**Multiplayer Sync:**
- Position/rotation auto-synced via NetworkObject
- Bullet firing fully synchronized via ServerRpc → ClientRpc
- Collision handling server-authoritative