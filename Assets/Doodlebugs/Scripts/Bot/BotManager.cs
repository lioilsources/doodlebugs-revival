using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only lifecycle of the warm-up bot: one AI-flown PlaneHolder that
/// exists while the lobby waits for a second device and nowhere else.
///
/// It is an ordinary plane (flight, wrap, death, respawn, smoke, weapon and
/// element trail all come from PlayerController and friends) that a BotBrain
/// flies instead of a device. It is host-owned, so on the host IsOwner is
/// true and the plane's own movement and shooting code drive it; it never
/// parks in the hangar because host-owned planes never do
/// (MatchManager.ShouldSpawnHiddenInHangar).
///
/// Nothing here can start or block a battle: every phase transition counts
/// NetworkManager.ConnectedClientsIds, and a spawned object is not a
/// connection. Keeping it out of scores, the HUD roster, READY and the look
/// registry is PlayerController.IsBot's job, checked at each of those seams.
///
/// Created by GameSetup next to MatchManager; ticked from MatchManager.Update
/// so the phase is polled rather than hooked - no future phase can leave a
/// bot alive by accident. Design notes: Prompts/25-CLAUDE-PLAN-warmup-bot.md.
/// </summary>
public class BotManager : MonoBehaviour
{
    public static BotManager Instance { get; private set; }

    // Clouds and the host's own plane exist well before this; the Waiting
    // hangar opens at 0.5 s.
    private const float SpawnDelaySeconds = 2f;

    // After a wreck: let the explosion play, then a fresh bot in a fresh look.
    private const float RespawnDelaySeconds = 3f;

    private PlayerController _bot;
    private float _spawnAt;          // 0 = not scheduled
    private bool _pendingDespawn;    // the current bot died; replace it next tick

    // "Fresh look every time": never the previous shape, never the previous skin.
    private int _lastModelId = -1;
    private int _lastSkinId = -1;

    private static bool IsServer =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Server, every frame while the phase is WaitingForPlayers.</summary>
    public void ServerTickWarmUp()
    {
        if (!IsServer) return;

        if (_pendingDespawn)
        {
            // The wreck's ClientRpc went out on the frame it died; the object
            // can go now, and the timer set by OnBotDied brings the next one.
            _pendingDespawn = false;
            DespawnBot();
        }

        if (_bot == null)
        {
            if (_spawnAt <= 0f) _spawnAt = Time.time + SpawnDelaySeconds;
            if (Time.time >= _spawnAt && WorldReady()) SpawnBot();
            return;
        }

        // Yield: a human just equipped the bot's shape (their TryClaim succeeds
        // because the bot holds no claim). Swap in place - the same thing every
        // client sees when a human changes shape in the hangar.
        var appearance = _bot.GetComponent<PlaneAppearance>();
        var skins = PlaneSkinManager.Instance;
        if (appearance != null && skins != null && skins.IsModelTakenByAnyone(appearance.NetModelId.Value))
        {
            int was = appearance.NetModelId.Value;
            var (modelId, skinId) = PickFreshLook();
            appearance.ServerSetLookUnclaimed(modelId, skinId);
            Debug.Log($"[BotManager] Yielded shape {PlaneModelCatalog.Get(was).Key} -> {PlaneModelCatalog.Get(modelId).Key}");
        }
    }

    /// <summary>Server, every frame the phase is anything but Waiting.</summary>
    public void ServerEnsureDespawned()
    {
        if (!IsServer) return;
        _spawnAt = 0f;
        _pendingDespawn = false;
        if (_bot != null) DespawnBot();
    }

