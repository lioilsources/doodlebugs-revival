#if !UNITY_IOS && !UNITY_ANDROID || UNITY_EDITOR
using System;
using UnityEngine;

namespace Doodlebugs.Network
{
    /// <summary>
    /// No-op backend for the Editor / desktop. The mobile-data fallback is never
    /// selected on these targets; this just keeps everything compiling and prevents
    /// the Editor from crashing if it is ever instantiated.
    /// </summary>
    public class UnsupportedLocalCoopBackend : ILocalCoopBackend
    {
        public bool IsAvailable => false;

#pragma warning disable 67 // events intentionally never raised
        public event Action<string> OnPeerFound;
        public event Action<string> OnPeerConnected;
        public event Action<string> OnPeerDisconnected;
        public event Action<string, byte[]> OnDataReceived;
#pragma warning restore 67

        public void EnsurePermissions(Action<bool> done) => done?.Invoke(true);

        public void Initialize(string serviceType, string displayName)
        {
            Debug.LogWarning("[NativeLocalCoop] Native local co-op is not supported on this platform.");
        }

        public void StartAdvertising(int maxPlayers) { }
        public void StartBrowsing() { }
        public void StopAdvertising() { }
        public void StopBrowsing() { }
        public void StopAll() { }
        public void Connect(string peerId) { }
        public void SendTo(string peerId, byte[] data, bool reliable) { }
        public void Disconnect(string peerId) { }
    }
}
#endif
