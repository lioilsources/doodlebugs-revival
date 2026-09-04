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

        // Session handshakes fail transiently on the radio; re-invite a few times
        // before giving up and restarting the whole discovery flow.
        private const float CONNECT_RETRY_SECONDS = 5f;
        private const int CONNECT_MAX_ATTEMPTS = 3;

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
        private int _connectAttempts;
        private int _beginToken; // invalidates a pending EnsurePermissions callback

        // The lobby we most recently failed to join, and until when to ignore it.
        // Without this the client loops forever against a host that has gone
        // quiet but is still advertised: 3 retries x 5 s -> "join failed" ->
        // Begin() -> browser finds the same peer -> "Joining lobby..." again. A
        // suspended iOS host is exactly that (see OnApplicationPause below), and
        // Bonjour can keep a killed one visible for a while too. Ignoring the
        // dead peer lets the browse window elapse, and BecomeHost() takes over.
        private string _ignoredPeerId;
        private float _ignoredPeerUntil;
        private const float FAILED_PEER_COOLDOWN_SECONDS = 45f;

        // Host side of the same problem: nothing used to stop advertising when
        // the app went to the background, so a host that was "closed" by going
        // Home kept its lobby visible to every browser on the network while
        // being unable to accept anyone.
        private bool _advertisingPausedByBackground;

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
        public void StopAdvertisingLobby()
        {
            _lobbyClosedAsFull = true;
            _backend?.StopAdvertising();
        }

        /// <summary>Reopen the lobby for new players (a player left). Called by ConnectionManager.</summary>
        public void StartAdvertisingLobby()
        {
            _lobbyClosedAsFull = false;
            _backend?.StartAdvertising(MAX_PLAYERS);
        }

        // Whether ConnectionManager deliberately closed the lobby (full), so a
        // resume from the background knows not to reopen it.
        private bool _lobbyClosedAsFull;

        public void Stop()
        {
            _beginToken++; // a pending permission callback must not restart browsing
            CancelInvoke(nameof(DeferredBecomeClient));
            CancelInvoke(nameof(ClientConnectWatchdog));
            CancelInvoke(nameof(RestartDiscoveryFlow));
            _pendingHostPeerId = null;

            if (_backend != null)
            {
                _backend.OnPeerFound -= HandlePeerFound;
                _backend.OnPeerConnected -= HandleClientSessionConnected;
                _backend.OnPeerDisconnected -= HandleClientSessionFailed;
                _backend.StopAll();
            }
            _clientHostPeerId = null;
            _connectAttempts = 0;
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

            // A lobby we just gave up on is not a lobby - let the window run out.
            if (peerId == _ignoredPeerId && Time.realtimeSinceStartup < _ignoredPeerUntil)
            {
                Debug.Log($"[NativeLocalCoop] Ignoring peer={peerId} - join failed " +
                          $"{FAILED_PEER_COOLDOWN_SECONDS - (_ignoredPeerUntil - Time.realtimeSinceStartup):0}s ago");
                return;
            }

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
            _connectAttempts = 1;
            _backend.OnPeerConnected += HandleClientSessionConnected;
            _backend.OnPeerDisconnected += HandleClientSessionFailed;

            Transport.Configure(_backend, isServer: false, hostPeerId: hostPeerId);
            OnStatus?.Invoke("Joining lobby...");
            Debug.Log($"[NativeLocalCoop] Joining existing lobby host={hostPeerId}");

            // ConnectionManager performs StartClient() synchronously here.
            OnBecomeClient?.Invoke(hostPeerId);

            // Establish the native session with the host (client invites host).
            _backend.Connect(hostPeerId);
            Invoke(nameof(ClientConnectWatchdog), CONNECT_RETRY_SECONDS);
        }

        // The browser's job is done once the host session is established; stop
        // browsing so the client isn't scanning forever. Safe here because
        // MCSessionStateConnected has already fired, so the session no longer
        // depends on the browser instance.
        private void HandleClientSessionConnected(string peerId)
        {
            if (peerId != _clientHostPeerId) return;
            CancelInvoke(nameof(ClientConnectWatchdog));
            _backend.OnPeerConnected -= HandleClientSessionConnected;
            _backend.OnPeerDisconnected -= HandleClientSessionFailed;
            _clientHostPeerId = null;
            _backend.StopBrowsing();
            Debug.Log($"[NativeLocalCoop] Client session established host={peerId}; stopped browsing");
        }

        // The pending session dropped before it ever connected (handshake failure)
        // - retry immediately instead of waiting out the watchdog timer.
        private void HandleClientSessionFailed(string peerId)
        {
            if (string.IsNullOrEmpty(_clientHostPeerId) || peerId != _clientHostPeerId) return;
            Debug.LogWarning($"[NativeLocalCoop] Session to host {peerId} failed before connecting");
            CancelInvoke(nameof(ClientConnectWatchdog));
            ClientConnectWatchdog();
        }

        private void ClientConnectWatchdog()
        {
            if (string.IsNullOrEmpty(_clientHostPeerId)) return; // connected or stopped

            if (_connectAttempts < CONNECT_MAX_ATTEMPTS)
            {
                _connectAttempts++;
                Debug.Log($"[NativeLocalCoop] Connect retry {_connectAttempts}/{CONNECT_MAX_ATTEMPTS} host={_clientHostPeerId}");
                _backend.Connect(_clientHostPeerId);
                Invoke(nameof(ClientConnectWatchdog), CONNECT_RETRY_SECONDS);
                return;
            }

            // Out of retries - the host may be gone; start the whole flow over,
            // but not straight back into the same lobby.
            Debug.LogWarning($"[NativeLocalCoop] Could not join lobby host={_clientHostPeerId} - restarting discovery");
            _ignoredPeerId = _clientHostPeerId;
            _ignoredPeerUntil = Time.realtimeSinceStartup + FAILED_PEER_COOLDOWN_SECONDS;
            OnStatus?.Invoke("Join failed - searching again...");
            OnResetNetworking?.Invoke(); // NGO client shuts down asynchronously
            Stop();
            Invoke(nameof(RestartDiscoveryFlow), 0.5f);
        }

        private void RestartDiscoveryFlow() => Begin();

        // A host that goes to the background can neither accept invites nor
        // run the game, yet its Multipeer advertisement stayed up - every
        // browser nearby kept "finding" it and trying to join. Pull the lobby
        // while suspended; put it back on resume if we are still the host and
        // the lobby is still open (StopAdvertisingLobby, for a full lobby, is
        // remembered by not having set the flag).
        private void OnApplicationPause(bool paused)
        {
            if (_backend == null || !_isHost) return;

            if (paused)
            {
                _backend.StopAdvertising();
                _advertisingPausedByBackground = true;
                Debug.Log("[NativeLocalCoop] Host backgrounded - lobby withdrawn");
            }
            else if (_advertisingPausedByBackground)
            {
                _advertisingPausedByBackground = false;
                if (!_lobbyClosedAsFull)
                {
                    _backend.StartAdvertising(MAX_PLAYERS);
                    Debug.Log("[NativeLocalCoop] Host resumed - lobby re-advertised");
                }
            }
        }
    }
}
