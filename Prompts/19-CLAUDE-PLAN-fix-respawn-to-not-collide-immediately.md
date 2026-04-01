# Plán: Náhodný respawn za mraky s ochranou proti kolizi

## Požadavky
1. Respawn za náhodným mrakem (již částečně implementováno)
2. Zabránit respawnu více hráčů za stejným mrakem najednou
3. Opravit respawn pro lokální hráče (všichni mají OwnerClientId=0)

## Nalezené problémy

### 1. Fallback pozice nefunguje pro lokální hráče
```csharp
// PlayerController.cs řádek 611 - BUG!
float spawnX = (OwnerClientId == 0) ? -15f : 15f;
// Všichni lokální hráči mají OwnerClientId=0 → všichni spawn na x=-15!
```

### 2. CloudManager možná není dostupný
- `CloudManager.Instance` nebo `AreCloudsReady()` vrací false
- Lokální hráči padají do fallback větve

### Současný stav (PlayerController.cs řádky 598-613)
```csharp
if (CloudManager.Instance != null && CloudManager.Instance.AreCloudsReady())
{
    var cloudPos = CloudManager.Instance.GetRandomCloudPosition();
    float offsetX = Random.Range(-5f, -2f);
    float offsetY = Random.Range(-1f, 1f);
    newPos = new Vector3(cloudPos.x + offsetX, cloudPos.y + offsetY, 0f);
}
else
{
    // FALLBACK - všichni lokální hráči jdou sem a všichni mají OwnerClientId=0!
    float spawnX = (OwnerClientId == 0) ? -15f : 15f;
    newPos = new Vector3(spawnX, 10f, 0f);
}
```

### CloudManager.cs
- `GetRandomCloudPosition()` - vrací pozici náhodného mraku
- `cloudCount = 3` - defaultně 3 mraky
- Mraky se pohybují doprava, různé výšky (5-15f)

## Řešení

### 1. Opravit fallback v PlayerController.cs

```csharp
// Použít LocalPlayerIndex místo OwnerClientId pro rozlišení lokálních hráčů
int playerIndex = LocalPlayerIndex >= 0 ? LocalPlayerIndex : (int)OwnerClientId;
float[] spawnPositions = { -15f, -8f, 8f, 15f }; // 4 různé pozice
float spawnX = spawnPositions[playerIndex % spawnPositions.Length];
newPos = new Vector3(spawnX, 10f, 0f);
```

### 2. CloudManager - cooldown + delay queue systém

```csharp
// Přidat do CloudManager.cs:
using System.Linq;

private Dictionary<int, float> _cloudCooldowns = new Dictionary<int, float>();
private Queue<PlayerController> _respawnQueue = new Queue<PlayerController>();
private const float CLOUD_COOLDOWN = 1.5f;

/// <summary>
/// Požádá o respawn. Pokud je volný mrak, respawn okamžitě. Jinak do fronty.
/// </summary>
public void RequestRespawn(PlayerController player)
{
    if (!AreCloudsReady() || _clouds.Count == 0)
    {
        // Fallback - okamžitý respawn na fixní pozici
        player.ExecuteRespawnAtPosition(GetFallbackPosition(player));
        return;
    }

    int? availableCloud = GetAvailableCloudIndex();
    if (availableCloud.HasValue)
    {
        // Mrak volný - okamžitý respawn
        SpawnPlayerAtCloud(player, availableCloud.Value);
    }
    else
    {
        // Všechny mraky obsazené - přidat do fronty
        _respawnQueue.Enqueue(player);
        Debug.Log($"[CloudManager] Player queued for respawn, queue size: {_respawnQueue.Count}");
    }
}

private int? GetAvailableCloudIndex()
{
    var available = new List<int>();
    for (int i = 0; i < _clouds.Count; i++)
    {
        if (!_cloudCooldowns.ContainsKey(i) || Time.time > _cloudCooldowns[i])
            available.Add(i);
    }

    if (available.Count == 0) return null;
    return available[Random.Range(0, available.Count)];
}

private void SpawnPlayerAtCloud(PlayerController player, int cloudIndex)
{
    _cloudCooldowns[cloudIndex] = Time.time + CLOUD_COOLDOWN;

    var cloudPos = _clouds[cloudIndex].transform.position;
    float offsetX = Random.Range(-5f, -2f);
    float offsetY = Random.Range(-1f, 1f);
    var spawnPos = new Vector3(cloudPos.x + offsetX, cloudPos.y + offsetY, 0f);

    player.ExecuteRespawnAtPosition(spawnPos);
}

private Vector3 GetFallbackPosition(PlayerController player)
{
    int playerIndex = player.LocalPlayerIndex >= 0 ? player.LocalPlayerIndex : (int)player.OwnerClientId;
    float[] positions = { -15f, -8f, 8f, 15f };
    return new Vector3(positions[playerIndex % positions.Length], 10f, 0f);
}

private void Update()
{
    // Zpracuj frontu - zkus respawnout čekající hráče
    if (_respawnQueue.Count > 0)
    {
        int? availableCloud = GetAvailableCloudIndex();
        if (availableCloud.HasValue)
        {
            var player = _respawnQueue.Dequeue();
            if (player != null && player.gameObject != null)
            {
                SpawnPlayerAtCloud(player, availableCloud.Value);
            }
        }
    }
}
```

