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

    /// <summary>
    /// Which local-coop discovery/transport stack is in use.
    ///   Lan       — UDP broadcast + UnityTransport (same Wi-Fi). Default.
    ///   NativeP2P — iOS Multipeer / Android Nearby fallback used on mobile data.
    /// </summary>
    public enum LocalCoopMode
    {
        Lan,
        NativeP2P
    }

    /// <summary>What the player chose on the Home screen (Prompts/27 S1).</summary>
    public enum SessionKind
    {
        None,   // Home screen: no socket, no discovery, no permission prompt
        Solo,   // one-device host on loopback against combat bots
        Party   // today's LAN / native pairing flow
    }

    /// <summary>This device's role in the current lobby, for a persistent UI badge.</summary>
    public enum LocalRole
    {
        None,
        Host,
        Client
    }

    public class ConnectionManager : MonoBehaviour
    {
        public static ConnectionManager Instance { get; private set; }

        [SerializeField] private NetworkDiscovery _discovery;

        public ConnectionState State { get; private set; } = ConnectionState.Idle;
        public event Action<ConnectionState> OnStateChanged;
        public event Action<string> OnStatusMessage;

        // Host/Client role for the UI badge. Set once the local device commits to a
        // role (LAN or native path); surfaced to the HUD via OnRoleChanged.
        public LocalRole Role { get; private set; } = LocalRole.None;
        public event Action<LocalRole> OnRoleChanged;

        public SessionKind Session { get; private set; } = SessionKind.None;
        public event Action<SessionKind> OnSessionChanged;

        // Solo hosts bind loopback only, on their own port range so a LAN
        // host on 7777 (or a ParrelSync clone in solo) never blocks them.
        private const string LoopbackAddress = "127.0.0.1";
        private const ushort SoloPortBase = 7790;
        private const int SoloPortTries = 5;
        private const string LanListenAddress = "0.0.0.0";   // Scene01's ServerListenAddress

        // Set while this device tears its own session down: the disconnect
        // callbacks that shutdown raises must not restart discovery.
        private bool _leaving;
        private UnityTransport _sceneTransport;

        private void SetSession(SessionKind kind)
        {
            if (Session == kind) return;
            Session = kind;
            Debug.Log($"[ConnectionManager] Session = {kind}");
            OnSessionChanged?.Invoke(kind);
        }

        private static bool NetworkRunning =>
            NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient);

        private void SetRole(LocalRole role)
        {
            if (Role == role) return;
            Role = role;
            Debug.Log($"[ConnectionManager] Local role = {role}");
            OnRoleChanged?.Invoke(role);
        }

        private const int MAX_PLAYERS = 20;         // LAN: 4 local on desktop host + 16 remote clients
        private const int MAX_PLAYERS_NATIVE = 8;   // Mobile-data native fallback limit

        private LocalCoopMode _mode = LocalCoopMode.Lan;
        private NativeLocalCoopManager _native;

        // Split-brain yield in progress: our empty host is shutting down so we can
        // join a competing host that won the instance-id tie-break.
        private bool _yieldingHost;
        private DiscoveryData _yieldTarget;

        private int MaxPlayers => _mode == LocalCoopMode.NativeP2P ? MAX_PLAYERS_NATIVE : MAX_PLAYERS;

        private static bool IsMobile => Application.platform == RuntimePlatform.Android ||
                                        Application.platform == RuntimePlatform.IPhonePlayer;

        // iOS always pairs over MultipeerConnectivity. The UDP broadcast path
        // needs Apple's multicast entitlement on iOS 14+ (not granted — see
        // iOSLocalNetworkPostProcess), so over Wi-Fi it fails to pair in
        // production even after the user accepts the local-network prompt.
        // Multipeer needs no entitlement and covers Wi-Fi, peer-to-peer Wi-Fi
        // and Bluetooth. Costs: 8-player cap and no iOS↔desktop LAN cross-play.
        // Android keeps the old behaviour: broadcast works there (with the
        // MulticastLock), so carrier data remains the only Nearby trigger.
        private LocalCoopMode DetermineMode()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                return LocalCoopMode.NativeP2P;
            }
            if (IsMobile &&
                Application.internetReachability == NetworkReachability.ReachableViaCarrierDataNetwork)
            {
                return LocalCoopMode.NativeP2P;
            }
            return LocalCoopMode.Lan;
        }

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
            // Subscribe to network events (both paths need these)
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            _sceneTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();

            if (_discovery != null)
            {
                _discovery.OnServerFound += OnServerFound;
                _discovery.OnDiscoveryTimeout += OnDiscoveryTimeout;
            }

            // Nothing else at boot (Prompts/27 S1): the iOS Local Network prompt
            // and Android's Nearby permissions fire the moment pairing starts,
            // so pairing starts only when the player taps PARTY.
            SetState(ConnectionState.Idle);
        }

        // --- Home screen entry points (Prompts/27 WP5) ---------------------

        /// <summary>PLAY: a one-device session on loopback against bots. From a
        /// running party or solo session it leaves that first.</summary>
        public void BeginSolo()
        {
            if (NetworkRunning || Session != SessionKind.None)
            {
                ReturnHome();
                // NGO completes Shutdown asynchronously (same defer as the host yield).
                Invoke(nameof(BeginSoloNow), 0.7f);
                return;
            }
            BeginSoloNow();
        }

        private void BeginSoloNow()
        {
            CancelInvoke(nameof(BeginSoloNow));
            SetSession(SessionKind.Solo);
            _discovery?.StopAllDiscovery();
            _native?.Stop();
            StartCoroutine(StartSoloHost());
            Telemetry.Log("boot_mode", ("mode", "solo"));
        }

        private System.Collections.IEnumerator StartSoloHost()
        {
            var nm = NetworkManager.Singleton;
            for (int attempt = 0; attempt < SoloPortTries; attempt++)
            {
                if (Session != SessionKind.Solo) yield break;   // left meanwhile
                UseSceneTransport();
                ushort port = (ushort)(SoloPortBase + attempt);
                // Listen on loopback only: no local-network access, no prompt, no peer can join.
                _sceneTransport.SetConnectionData(LoopbackAddress, port, LoopbackAddress);
                nm.ConnectionApprovalCallback = ApproveConnection;
                MatchManager.Instance?.ArmSolo(MatchManager.SoloBotCount);

                if (nm.StartHost())
                {
                    SetState(ConnectionState.Connected);
                    BackgroundManager.Instance?.SelectRandomBackground();
                    LocalPlayerManager.Instance?.OnHostStarted();
                    Debug.Log($"[ConnectionManager] Solo host on {LoopbackAddress}:{port}");
                    yield break;
                }

                Debug.LogWarning($"[ConnectionManager] Solo host failed on port {port} - trying the next one");
                MatchManager.Instance?.DisarmSolo();
                if (NetworkRunning) nm.Shutdown();
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Debug.LogError("[ConnectionManager] Solo host could not start on any loopback port");
            ReturnHome();
        }

        /// <summary>PARTY: today's pairing flow, unchanged (LAN broadcast or the
        /// native Multipeer/Nearby path). From a solo session it leaves first.</summary>
        public void BeginParty()
        {
            if (NetworkRunning || Session == SessionKind.Solo)
            {
                ReturnHome();
                Invoke(nameof(BeginPartyNow), 0.7f);
                return;
            }
            BeginPartyNow();
        }

        private void BeginPartyNow()
        {
            CancelInvoke(nameof(BeginPartyNow));
            SetSession(SessionKind.Party);
            // A solo session left loopback in the transport: a LAN host must
            // listen on every interface again or nobody can reach it.
            UseSceneTransport();
            if (_sceneTransport != null)
            {
                _sceneTransport.ConnectionData.ServerListenAddress = LanListenAddress;
                _sceneTransport.ConnectionData.Port = 7777;
            }

            _mode = DetermineMode();
            Debug.Log($"[ConnectionManager] LocalCoopMode = {_mode} (reachability={Application.internetReachability})");
            Telemetry.Log("boot_mode", ("mode", "party"), ("transport", _mode));

            if (_mode == LocalCoopMode.NativeP2P)
            {
                StartNativeP2P();
                return;
            }
            StartDiscovery();
        }

        /// <summary>CANCEL on the searching screen: stop pairing, back to Home.</summary>
        public void CancelParty()
        {
            if (Session != SessionKind.Party) return;
            ReturnHome();
        }

        /// <summary>Leave whatever session this device is in and show Home: the
        /// only way out of a solo session, and the PARTY cancel. Idempotent.</summary>
        public void ReturnHome()
        {
            _leaving = true;
            CancelInvoke(nameof(RestartDiscovery));
            CancelInvoke(nameof(NativeRestartDiscovery));
            CancelInvoke(nameof(FinishHostYield));
            CancelInvoke(nameof(StartDiscovery));
            CancelInvoke(nameof(BeginSoloNow));
            CancelInvoke(nameof(BeginPartyNow));
            StopAllCoroutines();
            _yieldingHost = false;
            _yieldTarget = null;

            // Server state first (coroutines, run tables, bots), while the
            // network objects still exist; a client resets its mirrors.
            var mm = MatchManager.Instance;
            if (mm != null)
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) mm.ServerAbortSession();
                else mm.LocalAbortSession();
            }

            _discovery?.StopAllDiscovery();
            _native?.Stop();
            if (NetworkRunning) NetworkManager.Singleton.Shutdown();

            SetSession(SessionKind.None);
            SetState(ConnectionState.Idle);
            SetRole(LocalRole.None);
            Invoke(nameof(FinishLeaving), 1f);
        }

        private void FinishLeaving() => _leaving = false;

        // The iOS native path swaps NGO's transport; solo and LAN need the
        // scene's UnityTransport back.
        private void UseSceneTransport()
        {
            if (_sceneTransport == null) _sceneTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (_sceneTransport != null) NetworkManager.Singleton.NetworkConfig.NetworkTransport = _sceneTransport;
        }

        private void OnDestroy()
        {
            // Stop discovery
            _discovery?.StopAllDiscovery();

            if (_discovery != null)
            {
                _discovery.OnServerFound -= OnServerFound;
                _discovery.OnDiscoveryTimeout -= OnDiscoveryTimeout;
            }

            // Tear down native fallback if it was in use
            if (_native != null)
            {
                _native.OnBecomeHost -= NativeBecomeHost;
                _native.OnBecomeClient -= NativeBecomeClient;
                _native.OnStatus -= SetStatus;
                _native.OnResetNetworking -= NativeResetNetworking;
                _native.Stop();
            }

            // Shutdown network to release port
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

                if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
                {
                    NetworkManager.Singleton.Shutdown();
                }
            }
        }

        #region Public API

        public void StartDiscovery()
        {
            SetState(ConnectionState.Searching);
            SetStatus($"{UI.ConnectionUI.GameVersion}  •  " + Loc.T("status.searching"));
            SetRole(LocalRole.None);

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

            // Only a PARTY session pairs. A solo host has an "empty lobby" by the
            // conflict test below and would yield to any host it hears.
            if (Session != SessionKind.Party) return;

            // We are already hosting: this broadcast comes from a competing host
            // on the same network (both instances timed out together). Decide who
            // yields instead of leaving players in two separate lobbies.
            if (NetworkManager.Singleton.IsServer || _yieldingHost)
            {
                HandleHostConflict(data);
                return;
            }

            if (data.currentPlayers >= data.maxPlayers)
            {
                SetStatus(Loc.T("status.full"));
                StartDiscovery();
                return;
            }

            SetState(ConnectionState.Connecting);
            SetStatus(Loc.T("status.connecting"));
            ConnectToHost(data);
        }

        // Configure UnityTransport with the discovered address and start as client.
        private void ConnectToHost(DiscoveryData data)
        {
            UseSceneTransport();
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.ConnectionData.Address = data.hostAddress;
                transport.ConnectionData.Port = (ushort)data.port;

                // Increase timeouts for mobile networks (default values are too aggressive)
                transport.ConnectTimeoutMS = 5000;      // 5 seconds (default: 1000)
                transport.MaxConnectAttempts = 10;      // 10 attempts (default: 60)
                transport.HeartbeatTimeoutMS = 1500;    // 1.5 seconds (default: 500)

                Debug.Log($"[ConnectionManager] Transport configured: {data.hostAddress}:{data.port}, ConnectTimeout={transport.ConnectTimeoutMS}ms, HeartbeatTimeout={transport.HeartbeatTimeoutMS}ms");
            }

            // Start as client
            NetworkManager.Singleton.StartClient();
            SetRole(LocalRole.Client);
        }

        // Deterministic tie-break against a competing LAN host: the host with the
        // larger instance id yields, but only while its lobby is still empty.
        private void HandleHostConflict(DiscoveryData data)
        {
            if (_yieldingHost) return;
            if (_discovery == null || string.IsNullOrEmpty(data.instanceId)) return;

            // Someone already joined us - we keep the lobby, the other host yields.
            if (NetworkManager.Singleton.ConnectedClientsIds.Count > 1) return;

            if (string.CompareOrdinal(_discovery.LocalInstanceId, data.instanceId) <= 0)
            {
                return; // We win the tie-break; the competing host will yield.
            }

            Debug.Log($"[ConnectionManager] Competing host detected at {data.hostAddress} - yielding (our lobby is empty)");

            _yieldingHost = true;
            _yieldTarget = data;

            _discovery.StopAllDiscovery();
            if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.Shutdown();
            }

            SetState(ConnectionState.Connecting);
            SetStatus(Loc.T("status.another_game"));

            // NGO completes Shutdown asynchronously - defer the client start.
            Invoke(nameof(FinishHostYield), 0.7f);
        }

        private void FinishHostYield()
        {
            _yieldingHost = false;
            if (_yieldTarget == null) return;

            var target = _yieldTarget;
            _yieldTarget = null;
            ConnectToHost(target);
        }

        private void OnDiscoveryTimeout()
        {
            if (Session != SessionKind.Party) return;
            Debug.Log("[ConnectionManager] No host found, becoming host");

            SetState(ConnectionState.WaitingForOpponent);
            SetStatus(Loc.T("status.waiting_opponent"));

            // Configure transport timeouts for mobile networks
            UseSceneTransport();
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.ConnectTimeoutMS = 5000;      // 5 seconds
                transport.MaxConnectAttempts = 10;      // 10 attempts
                transport.HeartbeatTimeoutMS = 1500;    // 1.5 seconds
                Debug.Log($"[ConnectionManager] Host transport configured: ConnectTimeout={transport.ConnectTimeoutMS}ms, HeartbeatTimeout={transport.HeartbeatTimeoutMS}ms");
            }

            // ConnectionApproval must be enabled in Unity Editor on NetworkManager component
            // Here we just assign the callback (safe to do at runtime)
            NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;
            Debug.Log("[ConnectionManager] ConnectionApprovalCallback assigned");

            // Start as host
            NetworkManager.Singleton.StartHost();
            SetRole(LocalRole.Host);

            // Select random background for this session
            BackgroundManager.Instance?.SelectRandomBackground();

            // Notify LocalPlayerManager to spawn P1 (desktop only)
            LocalPlayerManager.Instance?.OnHostStarted();

            // Start broadcasting
            _discovery?.StartBroadcast(1, MAX_PLAYERS);

            // Keep listening while hosting to catch a competing host that timed
            // out at the same moment (split-brain on shared WiFi).
            _discovery?.StartListening(hostWatch: true);
        }

        #endregion

        #region Native P2P Fallback (mobile data)

        private void StartNativeP2P()
        {
            SetState(ConnectionState.Searching);
            SetRole(LocalRole.None);

            // Orchestrator + transport live on a child object; RequireComponent
            // adds the NativeLocalTransport automatically. Created on the first
            // PARTY and reused after a CANCEL.
            if (_native == null)
            {
                var go = new GameObject("NativeLocalCoop");
                go.transform.SetParent(transform);
                _native = go.AddComponent<NativeLocalCoopManager>();

                _native.OnBecomeHost += NativeBecomeHost;
                _native.OnBecomeClient += NativeBecomeClient;
                _native.OnStatus += SetStatus;
                _native.OnResetNetworking += NativeResetNetworking;
            }

            _native.Begin();
        }

        // Invoked synchronously by the orchestrator when no lobby was found.
        private void NativeBecomeHost()
        {
            SetState(ConnectionState.WaitingForOpponent);

            NetworkManager.Singleton.NetworkConfig.NetworkTransport = _native.Transport;
            NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;
            NetworkManager.Singleton.StartHost();
            SetRole(LocalRole.Host);

            BackgroundManager.Instance?.SelectRandomBackground();
            LocalPlayerManager.Instance?.OnHostStarted();

            Debug.Log("[ConnectionManager] Native host started");
        }

        // Invoked synchronously by the orchestrator when an existing lobby was found.
        private void NativeBecomeClient(string hostPeerId)
        {
            SetState(ConnectionState.Connecting);

            NetworkManager.Singleton.NetworkConfig.NetworkTransport = _native.Transport;
            NetworkManager.Singleton.StartClient();
            SetRole(LocalRole.Client);

            Debug.Log($"[ConnectionManager] Native client started, host={hostPeerId}");
        }

        // Split-brain yield: shut down the half-started host so the orchestrator can
        // restart this device as a client of the surviving host.
        private void NativeResetNetworking()
        {
            if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        // Restart the native discovery flow after losing the host (mobile-data path).
        private void NativeRestartDiscovery()
        {
            if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.Shutdown();
            }
            _native?.Stop();
            _native?.Begin();
        }

        #endregion

        #region Network Callbacks

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            Debug.Log($"[ConnectionManager] ApproveConnection CALLED for ClientNetworkId={request.ClientNetworkId}");

            int currentPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;

            if (currentPlayers >= MaxPlayers)
            {
                response.Approved = false;
                response.Reason = "Game is full";
                Debug.Log($"[ConnectionManager] Connection rejected - game full ({currentPlayers}/{MaxPlayers})");
            }
            else
            {
                response.Approved = true;

                // Determine if we should auto-create player object:
                // - Desktop host: LocalPlayerManager handles spawning (CreatePlayerObject = false)
                // - Mobile host: No LocalPlayerManager, need auto-spawn (CreatePlayerObject = true)
                // - All clients: Auto-spawn (CreatePlayerObject = true)
                bool isHostConnection = currentPlayers == 0 && NetworkManager.Singleton.IsServer;
                bool hasLocalPlayerManager = LocalPlayerManager.Instance != null;

                // Desktop host with LocalPlayerManager: false (LocalPlayerManager spawns)
                // Mobile host without LocalPlayerManager: true (auto-spawn)
                // All clients: true (auto-spawn)
                response.CreatePlayerObject = !isHostConnection || !hasLocalPlayerManager;

                Debug.Log($"[ConnectionManager] Connection approved - ClientNetworkId={request.ClientNetworkId}, IsServer={NetworkManager.Singleton.IsServer}, CurrentPlayers={currentPlayers}, HasLocalPlayerManager={hasLocalPlayerManager}, CreatePlayerObject={response.CreatePlayerObject}");
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[ConnectionManager] Client connected: {clientId}");

            if (NetworkManager.Singleton.IsHost)
            {
                int playerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;

                // LocalPlayerManager.OnHostStarted() is called directly after StartHost()
                // No need to call it here anymore

                if (playerCount >= MaxPlayers)
                {
                    // Stop advertising the lobby when it is full
                    if (_mode == LocalCoopMode.NativeP2P) _native?.StopAdvertisingLobby();
                    else _discovery?.StopBroadcast();

                    SetState(ConnectionState.Connected);
                    SetStatus(Loc.T("status.starting"));
                }
                else if (_mode == LocalCoopMode.Lan)
                {
                    // Update broadcast with current player count (native keeps advertising as-is)
                    _discovery?.StopBroadcast();
                    _discovery?.StartBroadcast(playerCount, MaxPlayers);
                }
            }
            else if (NetworkManager.Singleton.IsClient && clientId == NetworkManager.Singleton.LocalClientId)
            {
                // We connected as client
                SetState(ConnectionState.Connected);
                SetStatus(Loc.T("status.starting"));
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.Log($"[ConnectionManager] Client disconnected: {clientId}, LocalClientId={NetworkManager.Singleton.LocalClientId}, IsHost={NetworkManager.Singleton.IsHost}, IsClient={NetworkManager.Singleton.IsClient}");

            // Ignore callbacks caused by our own shutdown while yielding the host
            // role - FinishHostYield is about to reconnect us as a client - and
            // while leaving to the Home screen.
            if (_yieldingHost || _leaving) return;

            // Additional diagnostics for debugging disconnect issues
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport != null)
            {
                Debug.Log($"[ConnectionManager] Transport state - ConnectTimeoutMS={transport.ConnectTimeoutMS}, HeartbeatTimeoutMS={transport.HeartbeatTimeoutMS}");
            }

            if (NetworkManager.Singleton.IsHost)
            {
                // Another player left, reopen the lobby for new joiners
                int playerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
                if (playerCount < MaxPlayers)
                {
                    SetState(ConnectionState.WaitingForOpponent);
                    SetStatus(Loc.T("status.opponent_left"));

                    if (_mode == LocalCoopMode.NativeP2P)
                    {
                        _native?.StartAdvertisingLobby();
                    }
                    else
                    {
                        _discovery?.StopBroadcast();
                        _discovery?.StartBroadcast(playerCount, MaxPlayers);
                    }
                }
            }
            else
            {
                // We were client and got disconnected (host left)
                SetState(ConnectionState.Disconnected);
                SetStatus(Loc.T("status.host_disconnected"));

                // Restart discovery after a short delay
                if (_mode == LocalCoopMode.NativeP2P)
                    Invoke(nameof(NativeRestartDiscovery), 2f);
                else
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
