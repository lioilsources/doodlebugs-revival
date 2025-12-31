 Plán je hotový. Shrnutí:

  Problém: Boundary objekty mají non-unit scale (0.13, 1.51...) což zkresluje velikost colliderů. Pozadí se neškáluje.

  Řešení: Nový ScreenSetup.cs který:
  1. Vytvoří boundary collidery dynamicky se scale=1
  2. Škáluje pozadí na velikost kamery
  3. Aktualizuje při změně aspect ratio
  4. Vytváří collidery v Awake() aby PlayerController je našel

  Akce ve scéně:
  - Smazat staré boundary objekty (Left, Right, Ground, Space)
  - Deaktivovat BoundaryManager
  - Přidat ScreenSetup na kameru s referencí na Background

───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
 Ready to code?

 Here is Claude's plan:
╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌
 Oprava Dynamických Okrajů a Pozadí

 Problém

 - Okraje (colliders) nejsou na správné pozici na hraně obrazovky
 - Pozadí nepokrývá celou obrazovku
 - Vyskytuje se na všech platformách (desktop, iOS, Android)

 Analýza Příčin

 1. Scale Problem na Boundary Objektech

 Existující boundary objekty ve scéně mají non-unit scale (např. 0.13, 1.51, 0.94).
 Když BoundaryManager nastaví collider.size, výsledná world-space velikost je:
 world_size = collider.size * transform.localScale
 Tím se collidery zkreslí.

 2. Timing Issue

 BoundaryManager.Start() volá UpdateBoundaries(), ale CameraAspectHandler možná ještě neaktualizoval orthographicSize.

 3. Pozadí není škálováno

 Neexistuje žádný skript pro dynamické škálování pozadí na velikost kamery.

 Řešení

 Přístup: Vytvořit nový ScreenSetup.cs

 Inspirováno poskytnutým kódem - jeden skript který:
 1. Nastaví kameru
 2. Škáluje pozadí
 3. Vytvoří boundary collidery dynamicky (s scale=1)

 Soubory k úpravě

 | Soubor                                                  | Akce                               |
 |---------------------------------------------------------|------------------------------------|
 | Assets/Doodlebugs/Scripts/Camera/ScreenSetup.cs         | NOVÝ - hlavní logika               |
 | Assets/Doodlebugs/Scripts/Camera/BoundaryManager.cs     | Smazat nebo deaktivovat            |
 | Assets/Doodlebugs/Scripts/Camera/CameraAspectHandler.cs | Ponechat pro aspect ratio handling |

 Implementace ScreenSetup.cs

 using UnityEngine;

 public class ScreenSetup : MonoBehaviour
 {
     [Header("References")]
     [SerializeField] private SpriteRenderer background;

     [Header("Settings")]
     [SerializeField] private float borderThickness = 2f;

     [Header("Overlap Into View (%)")]
     [SerializeField] private float leftOverlap = 0.01f;
     [SerializeField] private float rightOverlap = 0.01f;
     [SerializeField] private float groundOverlap = 0.02f;
     [SerializeField] private float spaceOverlap = 0.10f;

     private Camera cam;
     private float lastAspect;
     private GameObject[] borders = new GameObject[4];

     void Awake()
     {
         cam = Camera.main;
         CreateBorderColliders();  // Must be in Awake - PlayerController.Awake() needs these
     }

     void Start()
     {
         ScaleBackground();
         lastAspect = cam.aspect;
     }

     void Update()
     {
         // Update on aspect change
         float currentAspect = (float)Screen.width / Screen.height;
         if (Mathf.Abs(currentAspect - lastAspect) > 0.01f)
         {
             lastAspect = currentAspect;
             UpdateBorders();
             ScaleBackground();
         }
     }

     void ScaleBackground()
     {
         if (background == null || cam == null) return;

         float camHeight = cam.orthographicSize * 2f;
         float camWidth = camHeight * cam.aspect;

         Vector2 spriteSize = background.sprite.bounds.size;

         Vector3 scale = background.transform.localScale;
         scale.x = camWidth / spriteSize.x;
         scale.y = camHeight / spriteSize.y;
         background.transform.localScale = scale;
         background.transform.position = cam.transform.position + Vector3.forward * 10; // Behind everything
     }

     void CreateBorderColliders()
     {
         float camHeight = cam.orthographicSize * 2f;
         float camWidth = camHeight * cam.aspect;
         float halfHeight = camHeight / 2f;
         float halfWidth = camWidth / 2f;

         // Calculate overlap offsets
         float leftX = -halfWidth + (leftOverlap * camWidth);
         float rightX = halfWidth - (rightOverlap * camWidth);
         float bottomY = -halfHeight + (groundOverlap * camHeight);
         float topY = halfHeight - (spaceOverlap * camHeight);

         borders[0] = CreateCollider("Left",
             new Vector2(leftX - borderThickness / 2f, 0),
             new Vector2(borderThickness, camHeight * 2));

         borders[1] = CreateCollider("Right",
             new Vector2(rightX + borderThickness / 2f, 0),
             new Vector2(borderThickness, camHeight * 2));

         borders[2] = CreateCollider("Ground",
             new Vector2(0, bottomY - borderThickness / 2f),
             new Vector2(camWidth * 2, borderThickness),
             "Respawn");

         borders[3] = CreateCollider("Space",
             new Vector2(0, topY + borderThickness / 2f),
             new Vector2(camWidth * 2, borderThickness));
     }

     GameObject CreateCollider(string name, Vector2 position, Vector2 size, string tag = null)
     {
         GameObject border = new GameObject(name);
         border.transform.position = cam.transform.position + (Vector3)position;
         border.transform.localScale = Vector3.one; // IMPORTANT: scale = 1

         BoxCollider2D collider = border.AddComponent<BoxCollider2D>();
         collider.size = size;
         collider.isTrigger = true;

         if (tag != null) border.tag = tag;
         border.transform.parent = transform;

         return border;
     }

     void UpdateBorders()
     {
         // Destroy old borders and recreate
         foreach (var border in borders)
         {
             if (border != null) Destroy(border);
         }
         CreateBorderColliders();
     }
 }

 Kroky Implementace

 1. Vytvořit ScreenSetup.cs ve složce Assets/Doodlebugs/Scripts/Camera/
 2. Ve scéně:
   - Přidat ScreenSetup komponentu na Main Camera (nebo nový prázdný GameObject)
   - Nastavit referenci na Background SpriteRenderer
   - Deaktivovat/smazat BoundaryManager komponentu
   - Smazat staré boundary objekty (Left, Right, Ground, Space) ze scény
 3. CameraAspectHandler ponechat - stará se o orthographicSize
 4. PlayerController.cs - zkontrolovat že collision handling stále funguje
   - Používá collider.name == "Left" atd., což bude fungovat

 Výhody nového přístupu

 - Scale = 1 - žádné zkreslení colliderů
 - Dynamická tvorba - collidery se vytvoří správně podle aktuálního aspect ratio
 - Pozadí se škáluje - vždy pokryje celou obrazovku
 - Jeden skript - jednodušší správa než 2 separátní skripty

 Důležité: PlayerController Timing

 PlayerController.cs používá GameObject.Find("Left") v Awake() (řádky 77-78) pro referenci na boundaries.
 Nové boundaries vytvořené v ScreenSetup.Start() musí existovat dřív.

 Řešení: Použít Script Execution Order nebo vytvořit boundaries v Awake():
 void Awake()  // místo Start()
 {
     cam = Camera.main;
     CreateBorderColliders();
 }

 void Start()
 {
     ScaleBackground();
     lastAspect = cam.aspect;
 }

 Testování

 1. Unity Editor - různé aspect ratio v Game view
 2. iOS/Android build - různé telefony
 3. Ověřit že letadlo správně wrapuje na okrajích
 4. Ověřit že Space region je na správné pozici