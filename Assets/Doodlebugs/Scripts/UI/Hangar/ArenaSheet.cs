using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Host-only arena rotation (Prompts/30 §4, D5): tiles to switch arenas on
/// and off, committed to the server on DONE. Order on the phone is the
/// catalog order for new entries; arenas already in the playlist keep their
/// place. At least one arena always stays on.
/// </summary>
public class ArenaSheet : MonoBehaviour
{
    private readonly List<int> _order = new();
    private readonly HashSet<int> _on = new();
    private RectTransform _grid;

    public static ArenaSheet Open(RectTransform parent)
    {
        var mgr = BackgroundManager.Instance;
        if (mgr == null) return null;

        var root = Ui.Node(parent, "ArenaSheet");
        Ui.Inset(root, 0, 0, 0, 0);
        var sheet = root.gameObject.AddComponent<ArenaSheet>();
        sheet.Build(root, mgr);
        return sheet;
    }

    private void Build(RectTransform root, BackgroundManager mgr)
    {
        var dim = root.gameObject.AddComponent<Image>();
        dim.color = UiTheme.WithAlpha(UiTheme.Active.VeilBottom, 0.78f);   // swallows touches

        foreach (int i in mgr.SceneOrder) { _order.Add(i); _on.Add(i); }
        for (int i = 0; i < mgr.BackgroundCount; i++)
            if (mgr.GetProfile(i) != null && !_order.Contains(i)) _order.Add(i);

        var card = Ui.Card(root, "Card", UiTheme.Paper);
        Ui.Place(card.Root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 330f));
        var face = card.Face.transform;

        var title = Ui.Text(face, "Title", "ARENA ROTATION", UiTheme.Display, UiTheme.SizeTitle, UiTheme.Ink);
        Ui.TopBand(title.rectTransform, 10f, 34f, 18f, 18f);
        var sub = Ui.Text(face, "Sub", "Tap an arena to drop it or bring it back.", UiTheme.Body, UiTheme.SizeBody, UiTheme.TextDim);
        Ui.TopBand(sub.rectTransform, 44f, 20f, 18f, 18f);

        _grid = Ui.Node(face, "Grid");
        Ui.Inset(_grid, 18f, 70f, 18f, 72f);
        var g = _grid.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(186f, 90f);
        g.spacing = new Vector2(10f, 10f);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 3;

        var done = Ui.BigButton(face, "Done", "DONE", UiTheme.Confirm, Close);
        Ui.Place(done.Panel.Root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 14f), new Vector2(150f, 50f));

        BuildTiles();
        UiTween.Scale(card.Root, Vector3.one * 0.9f, Vector3.one, 0.25f);
        SfxManager.PlayNamed("tab", 0.8f, 0f);
    }

    private void BuildTiles()
    {
        foreach (Transform c in _grid) Destroy(c.gameObject);
        var mgr = BackgroundManager.Instance;
        int slot = 0;
        foreach (int idx in _order)
        {
            var profile = mgr.GetProfile(idx);
            if (profile == null) continue;
            bool on = _on.Contains(idx);
            if (on) slot++;

            var tile = Ui.Card(_grid, "Arena_" + idx, on ? UiTheme.Pick : UiTheme.PaperDim, 12f, 4f);
            var sky = Ui.Round(tile.Face.transform, "Sky", Color.white, 9f);
            Ui.Inset(sky.rectTransform, 3, 3, 3, 3);
            sky.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var pic = Ui.Sprite(sky.transform, "Pic", profile.backgroundSprite);
            pic.preserveAspect = false;
            Ui.Inset(pic.rectTransform, 0, 0, 0, 0);
            if (profile.foregroundSprite != null)
            {
                var fg = Ui.Sprite(sky.transform, "Terrain", profile.foregroundSprite);
                fg.preserveAspect = false;
                Ui.BottomBand(fg.rectTransform, 0f, 30f);
            }
            if (!on) { pic.color = new Color(0.5f, 0.5f, 0.55f, 0.6f); }

            string name = profile.backgroundSprite != null ? profile.backgroundSprite.name : profile.name;
            var (pill, _, _) = Ui.Pill(tile.Face.transform, "Name", (on ? slot + ". " : "") + name.ToUpperInvariant(),
                UiTheme.Glass, UiTheme.OnBackdrop, 22f, 12, UiTheme.Label);
            pill.anchorMin = pill.anchorMax = new Vector2(0f, 0f);
            pill.pivot = new Vector2(0f, 0f);
            pill.anchoredPosition = new Vector2(6f, 6f);

            int captured = idx;
            Ui.Press(tile.Face, () => Toggle(captured));
        }
    }

    private void Toggle(int idx)
    {
        if (_on.Contains(idx))
        {
            if (_on.Count <= 1) return;   // the round needs an arena
            _on.Remove(idx);
        }
        else _on.Add(idx);
        Telemetry.Log("arena_toggle", ("arena", idx), ("on", _on.Contains(idx)));
        BuildTiles();
    }

    public void Close()
    {
        var mgr = BackgroundManager.Instance;
        var enabled = _order.Where(i => _on.Contains(i)).ToArray();
        if (mgr != null && enabled.Length > 0) mgr.RequestSetSceneOrderServerRpc(enabled);
        Destroy(gameObject);
    }
}
