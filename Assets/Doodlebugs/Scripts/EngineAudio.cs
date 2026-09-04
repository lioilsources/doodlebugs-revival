using UnityEngine;
using Unity.Netcode;

public class EngineAudio : NetworkBehaviour
{
    [Header("Audio Clips")]
    public AudioClip engineHost;
    public AudioClip engineClient;

    [Header("Mix")]
    [Tooltip("Engine loop level. It used to inherit the AudioSource's own 1.0 " +
             "while MusicManager runs at 0.35, so the engine simply buried the " +
             "score in both the hangar and the battle. 0 silences it entirely.")]
    [Range(0f, 1f)]
    public float volume = 0.12f;

    [Header("Pitch Settings")]
    public float engineOffPitch = 0.5f;
    public float minPitch = 0.83f;
    public float maxPitch = 1.33f;
    public float pitchSmoothTime = 0.3f;

    private AudioSource _audioSource;
    private PlayerController _player;
    private float _targetPitch = 1f;
    private float _currentPitch = 1f;
    private float _pitchVelocity;

    void Start()
    {
        _player = GetComponent<PlayerController>();
        _audioSource = GetComponent<AudioSource>();

        // Only play audio for the local player's plane
        if (!IsOwner)
        {
            if (_audioSource != null)
                _audioSource.enabled = false;
            return;
        }

        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Muted: don't spend a voice on a silent looping source.
        if (volume <= 0f)
        {
            _audioSource.enabled = false;
            return;
        }

        // Select melody based on role (host vs client)
        bool isHost = NetworkManager.Singleton != null &&
                      NetworkManager.Singleton.IsHost;
        _audioSource.clip = isHost ? engineHost : engineClient;
        _audioSource.volume = volume;
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.Play();
    }

    void Update()
    {
        if (!IsOwner || _player == null || _audioSource == null || !_audioSource.enabled) return;

        bool engineOff = _player.IsEngineOff;
        float speed = _player.Speed;

        // Determine target pitch
        if (engineOff)
        {
            _targetPitch = engineOffPitch;
        }
        else
        {
            float speedPercent = (speed - 2f) / 18f;
            _targetPitch = Mathf.Lerp(minPitch, maxPitch, speedPercent);
        }

        // Smooth transition
        _currentPitch = Mathf.SmoothDamp(_currentPitch, _targetPitch,
            ref _pitchVelocity, pitchSmoothTime);
        _audioSource.pitch = _currentPitch;
    }
}
