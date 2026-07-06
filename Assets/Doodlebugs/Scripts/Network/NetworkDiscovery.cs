using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Doodlebugs.Network
{
    [Serializable]
    public class DiscoveryData
    {
        public string gameName;
        public string hostAddress;
        public int port;
        public int currentPlayers;
        public int maxPlayers;
        public string instanceId;
    }

    public class NetworkDiscovery : MonoBehaviour
    {
        public const int BROADCAST_PORT = 47777;
        public const int GAME_PORT = 7777;
        public const float BROADCAST_INTERVAL = 1f;
        public const float DISCOVERY_TIMEOUT_DESKTOP = 5f;
        public const float DISCOVERY_TIMEOUT_MOBILE = 10f;  // Mobile needs more time to receive broadcasts

        private static float DiscoveryTimeout =>
            (IsMobile ? DISCOVERY_TIMEOUT_MOBILE : DISCOVERY_TIMEOUT_DESKTOP) + TimeoutJitter;
        private static bool IsMobile => Application.platform == RuntimePlatform.Android ||
                                        Application.platform == RuntimePlatform.IPhonePlayer;

        // Identifies this process so we can filter our own broadcasts.
        // IP comparison doesn't work: ParrelSync clones share the machine's IP.
        private static readonly string InstanceId = Guid.NewGuid().ToString("N");

        // Per-instance 0..2s timeout offset. Two instances started at the same
        // moment would otherwise both time out together and both become hosts
        // (split-brain); the jitter staggers them so the earlier one is already
        // broadcasting before the later one gives up searching.
        private static readonly float TimeoutJitter =
            (Mathf.Abs(InstanceId.GetHashCode()) % 2000) / 1000f;

        /// <summary>This process's discovery id (used for host-conflict tie-breaks).</summary>
        public string LocalInstanceId => InstanceId;

        public event Action<DiscoveryData> OnServerFound;
        public event Action OnDiscoveryTimeout;

        private UdpClient _broadcastClient;
        private UdpClient _listenClient;
        private CancellationTokenSource _broadcastCancellation;
        private CancellationTokenSource _listenCancellation;
        private bool _isBroadcasting;
        private bool _isListening;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _multicastLock;
#endif

        public bool IsBroadcasting => _isBroadcasting;
        public bool IsListening => _isListening;

        private void OnDestroy()
        {
            StopAllDiscovery();
        }

        private void OnApplicationQuit()
        {
            StopAllDiscovery();
        }

        public void StopAllDiscovery()
        {
            StopBroadcast();
            StopListening();
        }

        #region Host Broadcasting

        public void StartBroadcast(int currentPlayers = 1, int maxPlayers = 2)
        {
            if (_isBroadcasting) return;

            _isBroadcasting = true;
            _broadcastCancellation = new CancellationTokenSource();

            string localIP = GetLocalIPAddress();
            Debug.Log($"[NetworkDiscovery] Starting broadcast on {localIP}:{GAME_PORT}");

            var data = new DiscoveryData
            {
                gameName = "Doodlebugs",
                hostAddress = localIP,
                port = GAME_PORT,
                currentPlayers = currentPlayers,
                maxPlayers = maxPlayers,
                instanceId = InstanceId
            };

            _ = BroadcastLoopAsync(data, _broadcastCancellation.Token);
        }

        public void StopBroadcast()
        {
            if (!_isBroadcasting) return;

            _isBroadcasting = false;
            _broadcastCancellation?.Cancel();
            _broadcastCancellation?.Dispose();
            _broadcastCancellation = null;
            _broadcastClient?.Close();
            _broadcastClient = null;

            Debug.Log("[NetworkDiscovery] Stopped broadcasting");
        }

        private async Task BroadcastLoopAsync(DiscoveryData data, CancellationToken token)
        {
            try
            {
                // Bind to local IP for better iOS compatibility
                string localIP = GetLocalIPAddress();
                IPEndPoint localEndpoint;
                if (localIP == "127.0.0.1")
                {
                    Debug.LogWarning("[NetworkDiscovery] No LAN IP found - binding to ANY; discovery may not work");
                    localEndpoint = new IPEndPoint(IPAddress.Any, 0);
                }
                else
                {
                    localEndpoint = new IPEndPoint(IPAddress.Parse(localIP), 0);
                }

                _broadcastClient = new UdpClient(localEndpoint);
                _broadcastClient.EnableBroadcast = true;
                _broadcastClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);

                // Send to the subnet-directed broadcast address (more compatible with iOS)
                // AND to the limited broadcast address as a fallback for odd network setups
                var endpoints = new List<IPEndPoint>
                {
                    new IPEndPoint(GetBroadcastAddress(), BROADCAST_PORT)
                };
                if (!endpoints[0].Address.Equals(IPAddress.Broadcast))
                {
                    endpoints.Add(new IPEndPoint(IPAddress.Broadcast, BROADCAST_PORT));
                }

                string json = JsonUtility.ToJson(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                Debug.Log($"[NetworkDiscovery] Broadcasting from {localIP} to {string.Join(", ", endpoints)}");

                while (!token.IsCancellationRequested && _isBroadcasting)
                {
                    bool anySent = false;
                    foreach (var endpoint in endpoints)
                    {
                        try
                        {
                            await _broadcastClient.SendAsync(bytes, bytes.Length, endpoint);
                            anySent = true;
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[NetworkDiscovery] Broadcast error to {endpoint}: {e.Message}");
                        }
                    }

                    if (!anySent)
                    {
                        // All sends failed - back off before retrying
                        await Task.Delay(1000, token);
                    }

                    await Task.Delay((int)(BROADCAST_INTERVAL * 1000), token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkDiscovery] Broadcast loop error: {e}");
            }
        }

        #endregion

        #region Client Listening

        /// <param name="hostWatch">
        /// When true the listener never times out and keeps running after a server
        /// is found. Used while hosting to detect a competing host on the same
        /// network (split-brain); ConnectionManager decides who yields.
        /// </param>
        public void StartListening(bool hostWatch = false)
        {
            if (_isListening) return;

            _isListening = true;
            _listenCancellation = new CancellationTokenSource();

            // JNI must run on the main thread; the dispatcher also keeps
            // acquire/release ordered when StopListening fires from the async loop
            MainThreadDispatcher.Enqueue(AcquireMulticastLock);

            Debug.Log($"[NetworkDiscovery] Starting to listen for hosts on port {BROADCAST_PORT}, timeout={DiscoveryTimeout}s, isMobile={IsMobile}, hostWatch={hostWatch}");
            _ = ListenLoopAsync(_listenCancellation.Token, hostWatch);
        }

        public void StopListening()
        {
            if (!_isListening) return;

            _isListening = false;
            _listenCancellation?.Cancel();
            _listenCancellation?.Dispose();
            _listenCancellation = null;
            _listenClient?.Close();
            _listenClient = null;

            MainThreadDispatcher.Enqueue(ReleaseMulticastLock);

            Debug.Log("[NetworkDiscovery] Stopped listening");
        }

        private async Task ListenLoopAsync(CancellationToken token, bool hostWatch)
        {
            float startTime = Time.realtimeSinceStartup;

            try
            {
                // Bind to any address on the broadcast port
                _listenClient = new UdpClient();
                _listenClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listenClient.Client.Bind(new IPEndPoint(IPAddress.Any, BROADCAST_PORT));
                _listenClient.EnableBroadcast = true;

                string localIP = GetLocalIPAddress();
                Debug.Log($"[NetworkDiscovery] Listening on port {BROADCAST_PORT}, local IP: {localIP}");

                while (!token.IsCancellationRequested && _isListening)
                {
                    // Check timeout (host-watch mode runs until stopped)
                    if (!hostWatch && Time.realtimeSinceStartup - startTime > DiscoveryTimeout)
                    {
                        Debug.Log("[NetworkDiscovery] Discovery timeout - no host found");
                        MainThreadDispatcher.Enqueue(() => OnDiscoveryTimeout?.Invoke());
                        StopListening();
                        return;
                    }

                    if (_listenClient.Available > 0)
                    {
                        try
                        {
                            var remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
                            byte[] bytes = _listenClient.Receive(ref remoteEndpoint);
                            string json = Encoding.UTF8.GetString(bytes);

                            Debug.Log($"[NetworkDiscovery] Received: {json} from {remoteEndpoint.Address}");

                            var data = JsonUtility.FromJson<DiscoveryData>(json);

                            // Ignore our own broadcasts (matched by instance ID, not IP -
                            // IP comparison breaks ParrelSync clones sharing one machine)
                            if (!string.IsNullOrEmpty(data.instanceId) && data.instanceId == InstanceId)
                            {
                                Debug.Log("[NetworkDiscovery] Ignoring own broadcast");
                                continue;
                            }

                            // Use remote endpoint address if hostAddress is local
                            if (string.IsNullOrEmpty(data.hostAddress) ||
                                data.hostAddress == "127.0.0.1" ||
                                data.hostAddress == "localhost")
                            {
                                data.hostAddress = remoteEndpoint.Address.ToString();
                            }

                            MainThreadDispatcher.Enqueue(() => OnServerFound?.Invoke(data));
                            if (!hostWatch)
                            {
                                StopListening();
                                return;
                            }
                            // Host-watch keeps listening; ConnectionManager stops
                            // discovery itself if it decides to yield the host role.
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[NetworkDiscovery] Receive error: {e.Message}");
                        }
                    }

                    await Task.Delay(100, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            catch (SocketException e)
            {
                Debug.LogError($"[NetworkDiscovery] Socket error (code={e.SocketErrorCode}): {e.Message}");
                Debug.LogError($"[NetworkDiscovery] This may indicate port {BROADCAST_PORT} is in use or blocked on this device");
                MainThreadDispatcher.Enqueue(() => OnDiscoveryTimeout?.Invoke());
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkDiscovery] Listen loop error ({e.GetType().Name}): {e.Message}");
                // Don't silently fail - trigger timeout so game doesn't hang
                MainThreadDispatcher.Enqueue(() => OnDiscoveryTimeout?.Invoke());
            }
        }

        #endregion

        #region Android Multicast Lock

        // Many Android devices drop inbound UDP broadcast/multicast at the WiFi
        // driver level unless the app holds a WifiManager.MulticastLock.
        // The CHANGE_WIFI_MULTICAST_STATE permission alone is not enough.

        private void AcquireMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_multicastLock != null) return;
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var context = activity.Call<AndroidJavaObject>("getApplicationContext");
                using var wifiManager = context.Call<AndroidJavaObject>("getSystemService", "wifi");
                _multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "DoodlebugsDiscovery");
                _multicastLock.Call("setReferenceCounted", false);
                _multicastLock.Call("acquire");
                Debug.Log("[NetworkDiscovery] Android multicast lock acquired");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkDiscovery] Failed to acquire multicast lock: {e.Message}");
            }
#endif
        }

        private void ReleaseMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_multicastLock != null && _multicastLock.Call<bool>("isHeld"))
                {
                    _multicastLock.Call("release");
                }
                _multicastLock?.Dispose();
                _multicastLock = null;
                Debug.Log("[NetworkDiscovery] Android multicast lock released");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkDiscovery] Failed to release multicast lock: {e.Message}");
            }
