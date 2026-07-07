using UnityEngine;

/// <summary>
/// Falling burning wreck shown when a plane dies: a local (non-networked)
/// visual clone of the plane sprite that tumbles down trailing smoke, then
/// explodes on the ground. Pure eye candy - each client simulates its own
/// copy from the same death position; gameplay (respawn) is untouched.
/// </summary>
public class WreckEffect : MonoBehaviour
{
    private const float Gravity = 9f;
    private const float MaxLifetime = 2.5f;
    private const float GroundY = 1.0f;   // foreground strip is ~1 world unit high

    private Vector2 _velocity;
    private float _angularVelocity;
    private float _spawnTime;
    private GameObject _explosionPrefab;
    private ParticleSystem _smoke;

    /// <summary>
    /// Spawn a wreck from the dying plane's visual. Safe to call on any client.
    /// </summary>
    public static void Spawn(SpriteRenderer sourceSprite, Vector3 position, Quaternion rotation,
        Vector2 initialVelocity, GameObject explosionPrefab)
    {
        if (sourceSprite == null || sourceSprite.sprite == null) return;

        var go = new GameObject("PlaneWreck");
        go.transform.position = position;
        go.transform.rotation = rotation;
        go.transform.localScale = sourceSprite.transform.lossyScale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sourceSprite.sprite;
        sr.sharedMaterial = sourceSprite.sharedMaterial; // keeps the player color shader
        sr.flipX = sourceSprite.flipX;
        sr.flipY = sourceSprite.flipY;
        sr.color = new Color(0.5f, 0.45f, 0.45f, 1f);    // charred tint
        sr.sortingOrder = sourceSprite.sortingOrder;

        var wreck = go.AddComponent<WreckEffect>();
        wreck._velocity = initialVelocity;
        wreck._angularVelocity = Random.Range(200f, 420f) * (Random.value < 0.5f ? -1f : 1f);
        wreck._explosionPrefab = explosionPrefab;
        wreck._spawnTime = Time.time;

        wreck._smoke = EffectAssets.CreateSmokeSystem(go.transform, sr.sortingOrder + 1);
        EffectAssets.SetSmokeIntensity(wreck._smoke, 35f, new Color(0.15f, 0.13f, 0.12f, 1f));
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        _velocity += Vector2.down * Gravity * dt;
        transform.position += (Vector3)(_velocity * dt);
        transform.Rotate(0f, 0f, _angularVelocity * dt);

        if (transform.position.y <= GroundY || Time.time - _spawnTime >= MaxLifetime)
        {
            Impact();
        }
    }

    private void Impact()
    {
        if (_explosionPrefab != null)
        {
            var effect = Instantiate(_explosionPrefab, transform.position, Quaternion.identity);
            var effectSprite = effect.GetComponent<SpriteRenderer>();
            if (effectSprite != null) effectSprite.sortingOrder = 100;
            Destroy(effect, 0.5f);
        }

        SfxManager.PlayHullHit(); // dull ground thud (the death boom already played)

        // Detach the smoke so remaining puffs fade out instead of vanishing
        if (_smoke != null)
        {
            EffectAssets.SetSmokeIntensity(_smoke, 0f, Color.white);
            _smoke.transform.SetParent(null);
            Destroy(_smoke.gameObject, 1.5f);
        }

        Destroy(gameObject);
    }
}
