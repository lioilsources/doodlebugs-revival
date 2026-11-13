using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The party host's "+ BOT" / "- BOT" pair (Prompts/27 S8, WP6) in the
/// Waiting, PreBattle and Intermission hangars, above the CTA in the bottom
/// right. Up to MatchManager.LanBotParticipantCap participants, tier Pilot.
/// Its own class over the Ui kit; HangarScreen only creates it and shows it
/// per mode.
/// </summary>
public class LanBotControls : MonoBehaviour
{
    private (RectTransform root, Image bg, Text label) _add, _remove;
    private bool _canAdd = true, _canRemove = true;

    public static LanBotControls Create(RectTransform parent)
    {
        var row = Ui.Node(parent, "LanBotControls");
        Ui.Place(row, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 76f), new Vector2(204f, 44f));
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleRight;
        h.childControlWidth = true;
        h.childControlHeight = false;
        h.childForceExpandWidth = true;
        var c = row.gameObject.AddComponent<LanBotControls>();
        c._remove = Ui.Pill(row, "RemoveBot", Loc.T("lan.remove_bot"), UiTheme.Glass, UiTheme.OnBackdrop, 44f, 15, UiTheme.Label);
        c._add = Ui.Pill(row, "AddBot", Loc.T("lan.add_bot"), UiTheme.Glass, UiTheme.Highlight, 44f, 15, UiTheme.Label);
        c._remove.bg.raycastTarget = true;
        c._add.bg.raycastTarget = true;
        Ui.Press(c._remove.bg, c.OnRemove);
        Ui.Press(c._add.bg, c.OnAdd);
        c.Refresh();
        return c;
    }

    private void Update() => Refresh();

    private void Refresh()
    {
        var mm = MatchManager.Instance;
        bool canAdd = mm != null && mm.CanAddLanBot;
        bool canRemove = mm != null && mm.CanRemoveLanBot;
        if (canAdd != _canAdd) { _canAdd = canAdd; Dim(_add, canAdd); }
        if (canRemove != _canRemove) { _canRemove = canRemove; Dim(_remove, canRemove); }
    }

    private static void Dim((RectTransform root, Image bg, Text label) pill, bool on)
    {
        var c = pill.label.color;
        c.a = on ? 1f : 0.35f;
        pill.label.color = c;
        pill.bg.raycastTarget = on;
    }

    private static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    private void OnAdd()
    {
        if (!IsHost || !_canAdd) return;
        SfxManager.Haptic(HapticKind.Light);
        MatchManager.Instance?.ServerAddLanBot();
    }

    private void OnRemove()
    {
        if (!IsHost || !_canRemove) return;
        SfxManager.Haptic(HapticKind.Light);
        MatchManager.Instance?.ServerRemoveLanBot();
    }
}
