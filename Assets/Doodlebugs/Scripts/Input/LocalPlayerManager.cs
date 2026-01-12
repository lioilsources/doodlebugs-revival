using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages up to 4 local players on desktop with keyboard + gamepad controls.
/// Each player can use their assigned gamepad (Gamepad.all[index]) OR keyboard.
/// Toggle players with keys 0-4 (0 disables P1, 1-4 toggles P1-P4).
/// P1 is enabled by default on startup.
/// </summary>
public class LocalPlayerManager : MonoBehaviour
{
    public static LocalPlayerManager Instance { get; private set; }

    [SerializeField] private GameObject playerPrefab;

    private const int MAX_LOCAL_PLAYERS = 4;

    private bool[] playerEnabled = new bool[MAX_LOCAL_PLAYERS];
    private HybridInputProvider[] inputProviders = new HybridInputProvider[MAX_LOCAL_PLAYERS];
    private NetworkObject[] playerNetworkObjects = new NetworkObject[MAX_LOCAL_PLAYERS];

    public event Action<int, bool> OnPlayerToggled; // (playerIndex, enabled)

    public int ActivePlayerCount => playerEnabled.Count(p => p);
    public bool IsPlayerEnabled(int index) => index >= 0 && index < MAX_LOCAL_PLAYERS && playerEnabled[index];

    private void Awake()
    {
        // Only run on desktop
        if (IsMobilePlatform())
        {
            Debug.Log("[LocalPlayerManager] Mobile platform - LocalPlayerManager disabled");
            Destroy(gameObject);
            return;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Create hybrid input providers for all players (keyboard + gamepad)
        for (int i = 0; i < MAX_LOCAL_PLAYERS; i++)
        {
            inputProviders[i] = new HybridInputProvider(i);
        }
    }

    private void Start()
    {
        // P1 will be enabled when host starts via OnHostStarted()
        // Don't enable here - wait for network to be ready
        Debug.Log("[LocalPlayerManager] Waiting for host to start before enabling P1");
    }

    private void Update()
    {
        // Manager is destroyed on mobile in Awake(), so this only runs on desktop

        // Update all active input providers
        for (int i = 0; i < MAX_LOCAL_PLAYERS; i++)
        {
            if (playerEnabled[i])
            {
                inputProviders[i].UpdateInput();
            }
        }

        // Check toggle keys (using new Input System)
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            Debug.LogWarning("[LocalPlayerManager] Keyboard.current is NULL!");
            return;
        }

        // Key 0 - disable P1 only
        if (keyboard.digit0Key.wasPressedThisFrame)
        {
            DisablePlayer(0);
        }

        // Keys 1-4 toggle P1-P4
        if (keyboard.digit1Key.wasPressedThisFrame)
        {
            Debug.Log("[LocalPlayerManager] Key 1 pressed - toggling P1");
            TogglePlayer(0);
        }
        if (keyboard.digit2Key.wasPressedThisFrame)
        {
            Debug.Log("[LocalPlayerManager] Key 2 pressed - toggling P2");
            TogglePlayer(1);
        }
        if (keyboard.digit3Key.wasPressedThisFrame)
        {
            Debug.Log("[LocalPlayerManager] Key 3 pressed - toggling P3");
            TogglePlayer(2);
        }
        if (keyboard.digit4Key.wasPressedThisFrame)
        {
            Debug.Log("[LocalPlayerManager] Key 4 pressed - toggling P4");
            TogglePlayer(3);
        }
    }

    public void TogglePlayer(int index)
    {
        if (index < 0 || index >= MAX_LOCAL_PLAYERS) return;

        if (playerEnabled[index])
            DisablePlayer(index);
        else
            EnablePlayer(index);
    }

    public void EnablePlayer(int index)
    {
        if (index < 0 || index >= MAX_LOCAL_PLAYERS) return;
        if (playerEnabled[index]) return; // Already enabled

        playerEnabled[index] = true;
        Debug.Log($"[LocalPlayerManager] Player {index + 1} enabled");

        // Spawn network object if we're the host
        SpawnPlayerObject(index);

        OnPlayerToggled?.Invoke(index, true);
    }

    public void DisablePlayer(int index)
    {
        if (index < 0 || index >= MAX_LOCAL_PLAYERS) return;
        if (!playerEnabled[index]) return; // Already disabled

        playerEnabled[index] = false;
        Debug.Log($"[LocalPlayerManager] Player {index + 1} disabled");

        // Despawn network object
        DespawnPlayerObject(index);

        OnPlayerToggled?.Invoke(index, false);
    }

    public IInputProvider GetInputProvider(int localPlayerIndex)
    {
        if (localPlayerIndex < 0 || localPlayerIndex >= MAX_LOCAL_PLAYERS)
            return null;

        return inputProviders[localPlayerIndex];
    }

    private void SpawnPlayerObject(int index)
    {
        // Check if network is running
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.Log($"[LocalPlayerManager] Cannot spawn P{index + 1} yet - not a server");
            return;
        }
        if (playerPrefab == null)
        {
            Debug.LogError("[LocalPlayerManager] Player prefab not assigned!");
            return;
        }
        if (playerNetworkObjects[index] != null) return; // Already spawned

        // Check total player count before spawning (network clients + local players already spawned)
        int networkClients = NetworkManager.Singleton.ConnectedClientsIds.Count;
        int localPlayersSpawned = playerNetworkObjects.Count(p => p != null);
        int totalAfterSpawn = networkClients + localPlayersSpawned;  // +1 is already counted in networkClients for host
        if (totalAfterSpawn >= 20)
        {
            Debug.Log($"[LocalPlayerManager] Cannot spawn P{index + 1} - game full ({totalAfterSpawn}/20)");
            return;
        }

        // Calculate spawn position (spread players horizontally)
        Vector3 spawnPos = GetSpawnPosition(index);

        // Spawn with ownership to host (local client)
        var go = NetworkObjectSpawner.SpawnNewNetworkObjectChangeOwnershipToClient(
            playerPrefab,
            spawnPos,
            NetworkManager.Singleton.LocalClientId,
            true
        );

        if (go != null)
        {
            playerNetworkObjects[index] = go.GetComponent<NetworkObject>();

            // Set local player index on PlayerController
            var controller = go.GetComponent<PlayerController>();
            if (controller != null)
            {
                controller.SetLocalPlayerIndex(index);
            }

            Debug.Log($"[LocalPlayerManager] Spawned player object for P{index + 1}");
        }
    }

    private void DespawnPlayerObject(int index)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (playerNetworkObjects[index] == null) return;

        playerNetworkObjects[index].Despawn();
        playerNetworkObjects[index] = null;

        Debug.Log($"[LocalPlayerManager] Despawned player object for P{index + 1}");
    }

    private Vector3 GetSpawnPosition(int index)
    {
        // Spread spawn positions horizontally
        float xOffset = (index - 1.5f) * 3f; // -4.5, -1.5, 1.5, 4.5
        return new Vector3(xOffset, 2f, 0f);
    }

    private bool IsMobilePlatform()
    {
        return Application.platform == RuntimePlatform.Android ||
               Application.platform == RuntimePlatform.IPhonePlayer;
    }

    /// <summary>
    /// Called when host starts - enables P1 and spawns the player object.
    /// Only works on desktop hosts.
    /// </summary>
    public void OnHostStarted()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
        {
            Debug.Log("[LocalPlayerManager] OnHostStarted called but not a host - ignoring");
            return;
        }

        Debug.Log("[LocalPlayerManager] Host started - enabling P1");
        EnablePlayer(0);
    }
}
