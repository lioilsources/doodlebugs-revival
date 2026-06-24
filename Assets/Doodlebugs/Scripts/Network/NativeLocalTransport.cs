using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Doodlebugs.Network
{
    /// <summary>
    /// Netcode for GameObjects transport that tunnels NGO traffic over a native
    /// local peer-to-peer session (<see cref="ILocalCoopBackend"/>) instead of UDP.
    ///
    /// Used only on the mobile-data fallback path. On Wi-Fi the game keeps using
    /// the stock <c>UnityTransport</c>.
    ///
    /// Role model:
    ///   - Host:   <see cref="StartServer"/>. Each peer that joins the native
    ///             session becomes an NGO client with an assigned transport id.
    ///   - Client: <see cref="StartClient"/>. The single host peer is mapped to
    ///             <see cref="ServerClientId"/>.
    /// </summary>
    public class NativeLocalTransport : NetworkTransport
    {
        private struct QueuedEvent
        {
            public NetworkEvent Type;
            public ulong ClientId;
            public ArraySegment<byte> Payload;
            public float ReceiveTime;
        }

        private ILocalCoopBackend _backend;
        private bool _isServer;
        private string _hostPeerId;
        private bool _running;

        // peerId <-> transport clientId mapping (server assigns ids; client uses ServerClientId)
        private readonly Dictionary<string, ulong> _peerToClient = new();
        private readonly Dictionary<ulong, string> _clientToPeer = new();
        private ulong _nextClientId = 1; // 0 is reserved for the server itself

        private readonly Queue<QueuedEvent> _events = new();
        private readonly object _lock = new();

        public override ulong ServerClientId => 0;

        /// <summary>True if at least one remote peer is currently mapped (server side).</summary>
        public bool HasConnectedPeers => _peerToClient.Count > 0;

        /// <summary>
        /// Wire up the native backend and role BEFORE StartServer/StartClient.
        /// </summary>
        public void Configure(ILocalCoopBackend backend, bool isServer, string hostPeerId)
        {
            _backend = backend;
            _isServer = isServer;
            _hostPeerId = hostPeerId;
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
            // Nothing to allocate; the native session is owned by the backend.
        }

        public override bool StartServer()
        {
            if (_backend == null)
            {
                Debug.LogError("[NativeLocalTransport] StartServer with no backend configured");
                return false;
            }

            _isServer = true;
            SubscribeBackend();
            _running = true;
            Debug.Log("[NativeLocalTransport] Server started");
            return true;
        }

        public override bool StartClient()
        {
            if (_backend == null || string.IsNullOrEmpty(_hostPeerId))
            {
                Debug.LogError("[NativeLocalTransport] StartClient with no backend/host configured");
                return false;
            }

            _isServer = false;
            SubscribeBackend();
            _running = true;
            Debug.Log($"[NativeLocalTransport] Client started, host peer={_hostPeerId}");
            return true;
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            if (_backend == null) return;

            string peerId = ResolvePeer(clientId);
            if (peerId == null)
            {
                Debug.LogWarning($"[NativeLocalTransport] Send to unknown clientId={clientId}");
                return;
            }

            // Copy out of the pooled segment before handing to native code.
            byte[] bytes = new byte[payload.Count];
            Array.Copy(payload.Array, payload.Offset, bytes, 0, payload.Count);

            bool reliable = networkDelivery != NetworkDelivery.Unreliable &&
                            networkDelivery != NetworkDelivery.UnreliableSequenced;
            _backend.SendTo(peerId, bytes, reliable);
        }

        // We deliver via InvokeOnTransportEvent during Update; PollEvent stays empty
        // (same approach as UnityTransport).
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload,
                                               out float receiveTime)
        {
            clientId = 0;
            payload = default;
            receiveTime = Time.realtimeSinceStartup;
            return NetworkEvent.Nothing;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            string peerId = ResolvePeer(clientId);
            if (peerId != null)
            {
                _backend?.Disconnect(peerId);
                RemovePeer(peerId);
            }
        }

        public override void DisconnectLocalClient()
        {
            // The native backend lifecycle is owned by NativeLocalCoopManager; here we
            // only stop pumping events. (Stopping it here would break the split-brain
            // host->client handover, which keeps the same backend alive.)
            _running = false;
        }

        public override ulong GetCurrentRtt(ulong clientId) => 0;

        public override void Shutdown()
        {
            _running = false;
            UnsubscribeBackend();
            lock (_lock) { _events.Clear(); }
            _peerToClient.Clear();
            _clientToPeer.Clear();
            // NOTE: do NOT stop the native backend here. Its lifecycle is owned by
            // NativeLocalCoopManager (manager.Stop() calls StopAll). NGO triggers this
            // Shutdown during the split-brain host->client handover, which must keep the
            // same backend alive to connect to the surviving host.
            Debug.Log("[NativeLocalTransport] Shutdown");
        }

        private void Update()
        {
            // Flush native events on the main thread once NGO has subscribed
            // (subscription happens synchronously inside StartServer/StartClient).
            if (!_running) return;

            while (true)
            {
                QueuedEvent e;
                lock (_lock)
                {
                    if (_events.Count == 0) return;
                    e = _events.Dequeue();
                }
                InvokeOnTransportEvent(e.Type, e.ClientId, e.Payload, e.ReceiveTime);
            }
        }

        #region Backend wiring

        private void SubscribeBackend()
        {
            if (_backend == null) return;
            _backend.OnPeerConnected += HandlePeerConnected;
            _backend.OnPeerDisconnected += HandlePeerDisconnected;
            _backend.OnDataReceived += HandleDataReceived;
        }

        private void UnsubscribeBackend()
        {
            if (_backend == null) return;
            _backend.OnPeerConnected -= HandlePeerConnected;
            _backend.OnPeerDisconnected -= HandlePeerDisconnected;
            _backend.OnDataReceived -= HandleDataReceived;
        }

        private void HandlePeerConnected(string peerId)
        {
            // On a client we only care about the host peer.
            if (!_isServer && peerId != _hostPeerId) return;

            ulong clientId = MapPeer(peerId);
            Enqueue(new QueuedEvent
            {
                Type = NetworkEvent.Connect,
                ClientId = clientId,
                Payload = default,
                ReceiveTime = Time.realtimeSinceStartup
            });
            Debug.Log($"[NativeLocalTransport] Peer connected peer={peerId} clientId={clientId}");
        }

        private void HandlePeerDisconnected(string peerId)
        {
            if (!_peerToClient.TryGetValue(peerId, out ulong clientId)) return;

            Enqueue(new QueuedEvent
            {
                Type = NetworkEvent.Disconnect,
                ClientId = clientId,
                Payload = default,
                ReceiveTime = Time.realtimeSinceStartup
            });
            RemovePeer(peerId);
            Debug.Log($"[NativeLocalTransport] Peer disconnected peer={peerId} clientId={clientId}");
        }

        private void HandleDataReceived(string peerId, byte[] data)
        {
            ulong clientId = MapPeer(peerId);
            Enqueue(new QueuedEvent
            {
                Type = NetworkEvent.Data,
                ClientId = clientId,
                Payload = new ArraySegment<byte>(data),
                ReceiveTime = Time.realtimeSinceStartup
            });
        }

        #endregion

        #region Peer id mapping

        // On the client every peer (the host) is ServerClientId(0). On the server we
        // assign a fresh id per peer.
        private ulong MapPeer(string peerId)
        {
            if (!_isServer) return ServerClientId;

            if (_peerToClient.TryGetValue(peerId, out ulong existing)) return existing;

            ulong id = _nextClientId++;
            _peerToClient[peerId] = id;
            _clientToPeer[id] = peerId;
            return id;
        }

        private string ResolvePeer(ulong clientId)
        {
            if (!_isServer) return _hostPeerId; // client always talks to the host
            return _clientToPeer.TryGetValue(clientId, out string peerId) ? peerId : null;
        }

        private void RemovePeer(string peerId)
        {
            if (_peerToClient.TryGetValue(peerId, out ulong clientId))
            {
                _peerToClient.Remove(peerId);
                _clientToPeer.Remove(clientId);
            }
        }

        private void Enqueue(QueuedEvent e)
        {
            lock (_lock) { _events.Enqueue(e); }
        }

        #endregion
    }
}
