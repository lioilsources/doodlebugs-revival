using UnityEngine;

/// <summary>
/// The way out of a solo session (Prompts/27 S10): a corner HOME button
/// while a solo battle runs (no pause - pause is Time.timeScale, Phase 2),
/// and a HOME button in the solo intermission hangar. Its own class so
/// GameHUD only delegates. Same top-right slot as the warm-up HANGAR
/// button, which never shows in solo (solo skips the Waiting lobby).
/// </summary>
public class SoloControls : MonoBehaviour
{
    public static SoloControls Instance { get; private set; }

    private static readonly Color HomeColor = new Color(0.3f, 0.3f, 0.34f, 0.9f);

    private GameObject _corner;

    public static SoloControls Create(Canvas canvas)
    {
        var go = new GameObject("SoloControls");
        go.transform.SetParent(canvas.transform, false);
        UiKit.Stretch(go);
        return go.AddComponent<SoloControls>();
    }

    private void Awake()
    {
        Instance = this;
        _corner = UiKit.FlatButton(transform, "HomeCorner", Loc.T("solo.home"), 13,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60, -20), new Vector2(180, 44),
            HomeColor, LeaveToHome).button.gameObject;
        _corner.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        var mm = MatchManager.Instance;
        bool show = mm != null && mm.IsSoloSession &&
                    mm.Phase == MatchManager.GamePhase.Battle && !mm.IsIntermission &&
                    (GameHUD.Instance == null || !GameHUD.Instance.IsAnyOverlayOpen) &&
                    !HomeScreen.IsOpen;
        if (_corner.activeSelf != show) _corner.SetActive(show);
    }

    /// <summary>HOME in the solo intermission hangar and the party host's
    /// Waiting lobby, above SETTINGS in the host's bottom-left column
    /// (PLANE 40, SCENES 116, SETTINGS 192).</summary>
    public static void AddHangarHomeButton(Transform hangar)
    {
        UiKit.FlatButton(hangar, "HomeButton", Loc.T("solo.home"), 16,
            Vector2.zero, Vector2.zero, new Vector2(60, 268), new Vector2(180, 68),
            new Color(0.3f, 0.3f, 0.34f, 1f), LeaveToHome);
    }

    private static void LeaveToHome()
    {
        SfxManager.PlayTick();
        ProfileRecorder.LogRoundQuit();
        Doodlebugs.Network.ConnectionManager.Instance?.ReturnHome();
    }
}