#endif
        }

        #endregion

        #region Utility

        private string GetLocalIPAddress()
        {
            // 1) Route-based trick - works whenever a default route exists
            //    (no packet is actually sent, so it also works offline)
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint endPoint && !IPAddress.IsLoopback(endPoint.Address))
                    {
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch
            {
                // No default route (e.g. offline LAN) - fall through to interface enumeration
            }

            // 2) Enumerate interfaces: prefer WiFi/Ethernet, skip loopback/down/link-local
            try
            {
                string candidate = null;
                string linkLocal = null;
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    bool preferred = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                                     nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet;

                    foreach (var address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(address.Address)) continue;

                        string ip = address.Address.ToString();
                        if (ip.StartsWith("169.254."))
                        {
                            linkLocal ??= ip;
                            continue;
                        }
                        if (preferred) return ip;
                        candidate ??= ip;
                    }
                }
                if (candidate != null) return candidate;
                if (linkLocal != null) return linkLocal;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkDiscovery] Interface enumeration failed: {e.Message}");
            }

            return "127.0.0.1";
        }

        // Calculate the subnet-directed broadcast address from the interface's
        // real netmask (e.g. 172.20.10.15 for 172.20.10.x/28 iOS hotspots).
        private IPAddress GetBroadcastAddress()
        {
            try
            {
                string localIPStr = GetLocalIPAddress();
                if (IPAddress.TryParse(localIPStr, out var localIP) && !IPAddress.IsLoopback(localIP))
                {
                    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus != OperationalStatus.Up) continue;
                        foreach (var address in nic.GetIPProperties().UnicastAddresses)
                        {
                            if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                            if (!address.Address.Equals(localIP)) continue;
                            try
                            {
                                var mask = address.IPv4Mask;
                                if (mask != null && !mask.Equals(IPAddress.Any))
                                {
                                    byte[] ipBytes = localIP.GetAddressBytes();
                                    byte[] maskBytes = mask.GetAddressBytes();
                                    var broadcastBytes = new byte[4];
                                    for (int i = 0; i < 4; i++)
                                    {
                                        broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                                    }
                                    var result = new IPAddress(broadcastBytes);
                                    Debug.Log($"[NetworkDiscovery] Using broadcast address: {result} (ip={localIP}, mask={mask})");
                                    return result;
                                }
                            }
                            catch (Exception)
                            {
                                // IPv4Mask can throw on some platforms - fall through to /24
                            }
                        }
                    }

                    // Fallback: assume /24 subnet (most common for home networks)
                    byte[] parts = localIP.GetAddressBytes();
                    parts[3] = 255;
                    var fallback = new IPAddress(parts);
                    Debug.Log($"[NetworkDiscovery] Using broadcast address (assumed /24): {fallback}");
                    return fallback;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkDiscovery] Failed to calculate broadcast address: {e.Message}");
            }

            return IPAddress.Broadcast;
        }

        #endregion
    }

    // Helper to dispatch actions to main thread
    public static class MainThreadDispatcher
    {
        private static readonly System.Collections.Generic.Queue<Action> _actions = new();
        private static readonly object _lock = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var go = new GameObject("MainThreadDispatcher");
            go.AddComponent<MainThreadDispatcherBehaviour>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static void Enqueue(Action action)
        {
            lock (_lock)
            {
                _actions.Enqueue(action);
            }
        }

        public static void ProcessQueue()
        {
            lock (_lock)
            {
                while (_actions.Count > 0)
                {
                    _actions.Dequeue()?.Invoke();
                }
            }
        }

        private class MainThreadDispatcherBehaviour : MonoBehaviour
        {
            private void Update()
            {
                ProcessQueue();
            }
        }
    }
}
