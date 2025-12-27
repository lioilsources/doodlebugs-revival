 Here is Claude's plan:
╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌
 Plán: Mobile Build pro iOS & Android

 Shrnutí požadavků

 Připravit hru Doodlebugs Revival pro mobilní platformy (iOS, Android) s podporou lokální WiFi multiplayer hry 1v1.

 ---
 1. Je Unity vhodná platforma?

 Ano, Unity je vynikající volba pro tento projekt.

 Výhody:

 - Projekt již používá Unity 2022.3.4f1 (LTS verze)
 - Netcode for GameObjects funguje na mobilech bez změn
 - Jeden codebase pro iOS, Android, desktop
 - 2D rendering je optimalizovaný pro mobily
 - Existující assets fungují bez konverze

 Potenciální problémy:

 - Velikost buildu (Unity runtime ~40-60 MB)
 - Nutnost IL2CPP pro iOS (delší build time)

 ---
 2. Obrazovka a scéna

 Aktuální stav:

 - Kamera: Orthographic, size = 5 (vidí 10 jednotek vertikálně)
 - Výchozí rozlišení: 1024x768 (4:3)
 - Žádná dynamická adaptace na aspect ratio
 - Boundaries (Left, Right) jsou hardcoded pozice

 Doporučený přístup: Fixed Height (konstantní výška)

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

 Implementační kroky:

 1. CameraController.cs - dynamická ortho size
   - Vypočítat ortho size podle aspect ratio
   - Zachovat minimální viditelnou šířku
 2. Dynamické hranice
   - Boundaries se přizpůsobí velikosti kamery
   - Použít Camera.ViewportToWorldPoint() pro výpočet
 3. CanvasScaler pro UI
   - Scale With Screen Size
   - Reference: 1920x1080 (16:9)
   - Match: 0.5 (vyvážit width/height)
 4. Safe Area
   - Respektovat notche a systémové lišty
   - Odsadit UI od okrajů

 ---
 3. Síťová vrstva - Local WiFi Discovery

 Aktuální stav:

 - Netcode for GameObjects 1.5.2
 - UTP transport na portu 7777
 - Hardcoded IP: 192.168.88.18
 - Manuální výběr Host/Client tlačítky

 Navržené řešení: UDP Broadcast Discovery

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

 Implementační kroky:

 1. NetworkDiscovery.cs (nový soubor)
   - UDP broadcast na portu 7778
   - Posílat: game name, host IP, player count
   - Parsovat příchozí pakety
 2. DiscoveryUI.cs (nový soubor)
   - "Hledám hry..." obrazovka
   - Seznam nalezených hostů
   - Tlačítko "Vytvořit hru"
 3. NetworkManagerUI.cs - refaktor
   - Integrace s discovery
   - Automatický connect/host
 4. Timeout a retry logika
   - 3 sekundy hledání
   - Automaticky host pokud nic nenalezeno
   - Refresh seznamu každých 2 sekund

 ---
 4. Ovládání

 Aktuální stav:

 - Legacy Input System
 - Klávesy: šipky (rotace), mezerník (střelba)
 - Input.GetAxis("Horizontal") v PlayerController.cs:156
 - Input.GetKeyDown(KeyCode.Space) v Shooting.cs:30

 Rozhodnutí: Dotykové zóny (primární)

 Gyro ovládání není priorita - implementovat pouze pokud zbyde čas.

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

 Implementační kroky:

 1. Abstrakce inputu
   - IInputProvider interface
   - DesktopInputProvider (stávající logika)
   - MobileInputProvider (nová)
 2. TouchControls.cs (nový soubor)
   - Detekce dotykových zón
   - Zpracování multi-touch
   - Tlačítka na canvas
 3. Integrace
   - PlayerController.cs - použít IInputProvider
   - Shooting.cs - použít IInputProvider
   - Detekce platformy v Start()
 4. Nastavení
   - Přepínač gyro/tlačítka
   - Citlivost ovládání
   - Velikost tlačítek

 ---
 5. Distribuce mobilních balíků

 Android

 Požadavky:
 - Android SDK (API 21+ doporučeno)
 - JDK 8 nebo 11
 - Gradle (bundled s Unity)

 Build nastavení:
 - IL2CPP nebo Mono (Mono jednodušší pro debug)
 - Target API: 33+ (Google Play požadavek 2024)
 - Min API: 21 (Android 5.0)

 Distribuce:
 - Google Play Console - $25 jednorázový poplatek
 - APK/AAB soubor
 - Content rating questionnaire
 - Privacy policy URL

 Testování:
 - Internal testing track (okamžitě)
 - Closed testing (vybraní testeři)
 - Open testing (veřejné)

 iOS

 Požadavky:
 - macOS počítač (povinné)
 - Xcode 14+
 - Apple Developer Account ($99/rok)
 - Provisioning profiles

 Build nastavení:
 - IL2CPP (povinné pro iOS)
 - Target iOS: 12.0+
 - Signing: automatické nebo manuální

 Distribuce:
 - App Store Connect
 - TestFlight (beta testing)
 - App Review (1-3 dny)

 Certifikáty:
 - Development certificate
 - Distribution certificate
 - App ID
 - Provisioning profile

 ---
 6. Další doporučené body

 A. Optimalizace výkonu

 - Sprite atlasy pro redukci draw calls
 - Object pooling pro střely
 - LOD pro vzdálené objekty

 B. Baterie a teplo

 - Capped frame rate (30-60 FPS)
 - Redukce physics update rate na mobilech
 - Vypnutí debug rendering

 C. Offline/Online režim

 - Detekce WiFi připojení
 - Graceful disconnect handling
 - Reconnect dialog

 D. UX vylepšení

 - Haptic feedback při střelbě
 - Vibrace při zásahu
 - Pause při ztrátě focus

 E. Analytics (volitelné)

 - Unity Analytics nebo Firebase
 - Sledování sessions, crashes

 F. Monetizace (budoucnost)

 - Unity Ads integration
 - In-app purchases framework

 ---
 Prioritní pořadí implementace

 Rozhodnutí: Obě platformy současně, dotykové ovládání jako primární

 Fáze 1: Základ (nutné pro fungující build)

 1. Input abstrakce + touch controls
 2. Dynamická kamera a boundaries
 3. CanvasScaler + Safe Area
 4. Android build test
 5. iOS build test (Mac k dispozici)

 Fáze 2: Multiplayer

 6. Network discovery (UDP broadcast)
 7. Discovery UI (hledání her)
 8. Testování na 2 Android zařízeních
 9. Testování na 2 iOS zařízeních

 Fáze 3: Polish

 10. Nastavení citlivosti ovládání
 11. Haptic feedback (vibrace)
 12. Cross-platform test (Android + iOS)

 Fáze 4: Distribuce

 13. Google Play setup ($25)
 14. App Store setup ($99/rok)
 15. Privacy policy, ikony, screenshots

 ---
 Kritické soubory k úpravě

 | Soubor                                | Změna                     |
 |---------------------------------------|---------------------------|
 | PlayerController.cs                   | Input abstrakce, line 156 |
 | Shooting.cs                           | Input abstrakce, line 30  |
 | NetworkManagerUI.cs                   | Discovery integrace       |
 | CameraBehaviour.cs                    | Dynamická ortho size      |
 | ProjectSettings/ProjectSettings.asset | Android/iOS nastavení     |
 | Scene boundaries                      | Dynamické pozice          |

 Nové soubory k vytvoření

 | Soubor                  | Účel                                              |
 |-------------------------|---------------------------------------------------|
 | IInputProvider.cs       | Interface pro input                               |
 | MobileInputProvider.cs  | Touch handling                                    |
 | DesktopInputProvider.cs | Keyboard handling                                 |
 | TouchControlsUI.cs      | UI tlačítka na canvas                             |
 | NetworkDiscovery.cs     | UDP broadcast/listen                              |
 | DiscoveryUI.cs          | Lobby UI pro hledání her                          |
 | CameraAspectHandler.cs  | Dynamické přizpůsobení kamery                     |
 | SafeAreaHandler.cs      | Handling notche a safe area                       |
 | InputManager.cs         | Factory pro správný InputProvider podle platformy |

 Testování iOS buildu na vlastním telefonu

  Způsoby testování:

  1. Development Build (nejjednodušší pro začátek)
  - Připoj iPhone kabelem k Macu
  - V Unity: File → Build Settings → iOS
  - Zaškrtni "Development Build"
  - Klikni "Build and Run"
  - Xcode se otevře automaticky
  - Vyber své zařízení a spusť

  Požadavky:
  - Apple ID (zdarma stačí)
  - Xcode nainstalovaný
  - iPhone v "Developer Mode" (Settings → Privacy & Security → Developer Mode)

  2. TestFlight (pro delší testování)
  - Vyžaduje Apple Developer Program ($99/rok)
  - Build nahrát do App Store Connect
  - Pozvat testery přes TestFlight app
  - Instalace bez kabelu, automatické updaty

  Kroky pro první iOS build:

  1. Unity → Build Settings → Switch to iOS
  2. Player Settings:
     - Company Name: tvoje jméno
     - Product Name: Doodlebugs
     - Bundle Identifier: com.tvojejmeno.doodlebugs
     - Signing Team ID: (tvůj Apple ID team)
  3. Build → vybrat složku
  4. Otevřít .xcodeproj v Xcode
  5. Xcode → Signing & Capabilities:
     - Team: tvůj Apple ID
     - Zaškrtni "Automatically manage signing"
  6. Připoj iPhone → Select device → Run

  První spuštění na zařízení:

  - iPhone se zeptá "Nedůvěryhodný vývojář"
  - Jdi do Settings → General → VPN & Device Management
  - Klikni na svůj developer profile → "Trust"

  Omezení s free Apple ID:

  - Build vyprší po 7 dnech (musíš reinstall)
  - Max 3 aplikace najednou
  - Některé capabilities nedostupné (push notifications, iCloud)