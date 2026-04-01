# Foreground Parallax System - Implementation Plan

## Overview
Scrolling foreground layer that adds visual depth and gameplay obstacles. Foreground sprites scroll infinitely to the left, with PolygonCollider2D generated from sprite alpha channel. Planes fly behind/through it, bullets collide and explode.

## Architecture

### Files Created
- **`ForegroundSpriteGenerator.cs`** — static utility that programatically generates a placeholder foreground sprite (4096x2732) with terrain silhouette using layered sine waves + transparent gaps
- **`ForegroundScroller.cs`** (rewritten) — scrolls two copies of foreground sprite, generates PolygonCollider2D from alpha channel
- **`BackgroundProfile.cs`** — ScriptableObject pairing background + foreground sprite with settings

### Files Modified
- **`BackgroundManager.cs`** — calls `ForegroundScroller.SetForeground()` with profile data on background change
- **`PlayerController.cs`** — ignores collisions with Foreground layer (line 737)
- **`TagManager.asset`** — added "Foreground" physics layer (index 6) and sorting layer

### Existing (unchanged, already handles foreground)
- **`Bullet.cs`** — already explodes + despawns on collision with any non-"Space" trigger without IDamagable (foreground falls into this case)

## How It Works

### Sprite & Collider Pipeline
1. `BackgroundManager` selects a `BackgroundProfile` (random on host start)
2. Profile has optional `foregroundSprite` — if null, `ForegroundSpriteGenerator` creates a placeholder
3. `ForegroundScroller` receives the sprite and:
   a. Assigns it to two SpriteRenderers (A and B, side by side)
   b. Sets GameObjects to "Foreground" layer
   c. Removes any old colliders (BoxCollider2D or PolygonCollider2D)
   d. Adds fresh `PolygonCollider2D` (isTrigger=true)
   e. If sprite has Unity physics shape → auto-used; otherwise → `GenerateColliderFromAlpha()` traces outline from texture
   f. Simplifies paths to reduce vertex count (configurable tolerance)

### Scrolling & Wrapping
- Both sprites scroll left every frame (`_scrollSpeed * Time.deltaTime`)
- When a sprite's right edge passes the camera's left edge, it wraps to the right of the other sprite
- Infinite seamless loop

### Collision Layers
- **Planes** (`PlayerController`): explicitly ignore Foreground layer collisions → planes fly through/behind
- **Bullets** (`Bullet`): collide with foreground trigger → explode + despawn (existing logic, no changes needed)

### Placeholder Sprite (ForegroundSpriteGenerator)
- 4096x2732px (same width as background, 2/3 height)
- Terrain profile from 3 layered sine waves (big hills, medium hills, small bumps)
- 2 transparent gaps (archways) at 50% and 80% width for fly-through
- Dark solid color (30,20,15) with lighter edge (50,35,25)
- Runtime-generated Texture2D → Sprite.Create()

## Settings (BackgroundProfile ScriptableObject)
- `foregroundSprite` — Sprite (null = use placeholder)
- `foregroundScrollSpeed` — float, default -2 (negative = scroll left)
- `foregroundYPosition` — float, default -8
- `foregroundScale` — float, default 1

## TODO / Future
- [ ] Create real foreground art sprites per background theme
- [ ] Tune collider simplification tolerance for performance vs accuracy
- [ ] Consider Perlin noise or seed-based randomization for placeholder variety
- [ ] Sorting layer: ensure foreground renders in front of planes (Foreground sorting layer exists)
- [ ] Test with ParrelSync for network sync (foreground is local-only, no sync needed)
