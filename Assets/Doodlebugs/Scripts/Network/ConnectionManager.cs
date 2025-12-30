using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Doodlebugs.Network
{
    public enum ConnectionState
    {
        Idle,
        Searching,
        WaitingForOpponent,
        Connecting,
        Connected,
        Disconnected
    }

    public class ConnectionManager : MonoBehaviour
    {
        public static ConnectionManager Instance { get; private set; }

        [SerializeField] private NetworkDiscovery _discovery;

        public ConnectionState State { get; private set; } = ConnectionState.Idle;
        public event Action<ConnectionState> OnStateChanged;
        public event Action<string> OnStatusMessage;

        private const int MAX_PLAYERS = 2;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (_discovery == null)
            {
                _discovery = GetComponent<NetworkDiscovery>();
            }
        }

        private void Start()
        {
            // Subscribe to discovery events
            if (_discovery != null)
            {
                _discovery.OnServerFound += OnServerFound;
                _discovery.OnDiscoveryTimeout += OnDiscoveryTimeout;
            }

            // Subscribe to network events
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            // Auto-start discovery
            StartDiscovery();
        }

        private void OnDestroy()
        {
            if (_discovery != null)
            {
                _discovery.OnServerFound -= OnServerFound;
                _discovery.OnDiscoveryTimeout -= OnDiscoveryTimeout;
            }

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        #region Public API

        public void StartDiscovery()
        {
            SetState(ConnectionState.Searching);
            SetStatus("Searching for game...");

            _discovery?.StartListening();
        }

        public void RestartDiscovery()
        {
            // Shutdown any existing connection
            if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.Shutdown();
            }

            _discovery?.StopAllDiscovery();

            // Small delay before restarting
            Invoke(nameof(StartDiscovery), 0.5f);
        }

        #endregion

        #region Discovery Callbacks

        private void OnServerFound(DiscoveryData data)
        {
            Debug.Log($"[ConnectionManager] Server found at {data.hostAddress}:{data.port}");

            if (data.currentPlayers >= data.maxPlayers)
            {
                SetStatus("Game is full, searching again...");
                StartDiscovery();
                return;
            }

            SetState(ConnectionState.Connecting);
            SetStatus("Connecting...");

            // Configure transport with discovered address
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.ConnectionData.Address = data.hostAddress;
                transport.ConnectionData.Port = (ushort)data.port;
                Debug.Log($"[ConnectionManager] Transport configured: {data.hostAddress}:{data.port}");
            }

            // Start as client
            NetworkManager.Singleton.StartClient();
        }

        private void OnDiscoveryTimeout()
        {
            Debug.Log("[ConnectionManager] No host found, becoming host");

            SetState(ConnectionState.WaitingForOpponent);
            SetStatus("Waiting for opponent...");

            // Configure connection approval for max players
            NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;

            // Start as host
            NetworkManager.Singleton.StartHost();

            // Start broadcasting
            _discovery?.StartBroadcast(1, MAX_PLAYERS);
        }

        #endregion

        #region Network Callbacks

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            int currentPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;

            if (currentPlayers >= MAX_PLAYERS)
            {
                response.Approved = false;
                response.Reason = "Game is full";
                Debug.Log("[ConnectionManager] Connection rejected - game full");
            }
            else
            {
                response.Approved = true;
                response.CreatePlayerObject = true;
                Debug.Log("[ConnectionManager] Connection approved");
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[ConnectionManager] Client connected: {clientId}");

            if (NetworkManager.Singleton.IsHost)
            {
                int playerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;

                if (playerCount >= MAX_PLAYERS)
                {
                    // Stop broadcasting when game is full
                    _discovery?.StopBroadcast();
                    SetState(ConnectionState.Connected);
                    SetStatus("Game starting!");
                }
                else
                {
                    // Update broadcast with current player count
                    _discovery?.StopBroadcast();
                    _discovery?.StartBroadcast(playerCount, MAX_PLAYERS);
                }
            }
            else if (NetworkManager.Singleton.IsClient && clientId == NetworkManager.Singleton.LocalClientId)
            {
                // We connected as client
                SetState(ConnectionState.Connected);
                SetStatus("Game starting!");
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.Log($"[ConnectionManager] Client disconnected: {clientId}");

            if (NetworkManager.Singleton.IsHost)
            {
                // Another player left, restart broadcasting
                int playerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
                if (playerCount < MAX_PLAYERS)
                {
                    SetState(ConnectionState.WaitingForOpponent);
                    SetStatus("Opponent left. Waiting for new opponent...");
                    _discovery?.StopBroadcast();
                    _discovery?.StartBroadcast(playerCount, MAX_PLAYERS);
                }
            }
            else
            {
                // We were client and got disconnected (host left)
                SetState(ConnectionState.Disconnected);
                SetStatus("Host disconnected");

                // Restart discovery after a short delay
                Invoke(nameof(RestartDiscovery), 2f);
            }
        }

        #endregion

        #region State Management

        private void SetState(ConnectionState newState)
        {
            if (State == newState) return;

            State = newState;
            Debug.Log($"[ConnectionManager] State: {newState}");
            OnStateChanged?.Invoke(newState);
        }

        private void SetStatus(string message)
        {
            Debug.Log($"[ConnectionManager] Status: {message}");
            OnStatusMessage?.Invoke(message);
        }

        #endregion
    }
}
