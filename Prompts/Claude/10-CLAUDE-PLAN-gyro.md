# Gyro Controls Implementation Plan

## Cíl
Implementovat gyroskopické ovládání na mobilních zařízeních při zachování klávesnicového ovládání na desktopu.

## Požadavky
- **Gyro rotace:** Naklonění od sebe → zatáčení doleva, k sobě → doprava
- **Střelba:** Dotyk kdekoli na obrazovce
- **Orientace:** Pouze landscape mode
- **Desktop:** Beze změny (šipky + mezerník)
- **Debug:** ~~Vodováha~~ (přeskočeno)

## Současný stav

### Existující input systém
```
Assets/Doodlebugs/Scripts/Input/
├── IInputProvider.cs           # Interface (beze změn)
├── InputManager.cs             # Singleton (drobná úprava)
├── DesktopInputProvider.cs     # Desktop input (beze změn)
├── MobileInputProvider.cs      # ← Hlavní změny
└── TouchControlsUI.cs          # ← Skrýt tlačítka
```

- `IInputProvider` - abstrakce: `GetHorizontalInput()`, `GetShootInput()`, `UpdateInput()`
- `MobileInputProvider` - aktuálně používá tlačítka přes `SetLeftPressed()`/`SetRightPressed()`
- `InputManager` - singleton, detekuje platformu, vybírá provider

## Technický přístup

### Gyroskop v Unity (landscape mode)
```csharp
Input.gyro.enabled = true;

// gravity.y < 0 = nakloněno od hráče → turn left
// gravity.y > 0 = nakloněno k hráči → turn right
float tilt = Input.gyro.gravity.y;
```

### Detekce dotyku pro střelbu
```csharp
if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
{
    if (!EventSystem.current.IsPointerOverGameObject(touch.fingerId))
        shootPressed = true;
}
```

## Implementační kroky

### 1. Upravit MobileInputProvider.cs

**Přidat proměnné:**
```csharp
private float deadZone = 0.1f;
private float maxTilt = 0.4f;
private bool gyroAvailable = false;
```

**Nová metoda Initialize():**
```csharp
public void Initialize()
{
    if (SystemInfo.supportsGyroscope)
    {
        Input.gyro.enabled = true;
        gyroAvailable = true;
    }
}
```

**Změnit UpdateInput() - gyro rotace:**
```csharp
public void UpdateInput()
{
    // Gyro rotation
    if (gyroAvailable)
    {
        float tilt = Input.gyro.gravity.y;
        if (Mathf.Abs(tilt) < deadZone)
            horizontalInput = 0f;
        else
            horizontalInput = Mathf.Clamp(tilt / maxTilt, -1f, 1f);
    }

    // Touch anywhere = shoot
    CheckTouchShoot();

    if (!shootPressed) shootConsumed = false;
}
```

**Nová metoda CheckTouchShoot():**
```csharp
private void CheckTouchShoot()
{
    if (Input.touchCount > 0)
    {
        Touch touch = Input.GetTouch(0);
        if (touch.phase == TouchPhase.Began)
        {
            shootPressed = true;
            return;
        }
    }
    shootPressed = false;
}
```

### 2. Upravit InputManager.cs

**Přidat Initialize volání (řádek ~40):**
```csharp
if (isMobile)
{
    mobileProvider = new MobileInputProvider();
    mobileProvider.Initialize();  // ← Přidat
    inputProvider = mobileProvider;
}
```

### 3. Upravit TouchControlsUI.cs

**Skrýt všechna tlačítka (Start metoda):**
```csharp
private void Start()
{
    // Na desktopu skrýt celý objekt
    if (InputManager.Instance == null || !InputManager.Instance.IsMobile())
    {
        gameObject.SetActive(false);
        return;
    }

    // Na mobilu skrýt tlačítka (gyro mode)
    if (leftButton != null) leftButton.gameObject.SetActive(false);
    if (rightButton != null) rightButton.gameObject.SetActive(false);
    if (shootButton != null) shootButton.gameObject.SetActive(false);
}
```

## Pořadí implementace

1. **MobileInputProvider.cs** - gyro logika + touch střelba
2. **InputManager.cs** - přidat Initialize volání
3. **TouchControlsUI.cs** - skrýt tlačítka
4. **Testování** - na reálném zařízení

## Soubory k úpravě

| Soubor | Akce |
|--------|------|
| `Assets/Doodlebugs/Scripts/Input/MobileInputProvider.cs` | Přidat gyro + touch |
| `Assets/Doodlebugs/Scripts/Input/InputManager.cs` | Přidat Initialize() |
| `Assets/Doodlebugs/Scripts/Input/TouchControlsUI.cs` | Skrýt tlačítka |

## Fallback

Pokud `SystemInfo.supportsGyroscope == false`:
- Gyro nebude použito
- Nutno zobrazit původní tlačítka (nebo alternativní ovládání)
- Zatím neimplementováno - předpoklad je že gyro bude dostupné

## Testování

- **Editor:** Gyro nefunguje, touch lze simulovat myší
- **Device:** Plné testování na iOS/Android zařízení
