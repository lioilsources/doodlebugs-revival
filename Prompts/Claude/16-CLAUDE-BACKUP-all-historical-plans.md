# Plán: Mobile Build pro iOS & Android

## Shrnutí požadavků

Připravit hru Doodlebugs Revival pro mobilní platformy (iOS, Android) s podporou lokální WiFi multiplayer hry 1v1.

---

## 1. Je Unity vhodná platforma?

**Ano, Unity je vynikající volba pro tento projekt.**

### Výhody:
- Projekt již používá Unity 2022.3.4f1 (LTS verze)
- Netcode for GameObjects funguje na mobilech bez změn
- Jeden codebase pro iOS, Android, desktop
- 2D rendering je optimalizovaný pro mobily
- Existující assets fungují bez konverze

### Potenciální problémy:
- Velikost buildu (Unity runtime ~40-60 MB)
- Nutnost IL2CPP pro iOS (delší build time)

---

## 2. Obrazovka a scéna

### Aktuální stav:
- Kamera: Orthographic, size = 5 (vidí 10 jednotek vertikálně)
- Výchozí rozlišení: 1024x768 (4:3)
- Žádná dynamická adaptace na aspect ratio
- Boundaries (Left, Right) jsou hardcoded pozice

### Doporučený přístup: **Fixed Height (konstantní výška)**

```
┌──────────────────────────────────────────────┐
│  16:9 telefon (širší)                        │
│  ┌────────────────────────────────────────┐  │
│  │ Hrací plocha vidí více do stran        │  │
│  └────────────────────────────────────────┘  │
└──────────────────────────────────────────────┘

┌─────────────────────────┐
│  4:3 tablet (užší)      │
│  ┌───────────────────┐  │
│  │ Vidí méně do stran│  │
│  └───────────────────┘  │
└─────────────────────────┘
```

### Implementační kroky:

1. **CameraController.cs** - dynamická ortho size
   - Vypočítat ortho size podle aspect ratio
   - Zachovat minimální viditelnou šířku

2. **Dynamické hranice**
   - Boundaries se přizpůsobí velikosti kamery
   - Použít Camera.ViewportToWorldPoint() pro výpočet

3. **CanvasScaler** pro UI
   - Scale With Screen Size
   - Reference: 1920x1080 (16:9)
   - Match: 0.5 (vyvážit width/height)

4. **Safe Area**
   - Respektovat notche a systémové lišty
   - Odsadit UI od okrajů

---

## 3. Síťová vrstva - Local WiFi Discovery

### Aktuální stav:
- Netcode for GameObjects 1.5.2
- UTP transport na portu 7777
- Hardcoded IP: 192.168.88.18
- Manuální výběr Host/Client tlačítky

### Navržené řešení: **UDP Broadcast Discovery**

```
┌─────────────────────────────────────────────────────┐
│                    Spuštění hry                     │
└─────────────────────────┬───────────────────────────┘
                          │
          ┌───────────────▼───────────────┐
          │ Poslouchej broadcast (3 sek)  │
          └───────────────┬───────────────┘
                          │
         ┌────────────────┼────────────────┐
         │                │                │
    ┌────▼────┐      ┌────▼────┐     ┌────▼────┐
    │ Nalezen │      │ Timeout │     │ Nalezeno│
    │ 1 host  │      │ Nic     │     │ více    │
    └────┬────┘      └────┬────┘     └────┬────┘
         │                │               │
    ┌────▼────┐      ┌────▼────┐     ┌────▼────┐
    │ Připoj  │      │ Staň se │     │ Zobraz  │
    │ jako    │      │ hostem  │     │ seznam  │
    │ client  │      └─────────┘     └─────────┘
    └─────────┘
```

### Implementační kroky:

1. **NetworkDiscovery.cs** (nový soubor)
   - UDP broadcast na portu 7778
   - Posílat: game name, host IP, player count
   - Parsovat příchozí pakety

2. **DiscoveryUI.cs** (nový soubor)
   - "Hledám hry..." obrazovka
   - Seznam nalezených hostů
   - Tlačítko "Vytvořit hru"

3. **NetworkManagerUI.cs** - refaktor
   - Integrace s discovery
   - Automatický connect/host

4. **Timeout a retry logika**
   - 3 sekundy hledání
   - Automaticky host pokud nic nenalezeno
   - Refresh seznamu každých 2 sekund

