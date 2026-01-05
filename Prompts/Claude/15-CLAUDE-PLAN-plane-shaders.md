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
