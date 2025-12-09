## Přehled

„Doodlebugs Revival“ je 2D arkádová letecká střílečka stavěná v Unity s podporou síťového multiplayeru (Netcode for GameObjects). Projekt kombinuje vlastní skripty s několika asset balíčky (2D War Machines, Warped Caves, TextMesh Pro aj.).

## Technologie

- **Unity**: 2022.3.4f1
- **Multiplayer**: Netcode for GameObjects (ServerRpc/ClientRpc, NetworkObject, ClientNetworkTransform)
- **Dev nástroje**: ParrelSync (pro paralelní běh více editor instancí)
- **UI/Fonts**: TextMesh Pro
- **Assety**: 2DWarMachines, Warped Caves, sprite muzzle flashes, Dynamic Space Background, FPS Gaming Font

## Scény

- `Assets/Doodlebugs/Scenes/SampleScene.unity` – hlavní pracovní scéna
- `Assets/Doodlebugs/Scenes/BumpCloudScene.unity` – test kolizí/respawnů u mraků a hran
- `Assets/2DWarMachines/Scene/Demo.unity` – ukázková scéna z asset balíčku
- `Assets/sprite muzzle flashes/demo.unity` – ukázka pro efekt střelby

## Klíčové skripty (Assets/Doodlebugs/Scripts)

- `PlayerController.cs` – řízení hráčova letounu a vstupy
- `Shooting.cs` – střelba, spouštění projektilů
- `Bullet.cs` – chování střely, kolize, poškození/výbuch
- `GameManager.cs` – herní stav, respawn, základní orchestrace
- `NetworkManagerUI.cs` – jednoduché UI pro Start Host/Client
- `NetworkObjectSpawner.cs` / `NetworkObjectDespawner.cs` – síťové spawnování a úklid objektů
- `IDamagable.cs` – rozhraní pro objekty přijímající poškození
- `DevMath.cs` – pomocné matematické funkce (např. výpočet úhlů a směrů)
- `PlayerDebug.cs` – diagnostika/debuginfo pro hráče

## Síťování (Netcode)

- Hráčův letoun je `NetworkObject` s klientskou autoritou (`ClientNetworkTransform`).
- Akce se replikují pomocí `ServerRpc`/`ClientRpc` (např. střelba, změny stavu).
- `NetworkManagerUI` umožňuje rychlý start režimu Host/Client v editoru.

## Jak spustit

1. Otevřete projekt v Unity 2022.3.4f1.
2. Načtěte `Assets/Doodlebugs/Scenes/SampleScene.unity`.
3. Stiskněte Play a v UI zvolte Host (nebo Client pro připojení k běžícímu hostu).
4. Ovládání a střelbu zajišťují skripty `PlayerController.cs` a `Shooting.cs` (mapování viz Input Manager/nový Input System dle konfigurace projektu).

## Struktura

- `Assets/Doodlebugs/` – scény, prefaby a herní skripty projektu
- `Assets/2DWarMachines/` – asset balíček (prefaby, animace, demo scéna)
- `Assets/Warped Caves/` – artwork asset balíček
- `Assets/sprite muzzle flashes/` – efekty záblesků zbraní
- `Assets/TextMesh Pro/` – písmo, UI utility

## Poznámky k vývoji

- Historii změn a odkazy na použité materiály najdete v `README.md`.


