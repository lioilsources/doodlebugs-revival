## Overview

"Doodlebugs Revival" is a 2D arcade air combat shooter built in Unity with networked multiplayer support (Netcode for GameObjects). The project combines custom scripts with several asset packages (2D War Machines, Warped Caves, TextMesh Pro, etc.).

## Technology

- **Unity**: 2022.3.4f1
- **Multiplayer**: Netcode for GameObjects (ServerRpc/ClientRpc, NetworkObject, ClientNetworkTransform)
- **Dev tools**: ParrelSync (for running multiple editor instances in parallel)
- **UI/Fonts**: TextMesh Pro
- **Assets**: 2DWarMachines, Warped Caves, sprite muzzle flashes, Dynamic Space Background, FPS Gaming Font

## Scenes

- `Assets/Doodlebugs/Scenes/SampleScene.unity` – main working scene
- `Assets/Doodlebugs/Scenes/BumpCloudScene.unity` – test for cloud/edge collisions and respawns
- `Assets/2DWarMachines/Scene/Demo.unity` – demo scene from asset package
- `Assets/sprite muzzle flashes/demo.unity` – demo for shooting effects

## Key Scripts (Assets/Doodlebugs/Scripts)

- `PlayerController.cs` – player aircraft control and inputs
- `Shooting.cs` – shooting, projectile spawning
- `Bullet.cs` – bullet behavior, collision, damage/explosion
- `GameManager.cs` – game state, respawn, basic orchestration
- `NetworkManagerUI.cs` – simple UI for Start Host/Client
- `NetworkObjectSpawner.cs` / `NetworkObjectDespawner.cs` – network spawning and object cleanup
- `IDamagable.cs` – interface for objects receiving damage
- `DevMath.cs` – helper math functions (e.g., angle and direction calculations)
- `PlayerDebug.cs` – diagnostics/debug info for player

## Networking (Netcode)

- Player aircraft is a `NetworkObject` with client authority (`ClientNetworkTransform`).
- Actions are replicated via `ServerRpc`/`ClientRpc` (e.g., shooting, state changes).
- `NetworkManagerUI` allows quick Host/Client mode start in editor.

## How to Run

1. Open the project in Unity 2022.3.4f1.
2. Load `Assets/Doodlebugs/Scenes/SampleScene.unity`.
3. Press Play and choose Host in the UI (or Client to connect to a running host).
4. Controls and shooting are handled by `PlayerController.cs` and `Shooting.cs` scripts (mapping via Input Manager/new Input System depending on project configuration).

## Structure

- `Assets/Doodlebugs/` – scenes, prefabs and game scripts
- `Assets/2DWarMachines/` – asset package (prefabs, animations, demo scene)
- `Assets/Warped Caves/` – artwork asset package
- `Assets/sprite muzzle flashes/` – weapon flash effects
- `Assets/TextMesh Pro/` – fonts, UI utilities

## Development Notes

- Change history and references to used materials can be found in `README.md`.
