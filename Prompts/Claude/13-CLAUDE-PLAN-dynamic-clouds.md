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
