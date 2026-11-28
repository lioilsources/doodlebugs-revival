// Unity IAP 5 (com.unity.purchasing 5.5.x, PurchasingController). Only the
// mobile stores sell skins: DOODLEBUGS_IAP is defined for iOS and Android in
// ProjectSettings; desktop builds and the editor on a desktop target compile
// without it and hide premium paints (PlaneSkinCatalog.PremiumOffered).
#if DOODLEBUGS_IAP
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

/// <summary>
/// Runtime-created singleton (same convention as SfxManager - no scene
/// wiring) that owns skin entitlements: which premium paint packs this device
/// has bought. Products are non-consumable; prices follow the fixed tiers
/// (one pack $0.99, a bundle of more than two packs $1.99) and the store's
/// localised price string replaces the placeholder as soon as it arrives.
///
/// Flow (IAP 5 two-step): Connect -> FetchProducts -> FetchPurchases (owned
/// packs); PurchaseProduct -> OnPurchasePending (grant, then ConfirmPurchase)
/// -> OnPurchaseConfirmed. RestoreTransactions re-delivers owned packs through
/// OnPurchasePending (Apple) and is followed by a FetchPurchases.
///
/// TRUST MODEL: entitlement is checked LOCALLY, on the buyer's own device.
/// The multiplayer host never re-validates a remote client's receipt;
/// PlaneSkinManager only enforces "not already taken". Skins are pure
/// liveries (no gameplay effect, ROADMAP §3 rule 4), so a faked unlock costs
/// a sale, never a fair match. No Unity Gaming Services: the store
/// connection needs none and the game never calls UnityServices.InitializeAsync.
/// </summary>
public class IAPManager : MonoBehaviour
{
    public static IAPManager Instance { get; private set; }

    public readonly struct SkinBundle
    {
        public readonly string StoreId;          // product id in App Store Connect AND Play Console
        public readonly string NameKey;          // Loc key of the pack name
        public readonly string PlaceholderPrice; // shown until the store returns its localised price
        public readonly int[] SkinIds;

        public SkinBundle(string storeId, string nameKey, string placeholderPrice, int[] skinIds)
        {
            StoreId = storeId;
            NameKey = nameKey;
            PlaceholderPrice = placeholderPrice;
            SkinIds = skinIds;
        }

        public string LocalizedName => Loc.T(NameKey);
    }

    // Price tiers are a business rule: a pack $0.99, a bundle of more than
    // two packs $1.99. Nothing else. The real price lives in each store
    // console; these strings only show before the store answers.
    public const string PackPrice = "$0.99";
    public const string AllPacksPrice = "$1.99";

    /// <summary>The four paint packs, matching PlaneSkinCatalog's BundleId groups.</summary>
    public static readonly SkinBundle[] SkinBundles =
    {
        new("skins_camo_pack",     "shop.pack.camo",     PackPrice, Range(12, 20)),
        new("skins_metallic_pack", "shop.pack.metallic", PackPrice, Range(21, 29)),
        new("skins_cosmic_pack",   "shop.pack.cosmic",   PackPrice, Range(30, 38)),
        new("skins_homage_pack",   "shop.pack.homage",   PackPrice, Range(39, 49)),
    };

    /// <summary>One product that unlocks all four packs.</summary>
    public static readonly SkinBundle AllPacks =
        new("skins_all_packs", "shop.pack.all", AllPacksPrice, Range(12, 49));

    private static int[] Range(int first, int last)
    {
        var ids = new int[last - first + 1];
        for (int i = 0; i < ids.Length; i++) ids[i] = first + i;
        return ids;
    }

    public enum RestoreResult { Restored, NothingToRestore, Failed }

    /// <summary>Owned packs changed (purchase, restore, store sync).</summary>
    public event Action OnEntitlementsChanged;
    /// <summary>Localised prices arrived from the store.</summary>
    public event Action OnPricesChanged;
    /// <summary>A purchase went through (also an approved Ask to Buy).</summary>
    public event Action<SkinBundle> OnPurchased;
    /// <summary>A purchase failed; the flag is true when the player cancelled.</summary>
    public event Action<bool> OnPurchaseFailed;
    /// <summary>Waiting for a parent's approval (Ask to Buy) or a pending payment.</summary>
    public event Action OnPurchaseDeferred;
    public event Action<RestoreResult> OnRestoreFinished;

    private PurchasingController _controller;
    private string _store;
    private bool _connected;
    private bool _productsReady;
    private bool _restoring;
    private int _restoredCount;
    private readonly HashSet<string> _owned = new();
    private readonly HashSet<string> _purchaseInFlight = new();

    public bool StoreReady => _connected && _productsReady;
    public bool Restoring => _restoring;

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

    private static List<ProductDefinition> Definitions()
    {
        var defs = new List<ProductDefinition>();
        foreach (var b in SkinBundles) defs.Add(new ProductDefinition(b.StoreId, ProductType.NonConsumable));
        defs.Add(new ProductDefinition(AllPacks.StoreId, ProductType.NonConsumable));
        return defs;
    }

