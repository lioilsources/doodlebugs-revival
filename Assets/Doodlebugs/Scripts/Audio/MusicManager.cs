using UnityEngine;

/// <summary>
/// Looping soundtrack, phase-aware. Auto-created at startup like SfxManager
/// (no scene wiring); loops load from Resources/Music.
///
/// Two beds: a calm hangar loop for every lobby/hangar state and a driving
/// battle loop for combat. MatchManager has no client-side phase event, so we
/// poll its Phase once per frame — one enum read, cheaper than plumbing a new
/// ClientRpc through, and late joiners are correct for free. Podium ducks the
/// music instead of switching: the sfx_match_end fanfare owns that moment.
/// </summary>
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    private const float Volume = 0.35f;
    private const float DuckVolume = 0.08f;
    private const float CrossfadeSeconds = 1f;

    private AudioSource _hangar;
    private AudioSource _battle;
    private float _battleWeight;   // 0 = hangar bed, 1 = battle bed
    private bool _ducked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        var go = new GameObject("MusicManager");
        go.AddComponent<MusicManager>();
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

        _hangar = CreateLoop("Music/music_hangar");
        _battle = CreateLoop("Music/music_battle");
        if (_hangar != null) _hangar.volume = Volume;
        if (_battle != null) _battle.volume = 0f;
    }

    private AudioSource CreateLoop(string resourcePath)
    {
        var clip = Resources.Load<AudioClip>(resourcePath);
        if (clip == null) return null;
        var source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.Play();
        return source;
    }

    private void Update()
    {
        if (_hangar == null || _battle == null) return;

        var mm = MatchManager.Instance;
        bool battle = mm != null && mm.Phase == MatchManager.GamePhase.Battle;
        _ducked = mm != null && mm.Phase == MatchManager.GamePhase.Podium;

        float target = battle ? 1f : 0f;
        _battleWeight = Mathf.MoveTowards(_battleWeight, target,
            Time.deltaTime / CrossfadeSeconds);

        float master = _ducked ? DuckVolume : Volume;
        // Equal-power crossfade keeps combined loudness level through the blend
        _hangar.volume = Mathf.Cos(_battleWeight * Mathf.PI / 2f) * master;
        _battle.volume = Mathf.Sin(_battleWeight * Mathf.PI / 2f) * master;
    }
}
