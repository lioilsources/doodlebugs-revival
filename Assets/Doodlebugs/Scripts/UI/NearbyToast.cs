using Doodlebugs.Network;
using UnityEngine;

/// <summary>
/// "PILOT NEARBY · JOIN" (Prompts/27 WP6): a non-modal chip under the match
/// timer when a solo session hears a party host on the LAN. Never joins on
/// its own - the round keeps running until the player taps it, and it fades
/// after <see cref="ShowSeconds"/>. Own class over UiKit; GameHUD untouched.
/// </summary>
public class NearbyToast : MonoBehaviour
{
    private const float ShowSeconds = 10f;
    private const float CooldownSeconds = 60f;   // per host, after an ignore

    private static readonly Color ChipColor = new Color(0.12f, 0.3f, 0.45f, 0.92f);

    private GameObject _chip;
    private ConnectionManager _connection;
    private float _hideAt;
    private float _quietUntil;
    private string _lastHost;

    public static NearbyToast Create(Canvas canvas)
    {
        var go = new GameObject("NearbyToast");
        go.transform.SetParent(canvas.transform, false);
        UiKit.Stretch(go);
        return go.AddComponent<NearbyToast>();
    }

    private void Awake()
    {
        _chip = UiKit.FlatButton(transform, "Chip", Loc.T("lan.nearby_join"), 12,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(460, 48),
            ChipColor, OnJoin).button.gameObject;
        _chip.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_connection != null) _connection.OnNearbyHost -= OnNearbyHost;
    }

    private void Update()
    {
        if (_connection == null && ConnectionManager.Instance != null)
        {
            _connection = ConnectionManager.Instance;
            _connection.OnNearbyHost += OnNearbyHost;
        }
        if (!_chip.activeSelf) return;

        bool solo = _connection != null && _connection.Session == SessionKind.Solo;
        if (!solo)
        {
            _chip.SetActive(false);
            return;
        }
        if (Time.unscaledTime >= _hideAt)
        {
            _chip.SetActive(false);
            _quietUntil = Time.unscaledTime + CooldownSeconds;
            Telemetry.Log("nearby_toast", ("action", "ignored"));
        }
    }

    private void OnNearbyHost(DiscoveryData host)
    {
        if (_connection == null || _connection.Session != SessionKind.Solo) return;
        if (host.instanceId == _lastHost && Time.unscaledTime < _quietUntil) return;
        _lastHost = host.instanceId;
        _hideAt = Time.unscaledTime + ShowSeconds;
        transform.SetAsLastSibling();
        _chip.SetActive(true);
        SfxManager.PlayTick();
        Telemetry.Log("nearby_toast", ("action", "shown"));
    }

    private void OnJoin()
    {
        _chip.SetActive(false);
        SfxManager.PlayTick();
        Telemetry.Log("nearby_toast", ("action", "joined"));
        ProfileRecorder.LogRoundQuit();
        _connection?.JoinNearby();
    }
}
