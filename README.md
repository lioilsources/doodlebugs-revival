26/12/2025
- align projectil speed with plane speed
- fix moving on border colision
- network: teleportovat pouze na straně ownera!

22/12/2025
- try to imitate legacy plane movement by switching off engine in space and more

15/12/2025
- add Claude Code terminal companion
- use CursorAi with GPT-5.2
- fix networking issues

20/12/2023
- do not respawn on left and right borders just reveal on other side

17/11/2023
- respawn PlaneHolder/Player/Plane in Cloud on edge collision
- respawn Player on hit with Bullet
- respawn on Players collision
- plane explosion
- Camera, Background, Cloud positioning to make bigger playground

24/9/2023
- PlayerController to control movement of Plane

20/9/2023
- GallacticKittens deep dive
- Scripts
  - PlayerShipController.cs: input handling for movement if IsOwner
  - PlayerShipMovement.cs: Update if IsOwner; on movement change call ServerRPC -> ClientRPC to propagate sprite change
  - PlayerShipShootBullet.cs: input handling for shooting if IsOwner; SpawnNewNetworkObject (Bullet Prefab) + attach character data to BulletController
  - CharacterDataSO.cs: ScriptableObject, game data, character variations
- Prefabs
  - PlayerShipBase: ClientNetworkTransform (controllable on client)
  - Bullet: NetworkObject
  - shield: IDamagable (Hit), OnTriggerEnter2D act on Server, 2x NetworkBehaviour scripts NetworkObject on Parent node

10/9/2023
- (doc) https://docs-multiplayer.unity3d.com/netcode/current/basics/object-spawning/
- NetworkManager behaviour
- automatic Spawning Player Prefab (Prefab with NetworkObject/NetworkBehaviour component added)
- player NetworkPrefab needs to be added in Network Prefab Lists list
  - host Instantiate local GameObject, on client join it Spawn this object on client with Server ownership (server authority) and sync transform.position
  - client join server/host and ask server to Instantiate GameObject on host and Spawn on client with Client ownership (client authority)
- destroying player
- id: NetworkObject.NetworkObjectId
  - host Destroy player, Instantiate player, Spawn player
  - clients call ServerRpc to Destroy client player, Instantiate player NetworkPrewab, Spawn it with client authority

9/9/2023
- make game playable
- destroy plane on collision
- respawn plane
- world boundaries
- dedicated server run on dedicated machine

4/9/2023
- spawning Birds
- (error) Only the owner can invoke a ServerRpc that requires ownership!
- (quiz) you can spawn Bird on client but you can move (A,S) only on host

3/9/2023
- Networking summary:
  - NetworkManager - Player Prefab to spawn Plane on Host or on Client on (0,0) position
  - NetworkManager - Network Prefabs Lists to hol all Network Object Prefab Assets
  - NetworkManager.Singleton.StartHost() - create host instance
  - NetworkManager.Singleton.StartClient() - join client instance into existing host
  - NetworkObject - to extend from NewtworkBehaviour
  - NetworkObject.IsOwner - to work only with object spawn on the right client
  - NetworkObject.OwnerClientId - unique network id
  - annotation [ServerRpc] - triggered on both host/client; executed on host
  - annotation [ClientRpc] - triggered on host; executed on all clients
  - ClientNetworkTransform - server authoritative false
- add Bullet.RigidBody2D Gravity Scale 0->2; if disable NetworkRigidBody2D gravity is also there
- (issue) there is de-sync with Bullet + Cloud collision; sometime Bullet explode on one screen but not on other

2/9/2023
- (fix) Plane flies in right direction on startup (x=0, y=0)
- (update) Bird starting position changed to (x=-19, y=1) to start on left border
- (update) Plane speed from 5 to 2 to slower movement for debuging purposes
- shooting logic use AddForce feature
- ServerRPC call to broadcast ClientRPC to instantiate local Bullet + AddForce localy on Host & Clients
- (?) it is confusing to me why in that case I'm not able to remove all that Bullet/Shooting NetworkBehaviour logic (client is not able to connect to host).

