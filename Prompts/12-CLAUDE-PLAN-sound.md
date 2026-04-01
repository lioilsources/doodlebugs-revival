# Engine Sound Implementation

## Task
Pridat zvuk motoru, jehoz rychlost (BPM) zavisi na rychlosti letadla.

## Audio Requirements (pro uzivatele)
- **Format**: WAV (seamless loop)
- **Delka**: 1-4 takty pri 120 BPM (cca 2-8 sekund)
- **Soubory**:
  - `engine_host.wav` - melodie pro hosta (120 BPM)
  - `engine_client.wav` - melodie pro klienta (120 BPM)

## Speed-to-BPM Mapping

| Stav | Speed | Pitch | BPM |
|------|-------|-------|-----|
| Motor OFF | `engineOff == true` | 0.5 | ~60 (vypadava) |
| Nizka (<20%) | 2.0 - 5.6 | 0.83 | ~100 |
| Default | 5.6 - 16.4 | 1.0 | 120 |
| Vysoka (>80%) | 16.4 - 20.0 | 1.33 | ~160 |

Vypocet procent: `(speed - 2) / (20 - 2) * 100`

**Prechod**: Gradualni (SmoothDamp) - plynuly prechod mezi BPM

## Implementation

### 1. Create EngineAudio.cs
**File**: `Assets/Doodlebugs/Scripts/EngineAudio.cs`

```csharp
using UnityEngine;
using Unity.Netcode;

public class EngineAudio : NetworkBehaviour
{
    [Header("Audio Clips")]
    public AudioClip engineHost;    // melodie pro hosta
    public AudioClip engineClient;  // melodie pro klienta

    [Header("Pitch Settings")]
    public float engineOffPitch = 0.5f;  // ~60 BPM (motor vypadava)
    public float minPitch = 0.83f;       // ~100 BPM
    public float maxPitch = 1.33f;       // ~160 BPM
    public float pitchSmoothTime = 0.3f;

    private AudioSource _audioSource;
    private PlayerController _player;
    private float _targetPitch = 1f;
    private float _currentPitch = 1f;
    private float _pitchVelocity;
}
```

### 2. Key Methods

```csharp
void Start()
{
    _player = GetComponent<PlayerController>();
    _audioSource = GetComponent<AudioSource>();

    // Vyber melodie podle role (host vs client)
    bool isHost = NetworkManager.Singleton.IsHost && IsOwner;
    _audioSource.clip = isHost ? engineHost : engineClient;
    _audioSource.loop = true;
    _audioSource.Play();
}

void Update()
{
    if (_player == null || _audioSource == null) return;

    bool engineOff = _player.IsEngineOff;
    float speed = _player.Speed;

    // Urcit cilovy pitch
    if (engineOff)
        _targetPitch = engineOffPitch;  // ~60 BPM
    else
    {
        float speedPercent = (speed - 2f) / 18f;  // 0-1
        _targetPitch = Mathf.Lerp(minPitch, maxPitch, speedPercent);
    }

    // Plynuly prechod
    _currentPitch = Mathf.SmoothDamp(_currentPitch, _targetPitch,
        ref _pitchVelocity, pitchSmoothTime);
    _audioSource.pitch = _currentPitch;
}
```

### 3. Modify PlayerController.cs
**File**: `Assets/Doodlebugs/Scripts/PlayerController.cs`

Pridat public accessory pro EngineAudio:

```csharp
// Add after line 48 (after engineOff property)
public bool IsEngineOff => engineOff;
public float Speed => speed;
```

### 4. Update PlaneHolder.prefab
Pridat komponenty:
- `AudioSource` (engine loop - loop=true, playOnAwake=false)
- `EngineAudio` script

### 5. Add Audio Files
Ulozit do: `Assets/Doodlebugs/Audio/`
- `engine_host.wav`
- `engine_client.wav`

## File Changes Summary

| File | Action |
|------|--------|
| `Scripts/EngineAudio.cs` | CREATE - novy script |
| `Scripts/PlayerController.cs` | EDIT - pridat 2 public accessory |
| `Prefabs/PlaneHolder.prefab` | EDIT - pridat AudioSource + EngineAudio |
| `Audio/engine_host.wav` | ADD - uzivatel nahraje |
| `Audio/engine_client.wav` | ADD - uzivatel nahraje |
