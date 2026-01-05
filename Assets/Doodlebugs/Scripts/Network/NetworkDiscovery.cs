using System;
using System.Net;
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
    }

    public class NetworkDiscovery : MonoBehaviour
    {
        public const int BROADCAST_PORT = 47777;
        public const int GAME_PORT = 7777;
        public const float BROADCAST_INTERVAL = 1f;
        public const float DISCOVERY_TIMEOUT = 5f;

        public event Action<DiscoveryData> OnServerFound;
        public event Action OnDiscoveryTimeout;

        private UdpClient _broadcastClient;
        private UdpClient _listenClient;
        private CancellationTokenSource _cancellationSource;
        private bool _isBroadcasting;
        private bool _isListening;

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
            _cancellationSource = new CancellationTokenSource();

            string localIP = GetLocalIPAddress();
            Debug.Log($"[NetworkDiscovery] Starting broadcast on {localIP}:{GAME_PORT}");

            var data = new DiscoveryData
            {
                gameName = "Doodlebugs",
                hostAddress = localIP,
                port = GAME_PORT,
                currentPlayers = currentPlayers,
                maxPlayers = maxPlayers
            };

            _ = BroadcastLoopAsync(data, _cancellationSource.Token);
        }

        public void StopBroadcast()
        {
            if (!_isBroadcasting) return;

            _isBroadcasting = false;
            _cancellationSource?.Cancel();
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
                var localEndpoint = new IPEndPoint(IPAddress.Parse(localIP), 0);

                _broadcastClient = new UdpClient(localEndpoint);
                _broadcastClient.EnableBroadcast = true;
                _broadcastClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);

                // Use subnet broadcast address instead of 255.255.255.255 (more compatible with iOS)
                var broadcastAddress = GetBroadcastAddress();
                var endpoint = new IPEndPoint(broadcastAddress, BROADCAST_PORT);
                string json = JsonUtility.ToJson(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                Debug.Log($"[NetworkDiscovery] Broadcasting from {localIP} to {broadcastAddress}:{BROADCAST_PORT}");

                while (!token.IsCancellationRequested && _isBroadcasting)
                {
                    try
                    {
                        await _broadcastClient.SendAsync(bytes, bytes.Length, endpoint);
                        Debug.Log($"[NetworkDiscovery] Broadcast sent: {json}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[NetworkDiscovery] Broadcast error: {e.Message}");
                        // Try to recreate the client on error
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

        public void StartListening()
        {
            if (_isListening) return;

            _isListening = true;
            _cancellationSource = new CancellationTokenSource();

            Debug.Log($"[NetworkDiscovery] Starting to listen for hosts on port {BROADCAST_PORT}");
            _ = ListenLoopAsync(_cancellationSource.Token);
        }

        public void StopListening()
        {
            if (!_isListening) return;

            _isListening = false;
            _cancellationSource?.Cancel();
            _listenClient?.Close();
            _listenClient = null;

            Debug.Log("[NetworkDiscovery] Stopped listening");
        }

        private async Task ListenLoopAsync(CancellationToken token)
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
                    // Check timeout
                    if (Time.realtimeSinceStartup - startTime > DISCOVERY_TIMEOUT)
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

                            // Ignore our own broadcasts
                            if (remoteEndpoint.Address.ToString() == localIP)
                            {
                                Debug.Log("[NetworkDiscovery] Ignoring own broadcast");
                                continue;
                            }

                            var data = JsonUtility.FromJson<DiscoveryData>(json);

                            // Use remote endpoint address if hostAddress is local
                            if (string.IsNullOrEmpty(data.hostAddress) ||
                                data.hostAddress == "127.0.0.1" ||
                                data.hostAddress == "localhost")
                            {
                                data.hostAddress = remoteEndpoint.Address.ToString();
                            }

                            MainThreadDispatcher.Enqueue(() => OnServerFound?.Invoke(data));
                            StopListening();
                            return;
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
                Debug.LogError($"[NetworkDiscovery] Socket error: {e.Message}");
                MainThreadDispatcher.Enqueue(() => OnDiscoveryTimeout?.Invoke());
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkDiscovery] Listen loop error: {e}");
            }
        }

        #endregion

        #region Utility

        private string GetLocalIPAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint?.Address.ToString() ?? "127.0.0.1";
                }
            }
            catch
            {
                // Fallback: try to find first valid IP
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
                return "127.0.0.1";
            }
        }

        // Calculate subnet broadcast address (e.g., 192.168.88.255 for 192.168.88.x/24)
        private IPAddress GetBroadcastAddress()
        {
            try
            {
                string localIP = GetLocalIPAddress();
                var ipParts = localIP.Split('.');
                if (ipParts.Length == 4)
                {
                    // Assume /24 subnet (most common for home networks)
                    string broadcastIP = $"{ipParts[0]}.{ipParts[1]}.{ipParts[2]}.255";
                    Debug.Log($"[NetworkDiscovery] Using broadcast address: {broadcastIP}");
                    return IPAddress.Parse(broadcastIP);
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
