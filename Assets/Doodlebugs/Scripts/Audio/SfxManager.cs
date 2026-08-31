using System.Collections;
using UnityEngine;

/// <summary>
/// Central one-shot sound effect player + mobile haptics.
/// Auto-created at startup (no scene wiring); clips load from Resources/Sfx.
/// All sounds are 2D — the arena is small, positional audio adds nothing.
/// </summary>
public class SfxManager : MonoBehaviour
{
    public static SfxManager Instance { get; private set; }

    private AudioSource _source;

    private AudioClip _shoot;
    private AudioClip _hitShield;
    private AudioClip _hitHull;
    private AudioClip _explosion;
    private AudioClip _powerUp;
    private AudioClip _kill;
    private AudioClip _matchEnd;
    private AudioClip _tick;

    // Local shots fire often and from close by - keep them quieter than events.
    private const float ShootVolume = 0.35f;
    private const float HitVolume = 0.7f;
    private const float ExplosionVolume = 0.9f;
    private const float UiVolume = 0.8f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        var go = new GameObject("SfxManager");
        go.AddComponent<SfxManager>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f; // 2D

        _shoot = Resources.Load<AudioClip>("Sfx/sfx_shoot");
        _hitShield = Resources.Load<AudioClip>("Sfx/sfx_hit_shield");
        _hitHull = Resources.Load<AudioClip>("Sfx/sfx_hit_hull");
        _explosion = Resources.Load<AudioClip>("Sfx/sfx_explosion");
        _powerUp = Resources.Load<AudioClip>("Sfx/sfx_powerup");
        _kill = Resources.Load<AudioClip>("Sfx/sfx_kill");
        _matchEnd = Resources.Load<AudioClip>("Sfx/sfx_match_end");
        _tick = Resources.Load<AudioClip>("Sfx/sfx_tick");

        StartCoroutine(SubscribeToScoreManager());
    }

    // ScoreManager is created by GameSetup after scene load - wait for it.
    private IEnumerator SubscribeToScoreManager()
    {
        while (ScoreManager.Instance == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        ScoreManager.Instance.OnScoreChanged += OnScoreChanged;
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
        }
    }

    private void OnScoreChanged(ulong clientId, int localPlayerIndex, int newScore)
    {
        // Kill confirm only for kills scored on this device (any local co-op pilot)
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null && clientId == nm.LocalClientId)
        {
            Play(_kill, UiVolume);
        }
    }

    private void Play(AudioClip clip, float volume, float pitchJitter = 0f)
    {
        if (clip == null || _source == null) return;
        _source.pitch = pitchJitter > 0f ? 1f + Random.Range(-pitchJitter, pitchJitter) : 1f;
        _source.PlayOneShot(clip, volume);
    }

    // --- public API (safe when Instance is null, e.g. in tests) ---

    // Terrain debris: up to 80 tiles can be airborne at once (MaxFalling),
    // so this is heavily rate-limited or destruction turns into white noise.
    private float _lastDebrisTime;
    public static void PlayDebris()
    {
        var i = Instance;
        if (i == null) return;
        if (Time.unscaledTime - i._lastDebrisTime < 0.1f) return;
        i._lastDebrisTime = Time.unscaledTime;
        i.Play(i._hitHull, 0.25f, 0.15f);
    }

    public static void PlayShoot() => Instance?.Play(Instance._shoot, ShootVolume, 0.08f);
    public static void PlayShieldHit() => Instance?.Play(Instance._hitShield, HitVolume, 0.05f);
    public static void PlayHullHit() => Instance?.Play(Instance._hitHull, HitVolume, 0.05f);
    public static void PlayExplosion() => Instance?.Play(Instance._explosion, ExplosionVolume, 0.06f);
    public static void PlayPowerUp() => Instance?.Play(Instance._powerUp, UiVolume);
    public static void PlayMatchEnd() => Instance?.Play(Instance._matchEnd, UiVolume);
    public static void PlayTick() => Instance?.Play(Instance._tick, UiVolume);

    /// <summary>Vibrate on mobile (own death, big moments). No-op elsewhere.</summary>
    public static void Haptic()
    {
#if UNITY_ANDROID || UNITY_IOS
        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.IPhonePlayer)
        {
            Handheld.Vibrate();
        }
#endif
    }
}
