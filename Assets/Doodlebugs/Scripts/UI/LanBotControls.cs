using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The party host's "+ BOT" / "- BOT" pair (Prompts/27 S8, WP6) in the
/// Waiting, PreBattle and Intermission hangars, above the bottom-right
/// action button. Up to MatchManager.LanBotParticipantCap participants,
/// tier Pilot. Its own class so GameHUD only calls AddButtons.
/// </summary>
public class LanBotControls : MonoBehaviour
{
    private static readonly Color AddColor = new Color(0.12f, 0.3f, 0.45f, 1f);
    private static readonly Color RemoveColor = new Color(0.3f, 0.3f, 0.34f, 1f);
    private static readonly Color Disabled = new Color(0.12f, 0.12f, 0.14f, 1f);

    private Button _add, _remove;
    private Image _addBg, _removeBg;

    public static void AddButtons(Transform hangar)
    {
        var go = new GameObject("LanBotControls");
        go.transform.SetParent(hangar, false);
        UiKit.Stretch(go);
        go.AddComponent<LanBotControls>();
    }

    private void Awake()
    {
        var corner = new Vector2(1f, 0f);
        (_remove, _removeBg, _) = UiKit.FlatButton(transform, "RemoveBot", Loc.T("lan.remove_bot"), 14,
            corner, corner, new Vector2(-214, 116), new Vector2(146, 56), RemoveColor, OnRemove);
        (_add, _addBg, _) = UiKit.FlatButton(transform, "AddBot", Loc.T("lan.add_bot"), 14,
            corner, corner, new Vector2(-60, 116), new Vector2(146, 56), AddColor, OnAdd);
        Refresh();
    }

    private void Update() => Refresh();

    private void Refresh()
    {
        var mm = MatchManager.Instance;
        bool canAdd = mm != null && mm.CanAddLanBot;
        bool canRemove = mm != null && mm.CanRemoveLanBot;
        if (_add.interactable != canAdd) { _add.interactable = canAdd; _addBg.color = canAdd ? AddColor : Disabled; }
        if (_remove.interactable != canRemove) { _remove.interactable = canRemove; _removeBg.color = canRemove ? RemoveColor : Disabled; }
    }

    private static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    private void OnAdd()
    {
        if (!IsHost) return;
        SfxManager.PlayTick();
        MatchManager.Instance?.ServerAddLanBot();
    }

    private void OnRemove()
    {
        if (!IsHost) return;
        SfxManager.PlayTick();
        MatchManager.Instance?.ServerRemoveLanBot();
    }
}