---

## 4. Ovládání

### Aktuální stav:
- Legacy Input System
- Klávesy: šipky (rotace), mezerník (střelba)
- `Input.GetAxis("Horizontal")` v PlayerController.cs:156
- `Input.GetKeyDown(KeyCode.Space)` v Shooting.cs:30

### Rozhodnutí: **Dotykové zóny (primární)**

> Gyro ovládání není priorita - implementovat pouze pokud zbyde čas.

```
┌────────────────────────────────────────────────┐
│                                                │
│                  HRACÍ PLOCHA                  │
│                                                │
├────────────────────────────────────────────────┤
│                                                │
│  ┌──────────┐                    ┌──────────┐  │
│  │          │                    │          │  │
│  │  DOLEVA  │                    │ DOPRAVA  │  │
│  │          │                    │          │  │
│  └──────────┘                    └──────────┘  │
│                                                │
│              ┌────────────────┐                │
│              │    STŘELBA     │                │
│              └────────────────┘                │
│                                                │
└────────────────────────────────────────────────┘
```

### Implementační kroky:

1. **Abstrakce inputu**
   - `IInputProvider` interface
   - `DesktopInputProvider` (stávající logika)
   - `MobileInputProvider` (nová)

2. **TouchControls.cs** (nový soubor)
   - Detekce dotykových zón
   - Zpracování multi-touch
   - Tlačítka na canvas

3. **Integrace**
   - PlayerController.cs - použít IInputProvider
   - Shooting.cs - použít IInputProvider
   - Detekce platformy v Start()

5. **Nastavení**
   - Přepínač gyro/tlačítka
   - Citlivost ovládání
   - Velikost tlačítek

---

## 5. Distribuce mobilních balíků

### Android

**Požadavky:**
- Android SDK (API 21+ doporučeno)
- JDK 8 nebo 11
- Gradle (bundled s Unity)

**Build nastavení:**
- IL2CPP nebo Mono (Mono jednodušší pro debug)
- Target API: 33+ (Google Play požadavek 2024)
- Min API: 21 (Android 5.0)

**Distribuce:**
- **Google Play Console** - $25 jednorázový poplatek
- APK/AAB soubor
- Content rating questionnaire
- Privacy policy URL

**Testování:**
- Internal testing track (okamžitě)
- Closed testing (vybraní testeři)
- Open testing (veřejné)

### iOS

**Požadavky:**
- macOS počítač (povinné)
- Xcode 14+
- Apple Developer Account ($99/rok)
- Provisioning profiles

**Build nastavení:**
- IL2CPP (povinné pro iOS)
- Target iOS: 12.0+
- Signing: automatické nebo manuální

**Distribuce:**
- **App Store Connect**
- TestFlight (beta testing)
- App Review (1-3 dny)

**Certifikáty:**
- Development certificate
- Distribution certificate
- App ID
- Provisioning profile

### iOS Testování s Free Apple ID

> **Rozhodnutí:** Použít free Apple ID pro vývoj, Developer Program později pro distribuci

**Setup:**
1. Unity → Build Settings → iOS
2. Player Settings → Bundle ID: `com.tvojejmeno.doodlebugs`
3. Build → otevře se Xcode projekt
4. Xcode → Signing: "Automatically manage signing" + tvůj Apple ID
5. iPhone v Developer Mode (Settings → Privacy → Developer Mode)
6. Připoj kabelem → Run

**Omezení free Apple ID:**
- Build vyprší po 7 dnech (nutný reinstall)
- Max 3 aplikace současně
- Bez TestFlight, push notifications

**Po prvním buildu na iPhone:**
- Settings → General → VPN & Device Management → Trust developer

---

## 6. Další doporučené body

### A. Optimalizace výkonu
- Sprite atlasy pro redukci draw calls
- Object pooling pro střely
- LOD pro vzdálené objekty

### B. Baterie a teplo
- Capped frame rate (30-60 FPS)
- Redukce physics update rate na mobilech
- Vypnutí debug rendering

### C. Offline/Online režim
- Detekce WiFi připojení
- Graceful disconnect handling
- Reconnect dialog

### D. UX vylepšení
- Haptic feedback při střelbě
- Vibrace při zásahu
- Pause při ztrátě focus

### E. Analytics (volitelné)
- Unity Analytics nebo Firebase
- Sledování sessions, crashes

