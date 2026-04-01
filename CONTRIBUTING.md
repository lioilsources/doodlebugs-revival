# Contributing

## Setup

1. Install **Unity 2022.3.4f1** (exact version — Netcode is version-sensitive)
2. Clone the repo and open the project folder in Unity Hub
3. Load scene: `Assets/Doodlebugs/Scenes/Scene01.unity`

## Multiplayer Testing

Use **ParrelSync** (included in the project):

1. Unity menu → ParrelSync → Clone Manager
2. Add clone → Open in New Editor
3. In the original editor: Play → **Start Host**
4. In the clone: Play → **Start Client**

Both instances share the same Assets folder — changes are reflected immediately without rebuilding.

## Adding Network Prefabs

Every prefab with a `NetworkObject` component must be registered:

1. Open `Assets/Doodlebugs/Prefabs/NetworkPrefabsList.asset`
2. Add the prefab to the list
3. Without this step the prefab cannot be spawned over the network

## Commit Conventions

Use short imperative messages describing what changed:

```
add foreground tile shatter effect on bullet hit
fix client desync on respawn
init power-up system
```

Prefix with `fix` for bug fixes, `add`/`init` for new features, `optimize` for performance, `remove` for deletions.

## IDE Setup

- Unity → Preferences → External Tools → Regenerate project files (generates `.csproj`)
- Recommended: VSCode with C# and Unity Debugger extensions

## Platform Builds

- **iOS:** Requires Xcode 26+ on macOS. `Editor/iOSPostProcessBuild.cs` auto-configures Info.plist for LAN discovery.
- **Android:** Build via Unity → File → Build Settings → Android. Gyro and touch work out of the box.
- **Desktop:** Standard Unity Player build.
