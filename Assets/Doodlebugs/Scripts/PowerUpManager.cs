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

    // Weighted random: Health=25%, Shield=20%, Repair=20%, Damage=15%, Weapon=20%
    private static readonly float[] TypeWeights = { 0.25f, 0.20f, 0.20f, 0.15f, 0.20f };

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