### F. Monetizace (budoucnost)
- Unity Ads integration
- In-app purchases framework

---

## Prioritní pořadí implementace

> **Rozhodnutí:** Obě platformy současně, dotykové ovládání jako primární

### Fáze 1: Základ (nutné pro fungující build)
1. [ ] Input abstrakce + touch controls
2. [ ] Dynamická kamera a boundaries
3. [ ] CanvasScaler + Safe Area
4. [ ] Android build test
5. [ ] iOS build test (Mac k dispozici)

### Fáze 2: Multiplayer
6. [ ] Network discovery (UDP broadcast)
7. [ ] Discovery UI (hledání her)
8. [ ] Testování na 2 Android zařízeních
9. [ ] Testování na 2 iOS zařízeních

### Fáze 3: Polish
10. [ ] Nastavení citlivosti ovládání
11. [ ] Haptic feedback (vibrace)
12. [ ] Cross-platform test (Android + iOS)

### Fáze 4: Distribuce
13. [ ] Google Play setup ($25)
14. [ ] App Store setup ($99/rok)
15. [ ] Privacy policy, ikony, screenshots

---

## Kritické soubory k úpravě

| Soubor | Změna |
|--------|-------|
| `PlayerController.cs` | Input abstrakce, line 156 |
| `Shooting.cs` | Input abstrakce, line 30 |
| `NetworkManagerUI.cs` | Discovery integrace |
| `CameraBehaviour.cs` | Dynamická ortho size |
| `ProjectSettings/ProjectSettings.asset` | Android/iOS nastavení |
| Scene boundaries | Dynamické pozice |

## Nové soubory k vytvoření

| Soubor | Účel |
|--------|------|
| `IInputProvider.cs` | Interface pro input |
| `MobileInputProvider.cs` | Touch handling |
| `DesktopInputProvider.cs` | Keyboard handling |
| `TouchControlsUI.cs` | UI tlačítka na canvas |
| `NetworkDiscovery.cs` | UDP broadcast/listen |
| `DiscoveryUI.cs` | Lobby UI pro hledání her |
| `CameraAspectHandler.cs` | Dynamické přizpůsobení kamery |
| `SafeAreaHandler.cs` | Handling notche a safe area |
| `InputManager.cs` | Factory pro správný InputProvider podle platformy |
# Dynamic Cloud System

## Požadavky
- Reuse Cloud prefab (již existuje s NetworkObject, trigger collider)
- Mraky různých velikostí
- Mraky se pohybují doprava různou rychlostí na stejné výšce
- Při respawnu letadlo spawne za náhodným mrakem
- Mrak při dosažení pravého okraje → nový mrak na levém okraji ve stejné výšce
- Ve vesmíru žádné mraky
- Mraky jen od určité výšky (např. od poloviny obrazovky nahoru)

---

## Současný stav

**Cloud.prefab** (`Assets/Doodlebugs/Prefabs/Cloud.prefab`):
- NetworkObject + ClientNetworkTransform
- Rigidbody2D (gravityScale=0)
- PolygonCollider2D (trigger)
- SpriteRenderer (sortingOrder=10 - nad letadly)
- Registrován v NetworkPrefabsList

**Boundary systém** (ScreenSetup.cs):
- `Left`, `Right`, `Space`, `Ground` colliders
- Detekce v OnTriggerEnter2D

**Aktuálně**: Mraky jsou staticky umístěny ve scéně, nepohybují se.

---

## Návrh implementace

### Architektura

```
CloudManager (singleton, na serveru)
├── Spravuje seznam aktivních mraků
├── Spawne mraky při startu
├── Reaguje na wrap (mrak dosáhl pravého okraje)
└── Poskytuje pozici náhodného mraku pro respawn

Cloud (script na každém mraku)
├── Pohyb doprava (server-authoritative)
├── Detekce pravého okraje → notify CloudManager
└── Náhodná velikost při spawnu
```

### 1. Cloud.cs (NEW)
`Assets/Doodlebugs/Scripts/Cloud.cs`

