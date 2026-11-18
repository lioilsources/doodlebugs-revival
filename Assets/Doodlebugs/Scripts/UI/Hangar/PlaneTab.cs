using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PLANE tab (Prompts/30 §4): a strip of shapes (one-of-a-kind per match,
/// taken ones show who flies them) above a strip of paints. Every tap goes
/// to the server like before; the big plane in the hangar's left column
/// shows the result. Premium paints say "soon" and buy nothing while the
/// store products do not exist (D4).
/// </summary>
public class PlaneTab : MonoBehaviour
{
    private enum Filter { All, Free, Premium }

    private RectTransform _shapeContent, _paintContent;
    private readonly List<(GameObject go, Text label)> _filterPills = new();
    private Filter _filter = Filter.All;
    private float _refreshAt = -1f;
    private int _lastClaimHash;

    public static PlaneTab Build(RectTransform parent)
    {
        var root = Ui.Node(parent, "PlaneTab");
        Ui.Inset(root, 0, 0, 0, 0);
        var tab = root.gameObject.AddComponent<PlaneTab>();
        tab.BuildLayout(root);
        tab.Refresh();
        return tab;
    }

    private static RectTransform Strip(RectTransform parent, string name, float top, float height, float spacing)
    {
        var view = Ui.Node(parent, name);
        Ui.TopBand(view, top, height, 0f, -16f);   // bleed to the screen edge on the right
        var mask = view.gameObject.AddComponent<RectMask2D>();
        mask.padding = new Vector4(0f, -8f, 0f, -8f);
        var hit = view.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.decelerationRate = 0.12f;
        scroll.viewport = view;

        var content = Ui.Node(view, "Content");
        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 0.5f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        var h = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.padding = new RectOffset(0, 24, 0, 6);
        h.childControlWidth = false;
        h.childControlHeight = false;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.UpperLeft;
        content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;
        return content;
    }

    private void BuildLayout(RectTransform root)
    {
        // Fits a content column of ~184 units (iPhone with the home
        // indicator inset); the paint row must stay clear of the CTA below.
        _shapeContent = Strip(root, "Shapes", 0f, 104f, 10f);

        var head = Ui.Node(root, "PaintHead");
        Ui.TopBand(head, 108f, 22f);
        var hl = head.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8f;
        hl.childControlWidth = true;
        hl.childControlHeight = false;
        hl.childForceExpandWidth = false;
        hl.childAlignment = TextAnchor.MiddleLeft;
        Ui.AutoText(head, "Title", "PAINT", UiTheme.Label, 15, UiTheme.OnBackdrop);
        foreach (Filter f in System.Enum.GetValues(typeof(Filter)))
        {
            string text = f == Filter.All ? "All " + PlaneSkinCatalog.Count
                        : f == Filter.Free ? "Free" : "Premium · soon";
            var (r, bg, label) = Ui.Pill(head, "F_" + f, text, UiTheme.GlassLight, UiTheme.OnBackdrop, 22f);
            var captured = f;
            bg.raycastTarget = true;
            Ui.Press(bg, () => { _filter = captured; Refresh(); });
            _filterPills.Add((r.gameObject, label));
        }

        _paintContent = Strip(root, "Paints", 134f, 52f, 8f);
    }

    private void Update()
    {
        // Claims stream in one by one on a join; coalesce into one rebuild
        var mgr = PlaneSkinManager.Instance;
        int hash = 17;
        if (mgr != null && mgr.IsSpawned)
        {
            foreach (var c in mgr.Claims) hash = hash * 31 + c.GetHashCode();
        }
        if (hash != _lastClaimHash) { _lastClaimHash = hash; _refreshAt = Time.unscaledTime + 0.1f; }
        if (_refreshAt > 0f && Time.unscaledTime >= _refreshAt) { _refreshAt = -1f; Refresh(); }
    }

    public void Refresh()
    {
        var owned = HangarScreen.OwnedPlane();
        var appearance = owned != null ? owned.GetComponent<PlaneAppearance>() : null;
        int model = appearance != null ? appearance.NetModelId.Value : PlaneModelCatalog.BaseModelId;
        int skin = appearance != null ? appearance.NetSkinId.Value : PlaneSkinCatalog.StarterSkinId;
        ulong me = owned != null ? owned.OwnerClientId : 0;
        int myIdx = owned != null ? Mathf.Max(0, owned.LocalPlayerIndex) : 0;

        BuildShapes(model, skin, me, myIdx);
        BuildPaints(model, skin);

        for (int i = 0; i < _filterPills.Count; i++)
        {
            bool on = (int)_filter == i;
            var bg = _filterPills[i].go.GetComponent<Image>();
            bg.color = on ? UiTheme.TabOn : UiTheme.GlassLight;
            _filterPills[i].label.color = on ? UiTheme.TabOnText : UiTheme.OnBackdrop;
        }
    }

    private void BuildShapes(int model, int skin, ulong me, int myIdx)
    {
        foreach (Transform c in _shapeContent) Destroy(c.gameObject);
        var mgr = PlaneSkinManager.Instance;

        foreach (int id in PlaneModelCatalog.Available)
        {
            var def = PlaneModelCatalog.Get(id);
            bool mine = id == model;
            bool taken = !mine && mgr != null && mgr.IsModelTaken(id, me, myIdx);

            var card = Ui.Card(_shapeContent, "Shape_" + def.Key,
                mine ? UiTheme.Pick : taken ? UiTheme.PaperDim : UiTheme.Paper, UiTheme.RadiusCard, 5f);
            card.Root.sizeDelta = new Vector2(128f, 96f);

            var img = Ui.Sprite(card.Face.transform, "Plane", PlaneModelCatalog.LoadSprite(id, skin));
            Ui.Place(img.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -3f), new Vector2(52f, 52f));
            if (taken) img.color = new Color(1f, 1f, 1f, 0.35f);
            else img.color = UiTheme.Active.ArtTint;

            var name = Ui.Text(card.Face.transform, "Name", def.DisplayName, UiTheme.Display, 18, mine ? UiTheme.OnPick : UiTheme.Ink, TextAnchor.MiddleCenter);
            Ui.TopBand(name.rectTransform, 55f, 20f);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            var el = ElementProfile.Get(def.Element);
            var elem = Ui.Text(card.Face.transform, "Element", el.DisplayName, UiTheme.Label, UiTheme.SizeSmall,
                mine ? UiTheme.OnPick : UiTheme.TextDim, TextAnchor.MiddleCenter);
            Ui.TopBand(elem.rectTransform, 75f, 16f);

            string chip = mine ? "YOURS" : taken ? WhoFlies(id) : null;
            if (chip != null)
            {
                var (r, _, _) = Ui.Pill(card.Face.transform, "Chip", chip, UiTheme.Ink, UiTheme.Paper, 18f, 12, UiTheme.Label);
                r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
                r.pivot = new Vector2(0f, 1f);
                r.anchoredPosition = new Vector2(4f, -4f);
            }

            if (!taken && !mine)
            {
                int captured = id;
                Ui.Press(card.Face, () => Select(captured, true));
            }
        }
    }

    private static string WhoFlies(int modelId)
    {
        var mgr = PlaneSkinManager.Instance;
        if (mgr == null) return "TAKEN";
        foreach (var c in mgr.Claims)
        {
            if (c.ModelId != modelId) continue;
            foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (p.OwnerClientId == c.ClientId && Mathf.Max(0, p.LocalPlayerIndex) == c.LocalPlayerIndex && !p.IsBot)
                {
                    string n = p.PlayerName;
                    int hash = n.IndexOf('#');
                    if (hash > 0) n = n.Substring(0, hash);
                    return n.Length > 10 ? n.Substring(0, 10).ToUpperInvariant() : n.ToUpperInvariant();
                }
            }
        }
        return "TAKEN";
    }

    private static readonly Dictionary<int, Sprite> _swatchSprites = new();

    private static Sprite Swatch(int skinId, int model)
    {
        if (_swatchSprites.TryGetValue(skinId, out var s) && s != null) return s;
        var tex = PlaneSkinCatalog.LoadSwatch(skinId);
        s = tex != null
            ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f)
            : null;
        if (s != null) _swatchSprites[skinId] = s;
        return s;
    }

    private void BuildPaints(int model, int skin)
    {
        foreach (Transform c in _paintContent) Destroy(c.gameObject);
        var iap = IAPManager.Instance;

        for (int i = 0; i < PlaneSkinCatalog.Count; i++)
        {
            var def = PlaneSkinCatalog.Get(i);
            bool unlocked = iap == null || iap.IsSkinUnlocked(i);
            if (_filter == Filter.Free && def.IsPremium) continue;
            if (_filter == Filter.Premium && !def.IsPremium) continue;
            bool mine = i == skin;

            var r = Ui.Node(_paintContent, "Paint_" + def.Key);
            r.sizeDelta = new Vector2(48f, 48f);
            var shadow = Ui.Round(r, "Shadow", UiTheme.Active.ShadowColor, 12f);
            shadow.rectTransform.offsetMin = new Vector2(0f, -4f);
            shadow.rectTransform.offsetMax = new Vector2(0f, -4f);
            var ring = Ui.Round(r, "Ring", mine ? UiTheme.Pick : UiTheme.Line, 12f);
            var face = Ui.Round(r, "Face", Color.white, 9f, true);
            Ui.Inset(face.rectTransform, mine ? 4f : 3f, mine ? 4f : 3f, mine ? 4f : 3f, mine ? 4f : 3f);

            var sw = Swatch(i, model);
            if (sw != null)
            {
                // Swatch texture inside the rounded face (masked)
                face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var tex = Ui.Sprite(face.transform, "Tex", sw);
                tex.preserveAspect = false;
                Ui.Inset(tex.rectTransform, 0, 0, 0, 0);
                if (!unlocked) tex.color = new Color(1f, 1f, 1f, 0.45f);
            }
            else
            {
                var plane = Ui.Sprite(face.transform, "Plane", PlaneModelCatalog.LoadSprite(model, i));
                Ui.Inset(plane.rectTransform, 2, 2, 2, 2);
            }

            if (!unlocked)
            {
                var lk = Ui.Sprite(r, "Lock", UiSprites.Lock);
                lk.color = UiTheme.Ink;
                Ui.Inset(lk.rectTransform, 12, 12, 12, 12);
                continue;   // D4: nothing to buy yet
            }
            if (!mine)
            {
                int captured = i;
                Ui.Press(face, () => Select(captured, false));
            }
        }
    }

    private void Select(int id, bool model)
    {
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (!p.IsOwner || p.IsBot) continue;
            var a = p.GetComponent<PlaneAppearance>();
            if (a == null) continue;
            if (model) a.RequestSelectModelServerRpc(id);
            else a.RequestSelectSkinServerRpc(id);
        }
        SfxManager.PlayConfirm();
        SfxManager.Haptic(HapticKind.Light);
        Telemetry.Log(model ? "plane_pick" : "skin_pick", ("id", id));
        _refreshAt = Time.unscaledTime + 0.15f;
        HangarScreen.Instance?.PunchHero();
    }

    private void OnDestroy()
    {
        // Free the composited previews, keep what planes wear
        var worn = new List<(int, int)>();
        foreach (var a in FindObjectsByType<PlaneAppearance>(FindObjectsSortMode.None))
            worn.Add((a.NetModelId.Value, a.NetSkinId.Value));
        PlaneModelCatalog.TrimComposites(worn);
    }
}
