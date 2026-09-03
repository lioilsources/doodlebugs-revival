using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension; // PurchaseFailureDescription (IAP 4.12 keeps it out of the root namespace)

/// <summary>
/// Runtime-created singleton (same convention as SfxManager - no scene
/// wiring) that owns skin entitlements: which premium skin bundles this
/// device has bought, gating the hangar picker's "Buy" buttons.
///
/// TRUST MODEL: entitlement is checked LOCALLY, on the buyer's own device,
/// using Unity IAP's receipt store - the same way every mobile game gates
/// purchased content. The multiplayer server (this game's host peer, not a
/// cloud backend - there is no backend, see CLAUDE.md) never re-validates a
/// remote client's receipt; PlaneSkinManager only enforces "not already
/// taken". A modified client could already fake a weapon pick
/// (RequestSelectWeaponServerRpc trusts its caller completely) - a faked
/// skin pick is the same class of problem, not a new one, and solving it
/// would need a real backend this project does not have. Acceptable for a
/// couch/LAN party game; revisit only if the game grows a trusted backend.
///
/// ### Manual setup this class cannot do for you
/// 1. Package: Window -> Package Manager -> "In App Purchasing" -> Install.
///    This adds the com.unity.purchasing dependency (a version placeholder
///    is already in Packages/manifest.json - bump it to whatever Package
///    Manager resolves).
/// 2. Unity Gaming Services: link this project (Services window) - Unity
///    IAP's receipt validation needs a UGS project id.
/// 3. Create the real store products in App Store Connect AND Google Play
///    Console - one non-consumable product per SkinBundle.StoreId below.
///    Nobody but the account owner can do this; there is no API shortcut.
/// 4. Pricing: SkinBundles below ships with $2.99 as a clearly-marked
///    placeholder for every bundle. Real price tiers are a business call -
///    set them in each store console, this file only needs the StoreId to
///    match.
/// </summary>
public class IAPManager : MonoBehaviour, IDetailedStoreListener
{
    public static IAPManager Instance { get; private set; }

    public readonly struct SkinBundle
    {
        public readonly string StoreId;       // must match the product id created in both store consoles
        public readonly string DisplayName;
        public readonly string PlaceholderPrice; // UI fallback before the store returns real localized pricing
        public readonly int[] SkinIds;

        public SkinBundle(string storeId, string displayName, string placeholderPrice, int[] skinIds)
        {
            StoreId = storeId;
            DisplayName = displayName;
            PlaceholderPrice = placeholderPrice;
            SkinIds = skinIds;
        }
    }

    // 4 bundles matching PlaneSkinCatalog's BundleId groups (9-10 skins
    // each) rather than 38 individual products - assumption flagged in
    // PlaneSkinCatalog's own header comment, cheap to split up later.
    public static readonly SkinBundle[] SkinBundles =
    {
        new("skins_camo_pack",     "Camo Pack",     "$2.99", Range(12, 20)),
        new("skins_metallic_pack", "Metallic Pack",  "$2.99", Range(21, 29)),
        new("skins_cosmic_pack",   "Cosmic Pack",    "$2.99", Range(30, 38)),
        new("skins_homage_pack",   "Homage Pack",    "$3.99", Range(39, 49)),
    };

    private static int[] Range(int first, int last)
    {
        var ids = new int[last - first + 1];
        for (int i = 0; i < ids.Length; i++) ids[i] = first + i;
        return ids;
    }

    private IStoreController _controller;
    private bool _initialized;
    private readonly HashSet<string> _ownedBundleIds = new();

    public event Action OnEntitlementsChanged;

    public static IAPManager GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("IAPManager");
        DontDestroyOnLoad(go);
        return go.AddComponent<IAPManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        LoadCachedEntitlements();
        InitializePurchasing();
    }

    private void InitializePurchasing()
    {
        var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
        foreach (var bundle in SkinBundles)
        {
            builder.AddProduct(bundle.StoreId, ProductType.NonConsumable);
        }
        UnityPurchasing.Initialize(this, builder);
    }

    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        _controller = controller;
        _initialized = true;

        foreach (var bundle in SkinBundles)
        {
            var product = controller.products.WithID(bundle.StoreId);
            if (product != null && product.hasReceipt)
            {
                _ownedBundleIds.Add(bundle.StoreId);
            }
        }
        SaveCachedEntitlements();
        OnEntitlementsChanged?.Invoke();
    }

    public void OnInitializeFailed(InitializationFailureReason error) =>
        Debug.LogWarning($"[IAPManager] Init failed: {error} - falling back to cached/offline entitlements");

    public void OnInitializeFailed(InitializationFailureReason error, string message) =>
        Debug.LogWarning($"[IAPManager] Init failed: {error} ({message}) - falling back to cached/offline entitlements");

    public SkinBundle? BundleForSkin(int skinId)
    {
        foreach (var b in SkinBundles)
        {
            if (b.SkinIds.Contains(skinId)) return b;
        }
        return null;
    }

    public bool IsSkinUnlocked(int skinId)
    {
        if (!PlaneSkinCatalog.Get(skinId).IsPremium) return true;
        var bundle = BundleForSkin(skinId);
        return bundle.HasValue && _ownedBundleIds.Contains(bundle.Value.StoreId);
    }

    public void PurchaseBundle(string storeId)
    {
        if (!_initialized || _controller == null)
        {
            Debug.LogWarning("[IAPManager] Store not initialized yet");
            return;
        }
        _controller.InitiatePurchase(storeId);
    }

    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
    {
        _ownedBundleIds.Add(args.purchasedProduct.definition.id);
        SaveCachedEntitlements();
        OnEntitlementsChanged?.Invoke();
        Debug.Log($"[IAPManager] Purchased {args.purchasedProduct.definition.id}");
        return PurchaseProcessingResult.Complete;
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureReason reason) =>
        Debug.LogWarning($"[IAPManager] Purchase failed for {product.definition.id}: {reason}");

    public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription) =>
        Debug.LogWarning($"[IAPManager] Purchase failed for {product.definition.id}: {failureDescription.reason} {failureDescription.message}");

    // Cached to PlayerPrefs so the picker still shows owned skins as owned
    // offline / before the store finishes initializing - refreshed from the
    // real receipt store as soon as OnInitialized runs.
    private const string PrefsKey = "Doodlebugs.OwnedSkinBundles";

    private void LoadCachedEntitlements()
    {
        var raw = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(raw)) return;
        foreach (var id in raw.Split(',')) _ownedBundleIds.Add(id);
    }

    private void SaveCachedEntitlements() =>
        PlayerPrefs.SetString(PrefsKey, string.Join(",", _ownedBundleIds));
}
