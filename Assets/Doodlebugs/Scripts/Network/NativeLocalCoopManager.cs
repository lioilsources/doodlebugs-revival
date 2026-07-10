using System;
using UnityEngine;

namespace Doodlebugs.Network
{
    /// <summary>
    /// Platform-agnostic "lobby" orchestrator for the mobile-data fallback.
    ///
    /// Flow (matches the requested behaviour):
    ///   1. Browse for a short window to see whether a lobby already exists.
    ///   2. If one is found  -> JOIN it (become client).
    ///   3. If none is found -> CREATE one (become host / start advertising), capped at 8.
    ///
    /// It owns the native backend and the <see cref="NativeLocalTransport"/>. Role
    /// decisions are surfaced to <see cref="ConnectionManager"/> via events; the
    /// manager keeps the actual StartHost/StartClient calls in one place.
    /// </summary>
    [RequireComponent(typeof(NativeLocalTransport))]
    public class NativeLocalCoopManager : MonoBehaviour
    {
        public const string SERVICE_TYPE = "doodlebugs"; // iOS: 1-15 chars, lowercase + hyphen
        public const int MAX_PLAYERS = 8;
        // Multipeer/Nearby discovery over the radio commonly takes a few seconds;
        // too short a window makes both devices give up and host (split-brain).
        private const float BROWSE_WINDOW_SECONDS = 4f;

        public NativeLocalTransport Transport { get; private set; }

        /// <summary>We decided to host. Handler should StartHost(); fired synchronously.</summary>
        public event Action OnBecomeHost;

        /// <summary>We decided to join host (arg = host peer id). Handler should StartClient().</summary>
        public event Action<string> OnBecomeClient;

        public event Action<string> OnStatus;

        /// <summary>Raised before switching role mid-flight so the host can shut down NGO.</summary>
        public event Action OnResetNetworking;

        private ILocalCoopBackend _backend;
        private string _localId;
        private bool _roleDecided;
        private bool _isHost;
        private float _browseStartTime;
        private float _browseWindow;
        private bool _browsing;
        private string _pendingHostPeerId;
        private string _clientHostPeerId;
        private int _beginToken; // invalidates a pending EnsurePermissions callback

        private void Awake()
        {
            Transport = GetComponent<NativeLocalTransport>();
            _localId = string.IsNullOrEmpty(SystemInfo.deviceUniqueIdentifier)
                ? Guid.NewGuid().ToString("N")
                : SystemInfo.deviceUniqueIdentifier;
        }

        /// <summary>Entry point — begins discovery on the native stack.</summary>
        public void Begin()
        {
            _backend = LocalCoopBackendFactory.Create();
            if (_backend == null || !_backend.IsAvailable)
            {
                Debug.LogWarning("[NativeLocalCoop] Native backend unavailable on this platform");
                OnStatus?.Invoke("Local co-op not supported here");
                return;
            }

            // Android needs runtime-granted Nearby/Bluetooth permissions before
            // discovery can work at all; the browse window must not start ticking
            // while the user is still answering the system dialogs.
            int token = ++_beginToken;
            OnStatus?.Invoke($"{UI.ConnectionUI.GameVersion}  •  Searching nearby (mobile data)...");
            _backend.EnsurePermissions(granted =>
            {
                if (token != _beginToken) return; // Stop()/Begin() cycled meanwhile

                if (!granted)
                {
                    Debug.LogWarning("[NativeLocalCoop] Required permissions denied - cannot discover players");
                    OnStatus?.Invoke("Nearby permissions denied - enable them in Settings");
                    return;
                }

                _backend.OnPeerFound += HandlePeerFound;
                _backend.Initialize(SERVICE_TYPE, _localId);

                // Look for an existing lobby first. A deterministic per-device jitter on
                // the window staggers timeouts so two devices rarely both become host
                // (split-brain) — the earlier one starts advertising and the other finds
                // it before its own window elapses.
                _browsing = true;
                _roleDecided = false;
                _browseStartTime = Time.realtimeSinceStartup;
                _browseWindow = BROWSE_WINDOW_SECONDS + JitterSeconds();
                _backend.StartBrowsing();
                Debug.Log("[NativeLocalCoop] Browsing for an existing lobby");
            });
        }

        /// <summary>Stop accepting new players (lobby full). Called by ConnectionManager.</summary>
        public void StopAdvertisingLobby() => _backend?.StopAdvertising();

        /// <summary>Reopen the lobby for new players (a player left). Called by ConnectionManager.</summary>
        public void StartAdvertisingLobby() => _backend?.StartAdvertising(MAX_PLAYERS);

