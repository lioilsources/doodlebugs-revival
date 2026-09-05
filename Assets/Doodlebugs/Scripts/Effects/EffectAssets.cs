using UnityEngine;

/// <summary>
/// Shared runtime-generated assets for code-created particle effects
/// (soft radial puff texture + sprite material). No asset files needed.
/// </summary>
public static class EffectAssets
{
    private static Material _particleMaterial;
    private static Texture2D _softCircle;

    // One material per particle texture - a ParticleSystemRenderer takes a
    // material, not a sprite, so each shape needs its own (they are shared
    // across every system using that shape, so this stays at five).
    private static readonly System.Collections.Generic.Dictionary<ParticleTexture, Material> _elementMaterials = new();
    private static readonly System.Collections.Generic.Dictionary<ParticleTexture, Texture2D> _elementTextures = new();

    /// <summary>Sprites/Default material with a soft radial-gradient circle texture.</summary>
    public static Material ParticleMaterial
    {
        get
        {
            if (_particleMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                _particleMaterial = new Material(shader) { mainTexture = SoftCircle };
            }
            return _particleMaterial;
        }
    }

    public static Texture2D SoftCircle
    {
        get
        {
            if (_softCircle == null)
            {
                const int size = 64;
                _softCircle = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float half = (size - 1) / 2f;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(half, half)) / half;
                        float a = Mathf.Clamp01(1f - d);
                        a *= a; // soft falloff
                        pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                    }
                }
                _softCircle.SetPixels(pixels);
                _softCircle.Apply();
            }
            return _softCircle;
        }
    }

    /// <summary>
    /// Create a world-space smoke ParticleSystem under the given parent.
    /// Emission starts at 0 - drive it via SetSmokeIntensity.
    /// </summary>
    public static ParticleSystem CreateSmokeSystem(Transform parent, int sortingOrder)
    {
        var go = new GameObject("Smoke");
        go.transform.SetParent(parent, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // trail lags behind
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.05f; // smoke drifts up slightly
        main.maxParticles = 80;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.12f;

        // Grow and fade out
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.6f);
        sizeCurve.AddKey(1f, 1.8f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0.0f, 0f),
                new GradientAlphaKey(0.8f, 0.15f),
                new GradientAlphaKey(0.0f, 1f)
            });
        colorOverLifetime.color = gradient;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = ParticleMaterial;
        renderer.sortingOrder = sortingOrder;

        ps.Play();
        return ps;
    }

    /// <summary>Set smoke emission rate + tint in one call (0 = off).</summary>
    public static void SetSmokeIntensity(ParticleSystem ps, float rate, Color tint)
    {
        if (ps == null) return;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var main = ps.main;
        main.startColor = tint;
    }

    /// <summary>Material for one of the runtime particle shapes, shared.</summary>
    public static Material MaterialFor(ParticleTexture kind)
    {
        if (kind == ParticleTexture.SoftCircle) return ParticleMaterial;
        if (!_elementMaterials.TryGetValue(kind, out var mat) || mat == null)
        {
            mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = TextureFor(kind) };
            _elementMaterials[kind] = mat;
        }
        return mat;
    }

    /// <summary>
    /// Runtime particle shapes. All 32x32 white-with-alpha - the tint comes
    /// from the ParticleSystem, so one texture serves every element that
    /// picks that shape. Generated once, like SoftCircle.
    /// </summary>
    public static Texture2D TextureFor(ParticleTexture kind)
    {
        if (kind == ParticleTexture.SoftCircle) return SoftCircle;
        if (_elementTextures.TryGetValue(kind, out var cached) && cached != null) return cached;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        float half = (size - 1) / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - half) / half;   // -1..1
                float ny = (y - half) / half;
                float a;

                switch (kind)
                {
                    case ParticleTexture.Spark:
                        // Thin horizontal streak with a hot centre.
                        a = Mathf.Clamp01(1f - Mathf.Abs(nx)) * Mathf.Clamp01(1f - Mathf.Abs(ny) * 5f);
                        a *= a;
                        break;

                    case ParticleTexture.Droplet:
                        // Teardrop: round below, drawn to a point above.
                        {
                            float squeeze = ny > 0f ? 1f + ny * 1.6f : 1f;
                            float d = Mathf.Sqrt(nx * squeeze * (nx * squeeze) + ny * ny);
                            a = Mathf.Clamp01(1f - d);
                            a = Mathf.Clamp01(a * 2.2f);   // crisp edge, it is a liquid
                        }
                        break;

                    case ParticleTexture.Square:
                        // Crisp ember with a 1px soft rim.
                        {
                            float m = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));
                            a = Mathf.Clamp01((0.75f - m) * 6f);
                        }
                        break;

                    case ParticleTexture.Feather:
                        // Leaf: pointed at both ends, widest in the middle.
                        {
                            float width = Mathf.Cos(nx * Mathf.PI * 0.5f);
                            a = Mathf.Clamp01((width * 0.45f - Mathf.Abs(ny)) * 5f);
                        }
                        break;

                    default:
                        a = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
                        break;
                }

                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        _elementTextures[kind] = tex;
        return tex;
    }

    /// <summary>
    /// Trail hung off a projectile. World-space so it lags behind the
    /// bullet instead of riding it. Caller detaches it on despawn - see
    /// Bullet.ReleaseTrail - or the last puff pops out of existence.
    /// </summary>
    public static ParticleSystem CreateTrailSystem(Transform parent, TrailPreset preset, int sortingOrder)
    {
        if (preset == null) return null;

        var go = new GameObject("ElementTrail");
        go.transform.SetParent(parent, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(preset.LifetimeMin, preset.LifetimeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, preset.Speed);
        main.startSize = new ParticleSystem.MinMaxCurve(preset.SizeMin, preset.SizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = preset.GravityModifier;
        main.maxParticles = 120;

        var emission = ps.emission;
        emission.rateOverTime = preset.Rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Max(0.001f, preset.Jitter);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 1f);
        sizeCurve.AddKey(1f, preset.StartSizeCurveEnd);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ApplyGradient(ps, preset.ColorStart, preset.ColorEnd, preset.AlphaPeak);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = MaterialFor(preset.Texture);
        renderer.sortingOrder = sortingOrder;

        ps.Play();
        return ps;
    }

    /// <summary>One-shot puff under an impact/explosion flipbook. Self-destructs.</summary>
    public static ParticleSystem CreateBurst(Vector3 position, BurstPreset preset, float scale, int sortingOrder)
    {
        if (preset == null) return null;

        var go = new GameObject("ElementBurst");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(preset.LifetimeMin, preset.LifetimeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(preset.SpeedMin * scale, preset.SpeedMax * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(preset.SizeMin * scale, preset.SizeMax * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = preset.GravityModifier;
        main.maxParticles = 128;
        main.loop = false;
        main.duration = 0.2f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(preset.Count * Mathf.Max(1f, scale)))
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.05f * scale;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 1f);
        sizeCurve.AddKey(1f, 0.2f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ApplyGradient(ps, preset.ColorStart, preset.ColorEnd, 1f);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = MaterialFor(preset.Texture);
        renderer.sortingOrder = sortingOrder;

        ps.Play();
        Object.Destroy(go, preset.LifetimeMax + 0.3f);
        return ps;
    }

    private static void ApplyGradient(ParticleSystem ps, Color from, Color to, float alphaPeak)
    {
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(Mathf.Clamp01(alphaPeak), 0.2f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;
    }
}
