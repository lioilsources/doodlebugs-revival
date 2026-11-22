using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Manages power-up spawning when players die in combat.
/// Singleton, server-only logic.
/// </summary>
public class PowerUpManager : MonoBehaviour
{
    public static PowerUpManager Instance { get; private set; }

    [Header("Power-Up Prefab (must be registered in NetworkPrefabsList)")]
    [SerializeField] private GameObject powerUpPrefab;

    [Header("Settings")]
    [SerializeField] private float dropChance = 0.7f;
    [SerializeField] private int maxActivePowerUps = 5;

    // Weighted random (Prompts/29 §5): Health 22, Shield 18, Repair 12,
    // Damage 15, Weapon 18, Nitro 15 - in PowerUpType order.
    private static readonly float[] TypeWeights = { 0.22f, 0.18f, 0.12f, 0.15f, 0.18f, 0.15f };

    // --- supply drop (Prompts/29 D7) ---
    private const float DropDelayMin = 35f, DropDelayMax = 50f;
    private NetworkObject _activeDrop;
    private float _nextDropAt = -1f;

    private List<NetworkObject> _activePowerUps = new List<NetworkObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Try to spawn a power-up at the death position.
    /// Only call from server for combat deaths (bullet/plane collision).
    /// </summary>
    public void SpawnPowerUp(Vector3 deathPosition)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (powerUpPrefab == null)
        {
            Debug.LogWarning("[PowerUpManager] powerUpPrefab not assigned!");
            return;
        }

        // Clean up despawned references
        _activePowerUps.RemoveAll(p => p == null || !p.IsSpawned);

        // Check limits
        if (_activePowerUps.Count >= maxActivePowerUps) return;

        // Roll for drop
        if (Random.value > dropChance) return;

        // Pick type with weighted random
        PowerUpType type = PickRandomType();

        // Spawn
        var go = Object.Instantiate(powerUpPrefab, deathPosition, Quaternion.identity);
        var netObj = go.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn(true);

            // Set type after spawn (NetworkVariable)
            var powerUp = go.GetComponent<PowerUp>();
            if (powerUp != null)
            {
                powerUp.NetPowerUpType.Value = (int)type;
            }

            _activePowerUps.Add(netObj);
            Debug.Log($"[PowerUpManager] Spawned {type} power-up at {deathPosition}");
        }
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || powerUpPrefab == null) return;
        var mm = MatchManager.Instance;
        bool battle = mm != null && mm.Phase == MatchManager.GamePhase.Battle;
        if (!battle)
        {
            _nextDropAt = -1f;
            return;
        }
        if (_activeDrop != null && _activeDrop.IsSpawned) return;
        if (_nextDropAt < 0f)
        {
            _nextDropAt = Time.time + Random.Range(DropDelayMin, DropDelayMax);
            return;
        }
        if (Time.time >= _nextDropAt)
        {
            _nextDropAt = Time.time + Random.Range(DropDelayMin, DropDelayMax);
            ServerSpawnSupplyDrop();
        }
    }

    /// <summary>Server: one crate on a parachute from the top edge.
    /// Contents: special 60 % (Big One / Swarm / EMP), full repair 20 %,
    /// weapon tier 20 %.</summary>
    public void ServerSpawnSupplyDrop()
    {
        var cam = Camera.main;
        if (cam == null) return;
        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
        Vector3 c = cam.transform.position;
        var pos = new Vector3(c.x + Random.Range(-halfW * 0.75f, halfW * 0.75f), c.y + halfH + 1f, 0f);

        PowerUpType type;
        int payload = -1;
        float roll = Random.value;
        if (roll < 0.6f)
        {
            type = PowerUpType.Special;
            float total = 0f;
            foreach (var s in WeaponProfile.SupplySpecials) total += s.weight;
            float pick = Random.value * total;
            payload = (int)WeaponProfile.SupplySpecials[0].type;
            foreach (var s in WeaponProfile.SupplySpecials)
            {
                if ((pick -= s.weight) <= 0f) { payload = (int)s.type; break; }
            }
        }
        else type = roll < 0.8f ? PowerUpType.FullRepair : PowerUpType.Weapon;

        var go = Object.Instantiate(powerUpPrefab, pos, Quaternion.identity);
        var netObj = go.GetComponent<NetworkObject>();
        if (netObj == null) return;
        netObj.Spawn(true);
        go.GetComponent<PowerUp>()?.ServerMakeSupplyDrop(type, payload, c.y + Random.Range(-1f, 3f));
        _activeDrop = netObj;
        Telemetry.Log("supply_drop", ("payload", type == PowerUpType.Special ? ((WeaponType)payload).ToString() : type.ToString()));
    }

    private PowerUpType PickRandomType()
    {
        float roll = Random.value;
        float cumulative = 0f;

        for (int i = 0; i < TypeWeights.Length; i++)
        {
            cumulative += TypeWeights[i];
            if (roll <= cumulative)
            {
                return (PowerUpType)i;
            }
        }

        return PowerUpType.Health;
    }
}
