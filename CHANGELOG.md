# Changelog

## [31/03/2026]
- Foreground tile shatter effect on bullet hit — tiles break apart on projectile collision
- Fix invisible parallax foreground caused by wrong tile pivot offset

## [16/03/2026]
- Initial power-up system: `PowerUp`, `PowerUpManager`, `PowerUpType`
- Plane stats system (`PlaneStats`): shield, health, maneuverability, damage

## [15/03/2026]
- Fix client desync on respawn

## [13/03/2026]
- Foreground layer optimization
- Replace PolygonCollider2D foreground with destructible tile system (100×100 px tiles)

## [09/03/2026]
- NYC background with Central Park parallax foreground

## [15/02/2026]
- Parallax foreground system: infinite scroll left, PolygonCollider2D generated from sprite alpha channel
- Planes fly behind the foreground (render order), bullets collide and explode on it

## [12/02/2026]
- `BackgroundManager` for random background selection on host start
- Cleanup unused 3rd party assets — only 10 referenced sprites kept

## [12/01/2026]
- Couch co-op up to 4 players on one desktop (keys 1–4 + gamepads)
- WiFi multiplayer player limit increased to 20
- Respawn behind a random cloud with spawn collision protection
- Compact HUD sorted by stats
- Leveling: 10 kills → Advanced, 20 kills → Expert (different physics profiles)

## [09/01/2026]
- Gamepad migration to Unity Input System Package (Xbox, DualShock, MFi)
- DualShock support for mobile devices
- Gyro and touch migrated to new Input System — unified approach for all input methods
- Gyro controls: tilt X = steering, tilt Y = speed change
- Android build complete

## [05/01/2026]
- Plane visual effects: glow outline, damage flash, expert sparkles (`PlaneVisualEffects`)
- Pilot maturity profiles: Novice / Advanced / Expert with different physics parameters
- Shaders: outline for own player, gold effect for Expert

## [04/01/2026]
- v0.2.0-alpha tag

## [03/01/2026]
- Dynamic clouds: varied sizes, motion, network sync (`CloudManager`)
- HUD: speed, score, time
- Client timer sync on join
- Respawn behind clouds instead of at screen edge

## [01/01/2026]
- Engine sound synced with music BPM — plane speed changes the tempo
- Android build + larger plane sprite

## [31/12/2025]
- LAN network discovery (UDP broadcast, iOS Bonjour compatibility)
- Gyro input for mobile devices
- Dynamic screen size and game boundaries
- Color replacement shader for player plane distinction
- iOS build (Xcode 26.2 + Unity 6.2)

## [27/12/2025]
- Touch controls and dynamic camera for mobile

## [26/12/2025]
- Desktop local game for 2 players — first playable version ([Claude session #1](Prompts/03-CLAUDE-local-desktop-game.md))
- Fix projectile network sync and boundary wrapping

## [22/12/2025]
- Experimental Space movement: engine cuts out in space region, plane falls

## [20/12/2023]
- Fix rotation
- WIP Space movement with engine on/off switching

## [17/11/2023]
- Plane respawn on bullet hit, player collision, and boundary collision
- Explosion on collision
- Camera, background, clouds — larger playground

## [24/09/2023]
- `PlayerController` for plane movement
- Unity Netcode basics: NetworkManager, NetworkObject, ServerRpc, ClientRpc
- Shooting: `ShootServerRpc` → `AddForceClientRpc`

## [08/11/2020]
- Initial implementation: plane movement, shooting, explosion, bounded playground
- Physics: `rb.velocity = rb.transform.right * speed`
