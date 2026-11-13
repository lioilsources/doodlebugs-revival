using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only lifecycle of every AI-flown plane.
///
/// Warm-up bot: one scoreless PlaneHolder that exists while the lobby waits
/// for a second device and nowhere else (Prompts/25).
///
/// Combat bots (Prompts/27 WP3): up to PlayerController.MaxCombatBots planes at
/// seats (0, 90+seat) that play rounds like pilots - they score, respawn
/// through the normal death path, keep their look for the whole session and
/// count as participants (MatchManager.ParticipantCount). They live until
/// ServerDespawnCombat; the phase poll only ever touches the warm-up bot.
///
/// It is an ordinary plane (flight, wrap, death, respawn, smoke, weapon and
/// element trail all come from PlayerController and friends) that a BotBrain
/// flies instead of a device. It is host-owned, so on the host IsOwner is
/// true and the plane's own movement and shooting code drive it; it never
/// parks in the hangar because host-owned planes never do
/// (MatchManager.ShouldSpawnHiddenInHangar).
///
/// The warm-up bot cannot start or block a battle: phase transitions count
/// connected clients plus COMBAT bots. Keeping it out of scores, the HUD
/// roster, READY and the look registry is PlayerController.IsWarmupBot's job;
/// PlayerController.IsBot (any bot) keeps every bot out of READY, haptics,
/// touch controls and the look registry.
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

    // --- combat roster --------------------------------------------------------

    // Index = seat; null = free seat.
    private readonly PlayerController[] _combat = new PlayerController[PlayerController.MaxCombatBots];
    private readonly byte[] _seatTier = new byte[PlayerController.MaxCombatBots];
    private int _combatTarget;
    private byte _combatTier;

    /// <summary>Spawned combat bots.</summary>
    public int CombatCount
    {
        get
        {
            int n = 0;
            foreach (var bot in _combat) if (bot != null) n++;
            return n;
        }
    }

    /// <summary>Combat bots asked for (spawned or waiting for the world).</summary>
    public int CombatTarget => _combatTarget;
    public byte CombatTier => _combatTier;

    /// <summary>The live combat bots, seat order, nulls skipped.</summary>
    public IEnumerable<PlayerController> CombatBots
    {
        get { foreach (var bot in _combat) if (bot != null) yield return bot; }
    }

    /// <summary>Server: keep exactly `count` combat bots of `tier`. Spawns or
    /// despawns from the highest seat; a tier change applies to every bot
    /// at once (synced, so names update on every client).</summary>
    public void ServerSetCombatRoster(int count, byte tier)
    {
        if (!IsServer) return;
        count = Mathf.Clamp(count, 0, PlayerController.MaxCombatBots);
        var tiers = new byte[count];
        for (int i = 0; i < count; i++) tiers[i] = tier;
        ServerSetCombatRoster(tiers);
    }

    /// <summary>Server: one bot per entry, seat = index, each with its own
    /// tier (the soak harness mixes tiers; play uses one tier for all).</summary>
    public void ServerSetCombatRoster(byte[] tiers)
    {
        if (!IsServer) return;
        _combatTarget = Mathf.Clamp(tiers.Length, 0, PlayerController.MaxCombatBots);
        for (int seat = 0; seat < _seatTier.Length; seat++)
        {
            _seatTier[seat] = seat < _combatTarget
                ? (byte)Mathf.Clamp(tiers[seat], 0, PlayerController.BotTierCount - 1)
                : (byte)0;
        }
        _combatTier = _combatTarget > 0 ? _seatTier[0] : (byte)0;
        // The warm-up bot is the lobby's screensaver; combat bots replace it.
        if (_combatTarget > 0) ServerEnsureDespawned();
        ReconcileCombat();
    }

    /// <summary>Server: remove every combat bot (session end, freeze to waiting).</summary>
    public void ServerDespawnCombat()
    {
        if (!IsServer) return;
        _combatTarget = 0;
        for (int seat = 0; seat < _combat.Length; seat++) DespawnCombat(seat);
    }

    /// <summary>Server, every frame in every phase: spawn what the roster
    /// still lacks once the world is ready, and yield shapes humans pick.</summary>
    public void ServerTickCombat()
    {
        if (!IsServer) return;
        for (int seat = 0; seat < _combat.Length; seat++)
        {
            // Despawned from outside (scene teardown, shutdown): free the seat.
            if (_combat[seat] != null && !_combat[seat].IsSpawned) _combat[seat] = null;
        }
        if (CombatCount != _combatTarget) ReconcileCombat();
        foreach (var bot in _combat) if (bot != null) YieldShapeIfTaken(bot);
    }

    /// <summary>Server, at every round start before the deploy: per-round
    /// loadout (Prompts/27 S5: a weapon from the tier's pool).</summary>
    public void ServerPrepareRound()
    {
        if (!IsServer) return;
        foreach (var bot in _combat)
        {
            if (bot == null) continue;
            var weapon = BotTier.Get(bot.NetBotTier.Value).PickRoundWeapon();
            bot.GetComponent<Shooting>()?.ServerSetSelectedWeapon((int)weapon);
        }
    }

    private void ReconcileCombat()
    {
        // Too many: drop the highest seats first.
        for (int seat = _combat.Length - 1; seat >= 0 && CombatCount > _combatTarget; seat--)
        {
            DespawnCombat(seat);
        }
        for (int seat = 0; seat < _combat.Length; seat++)
        {
            var bot = _combat[seat];
            if (bot != null && bot.NetBotTier.Value != _seatTier[seat]) bot.NetBotTier.Value = _seatTier[seat];
        }
        if (CombatCount >= _combatTarget || !WorldReady()) return;
        for (int seat = 0; seat < _combatTarget; seat++)
        {
            if (_combat[seat] == null) _combat[seat] = SpawnPlane(seat, _seatTier[seat]);
        }
    }

    private void DespawnCombat(int seat)
    {
        var bot = _combat[seat];
        _combat[seat] = null;
        if (bot == null) return;
        var brain = bot.GetComponent<BotBrain>();
        if (brain != null) bot.OnServerDeath -= brain.ResetAfterDeath;
        var netObj = bot.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned) NetworkObjectDespawner.DespawnNetworkObject(netObj);
        Debug.Log($"[BotManager] Despawned combat bot at seat {seat}");
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

        YieldShapeIfTaken(_bot);
    }

    // Yield: a human just equipped the bot's shape (their TryClaim succeeds
    // because no bot holds a claim). Swap in place - the same thing every
    // client sees when a human changes shape in the hangar.
    private void YieldShapeIfTaken(PlayerController bot)
    {
        var appearance = bot.GetComponent<PlaneAppearance>();
        var skins = PlaneSkinManager.Instance;
        if (appearance != null && skins != null && skins.IsModelTakenByAnyone(appearance.NetModelId.Value))
        {
            int was = appearance.NetModelId.Value;
            var (modelId, skinId) = PickFreshLook(bot);
            appearance.ServerSetLookUnclaimed(modelId, skinId);
            Debug.Log($"[BotManager] Yielded shape {PlaneModelCatalog.Get(was).Key} -> {PlaneModelCatalog.Get(modelId).Key}");
        }
    }

    /// <summary>Server, every frame the phase is anything but Waiting. Only
    /// the warm-up bot - combat bots outlive every phase.</summary>
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
        var pc = SpawnPlane(-1, 0);
        if (pc == null) return;
        pc.OnServerDeath += OnBotDied;
        _bot = pc;
        _spawnAt = 0f;
    }

    // One AI plane: seat -1 = warm-up bot, otherwise a combat seat.
    private PlayerController SpawnPlane(int combatSeat, byte tier)
    {
        var prefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;

        // Behind a cloud like everyone else; a plain sky position if none is free.
        Vector3? cloudPos = null;
        if (CloudManager.Instance != null) cloudPos = CloudManager.Instance.GetInitialSpawnPosition();
        Vector3 pos = cloudPos ?? new Vector3(Random.Range(-18f, 18f), 8f, 0f);

        var go = NetworkObjectSpawner.SpawnNewNetworkObject(prefab, pos, Quaternion.identity);
        if (go == null) return null;

        var pc = go.GetComponent<PlayerController>();
        if (pc == null)
        {
            Debug.LogError("[BotManager] Player prefab has no PlayerController");
            NetworkObjectDespawner.DespawnNetworkObject(go.GetComponent<NetworkObject>());
            return null;
        }

        // Everything below happens in this call stack, before the first
        // FixedUpdate: a frame with the flag unset would let the plane claim a
        // look, and a frame without the override would fly it with the host's
        // stick (or a phone's tilt).
        pc.ServerSetBotIdentity(combatSeat, tier);

        var (modelId, skinId) = PickFreshLook(pc);
        go.GetComponent<PlaneAppearance>()?.ServerSetLookUnclaimed(modelId, skinId);

        // Two seconds of invulnerability, so a fallback-position spawn cannot
        // midair a human on its first frame.
        pc.PlaneStats?.ResetStats();

        var brain = go.AddComponent<BotBrain>();
        brain.Init(pc, combat: combatSeat >= 0);
        pc.SetInputOverride(brain);
        // Combat bots respawn through the normal death path; the brain starts over.
        if (combatSeat >= 0) pc.OnServerDeath += brain.ResetAfterDeath;

        string who = combatSeat >= 0 ? $"combat bot seat {combatSeat} tier {tier}" : "warm-up bot";
        Debug.Log($"[BotManager] Spawned {who} as {PlaneModelCatalog.Get(modelId).Key} / {PlaneSkinCatalog.Get(skinId).Key} at {pos}");
        return pc;
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
    /// A random shipped shape no human flies, no other bot wears and the
    /// warm-up bot did not wear last time, in a random free livery it did not
    /// wear last time. Premium skins are excluded on purpose - the bot must
    /// not advertise paint the store does not sell yet.
    /// </summary>
    private (int modelId, int skinId) PickFreshLook(PlayerController self)
    {
        var skins = PlaneSkinManager.Instance;

        var otherBots = new HashSet<int>();
        void Wearing(PlayerController bot)
        {
            if (bot == null || bot == self) return;
            var look = bot.GetComponent<PlaneAppearance>();
            if (look != null) otherBots.Add(look.NetModelId.Value);
        }
        Wearing(_bot);
        foreach (var bot in _combat) Wearing(bot);

        var shapes = new List<int>();
        foreach (int id in PlaneModelCatalog.Available)
        {
            if (skins != null && skins.IsModelTakenByAnyone(id)) continue;
            if (otherBots.Contains(id) || id == _lastModelId) continue;
            shapes.Add(id);
        }
        if (shapes.Count == 0)
        {
            // Out of free shapes: repeating beats nothing.
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
