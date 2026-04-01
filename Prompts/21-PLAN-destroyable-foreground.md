# Destructible Foreground - Implementation Plan

## Context

Foreground parallax system aktualne pouziva 2 SpriteRenderers (spriteA/spriteB) s PolygonCollider2D. Chceme ho rozsekat na male dlazdice (100x100px), ktere pri zasahu strelou zmizi.

**Klicovy trik - ZADNY NETWORKING neni potreba:**
Unity vola `OnTriggerEnter2D` na OBOU objektech pri trigger kolizi. Bullet existuje na vsech klientech (NetworkObject). I kdyz Bullet.cs ma `if (!IsServer) return;`, ForegroundTile dostane svuj vlastni `OnTriggerEnter2D` na VSECH klientech lokalne. Kazdy klient si znici dlazdici sam.

## Files to Create

### 1. `Assets/Doodlebugs/Scripts/ForegroundTile.cs` (NEW ~10 lines)

```csharp
public class ForegroundTile : MonoBehaviour
{
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.CompareTag("Bullet"))
            Destroy(gameObject);
    }
}
```

## Files to Modify

### 2. `Assets/Doodlebugs/Scripts/ForegroundScroller.cs` (REFACTOR)

**Remove:**
- `colliderSimplifyTolerance` field
- `RebuildPolygonCollider()`, `GenerateColliderFromAlpha()`, `SimplifyPaths()`, `DistancePointToLine()`, `TotalVertexCount()`, `PixelToLocal()`

**Add:**
- `[SerializeField] int tilePixelSize = 100` field
- `BuildTiles(SpriteRenderer sr)` - hlavni nova metoda:
  1. Precte texturu (`GetPixels32()`)
  2. Projde grid po `tilePixelSize` blocich
  3. Pro kazdy blok s nepruhlednymi pixely (alpha > 128) vytvori child GameObject:
     - SpriteRenderer (sub-sprite pres `Sprite.Create(texture, rect, pivot, ppu)`)
     - BoxCollider2D (isTrigger=true, Foreground layer)
     - ForegroundTile component
  4. Disabluje hlavni SpriteRenderer (dlazdice ho nahradi vizualne)
- `DestroyTiles(SpriteRenderer sr)` - znici vsechny tile children
- Pozice dlazdice: `localPos = (col*size + w/2 - texWidth/2, row*size + h/2 - texHeight/2) / ppu`

**Modify:**
- `SetForeground()`: `RebuildPolygonCollider()` → `BuildTiles()`
- `WrapIfNeeded()`: po repositioningu zavolat `DestroyTiles()` + `BuildTiles()` (foreground se "zahoji" pri scrollu zpet)
- `DisableForeground()`: pridat `DestroyTiles()` pro obe sprite

### 3. Texture .meta files (isReadable: 0 → 1)

- `Assets/Doodlebugs/Sprites/Foreground/central_park.png.meta`
- `Assets/Doodlebugs/Sprites/Foreground/siera_nevada.png.meta`

## Files NOT Changed

- **Bullet.cs** - strely uz spravne handluji foreground (explosion FX + despawn)
- **PlayerController.cs** - skip Foreground layer na line 737 funguje i s tiles
- **BackgroundManager.cs** - vola `SetForeground()` se stejnym API
- **BackgroundProfile.cs** - beze zmeny

## Tile Count Estimate

| Sprite | Grid | Max tiles | ~Opaque (35%) | x2 sprites |
|--------|------|-----------|---------------|------------|
| central_park (4096x1297) | 41x13 | 533 | ~186 | ~372 |
| siera_nevada (3216x861) | 32x9 | 288 | ~100 | ~200 |

Zvladnutelne pro Unity - stovky jednoduchych GameObjectu s BoxCollider2D.

## Technicke Vyzvy

1. **Tile seams** - sousedni Sprite.Create mohou ukazovat tenke mezery. Reseni: pouzit `FilterMode.Point` na texture, nebo 1px overlap v rectech.

2. **Mirny desync mezi klienty** - bullet pozice se muze lisit o par pixelu, takze ruzni klienti mohou znicit jinou dlazdici. Akceptovatelne - rozdil je max 1 tile a pri scrollu se to resetuje.

3. **Texture Read/Write** - sprite textura musi mit `isReadable: 1`. Uz je to potreba pro soucasny PolygonCollider2D system, ale meta soubory to maji na 0 (bug?). Opravime.

4. **Wrap regenerace** - pri wrapu se znici stare tiles a vytvori nove (~170 GameObjectu). Zabere <50ms, stava se kazdych ~20s. OK pro zacatek, pozdeji mozno object pooling.

## Implementation Order

1. Vytvorit `ForegroundTile.cs`
2. Opravit `.meta` soubory (isReadable: 1)
3. Refaktorovat `ForegroundScroller.cs` (remove polygon, add tile system)
4. Otestovat v editoru

## Verification

- Spustit hru v Unity editoru
- Overit ze foreground se renderuje spravne (zadne mezery, spravna pozice)
- Vystrelit na foreground - zasazena dlazdice zmizi
- Overit ze letadla prochazi foreground bez kolize
- Overit ze se foreground "zahoji" po prescrollovani
- (Bonus) ParrelSync test pro overeni sync mezi host/client
