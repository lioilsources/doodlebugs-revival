using UnityEngine;

/// <summary>
/// The way out of a solo session (Prompts/27 S10): a corner HOME button
/// while a solo battle runs (no pause - pause is Time.timeScale, Phase 2).
/// Between rounds the HOME pill in HangarScreen's top bar does the same.
/// Same top-right slot as the warm-up HANGAR corner, which never shows in
/// solo (solo skips the Waiting lobby).
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
                    !HangarScreen.IsOpen;
        if (_corner.activeSelf != show) _corner.SetActive(show);
    }

    /// <summary>Leave the session for the Home screen (solo or party).</summary>
    public static void LeaveToHome()
    {
        SfxManager.PlayTick();
        ProfileRecorder.LogRoundQuit();
        Doodlebugs.Network.ConnectionManager.Instance?.ReturnHome();
    }
}
