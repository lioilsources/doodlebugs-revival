# Doodlebugs Revival

> 2D multiplayer arcade air combat. WWI-era biplanes. Local WiFi or couch co-op. One hit kills.

Players control physics-based biplanes with constant forward thrust and rotational steering. Shoot down opponents, level up from Novice to Expert, collect power-ups from downed enemies. Engine cuts out in space — gravity takes over.

---

## Platforms

| Platform | Controls | Multiplayer |
|---|---|---|
| Desktop (macOS / Windows) | Keyboard (arrows + space) or gamepad | WiFi host, up to 4 local co-op |
| iOS | Gyro (tilt) + touch, gamepad (DualShock) | WiFi client |
| Android | Gyro (tilt) + touch, gamepad | WiFi client |

**WiFi multiplayer:** up to 20 players on a local network (LAN discovery, no setup needed)  
**Couch co-op:** up to 4 players on one desktop (keyboard 1–4 + gamepads)

---

## Screenshots & Videos

See [GALLERY.md](GALLERY.md)

---

## Getting Started

**Requirements:** Unity 6000.2.9f1

1. Open the project in Unity
2. Load scene: `Assets/Doodlebugs/Scenes/Scene01.unity`
3. Press Play → click **Start Host**
4. Second player: open a clone via ParrelSync → Press Play → click **Start Client**

**ParrelSync** (included): Unity menu → ParrelSync → Clone Manager

---

## Documentation

- [CHANGELOG.md](CHANGELOG.md) — version history
- [GALLERY.md](GALLERY.md) — screenshots and videos
- [CONTRIBUTING.md](CONTRIBUTING.md) — dev setup and contribution guide
- [CLAUDE.md](CLAUDE.md) — AI assistant instructions and architecture reference

---

## Tech Stack

- Unity 6000.2.9f1
- Unity Netcode for GameObjects 1.14.1
- Unity Input System 1.7.0
- ParrelSync (multi-editor testing)
