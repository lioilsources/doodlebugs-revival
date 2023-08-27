28/8/2023
- (unity) ParrelSync https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync

27/8/2023
- (unity) version updated to 2022.4.4f1
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