        public void Stop()
        {
            _beginToken++; // a pending permission callback must not restart browsing
            CancelInvoke(nameof(DeferredBecomeClient));
            _pendingHostPeerId = null;

            if (_backend != null)
            {
                _backend.OnPeerFound -= HandlePeerFound;
                _backend.OnPeerConnected -= HandleClientSessionConnected;
                _backend.StopAll();
            }
            _clientHostPeerId = null;
            _browsing = false;
            _roleDecided = false;
        }

        private void Update()
        {
            if (!_browsing || _roleDecided) return;

            // No lobby discovered within the window -> create one (become host).
            if (Time.realtimeSinceStartup - _browseStartTime >= _browseWindow)
            {
                BecomeHost();
            }
        }

        // Stable 0..1.5s offset derived from this device's id.
        private float JitterSeconds()
        {
            int h = _localId.GetHashCode();
            return (Mathf.Abs(h) % 1500) / 1000f;
        }

        private void HandlePeerFound(string peerId)
        {
            Debug.Log($"[NativeLocalCoop] Lobby found: peer={peerId}");

            // While still deciding, the first discovered lobby wins -> we join it.
            if (!_roleDecided)
            {
                // A yield handover may already be scheduled (DeferredBecomeClient);
                // don't start a second client for a different peer meanwhile.
                if (!string.IsNullOrEmpty(_pendingHostPeerId)) return;

                BecomeClient(peerId);
                return;
            }

            // Split-brain guard: two devices may both have become hosts at once.
            // Deterministic tie-break — the host with the larger id yields and joins
            // the other, but only if nobody has joined us yet.
            if (_isHost && !Transport.HasConnectedPeers &&
                string.CompareOrdinal(_localId, peerId) > 0)
            {
                Debug.Log($"[NativeLocalCoop] Yielding host role to lower id peer={peerId}");
                _backend.StopAdvertising();
                _isHost = false;
                _roleDecided = false;
                _pendingHostPeerId = peerId;

                // Shut the half-started host down, then become a client next frame
                // (NGO completes Shutdown asynchronously, so defer the restart).
                OnResetNetworking?.Invoke();
                Invoke(nameof(DeferredBecomeClient), 0.5f);
            }
        }

        private void DeferredBecomeClient()
        {
            // Role may have been decided again in the meantime (e.g. Stop()/Begin()
            // cycled the flow) - never start a second client on top of it.
            if (_roleDecided)
            {
                _pendingHostPeerId = null;
                return;
            }

            if (!string.IsNullOrEmpty(_pendingHostPeerId))
            {
                BecomeClient(_pendingHostPeerId);
                _pendingHostPeerId = null;
            }
        }

        private void BecomeHost()
        {
            _roleDecided = true;
            _isHost = true;
            // Keep browsing to detect a competing host (split-brain), but stop the
            // window-driven decision loop.
            _browsing = false;

            Transport.Configure(_backend, isServer: true, hostPeerId: null);
            OnStatus?.Invoke("Created lobby. Waiting for players...");
            Debug.Log("[NativeLocalCoop] No lobby found -> creating one (host)");

            // ConnectionManager performs StartHost() synchronously here.
            OnBecomeHost?.Invoke();

            // Now open the lobby for joiners.
            _backend.StartAdvertising(MAX_PLAYERS);
        }

        private void BecomeClient(string hostPeerId)
        {
            _roleDecided = true;
            _isHost = false;
            _browsing = false;

            // Do NOT StopBrowsing() here. On iOS Multipeer the invitation issued
            // by Connect() is sent through the live MCNearbyServiceBrowser;
            // StopBrowsing nil's that browser natively and cancels the in-flight
            // invite, so the host never sees it. Keep the browser alive until the
            // session is Connected, then stop browsing in HandleClientSessionConnected.
            _clientHostPeerId = hostPeerId;
            _backend.OnPeerConnected += HandleClientSessionConnected;

            Transport.Configure(_backend, isServer: false, hostPeerId: hostPeerId);
            OnStatus?.Invoke("Joining lobby...");
            Debug.Log($"[NativeLocalCoop] Joining existing lobby host={hostPeerId}");

            // ConnectionManager performs StartClient() synchronously here.
            OnBecomeClient?.Invoke(hostPeerId);

            // Establish the native session with the host (client invites host).
            _backend.Connect(hostPeerId);
        }

        // The browser's job is done once the host session is established; stop
        // browsing so the client isn't scanning forever. Safe here because
        // MCSessionStateConnected has already fired, so the session no longer
        // depends on the browser instance.
        private void HandleClientSessionConnected(string peerId)
        {
            if (peerId != _clientHostPeerId) return;
            _backend.OnPeerConnected -= HandleClientSessionConnected;
            _backend.StopBrowsing();
            Debug.Log($"[NativeLocalCoop] Client session established host={peerId}; stopped browsing");
        }
    }
}