    private static bool WorldReady()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.NetworkConfig == null || nm.NetworkConfig.PlayerPrefab == null) return false;
        // The look registry is a scene NetworkObject; until it is spawned the
        // "is this shape taken" question has no answer.
        var skins = PlaneSkinManager.Instance;
        return skins != null && skins.IsSpawned;
    }

    private void SpawnBot()
    {
        var prefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;

        // Behind a cloud like everyone else; a plain sky position if none is free.
        Vector3? cloudPos = null;
        if (CloudManager.Instance != null) cloudPos = CloudManager.Instance.GetInitialSpawnPosition();
        Vector3 pos = cloudPos ?? new Vector3(Random.Range(-18f, 18f), 8f, 0f);

        var go = NetworkObjectSpawner.SpawnNewNetworkObject(prefab, pos, Quaternion.identity);
        if (go == null) return;

        var pc = go.GetComponent<PlayerController>();
        if (pc == null)
        {
            Debug.LogError("[BotManager] Player prefab has no PlayerController");
            NetworkObjectDespawner.DespawnNetworkObject(go.GetComponent<NetworkObject>());
            return;
        }

        // Everything below happens in this call stack, before the first
        // FixedUpdate: a frame with the flag unset would let the plane claim a
        // look, and a frame without the override would fly it with the host's
        // stick (or a phone's tilt).
        pc.ServerSetBotIdentity();

        var (modelId, skinId) = PickFreshLook();
        go.GetComponent<PlaneAppearance>()?.ServerSetLookUnclaimed(modelId, skinId);

        // Two seconds of invulnerability, so a fallback-position spawn cannot
        // midair a human on its first frame.
        pc.PlaneStats?.ResetStats();

        var brain = go.AddComponent<BotBrain>();
        brain.Init(pc);
        pc.SetInputOverride(brain);

        pc.OnServerDeath += OnBotDied;

        _bot = pc;
        _spawnAt = 0f;
        Debug.Log($"[BotManager] Spawned bot as {PlaneModelCatalog.Get(modelId).Key} / {PlaneSkinCatalog.Get(skinId).Key} at {pos}");
    }

    private void DespawnBot()
    {
        if (_bot == null) return;
        _bot.OnServerDeath -= OnBotDied;
        var netObj = _bot.GetComponent<NetworkObject>();
        NetworkObjectDespawner.DespawnNetworkObject(netObj);
        _bot = null;
        Debug.Log("[BotManager] Despawned bot");
    }

    private void OnBotDied()
    {
        // The plane teleports to a cloud on its own; we replace it instead so
        // "new appearance = new look" holds, and so the wreck gets its frame.
        _pendingDespawn = true;
        _spawnAt = Time.time + RespawnDelaySeconds;
    }

    /// <summary>
    /// A random shipped shape no human flies and the bot did not wear last
    /// time, in a random free livery it did not wear last time. Premium skins
    /// are excluded on purpose - the bot must not advertise paint the store
    /// does not sell yet.
    /// </summary>
    private (int modelId, int skinId) PickFreshLook()
    {
        var skins = PlaneSkinManager.Instance;

        var shapes = new List<int>();
        foreach (int id in PlaneModelCatalog.Available)
        {
            if (skins != null && skins.IsModelTakenByAnyone(id)) continue;
            if (id == _lastModelId) continue;
            shapes.Add(id);
        }
        if (shapes.Count == 0)
        {
            // Only one free shape left: repeating beats nothing.
            foreach (int id in PlaneModelCatalog.Available)
            {
                if (skins == null || !skins.IsModelTakenByAnyone(id)) shapes.Add(id);
            }
        }
        if (shapes.Count == 0) shapes.Add(PlaneModelCatalog.BaseModelId);

        var liveries = new List<int>();
        for (int id = 0; id < PlaneSkinCatalog.Count; id++)
        {
            if (PlaneSkinCatalog.Get(id).IsPremium) continue;
            if (id == _lastSkinId) continue;
            liveries.Add(id);
        }
        if (liveries.Count == 0) liveries.Add(PlaneSkinCatalog.StarterSkinId);

        int model = shapes[Random.Range(0, shapes.Count)];
        int skin = liveries[Random.Range(0, liveries.Count)];
        _lastModelId = model;
        _lastSkinId = skin;
        return (model, skin);
    }
}
