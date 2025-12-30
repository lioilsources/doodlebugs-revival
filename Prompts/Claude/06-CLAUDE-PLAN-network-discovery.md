# Network Discovery Implementation Plan

## Goal
Implement fully automatic LAN discovery for 2 players. No buttons - first player automatically becomes host, second player automatically connects.

## Requirements
- **Max players:** 2
- **Discovery:** Fully automatic (no buttons)
- **Host disconnect:** Game ends, client returns to lobby
- **Platforms:** iOS + Android

## Current State
- `NetworkManagerUI.cs` - manual buttons for StartHost/StartClient (to be removed)
- No discovery system
- Unity Transport (default)
- Netcode for GameObjects 1.14.1

## Technical Approach

### UDP Broadcast Discovery
1. **On game start:** Automatically search for host (5s timeout)
2. **Host found:** Auto-connect as client
3. **Host not found:** Become host + start broadcasting

### Why UDP broadcast
- Works on iOS and Android identically
- No server/cloud services required
- Low latency for LAN games
- Unity Transport can be configured with IP obtained from discovery

## Implementation Steps

### 1. Create NetworkDiscovery.cs
**File:** `Assets/Doodlebugs/Scripts/Network/NetworkDiscovery.cs`

```csharp
// Main components:
- UdpClient for broadcast (host) and listen (client)
- Broadcast port: 47777
- Broadcast interval: 1 second
- Broadcast message: JSON with game name, player count, host name
- ServerResponse event for UI
```

### 2. Create DiscoveryResponseData
**File:** `Assets/Doodlebugs/Scripts/Network/DiscoveryResponseData.cs`

Structure for broadcast data:
- ServerName (string)
- ServerAddress (IPAddress)
- Port (int)
- CurrentPlayers (int)
- MaxPlayers (int)

### 3. Modify NetworkManagerUI.cs
**File:** `Assets/Doodlebugs/Scripts/NetworkManagerUI.cs`

Changes:
- Add reference to NetworkDiscovery
- New method `StartHostWithDiscovery()` - starts host + broadcast
- New method `StartClientWithDiscovery()` - starts listen + auto-connect
- Event handler for `OnServerFound`

### 4. Create ConnectionUI.cs
**File:** `Assets/Doodlebugs/Scripts/UI/ConnectionUI.cs`

Simple status UI:
- "Searching for game..." (during discovery)
- "Waiting for opponent..." (host waiting)
- "Connecting..." (client connecting)
- "Game starting!" (both connected)

### 5. Host disconnect handling
**File:** `Assets/Doodlebugs/Scripts/Network/ConnectionManager.cs`

- Subscribe to `NetworkManager.Singleton.OnClientDisconnectCallback`
- If host disconnects → show "Host disconnected", restart discovery
- Max 2 players = use `NetworkManager.Singleton.ConnectionApprovalCallback`

### 6. Modify scene
**File:** `Assets/Doodlebugs/Scenes/BumpCloudScene.unity` (main scene)
**Delete:** `Assets/Doodlebugs/Scenes/SampleScene.unity` (unused)
**Rename:** BumpCloudScene.unity → Scene01.unity

- Add Canvas with ConnectionUI
- Add NetworkDiscovery component
- Configure NetworkManager for dynamic IP

### 7. Platform-specific configuration

**iOS (Info.plist):**
- `NSLocalNetworkUsageDescription` - description for local network permission
- Bonjour services (optional)

**Android (AndroidManifest.xml):**
- `android.permission.INTERNET`
- `android.permission.ACCESS_WIFI_STATE`
- `android.permission.CHANGE_WIFI_MULTICAST_STATE`

## Flow diagram

```
Game start (both players)
    │
    ▼
StartDiscovery()
    │
    ├──► Listen for broadcast (5s timeout)
    │
    ├─[Host found]──► SetTransportAddress(hostIP)
    │                        │
    │                        ▼
    │                   StartClient()
    │                        │
    │                        ▼
    │                   "Connecting..."
    │                        │
    │                        ▼
    │                   OnClientConnected
    │                        │
    │                        ▼
    │                   "Game starting!"
    │
    └─[Timeout]──► StartHost()
                       │
                       ▼
                  StartBroadcast()
                       │
                       ▼
                  "Waiting for opponent..."
                       │
                       ▼
                  OnClientConnected (2nd player)
                       │
                       ▼
                  StopBroadcast()
                       │
                       ▼
                  "Game starting!"
```

## Host disconnect flow

```
During game
    │
    ▼
Host disconnects
    │
    ▼
OnClientDisconnect (on client)
    │
    ▼
"Host disconnected"
    │
    ▼
NetworkManager.Shutdown()
    │
    ▼
Restart discovery (start from beginning)
```

## Critical files to modify

### New files
1. `Assets/Doodlebugs/Scripts/Network/NetworkDiscovery.cs` - UDP broadcast/listen logic
2. `Assets/Doodlebugs/Scripts/Network/ConnectionManager.cs` - orchestration discovery → connect → game
3. `Assets/Doodlebugs/Scripts/UI/ConnectionUI.cs` - status text UI

### Modify existing
4. `Assets/Doodlebugs/Scripts/NetworkManagerUI.cs` - delete or minimize
5. `Assets/Doodlebugs/Scenes/BumpCloudScene.unity` → rename to `Scene01.unity`, add Canvas + components

### Delete
- `Assets/Doodlebugs/Scenes/SampleScene.unity` (unused)

### Platform configuration
6. `Assets/Plugins/iOS/Info.plist` - NSLocalNetworkUsageDescription
7. `Assets/Plugins/Android/AndroidManifest.xml` - network permissions

## Implementation order

1. **NetworkDiscovery.cs** - core UDP discovery
2. **ConnectionManager.cs** - connect discovery with NetworkManager
3. **ConnectionUI.cs** - visual feedback
4. **Scene modification** - add components
5. **Platform configuration** - iOS/Android permissions
6. **Testing** - ParrelSync for 2 instances