28/8/2023
- (unity) ParrelSync https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync

27/8/2023
- (unity) version updated to 2022.3.4f1
- (t) Code Monkey: COMPLETE Unity Multiplayer Tutorial (Netcode for Game Objects)
- https://www.youtube.com/watch?v=3yuBOB3VrCk&t=540s
- PackageManager add com.unity.netcode.gameobjects
- PackageManager add com.unity.multiplayer.tools
- (update) NetworkManager.Network Prefabs Lists += NetworkPrefabList
- (bug) fonts in TextMesh Pro, you need to import package
- (bug) NetworkManagerUi [SerializeField] private does not show in editor
- Quantum Console is not free
- (logs) macOS ~/Library/Company/Project$ tail -f Player.log
- NetworkManager host, client, server
- NetworkBehaviour server authority
- ClientNetworkTransform client authority

16/7/2023
- (t) GameDevHQ - How to Calculate Angles in Unity - A Unity Math Tutorial
- https://www.youtube.com/watch?v=s-Ho5hF2Yww
- direction
  ```
  // calculate direction = destination - source
  Vector3 direction = enemy.position - transform.position
  ```
- angle
  ``` 
  // calculate the angle using the inverse tangent method
  // Unity eagle system is clockwise (0 on top) oriented; shifted -90 from regular
  float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90;
  // #1 define the rotation along a specific axis using angle
  Quaterion angleAxis = Quaterion.AngleAxis(angle, Vector3.forward);
  // slerp from our current rotation to the new specific rotation
  transform.rotation = Quaterion.Slerp(transform.rotation, angleAxis, Time.deltaTime * 50);
  // or
  // #2 take our current euler angles and we just add our new angle to it
  transform.eulerAngles = Vector3.forward * angle
  ```
- mouse mapping (mouse pointer is enemy now)
  ```
  // camera is placed Z = -10
  Vector3 direction = Camera.main.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 10)) - transform.position
  ```
- debug commands
  ```
  Debug.DrawRay(transform.position, direction, Color.green);
  Debug.Log("Angle: " + angle);
  ```
- (n) Visual Studio Code reads all projects
  - cmd + shift + p: shell (Install 'code' command in PATH)
- (n) Unity Settings: External Tools; generate .csproj files (all)
- (n) VSCode keyboard mapping
  - Navigate Forward [ctrl + option + cmd + right arrow]
  - Navigate Backward [ctrl + option + cmd + left arrow]
  - Go to Definition [ctrl + option + cmd + enter]
  - All References [ctrl + option + cmd + space]

15/7/2023
- move + camera rework based on
- https://github.com/oussamabonnor1/ChasingPlanes_Unity3D

8/11/2020
- speed control: down => speed up, up => speed down

7/11/2020
- movement restriction
- space: speed = 0
- left, right: delayed flip, rotation = 0
- ground: explosion
- speed incerementation on fall down

4/11/2020
- fix rotation on reverse direction
- GameObject.SendMessage() instead of sharing  variables between scripts or even have some globals

03/11/2020
- playground by left and right border collider triggers show off bug in rotation code

02/11/2020
- explosion animation
- ground and cloud explosion on trigger
- (n) git repo init
- (n) .gitignore
- (n) Debug in VSCode
- (t) 2D Animation in Unity https://www.youtube.com/watch?v=hkaysu1Z-N8

01/11/2020
- missle shooting
- (t) TOP DOWN SHOOTING in Unity https://www.youtube.com/watch?v=LNLVOjbrQj4&t=26s

30/10/2020
- arrow control rotation amount
- (n) VSCode setup (.Net, Mono, VSCode plugins)

28/10/2020
- plane asset
- plane movement: `rb.velocity = rb.transform.right * speed;`
- cloud colision rotation: `rb.transform.localRotation *= Quaternion.Euler(0, 180, 0);`

Types:
- functional (game development)
- non functional (ide setup)
- tutorials (video, ...)
- math