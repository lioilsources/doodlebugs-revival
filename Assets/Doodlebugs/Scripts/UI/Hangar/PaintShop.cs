using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The buying half of the PLANE tab's paint strip (Phase 4 "IAP naostro"):
/// BUY cards with the store's localised price in front of every locked pack,
/// one ALL PACKS card, the RESTORE PURCHASES pill (App Store requirement) and
/// the purchase feedback - banner text, sound, haptic and the paint strip
/// unlocking at once (ROADMAP §3 rule 3: four channels per purchase).
/// Own class over Ui; GameHUD untouched. Premium paints are cosmetic only.
/// Everything here is inert without DOODLEBUGS_IAP (desktop builds).
/// </summary>
public static class PaintShop
{
    public const float CardWidth = 118f;
    public const float CardHeight = 48f;

#if DOODLEBUGS_IAP
    private static bool _hooked;
    private static int _wantSkin = -1;   // paint tapped before buying: equip it once bought

    /// <summary>Subscribes the purchase feedback once (GameSetup, after IAPManager.GetOrCreate).</summary>
    public static void Hook(IAPManager iap)
    {
        if (_hooked || iap == null) return;
        _hooked = true;
        iap.OnPurchased += OnPurchased;
        iap.OnPurchaseFailed += OnPurchaseFailed;
        iap.OnPurchaseDeferred += OnPurchaseDeferred;
        iap.OnRestoreFinished += OnRestoreFinished;
    }

    /// <summary>A green BUY card for <paramref name="bundle"/>: pack name over "BUY $0.99".</summary>
    public static void BuyCard(Transform parent, IAPManager.SkinBundle bundle, int wantSkin = -1)
    {
        var iap = IAPManager.Instance;
        var card = Ui.Card(parent, "Buy_" + bundle.StoreId, UiTheme.Confirm, 12f, 4f);
        card.Root.sizeDelta = new Vector2(CardWidth, CardHeight);

        var name = Ui.Text(card.Face.transform, "Pack", Loc.Upper(bundle.LocalizedName), UiTheme.Label,
            UiTheme.SizeSmall, UiTheme.OnConfirm, TextAnchor.MiddleCenter);
        Ui.TopBand(name.rectTransform, 4f, 18f, 4f, 4f);
        name.resizeTextForBestFit = true;
        name.resizeTextMinSize = 9;
        name.resizeTextMaxSize = UiTheme.SizeSmall;

        string price = iap != null ? iap.PriceFor(bundle) : bundle.PlaceholderPrice;
        var buy = Ui.Text(card.Face.transform, "Price", Loc.F("shop.buy", price), UiTheme.BodyStrong,
            UiTheme.SizeBody, UiTheme.OnConfirm, TextAnchor.MiddleCenter);
        Ui.TopBand(buy.rectTransform, 22f, 20f, 4f, 4f);
        buy.resizeTextForBestFit = true;
        buy.resizeTextMinSize = 9;
        buy.resizeTextMaxSize = UiTheme.SizeBody;

        var captured = bundle;
        Ui.Press(card.Face, () => Buy(captured, wantSkin));
    }

    /// <summary>The RESTORE PURCHASES pill, right-aligned in <paramref name="row"/>.</summary>
    public static void RestorePill(RectTransform row)
    {
        var (r, bg, _) = Ui.Pill(row, "Restore", Loc.T("shop.restore"), UiTheme.GlassLight, UiTheme.OnBackdrop, 22f);
        r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
        r.pivot = new Vector2(1f, 0.5f);
        r.anchoredPosition = Vector2.zero;
        bg.raycastTarget = true;
        Ui.Press(bg, Restore);
    }

    public static void Buy(IAPManager.SkinBundle bundle, int wantSkin)
    {
        var iap = IAPManager.Instance;
        if (iap == null || !iap.Purchase(bundle))
        {
            Notice("shop.unavailable", UiTheme.Danger, HapticKind.Light);
            return;
        }
        _wantSkin = wantSkin;
    }

    public static void Restore()
    {
        var iap = IAPManager.Instance;
        if (iap == null || !iap.Restore())
        {
            Notice("shop.unavailable", UiTheme.Danger, HapticKind.Light);
            return;
        }
        Notice("shop.restoring", UiTheme.Highlight, HapticKind.Light);
    }

    private static void OnPurchased(IAPManager.SkinBundle bundle)
    {
        BannerOverlay.Show(Loc.Upper(Loc.F("shop.unlocked", bundle.LocalizedName)), UiTheme.Highlight);
        SfxManager.PlayPowerUp();
        SfxManager.Haptic(HapticKind.Success);
        HangarScreen.Instance?.PunchHero();

        int want = _wantSkin;
        _wantSkin = -1;
        if (want >= 0 && System.Array.IndexOf(bundle.SkinIds, want) >= 0)
            PlaneTab.SendPick(want, false);
    }

    private static void OnPurchaseFailed(bool cancelled)
    {
        _wantSkin = -1;
        if (cancelled) return;   // the player backed out - nothing to say
        Notice("shop.failed", UiTheme.Danger, HapticKind.Medium);
    }

    private static void OnPurchaseDeferred() => Notice("shop.pending", UiTheme.Highlight, HapticKind.Light);

    private static void OnRestoreFinished(IAPManager.RestoreResult result)
    {
        switch (result)
        {
            case IAPManager.RestoreResult.Restored:
                BannerOverlay.Show(Loc.Upper(Loc.T("shop.restored")), UiTheme.Highlight);
                SfxManager.PlayPowerUp();
                SfxManager.Haptic(HapticKind.Success);
                break;
            case IAPManager.RestoreResult.NothingToRestore:
                Notice("shop.nothing_to_restore", UiTheme.Highlight, HapticKind.Light);
                break;
            default:
                Notice("shop.failed", UiTheme.Danger, HapticKind.Medium);
                break;
        }
    }

    private static void Notice(string key, Color color, HapticKind haptic)
    {
        BannerOverlay.Show(Loc.Upper(Loc.T(key)), color);
        SfxManager.PlayTick();
        SfxManager.Haptic(haptic);
    }
#endif
}