```csharp
public class Cloud : NetworkBehaviour
{
    // Náhodná rychlost při spawnu
    private NetworkVariable<float> _speed = new NetworkVariable<float>();

    // Pohyb doprava
    void FixedUpdate()
    {
        if (!IsServer) return;
        transform.position += Vector3.right * _speed.Value * Time.fixedDeltaTime;
    }

    // Detekce pravého okraje
    void OnTriggerEnter2D(Collider2D col)
    {
        if (!IsServer) return;
        if (col.name == "Right")
        {
            CloudManager.Instance.OnCloudReachedRightEdge(this);
        }
    }

    // Inicializace s náhodnou rychlostí a velikostí
    public void Initialize(float speed, float scale)
    {
        _speed.Value = speed;
        transform.localScale = Vector3.one * scale;
    }
}
```

### 2. CloudManager.cs (NEW)
`Assets/Doodlebugs/Scripts/CloudManager.cs`

```csharp
public class CloudManager : MonoBehaviour
{
    public static CloudManager Instance { get; private set; }

    [Header("Settings")]
    public int cloudCount = 3;
    public float minSpeed = 1f;
    public float maxSpeed = 3f;
    public float minScale = 0.5f;
    public float maxScale = 1.5f;
    public float minHeight = 5f;
    public float maxHeight = 15f;

    private List<Cloud> _clouds = new List<Cloud>();
    private Collider2D _leftBoundary;
    private Collider2D _rightBoundary;
    private GameObject _cloudPrefab;

    // Auto-inicializace
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInit()
    {
        if (Instance != null) return;
        var obj = new GameObject("CloudManager");
        Instance = obj.AddComponent<CloudManager>();
        DontDestroyOnLoad(obj);
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // Cache boundaries
        _leftBoundary = GameObject.Find("Left")?.GetComponent<Collider2D>();
        _rightBoundary = GameObject.Find("Right")?.GetComponent<Collider2D>();

        // Load prefab
        _cloudPrefab = Resources.Load<GameObject>("Cloud");
        // Fallback: najít existující prefab přes NetworkManager
        if (_cloudPrefab == null)
        {
            // Použít NetworkPrefabsList nebo najít ve scéně
        }

        // Subscribe na network start
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
    }

    void OnServerStarted()
    {
        SpawnInitialClouds();
    }

    void SpawnInitialClouds()
    {
        // Rovnoměrně rozložit výšky
        for (int i = 0; i < cloudCount; i++)
        {
            float heightPercent = (float)i / (cloudCount - 1);
            float y = Mathf.Lerp(minHeight, maxHeight, heightPercent);
            float x = Random.Range(-20f, 20f);
            SpawnCloud(x, y);
        }
    }

    // ... zbytek stejný
}
```

### 3. PlayerController.cs změny
`Assets/Doodlebugs/Scripts/PlayerController.cs`

```csharp
// V RespawnWithExplosionClientRpc():
// Místo fixní pozice použít pozici za mrakem

Vector3 newPos;
if (CloudManager.Instance != null)
{
    var cloudPos = CloudManager.Instance.GetRandomCloudPosition();
    // Spawn mírně za mrakem (vlevo od něj)
    newPos = new Vector3(cloudPos.x - 3f, cloudPos.y, 0f);
}
else
{
    // Fallback na původní logiku
    float spawnX = (OwnerClientId == 0) ? -15f : 15f;
    newPos = new Vector3(spawnX, 10f, 0f);
}
```

---

## Konfigurace výšky mraků

```
┌─────────────────────────────────────────┐
│             SPACE (bez mraků)           │  y > maxHeight
├─────────────────────────────────────────┤
│                                         │
│    ☁️    ☁️         ☁️                   │  minHeight < y < maxHeight
│         ☁️              ☁️               │  (zóna mraků)
│                                         │
├─────────────────────────────────────────┤
│             (bez mraků)                 │  y < minHeight
│                                         │
│▓▓▓▓▓▓▓▓▓▓▓ GROUND ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓│
└─────────────────────────────────────────┘
```

---

## File Changes Summary

| Soubor | Akce |
|--------|------|
| `Scripts/Cloud.cs` | CREATE - pohyb a detekce okraje |
| `Scripts/CloudManager.cs` | CREATE - spawn a management |
| `Scripts/PlayerController.cs` | EDIT - respawn za mrakem |
| `Prefabs/Cloud.prefab` | EDIT - přidat Cloud.cs komponent |

---

## Síťová architektura

