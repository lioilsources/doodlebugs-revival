using System.Collections;
using UnityEngine;

/// <summary>
/// Small local, non-networked world effects (Prompts/29 §6): blast rings,
/// hit markers, floating numbers, puffs. Every caller runs them on each
/// client from an RPC it already receives, so they need no netcode.
/// </summary>
public static class WorldFx
{
    public const int Order = 120;   // above planes (1) and clouds (110)

    private class Anim : MonoBehaviour { }

    private static GameObject NewSprite(string name, Vector3 pos, Sprite sprite, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return go;
    }

    /// <summary>Ring that expands to `radius` world units and fades.</summary>
    public static void Ring(Vector3 pos, float radius, Color color, float seconds = 0.25f, float width = 0.18f)
    {
        if (radius <= 0f) return;
        var go = NewSprite("FxRing", pos, UiSprites.Ring(Mathf.Clamp(width / 2f, 0.03f, 0.3f)), color, Order);
        var a = go.AddComponent<Anim>();
        a.StartCoroutine(RingRoutine(go, radius, color, seconds));
    }

    private static IEnumerator RingRoutine(GameObject go, float radius, Color color, float seconds)
    {
        var sr = go.GetComponent<SpriteRenderer>();
        // The ring sprite is 128 px at PPU 100 = 1.28 units across
        float full = radius * 2f / 1.28f;
        float t = 0f;
        while (t < seconds && go != null)
        {
            float k = t / seconds;
            float e = 1f - Mathf.Pow(1f - k, 3f);
            go.transform.localScale = Vector3.one * Mathf.Lerp(full * 0.3f, full, e);
            sr.color = new Color(color.r, color.g, color.b, color.a * (1f - k));
            t += Time.deltaTime;
            yield return null;
        }
        if (go != null) Object.Destroy(go);
    }

    /// <summary>A ring that follows a transform (mine trigger, airburst radius).</summary>
    public static SpriteRenderer AttachRing(Transform parent, float radius, Color color, float width = 0.1f)
    {
        var go = new GameObject("FxRadius");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = UiSprites.Ring(Mathf.Clamp(width / 2f, 0.02f, 0.3f));
        sr.color = color;
        sr.sortingOrder = Order - 5;
        SetRadius(sr, radius);
        return sr;
    }

    /// <summary>Size an attached ring in world units, whatever the parent's scale.</summary>
    public static void SetRadius(SpriteRenderer ring, float radius)
    {
        if (ring == null) return;
        var parent = ring.transform.parent;
        float ps = parent != null ? Mathf.Max(0.0001f, parent.lossyScale.x) : 1f;
        ring.transform.localScale = Vector3.one * (radius * 2f / 1.28f / ps);
    }

    /// <summary>Floating text (damage numbers, pickup names).</summary>
    public static void Text(Vector3 pos, string text, Color color, float size = 0.5f, float seconds = 0.8f)
    {
        var go = new GameObject("FxText");
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = UiTheme.Display;
        tm.fontSize = 64;
        tm.characterSize = size / 6.4f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = tm.font != null ? tm.font.material : mr.sharedMaterial;
        mr.sortingOrder = Order + 2;
        go.AddComponent<Anim>().StartCoroutine(TextRoutine(go, tm, color, seconds));
    }

    private static IEnumerator TextRoutine(GameObject go, TextMesh tm, Color color, float seconds)
    {
        Vector3 start = go.transform.position;
        float t = 0f;
        while (t < seconds && go != null)
        {
            float k = t / seconds;
            go.transform.position = start + Vector3.up * (1.2f * (1f - (1f - k) * (1f - k)));
            float pop = k < 0.15f ? Mathf.Lerp(0.6f, 1.15f, k / 0.15f)
                                  : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((k - 0.15f) / 0.2f));
            go.transform.localScale = Vector3.one * pop;
            tm.color = new Color(color.r, color.g, color.b, k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f);
            t += Time.deltaTime;
            yield return null;
        }
        if (go != null) Object.Destroy(go);
    }

    /// <summary>White cross where your shot connected (shooter only).</summary>
    public static void HitMarker(Vector3 pos)
    {
        for (int i = 0; i < 2; i++)
        {
            var go = NewSprite("FxHit", pos, UiSprites.RoundedRect, Color.white, Order + 1);
            go.transform.rotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
            go.AddComponent<Anim>().StartCoroutine(HitRoutine(go));
        }
    }

    private static IEnumerator HitRoutine(GameObject go)
    {
        var sr = go.GetComponent<SpriteRenderer>();
        float t = 0f;
        const float seconds = 0.25f;
        while (t < seconds && go != null)
        {
            float k = t / seconds;
            float len = Mathf.Lerp(0.9f, 0.6f, k);
            go.transform.localScale = new Vector3(len / 0.68f, 0.09f / 0.68f, 1f);
            sr.color = new Color(1f, 1f, 1f, 1f - k * k);
            t += Time.deltaTime;
            yield return null;
        }
        if (go != null) Object.Destroy(go);
    }

    public static void Puff(Vector3 pos, Color color, int count = 6, float scale = 1f)
    {
        EffectAssets.CreateBurst(pos, new BurstPreset
        {
            Count = count, SpeedMin = 0.6f, SpeedMax = 2f, LifetimeMin = 0.15f, LifetimeMax = 0.35f,
            SizeMin = 0.12f, SizeMax = 0.24f, ColorStart = color, ColorEnd = new Color(color.r, color.g, color.b, 0f),
        }, scale, Order - 10);
    }

    /// <summary>Muzzle flash: a quick bright puff at the barrel.</summary>
    public static void Muzzle(Vector3 pos, Color color)
    {
        EffectAssets.CreateBurst(pos, new BurstPreset
        {
            Count = 4, SpeedMin = 0.5f, SpeedMax = 1.5f, LifetimeMin = 0.05f, LifetimeMax = 0.1f,
            SizeMin = 0.18f, SizeMax = 0.3f, ColorStart = Color.white, ColorEnd = color,
            Texture = ParticleTexture.Spark,
        }, 1f, Order - 10);
    }
}
