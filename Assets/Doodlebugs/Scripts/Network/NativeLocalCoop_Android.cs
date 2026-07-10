#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

namespace Doodlebugs.Network
{
    /// <summary>
    /// Android backend backed by Google Nearby Connections
    /// (see Assets/Plugins/Android/DoodlebugsNearby.java).
    ///
    /// Native callbacks arrive via UnityPlayer.UnitySendMessage on a hidden
    /// receiver GameObject (already on the main thread) and are forwarded to the
    /// live instance.
    /// </summary>
    public class NearbyLocalCoopBackend : ILocalCoopBackend
    {
        private const string RECEIVER_NAME = "DoodlebugsNearbyReceiver";
        private const string JAVA_CLASS = "com.doodlebugs.nearby.DoodlebugsNearby";

        private AndroidJavaObject _java;
        private NearbyCallbackReceiver _receiver;

        public bool IsAvailable => true;

        public event Action<string> OnPeerFound;
        public event Action<string> OnPeerConnected;
        public event Action<string> OnPeerDisconnected;
        public event Action<string, byte[]> OnDataReceived;

        /// <summary>
        /// Nearby Connections needs dangerous runtime permissions that the user
        /// must grant (they are declared in the manifest but denied by default):
        /// Android 12+ BLUETOOTH_SCAN/ADVERTISE/CONNECT, 13+ NEARBY_WIFI_DEVICES,
        /// pre-12 fine location. Without them startAdvertising/startDiscovery
        /// fail silently and no lobby is ever found.
        /// </summary>
        public void EnsurePermissions(Action<bool> done)
        {
            string[] required = RequiredPermissions();

            var missing = new List<string>();
            foreach (string p in required)
            {
                if (!Permission.HasUserAuthorizedPermission(p)) missing.Add(p);
            }
            if (missing.Count == 0)
            {
                done?.Invoke(true);
                return;
            }

            Debug.Log($"[Nearby] Requesting runtime permissions: {string.Join(", ", missing)}");

            int pending = missing.Count;
            var callbacks = new PermissionCallbacks();
            void OnOneResolved(string _)
            {
                if (--pending > 0) return;
                bool allGranted = true;
                foreach (string p in required)
                {
                    if (!Permission.HasUserAuthorizedPermission(p)) { allGranted = false; break; }
                }
                Debug.Log($"[Nearby] Permission request finished, allGranted={allGranted}");
                done?.Invoke(allGranted);
            }
            callbacks.PermissionGranted += OnOneResolved;
            callbacks.PermissionDenied += OnOneResolved;
            Permission.RequestUserPermissions(missing.ToArray(), callbacks);
        }

        private static string[] RequiredPermissions()
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            int sdk = version.GetStatic<int>("SDK_INT");

            if (sdk >= 33)
            {
                return new[]
                {
                    "android.permission.BLUETOOTH_SCAN",
                    "android.permission.BLUETOOTH_ADVERTISE",
                    "android.permission.BLUETOOTH_CONNECT",
                    "android.permission.NEARBY_WIFI_DEVICES",
                };
            }
            if (sdk >= 31)
            {
                return new[]
                {
                    "android.permission.BLUETOOTH_SCAN",
                    "android.permission.BLUETOOTH_ADVERTISE",
                    "android.permission.BLUETOOTH_CONNECT",
                };
            }
            // Legacy Bluetooth permissions are install-time; Nearby additionally
            // needs location at runtime on Android 11 and below.
            return new[] { Permission.FineLocation };
        }

        public void Initialize(string serviceType, string displayName)
        {
            // Hidden GameObject that receives UnitySendMessage callbacks.
            var go = new GameObject(RECEIVER_NAME);
            UnityEngine.Object.DontDestroyOnLoad(go);
            _receiver = go.AddComponent<NearbyCallbackReceiver>();
            _receiver.Bind(this);

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            _java = new AndroidJavaObject(JAVA_CLASS, activity, serviceType, displayName, RECEIVER_NAME);
        }

        public void StartAdvertising(int maxPlayers) => _java?.Call("startAdvertising", maxPlayers);
        public void StartBrowsing() => _java?.Call("startDiscovery");
        public void StopAdvertising() => _java?.Call("stopAdvertising");
        public void StopBrowsing() => _java?.Call("stopDiscovery");
        public void Connect(string peerId) => _java?.Call("connect", peerId);
        public void Disconnect(string peerId) => _java?.Call("disconnect", peerId);

        public void SendTo(string peerId, byte[] data, bool reliable)
        {
            // Unity marshals byte[] to a Java byte[]; Nearby payloads are reliable.
            _java?.Call("send", peerId, data, reliable);
        }

        public void StopAll()
        {
            _java?.Call("stopAll");
            _java?.Dispose();
            _java = null;
            if (_receiver != null)
            {
                UnityEngine.Object.Destroy(_receiver.gameObject);
                _receiver = null;
            }
        }

        // Invoked (on the main thread) by the receiver component.
        internal void RaisePeerFound(string id) => OnPeerFound?.Invoke(id);
        internal void RaisePeerConnected(string id) => OnPeerConnected?.Invoke(id);
        internal void RaisePeerDisconnected(string id) => OnPeerDisconnected?.Invoke(id);
        internal void RaiseDataReceived(string id, byte[] data) => OnDataReceived?.Invoke(id, data);
    }

    /// <summary>
    /// Receives UnitySendMessage callbacks from the Java Nearby helper and forwards
    /// them to the owning backend. Data arrives as "endpointId|base64".
    /// </summary>
    public class NearbyCallbackReceiver : MonoBehaviour
    {
        private NearbyLocalCoopBackend _backend;

        public void Bind(NearbyLocalCoopBackend backend) => _backend = backend;

        public void OnPeerFound(string endpointId) => _backend?.RaisePeerFound(endpointId);
        public void OnPeerConnected(string endpointId) => _backend?.RaisePeerConnected(endpointId);
        public void OnPeerDisconnected(string endpointId) => _backend?.RaisePeerDisconnected(endpointId);

        public void OnDataReceived(string message)
        {
            int split = message.IndexOf('|');
            if (split < 0) return;
            string endpointId = message.Substring(0, split);
            byte[] data = Convert.FromBase64String(message.Substring(split + 1));
            _backend?.RaiseDataReceived(endpointId, data);
        }

        // "where|message" from the Java failure listeners — Nearby calls used to
        // fail completely silently (e.g. on missing runtime permissions).
        public void OnNearbyError(string message) => Debug.LogError($"[Nearby] {message}");
    }
}
#endif