    private async void InitializePurchasing()
    {
        try
        {
            _store = UnityIAPServices.GetDefaultStore();
            _controller = new PurchasingController(_store);

            // Every handler before Connect: unfinished purchases from an
            // earlier session may be delivered right away.
            _controller.OnStoreConnected += HandleStoreConnected;
            _controller.OnStoreDisconnected += HandleStoreDisconnected;
            _controller.OnProductsFetched += HandleProductsFetched;
            _controller.OnProductsFetchFailed += HandleProductsFetchFailed;
            _controller.OnPurchasesFetched += HandlePurchasesFetched;
            _controller.OnPurchasesFetchFailed += HandlePurchasesFetchFailed;
            _controller.OnPurchasePending += HandlePurchasePending;
            _controller.OnPurchaseConfirmed += HandlePurchaseConfirmed;
            _controller.OnPurchaseFailed += HandlePurchaseFailed;
            _controller.OnPurchaseDeferred += HandlePurchaseDeferred;

            await _controller.Connect();
        }
        catch (Exception e)
        {
            // No store on this device, sideloaded build: the cached
            // entitlements keep owned paints owned, Buy reports "unavailable".
            Debug.LogWarning($"[IAPManager] Store init failed: {e.Message} - using cached entitlements");
        }
    }

    private void HandleStoreConnected(string store)
    {
        _connected = true;
        _controller.FetchProducts(Definitions(), store);
    }

    private void HandleStoreDisconnected(string store, StoreConnectionFailureDescription failure)
    {
        _connected = false;
        Debug.LogWarning($"[IAPManager] Store {store} disconnected: {failure.Message} - using cached entitlements");
    }

    private void HandleProductsFetched(string store, List<Product> products)
    {
        _productsReady = true;
        OnPricesChanged?.Invoke();
        // StoreKit 1 (iOS < 15) needs the products cached before purchases.
        _controller.FetchPurchases(store);
    }

    private void HandleProductsFetchFailed(string store, ProductFetchFailed failure)
    {
        Debug.LogWarning($"[IAPManager] Product fetch failed: {failure.FailureReason}");
        // Some products may still have arrived (e.g. one not yet approved in a console)
        if (_controller.GetProducts(store).Count > 0)
        {
            _productsReady = true;
            OnPricesChanged?.Invoke();
            _controller.FetchPurchases(store);
        }
    }

    private void HandlePurchasesFetched(string store, Orders orders)
    {
        // The store's answer is the truth (refunds included); the cache only
        // bridges offline launches.
        var owned = new HashSet<string>();
        foreach (var o in orders.ConfirmedOrders) AddProducts(o, owned);
        foreach (var o in orders.PendingOrders) AddProducts(o, owned);
        bool changed = !owned.SetEquals(_owned);
        _owned.Clear();
        _owned.UnionWith(owned);
        SaveCachedEntitlements();
        if (changed) OnEntitlementsChanged?.Invoke();

        if (_restoring) FinishRestore();
    }

    private void HandlePurchasesFetchFailed(string store, PurchasesFetchFailureDescription failure)
    {
        Debug.LogWarning($"[IAPManager] Purchases fetch failed: {failure.Message} - keeping cached entitlements");
        if (_restoring)
        {
            _restoring = false;
            OnRestoreFinished?.Invoke(RestoreResult.Failed);
        }
    }

    private static void AddProducts(Order order, HashSet<string> into)
    {
        var items = order?.CartOrdered?.Items();
        if (items == null) return;
        foreach (var item in items)
        {
            var id = item?.Product?.definition?.id;
            if (IsOurProduct(id)) into.Add(id);
        }
    }

    private static bool IsOurProduct(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (id == AllPacks.StoreId) return true;
        foreach (var b in SkinBundles) if (b.StoreId == id) return true;
        return false;
    }

    private void HandlePurchasePending(string store, PendingOrder order)
    {
        // Grant first, then confirm (IAP 5). Delivered again after a crash
        // before ConfirmPurchase - granting is idempotent.
        var got = new HashSet<string>();
        AddProducts(order, got);
        var fresh = new List<string>();
        foreach (var id in got)
        {
            if (_owned.Add(id)) fresh.Add(id);
            if (_restoring) _restoredCount++;
        }
        SaveCachedEntitlements();
        _controller.ConfirmPurchase(order);

        if (fresh.Count == 0) return;
        OnEntitlementsChanged?.Invoke();
        foreach (var id in fresh)
        {
            bool asked = _purchaseInFlight.Remove(id);
            if (_restoring && !asked) continue;   // a restore reports once, in FinishRestore
            Telemetry.Log("iap_purchase", ("product", id), ("asked", asked));
            OnPurchased?.Invoke(BundleById(id));
        }
    }

    private void HandlePurchaseConfirmed(string store, Order order)
    {
        if (order is FailedOrder failed)
        {
            // Content is already granted; the store redelivers the
            // unfinished transaction on the next launch and we confirm again.
            Debug.LogWarning($"[IAPManager] Confirm failed: {failed.FailureReason} {failed.Details}");
        }
    }