```
[Server]
  CloudManager
    - Spawne mraky při startu
    - Reaguje na wrap
    - Poskytuje pozice pro respawn

  Cloud (NetworkObject)
    - NetworkVariable<float> _speed
    - Server pohybuje, klienti interpolují (ClientNetworkTransform)

[All Clients]
  - Vidí mraky přes ClientNetworkTransform
  - Respawn pozice získána z CloudManager
```

---

## Schválená konfigurace

✅ **Počet mraků:** 3
✅ **Výšky:** Různé (každý mrak na jiné Y pozici)
✅ **Inicializace:** Auto-init (RuntimeInitializeOnLoadMethod)
# Plane Shaders Implementation Plan

## Cíl
Přidat vizuální efekty pro lepší identifikaci a vzhled letadel v Doodlebugs Revival.

## Požadované efekty
1. **Soft glow outline** - vlastní letadlo (pouze pro IsOwner)
2. **Animated gold sparkles** - Expert level piloti
3. **Kombinace** - Expert vidí oba efekty současně
4. **DamageFlash** - bílý/červený flash při zásahu letadla

---

## Architektura řešení

### Glow Outline: Child Sprite přístup
- Nový child objekt `GlowOutline` pod `Plane`
- Stejný sprite jako letadlo, scale 1.15x
- Dedikovaný `SoftGlow.shader` s additive blending
- Žádná network synchronizace - čistě lokální `IsOwner` check

### Gold Sparkles: ParticleSystem
- Child `SparkleParticles` ParticleSystem pod `Plane`
- NetworkVariable `isExpert` pro sync stavu mezi klienty
- Všichni vidí sparkles Expert hráčů

### DamageFlash: Material Property Animation
- `DamageFlash.shader` s `_FlashAmount` property (0-1)
- Lerp mezi normální barvou a flash barvou (bílá → červená)
- Trigger při zásahu - animace 0→1→0 přes ~0.2s
- Server-authoritative: volá se z `OnTriggerEnter2D` collision handleru

### Rendering order (back to front)
1. GlowOutline (sortingOrder: 0)
2. Plane sprite (sortingOrder: 1)
3. SparkleParticles (sortingOrder: 2)

---

## Nové soubory

```
Assets/Doodlebugs/
├── Shaders/
│   ├── SoftGlow.shader              # Glow outline shader
│   └── DamageFlash.shader           # Hit flash shader
├── Materials/
│   ├── GlowOutlineMaterial.mat      # Soft cyan glow
│   ├── ExpertSparklesMaterial.mat   # Gold particles
│   └── DamageFlashMaterial.mat      # Hit flash material
├── Prefabs/
│   └── SparkleParticles.prefab      # Reusable particle prefab
└── Scripts/
    └── PlaneVisualEffects.cs        # Effect controller
```

## Modifikované soubory

- `Assets/Doodlebugs/Prefabs/PlaneHolder.prefab` - přidat GlowOutline + SparkleParticles children
- `Assets/Doodlebugs/Scripts/PlayerController.cs` - integrace PlaneVisualEffects
- `Assets/Doodlebugs/Scripts/PilotMaturityManager.cs` - přidat OnLevelChanged event

---

## Implementační kroky

