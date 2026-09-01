using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Occasional wandering advertisement: a billboard drifting across the sky
/// right-to-left, or slowly falling from above until it sinks below the
/// terrain. Purely local-visual cosmetics — no collider, no NetworkObject —
/// so a small desync between clients is invisible and acceptable, the same
/// contract as terrain debris. Runtime-created like SfxManager; sprites load
/// from Resources/Sprites/FloatingAds.
/// </summary>
public class FloatingAdManager : MonoBehaviour
{
    public static FloatingAdManager Instance { get; private set; }

    private const float MinIntervalSeconds = 45f;
    private const float MaxIntervalSeconds = 120f;
    // Planes render at sortingOrder 100, clouds at 110 (they hide planes);
    // ads glide behind both but in front of the flat background.
    private const int SortingOrder = 5;
    private const float ArenaHalfWidth = 32f;   // camera shows 54 units; spawn past the edge

    private Sprite[] _sprites;
    private readonly List<GameObject> _live = new List<GameObject>();
    private float _nextSpawnAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        var go = new GameObject("FloatingAdManager");
        go.AddComponent<FloatingAdManager>();
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
        _sprites = Resources.LoadAll<Sprite>("Sprites/FloatingAds");
        ScheduleNext();
    }

    private void ScheduleNext()
    {
        _nextSpawnAt = Time.time + Random.Range(MinIntervalSeconds, MaxIntervalSeconds);
    }

    private void Update()
    {
        if (_sprites == null || _sprites.Length == 0) return;
        _live.RemoveAll(go => go == null);
        if (Time.time >= _nextSpawnAt && _live.Count < 2)
        {
            Spawn();
            ScheduleNext();
        }
    }

    private void Spawn()
    {
        var go = new GameObject("FloatingAd");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _sprites[Random.Range(0, _sprites.Length)];
        sr.sortingOrder = SortingOrder;
        var mover = go.AddComponent<FloatingAdMover>();

        if (Random.value < 0.6f)
        {
            // Drift: enters stage right above the skyline, floats left.
            go.transform.position = new Vector3(ArenaHalfWidth + 4f, Random.Range(14f, 22f), 0f);
            mover.velocity = new Vector2(-Random.Range(1.2f, 2.4f), 0f);
            mover.bobAmplitude = Random.Range(0.2f, 0.6f);
            mover.killBelowX = -(ArenaHalfWidth + 6f);
        }
        else
        {
            // Fall: released somewhere over the arena, sinks with a lazy sway.
            go.transform.position = new Vector3(
                Random.Range(-ArenaHalfWidth, ArenaHalfWidth), 26f, 0f);
            mover.velocity = new Vector2(0f, -Random.Range(0.8f, 1.5f));
            mover.swayAmplitude = Random.Range(0.4f, 1.0f);
            mover.spinDegPerSec = Random.Range(-12f, 12f);
            mover.killBelowY = -6f;
        }
        _live.Add(go);
    }
}

/// <summary>Motion for one floating ad; destroys itself off-stage.</summary>
public class FloatingAdMover : MonoBehaviour
{
    public Vector2 velocity;
    public float bobAmplitude;     // vertical sine while drifting
    public float swayAmplitude;    // horizontal sine while falling
    public float spinDegPerSec;
    public float killBelowX = float.NegativeInfinity;
    public float killBelowY = float.NegativeInfinity;

    private float _t;
    private Vector3 _base;

    private void Start() => _base = transform.position;

    private void Update()
    {
        _t += Time.deltaTime;
        _base += (Vector3)(velocity * Time.deltaTime);
        var offset = new Vector3(
            swayAmplitude * Mathf.Sin(_t * 0.9f),
            bobAmplitude * Mathf.Sin(_t * 1.3f), 0f);
        transform.position = _base + offset;
        if (spinDegPerSec != 0f)
        {
            transform.Rotate(0f, 0f, spinDegPerSec * Time.deltaTime);
        }
        if (_base.x < killBelowX || _base.y < killBelowY)
        {
            Destroy(gameObject);
        }
    }
}
