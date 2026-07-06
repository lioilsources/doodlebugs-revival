using UnityEngine;

/// <summary>
/// Shared runtime-generated assets for code-created particle effects
/// (soft radial puff texture + sprite material). No asset files needed.
/// </summary>
public static class EffectAssets
{
    private static Material _particleMaterial;
    private static Texture2D _softCircle;

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
}
