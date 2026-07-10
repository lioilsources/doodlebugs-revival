#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace Doodlebugs.Network
{
    /// <summary>
    /// iOS backend backed by the MultipeerConnectivity framework
    /// (see Assets/Plugins/iOS/DoodlebugsMultipeer.m).
    ///
    /// Native callbacks are static (AOT requirement); they marshal onto the Unity
    /// main thread via <see cref="MainThreadDispatcher"/> and forward to the single
    /// live instance.
    /// </summary>
    public class MultipeerLocalCoopBackend : ILocalCoopBackend
    {
        private delegate void PeerCallback(IntPtr peerIdUtf8);
        private delegate void DataCallback(IntPtr peerIdUtf8, IntPtr data, int length);

        [DllImport("__Internal")] private static extern void _DBMP_Initialize(string serviceType, string displayName);
        [DllImport("__Internal")] private static extern void _DBMP_SetCallbacks(PeerCallback found, PeerCallback connected, PeerCallback disconnected, DataCallback data);
        [DllImport("__Internal")] private static extern void _DBMP_StartAdvertising();
        [DllImport("__Internal")] private static extern void _DBMP_StartBrowsing();
        [DllImport("__Internal")] private static extern void _DBMP_StopAdvertising();
        [DllImport("__Internal")] private static extern void _DBMP_StopBrowsing();
        [DllImport("__Internal")] private static extern void _DBMP_StopAll();
        [DllImport("__Internal")] private static extern void _DBMP_Connect(string peerId);
        [DllImport("__Internal")] private static extern void _DBMP_Send(string peerId, byte[] data, int length, int reliable);
        [DllImport("__Internal")] private static extern void _DBMP_Disconnect(string peerId);

        private static MultipeerLocalCoopBackend _instance;

        public bool IsAvailable => true;

        public event Action<string> OnPeerFound;
        public event Action<string> OnPeerConnected;
        public event Action<string> OnPeerDisconnected;
        public event Action<string, byte[]> OnDataReceived;

        public void Initialize(string serviceType, string displayName)
        {
            _instance = this;
            // Initialize FIRST: the native _DBMP_SetCallbacks used to silently
            // drop the registration when gInstance did not exist yet, which left
            // all callbacks NULL and no discovery event ever reached C#.
            _DBMP_Initialize(serviceType, displayName);
            _DBMP_SetCallbacks(OnFoundNative, OnConnectedNative, OnDisconnectedNative, OnDataNative);
        }

        public void EnsurePermissions(Action<bool> done)
        {
            // iOS surfaces its Local Network prompt automatically on first
            // Multipeer use; there is nothing to pre-request here.
            done?.Invoke(true);
        }

        public void StartAdvertising(int maxPlayers) => _DBMP_StartAdvertising();
        public void StartBrowsing() => _DBMP_StartBrowsing();
        public void StopAdvertising() => _DBMP_StopAdvertising();
        public void StopBrowsing() => _DBMP_StopBrowsing();

        public void StopAll()
        {
            _DBMP_StopAll();
            _instance = null;
        }

        public void Connect(string peerId) => _DBMP_Connect(peerId);
        public void Disconnect(string peerId) => _DBMP_Disconnect(peerId);

        public void SendTo(string peerId, byte[] data, bool reliable)
        {
            _DBMP_Send(peerId, data, data.Length, reliable ? 1 : 0);
        }

        #region Native callbacks (static, AOT)

        [MonoPInvokeCallback(typeof(PeerCallback))]
        private static void OnFoundNative(IntPtr peerIdUtf8)
        {
            string peerId = Marshal.PtrToStringAnsi(peerIdUtf8);
            MainThreadDispatcher.Enqueue(() => _instance?.OnPeerFound?.Invoke(peerId));
        }

        [MonoPInvokeCallback(typeof(PeerCallback))]
        private static void OnConnectedNative(IntPtr peerIdUtf8)
        {
            string peerId = Marshal.PtrToStringAnsi(peerIdUtf8);
            MainThreadDispatcher.Enqueue(() => _instance?.OnPeerConnected?.Invoke(peerId));
        }

        [MonoPInvokeCallback(typeof(PeerCallback))]
        private static void OnDisconnectedNative(IntPtr peerIdUtf8)
        {
            string peerId = Marshal.PtrToStringAnsi(peerIdUtf8);
            MainThreadDispatcher.Enqueue(() => _instance?.OnPeerDisconnected?.Invoke(peerId));
        }

        [MonoPInvokeCallback(typeof(DataCallback))]
        private static void OnDataNative(IntPtr peerIdUtf8, IntPtr data, int length)
        {
            string peerId = Marshal.PtrToStringAnsi(peerIdUtf8);
            byte[] bytes = new byte[length];
            if (length > 0) Marshal.Copy(data, bytes, 0, length);
            MainThreadDispatcher.Enqueue(() => _instance?.OnDataReceived?.Invoke(peerId, bytes));
        }

        #endregion
    }
}
#endif