### Fáze 1: Shader a materiály
1. Vytvořit `SoftGlow.shader` s parametry:
   - _GlowColor (default: cyan #80FFFF)
   - _GlowIntensity
   - _PulseSpeed, _PulseAmount (animovaný dech)
2. Vytvořit Materials/ složku
3. Vytvořit GlowOutlineMaterial.mat

### Fáze 2: Glow Outline efekt
4. Přidat GlowOutline child do PlaneHolder.prefab:
   - SpriteRenderer se stejným sprite
   - Scale (1.15, 1.15, 1)
   - SortingOrder: 0
   - Material: GlowOutlineMaterial
5. Vytvořit PlaneVisualEffects.cs:
   - Reference na glowOutlineRenderer
   - `UpdateGlowVisibility()` - enabled pouze pro IsOwner
6. Přidat PlaneVisualEffects component na PlaneHolder

### Fáze 3: Expert Sparkle efekt
7. Vytvořit SparkleParticles.prefab:
   - Shape: Circle
   - Emission: 5-10 particles/sec
   - Lifetime: 0.5-1.0s
   - Color: Gold gradient
   - Size: 0.05-0.1
8. Přidat SparkleParticles jako child Plane v PlaneHolder.prefab
9. Rozšířit PlaneVisualEffects.cs:
   - NetworkVariable<bool> isExpert
   - UpdateSparkleVisibility()
   - ServerRpc pro sync expert stavu
10. Přidat OnLevelChanged event do PilotMaturityManager.cs
11. Subscribe na event v PlaneVisualEffects

### Fáze 4: DamageFlash efekt
12. Vytvořit `DamageFlash.shader`:
    - _MainTex (sprite texture)
    - _FlashColor (default: white)
    - _FlashAmount (0-1)
    - Lerp mezi original color a flash color
13. Vytvořit DamageFlashMaterial.mat
14. Rozšířit PlaneVisualEffects.cs:
    - `TriggerDamageFlash()` metoda
    - Coroutine pro animaci _FlashAmount (0→1→0 přes 0.2s)
    - ClientRpc pro sync flash efektu
15. Volat `TriggerDamageFlash()` z PlayerController.OnTriggerEnter2D při zásahu

### Fáze 5: Polish
16. Testovat všechny efekty současně (glow + sparkles + flash)
17. Ladit vizuální parametry
18. Performance test s více hráči

---

## Klíčový kód

### PlaneVisualEffects.cs (nový)
```csharp
public class PlaneVisualEffects : NetworkBehaviour
{
    [SerializeField] private SpriteRenderer glowOutlineRenderer;
    [SerializeField] private ParticleSystem sparkleParticles;
    [SerializeField] private SpriteRenderer planeRenderer;
    [SerializeField] private Material damageFlashMaterial;

    private NetworkVariable<bool> isExpert = new NetworkVariable<bool>();
    private Material planeMaterialInstance;

    public override void OnNetworkSpawn()
    {
        // Owner-only glow
        glowOutlineRenderer.enabled = IsOwner;

        // Expert sparkles sync
        if (IsOwner)
            PilotMaturityManager.Instance.OnLevelChanged += HandleLevelChange;

        isExpert.OnValueChanged += (_, expert) => UpdateSparkles(expert);
    }

    private void HandleLevelChange(PilotMaturityLevel level)
    {
        if (IsOwner)
            UpdateExpertStatusServerRpc(level == PilotMaturityLevel.Expert);
    }

    [ServerRpc]
    private void UpdateExpertStatusServerRpc(bool expert) => isExpert.Value = expert;

    private void UpdateSparkles(bool expert)
    {
        if (expert) sparkleParticles.Play();
        else sparkleParticles.Stop();
    }

    // DamageFlash - volá se z PlayerController při zásahu
    public void TriggerDamageFlash() => TriggerDamageFlashClientRpc();

    [ClientRpc]
    private void TriggerDamageFlashClientRpc() => StartCoroutine(DamageFlashCoroutine());

    private IEnumerator DamageFlashCoroutine()
    {
        if (planeMaterialInstance == null) yield break;

        float duration = 0.2f;
        float halfDuration = duration / 2f;

        // Flash in (0 → 1)
        for (float t = 0; t < halfDuration; t += Time.deltaTime)
        {
            planeMaterialInstance.SetFloat("_FlashAmount", t / halfDuration);
            yield return null;
        }

        // Flash out (1 → 0)
        for (float t = 0; t < halfDuration; t += Time.deltaTime)
        {
            planeMaterialInstance.SetFloat("_FlashAmount", 1f - (t / halfDuration));
            yield return null;
        }

        planeMaterialInstance.SetFloat("_FlashAmount", 0f);
    }
}
```

### PilotMaturityManager.cs (modifikace)
```csharp
// Přidat event
public event System.Action<PilotMaturityLevel> OnLevelChanged;

// V SetLevel() přidat invoke
OnLevelChanged?.Invoke(level);
```

---

## Případné budoucí rozšíření

| Shader | Účel |
|--------|------|
| **SpriteOutline.shader** | Červený outline nepřátelských letadel |
| **BulletTrail** (TrailRenderer) | Viditelnost střel |
| **CloudWarning** (reuse SoftGlow) | Pulzující glow na cloudy |

---

## Kritické soubory
- `/Assets/Doodlebugs/Scripts/PlayerController.cs:99-121` - SetPlaneColor() integrace
- `/Assets/Doodlebugs/Scripts/PilotMaturityManager.cs:61-71` - SetLevel() pro event
- `/Assets/Doodlebugs/Shaders/ColorReplace.shader` - reference pro shader pattern
- `/Assets/Doodlebugs/Prefabs/PlaneHolder.prefab` - hlavní prefab modifikace
# Plán: 4-player local desktop multiplayer

## Cíl
Podpora až 4 lokálních hráčů na jedné klávesnici na desktopu s možností zapínat/vypínat klávesami 0-3.

## Klávesové mapování

| Hráč | Rotace (L/R) | Throttle (Up/Down) | Střelba | Toggle |
|------|--------------|-------------------|---------|--------|
| P1 | A / D | W / S | Space | 1 (0=off) |
| P2 | J / L | I / K | H | 2 |
| P3 | ← / → | ↑ / ↓ | RCtrl | 3 |
| P4 | O / P | [ / ] | \ | 4 |

**Toggle logika:**
- P1 je automaticky zapnutý po startu
- Klávesa `1` zapne/vypne P1
- Klávesa `0` pouze vypne P1 (alternativa)
- Klávesy `2`, `3`, `4` zapnou/vypnou P2, P3, P4
- Toggle funguje kdykoliv během hry

---

## Architektura

### Nový koncept: LocalPlayerManager

Místo refaktoru InputManager singletonu vytvoříme nový systém pro lokální hráče:

```
LocalPlayerManager (singleton)
├── localPlayers[4] - pole lokálních hráčů
├── EnablePlayer(index) / DisablePlayer(index)
├── GetInputProvider(index) - vrátí DesktopInputProvider pro daného hráče
└── SpawnLocalPlayer(index) / DespawnLocalPlayer(index)
```

### DesktopInputProvider rozšíření

Přidat `playerIndex` parametr do konstruktoru:

```csharp
public class DesktopInputProvider : IInputProvider
{
    private int playerIndex;
    private KeyCode leftKey, rightKey, upKey, downKey, shootKey;

    public DesktopInputProvider(int playerIndex = 0)
    {
        this.playerIndex = playerIndex;
        SetupKeysForPlayer(playerIndex);
    }
}
```

### Network vs Local vztah

**Aktuální stav:**
- 1 network klient = 1 hráč
- `IsOwner` kontroluje input

**Nový stav pro local coop:**
- Host klient může mít 1-4 lokálních hráčů
- Každý lokální hráč má vlastní NetworkObject (všechny owned by host)
- `localPlayerIndex` na PlayerController určuje který input provider použít

---

## Implementační kroky

### 1. Nový LocalPlayerManager.cs
**Soubor:** `Assets/Doodlebugs/Scripts/Input/LocalPlayerManager.cs`

```csharp
public class LocalPlayerManager : MonoBehaviour
{
    public static LocalPlayerManager Instance { get; private set; }

    private const int MAX_LOCAL_PLAYERS = 4;
    private bool[] playerEnabled = new bool[MAX_LOCAL_PLAYERS];
    private DesktopInputProvider[] inputProviders = new DesktopInputProvider[MAX_LOCAL_PLAYERS];
    private NetworkObject[] playerObjects = new NetworkObject[MAX_LOCAL_PLAYERS];

    public int ActivePlayerCount => playerEnabled.Count(p => p);

    public void TogglePlayer(int index);
    public void EnablePlayer(int index);
    public void DisablePlayer(int index);
    public IInputProvider GetInputProvider(int index);
}
```

### 2. Rozšířit DesktopInputProvider.cs
**Soubor:** `Assets/Doodlebugs/Scripts/Input/DesktopInputProvider.cs`

- Přidat konstruktor s `playerIndex`
- Přidat `SetupKeysForPlayer(int index)` metodu
- Používat KeyCode přímo místo Input.GetAxis (pro nezávislost)

### 3. Upravit PlayerController.cs
**Soubor:** `Assets/Doodlebugs/Scripts/PlayerController.cs`

- Přidat `[SyncVar] public int localPlayerIndex = -1;` (-1 = network player, 0-3 = local player)
- V `HandleMovement()` použít `LocalPlayerManager.Instance.GetInputProvider(localPlayerIndex)` místo singletonu

### 4. Upravit Shooting.cs
**Soubor:** `Assets/Doodlebugs/Scripts/Shooting.cs`

- Stejná logika jako PlayerController - použít localPlayerIndex

### 5. Spawn logika v LocalPlayerManager
- Host může spawnnout lokální hráče pomocí `NetworkObjectSpawner.SpawnNewNetworkObjectAsPlayerObject()`
- Ownership všech lokálních hráčů patří hostu
- Nastavit `localPlayerIndex` po spawnu

### 6. Update ConnectionManager.cs
**Soubor:** `Assets/Doodlebugs/Scripts/Network/ConnectionManager.cs`

- Zvýšit `MAX_PLAYERS` z 2 na 4
- Upravit logiku pro počítání hráčů (network + local)

---

## Klíčové soubory k úpravě

| Soubor | Změna |
|--------|-------|
| `Assets/Doodlebugs/Scripts/Input/LocalPlayerManager.cs` | **NOVÝ** - správa lokálních hráčů |
| `Assets/Doodlebugs/Scripts/Input/DesktopInputProvider.cs` | Přidat playerIndex a key mapping |
| `Assets/Doodlebugs/Scripts/PlayerController.cs` | Přidat localPlayerIndex, upravit input lookup |
| `Assets/Doodlebugs/Scripts/Shooting.cs` | Upravit input lookup |
| `Assets/Doodlebugs/Scripts/Network/ConnectionManager.cs` | Zvýšit MAX_PLAYERS |

---

## Verifikace

1. **Desktop test:**
   - Spustit hru, stisknout 1,2,3,4 - ověřit spawn/despawn hráčů
   - Ověřit nezávislé ovládání každého hráče
   - Stisknout 0 - ověřit vypnutí P1

2. **Network test:**
   - Host se 2 lokálními hráči + klient s 1 hráčem
   - Ověřit synchronizaci všech hráčů

3. **Edge cases:**
   - Zapnout/vypnout hráče během hry
   - Max 4 hráči celkem (local + network)

---

## Rozhodnutí

- ✅ P1 automaticky zapnutý po startu
- ✅ Toggle funguje kdykoliv během hry
- ✅ HUD už podporuje 4 hráče (existující GameHUD)

---

## Gamepad podpora pro lokální hráče

### Koncept
Gamepad 1 → P1, Gamepad 2 → P2, atd. Každý lokální hráč může používat buď klávesnici NEBO gamepad.

### Implementace

**1. Rozšířit GamepadInputProvider o gamepadIndex:**
```csharp
public class GamepadInputProvider : IInputProvider
{
    private int gamepadIndex;  // 0-3, mapuje na Gamepad.all[index]

    public GamepadInputProvider(int gamepadIndex = 0)
    {
        this.gamepadIndex = gamepadIndex;
    }

    private Gamepad GetGamepad()
    {
        var gamepads = Gamepad.all;
        if (gamepadIndex < gamepads.Count)
            return gamepads[gamepadIndex];
        return null;
    }
}
```

**2. Nový HybridInputProvider - kombinuje klávesnici + gamepad:**
```csharp
public class HybridInputProvider : IInputProvider
{
    private DesktopInputProvider keyboard;
    private GamepadInputProvider gamepad;

    public HybridInputProvider(int playerIndex)
    {
        keyboard = new DesktopInputProvider(playerIndex);
        gamepad = new GamepadInputProvider(playerIndex);
    }

    public float GetHorizontalInput()
    {
        // Preferuje gamepad pokud má input, jinak klávesnice
        float gp = gamepad.GetHorizontalInput();
        if (Mathf.Abs(gp) > 0.1f) return gp;
        return keyboard.GetHorizontalInput();
    }
}
```

**3. LocalPlayerManager použije HybridInputProvider:**
```csharp
// Místo:
inputProviders[i] = new DesktopInputProvider(i);
// Použít:
inputProviders[i] = new HybridInputProvider(i);
```

### Soubory k úpravě

| Soubor | Změna |
|--------|-------|
| `GamepadInputProvider.cs` | Přidat `gamepadIndex` konstruktor |
| `HybridInputProvider.cs` | **NOVÝ** - kombinuje keyboard + gamepad |
| `LocalPlayerManager.cs` | Použít HybridInputProvider |

### Chování

- P1 používá Gamepad.all[0] nebo klávesy A/D/W/S/Space
- P2 používá Gamepad.all[1] nebo klávesy J/L/I/K/H
- P3 používá Gamepad.all[2] nebo šipky + RCtrl
- P4 používá Gamepad.all[3] nebo O/P/[/]/\
- Pokud gamepad má input, použije se; jinak klávesnice
- Hráči mohou přepínat mezi gamepad/klávesnicí za běhu