    private void HandlePurchaseFailed(string store, FailedOrder order)
    {
        var got = new HashSet<string>();
        AddProducts(order, got);
        if (got.Count == 0) _purchaseInFlight.Clear();
        foreach (var id in got) _purchaseInFlight.Remove(id);

        bool cancelled = order.FailureReason == PurchaseFailureReason.UserCancelled;
        Telemetry.Log("iap_fail", ("reason", order.FailureReason));
        Debug.LogWarning($"[IAPManager] Purchase failed: {order.FailureReason} {order.Details}");
        OnPurchaseFailed?.Invoke(cancelled);
    }

    private void HandlePurchaseDeferred(string store, DeferredOrder order)
    {
        // Ask to Buy / pending payment: nothing to grant until it turns into
        // a PendingOrder (HandlePurchasePending).
        var got = new HashSet<string>();
        AddProducts(order, got);
        foreach (var id in got) _purchaseInFlight.Remove(id);
        Telemetry.Log("iap_deferred");
        OnPurchaseDeferred?.Invoke();
    }

    // ── Queries ────────────────────────────────────────────────────────────

    public static SkinBundle? BundleForSkin(int skinId)
    {
        if (!PlaneSkinCatalog.Get(skinId).IsPremium) return null;
        foreach (var b in SkinBundles)
            if (Array.IndexOf(b.SkinIds, skinId) >= 0) return b;
        return null;
    }

    public static SkinBundle BundleById(string storeId)
    {
        foreach (var b in SkinBundles) if (b.StoreId == storeId) return b;
        return AllPacks;
    }

    public bool Owns(SkinBundle bundle)
    {
        if (_owned.Contains(AllPacks.StoreId) || _owned.Contains(bundle.StoreId)) return true;
        if (bundle.StoreId != AllPacks.StoreId) return false;
        return UnownedPackCount == 0;   // all four bought one by one
    }

    public int UnownedPackCount
    {
        get
        {
            if (_owned.Contains(AllPacks.StoreId)) return 0;
            int n = 0;
            foreach (var b in SkinBundles) if (!_owned.Contains(b.StoreId)) n++;
            return n;
        }
    }

    public bool IsSkinUnlocked(int skinId)
    {
        if (!PlaneSkinCatalog.Get(skinId).IsPremium) return true;
        var bundle = BundleForSkin(skinId);
        return bundle.HasValue && Owns(bundle.Value);
    }

    /// <summary>The store's localised price, or the placeholder before it answers.</summary>
    public string PriceFor(SkinBundle bundle)
    {
        var p = _controller != null && _store != null ? _controller.GetProductById(bundle.StoreId, _store) : null;
        var s = p?.metadata?.localizedPriceString;
        return string.IsNullOrEmpty(s) ? bundle.PlaceholderPrice : s;
    }

    // ── Actions ────────────────────────────────────────────────────────────

    /// <summary>Starts a purchase. False when the store is not reachable.</summary>
    public bool Purchase(SkinBundle bundle)
    {
        if (Owns(bundle)) return true;
        var p = StoreReady ? _controller.GetProductById(bundle.StoreId, _store) : null;
        if (p == null || !p.availableToPurchase)
        {
            Debug.LogWarning($"[IAPManager] {bundle.StoreId} not purchasable now (store ready: {StoreReady})");
            return false;
        }
        if (!_purchaseInFlight.Add(bundle.StoreId)) return true;
        Telemetry.Log("iap_view", ("product", bundle.StoreId));
        _controller.PurchaseProduct(p);
        return true;
    }

    /// <summary>Restore Purchases (App Store requirement). False when the store is not reachable.</summary>
    public bool Restore()
    {
        if (_controller == null || !_connected) return false;
        if (_restoring) return true;
        _restoring = true;
        _restoredCount = 0;
        Telemetry.Log("iap_restore");
        _controller.RestoreTransactions((store, success, error) =>
        {
            if (!success)
            {
                Debug.LogWarning($"[IAPManager] Restore failed: {error}");
                _restoring = false;
                OnRestoreFinished?.Invoke(RestoreResult.Failed);
                return;
            }
            // Google Play restores by re-reading purchases; Apple re-delivers
            // through OnPurchasePending. Either way, re-read the full list.
            _controller.FetchPurchases(store);
        }, _store);
        return true;
    }

    private void FinishRestore()
    {
        _restoring = false;
        Telemetry.Log("iap_restore_done", ("owned", _owned.Count), ("delivered", _restoredCount));
        OnRestoreFinished?.Invoke(_owned.Count > 0 ? RestoreResult.Restored : RestoreResult.NothingToRestore);
    }

    // Cached to PlayerPrefs so owned paints stay owned offline / before the
    // store answers - replaced by the store's list in HandlePurchasesFetched.
    private const string PrefsKey = "Doodlebugs.OwnedSkinBundles";

    private void LoadCachedEntitlements()
    {
        var raw = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(raw)) return;
        foreach (var id in raw.Split(','))
            if (IsOurProduct(id)) _owned.Add(id);
    }

    private void SaveCachedEntitlements()
    {
        PlayerPrefs.SetString(PrefsKey, string.Join(",", _owned));
        PlayerPrefs.Save();
    }
}
#endif