### 3. Aktualizovat PlayerController.cs

```csharp
// Změnit smrt hráče - místo okamžitého respawnu požádat CloudManager
// V metodě kde se volá RespawnWithExplosionClientRpc():

private void HandleDeath()
{
    // Exploze na všech klientech
    ShowExplosionClientRpc();

    // Požádat o respawn (server-side)
    if (IsServer)
    {
        if (CloudManager.Instance != null)
        {
            CloudManager.Instance.RequestRespawn(this);
        }
        else
        {
            // Fallback
            ExecuteRespawnAtPosition(GetFallbackSpawnPosition());
        }
    }
}

/// <summary>
/// Voláno z CloudManager když je přidělen mrak.
/// </summary>
public void ExecuteRespawnAtPosition(Vector3 position)
{
    RespawnAtPositionClientRpc(position);
}

[ClientRpc]
private void RespawnAtPositionClientRpc(Vector3 position)
{
    if (!IsOwner) return;

    // Teleport
    networkTransform?.Teleport(position, Quaternion.Euler(0, 0, 0), transform.localScale);

    // Reset state
    speed = defaultSpeed;
    engineOff = false;
    inSpace = false;
    currentGravity = 0f;

    if (rb != null)
        rb.linearVelocity = transform.right * speed;
}
```

## Průběh s 3 mraky a 4 hráči

```
Čas 0.00s - 4 hráči zemřou

Hráč 1: mrak 0 volný → spawn okamžitě, cooldown[0] = 1.5s
Hráč 2: mrak 1 volný → spawn okamžitě, cooldown[1] = 1.5s
Hráč 3: mrak 2 volný → spawn okamžitě, cooldown[2] = 1.5s
Hráč 4: žádný mrak volný → FRONTA (čeká)

Čas 1.50s - cooldown[0] expiruje

Update(): mrak 0 volný, fronta neprázdná
Hráč 4: spawn za mrakem 0 ✓
```

## Soubory k úpravě

| Soubor | Změna |
|--------|-------|
| `CloudManager.cs` | Přidat `_cloudCooldowns`, `_respawnQueue`, `RequestRespawn()`, `Update()` |
| `PlayerController.cs` | Změnit death handling na request-based, přidat `ExecuteRespawnAtPosition()` |

## Ověření
1. Spustit s 4 lokálními hráči
2. Zabít všechny 4 najednou
3. Ověřit: 3 respawn okamžitě za různými mraky, 4. čeká ~1.5s
4. Po 1.5s se 4. hráč respawnuje za uvolněným mrakem
