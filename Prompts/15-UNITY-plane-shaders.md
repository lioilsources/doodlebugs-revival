---
  Zbývá dokončit v Unity Editoru:

  1. Vytvořit materiály

  GlowOutlineMaterial:
  1. Right-click v Assets/Doodlebugs/Materials/ → Create → Material
  2. Pojmenuj "GlowOutlineMaterial"
  3. Shader: Custom/SoftGlow
  4. Nastavení:
    - Glow Color: Cyan (0.5, 1, 1, 0.6)
    - Intensity: 1.0
    - Pulse Speed: 1.0
    - Pulse Amount: 0.15

  2. Upravit PlaneHolder.prefab

  Přidat GlowOutline child:
  1. Otevři Assets/Doodlebugs/Prefabs/PlaneHolder.prefab
  2. Pod "Plane" child vytvoř nový GameObject "GlowOutline"
  3. Přidej SpriteRenderer:
    - Sprite: BiPlane1 (stejný jako Plane)
    - Material: GlowOutlineMaterial
    - Sorting Order: 0 (za letadlem)
  4. Scale: (1.15, 1.15, 1)

  Přidat SparkleParticles:
  1. Pod "Plane" vytvoř ParticleSystem "SparkleParticles"
  2. Nastavení:
    - Shape: Circle (radius ~0.5)
    - Emission Rate: 8 particles/sec
    - Start Lifetime: 0.5-1.0s
    - Start Size: 0.05-0.1
    - Start Color: Gold gradient (FFD700)
    - Renderer Sorting Order: 2
    - Play on Awake: OFF

  Přidat PlaneVisualEffects component:
  1. Na PlaneHolder přidej component PlaneVisualEffects
  2. Přiřaď reference:
    - Glow Outline Renderer: GlowOutline/SpriteRenderer
    - Sparkle Particles: SparkleParticles
    - Plane Renderer: Plane/SpriteRenderer

  ---