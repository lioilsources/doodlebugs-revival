using System;

namespace Doodlebugs.Network
{
    /// <summary>
    /// Platform-agnostic abstraction over a native local peer-to-peer stack
    /// (iOS Multipeer Connectivity / Android Nearby Connections).
    ///
    /// These stacks form a local link between nearby devices over Bluetooth /
    /// Wi-Fi Direct / peer-to-peer Wi-Fi WITHOUT requiring a shared router or
    /// internet, so they work even when the phone is on mobile data.
    ///
    /// "Lobby" maps to the advertise/browse model:
    ///   - Advertising = creating a lobby (becoming host).
    ///   - Browsing    = looking for an existing lobby.
    /// </summary>
    public interface ILocalCoopBackend
    {
        /// <summary>True if the native stack is present/usable on this platform.</summary>
        bool IsAvailable { get; }

        /// <summary>A nearby advertised lobby (host) was discovered. Arg = opaque peer id.</summary>
        event Action<string> OnPeerFound;

        /// <summary>A session was fully established with a peer. Arg = peer id.</summary>
        event Action<string> OnPeerConnected;

        /// <summary>A peer dropped / session closed. Arg = peer id.</summary>
        event Action<string> OnPeerDisconnected;

        /// <summary>Data arrived from a peer. Args = peer id, payload bytes.</summary>
        event Action<string, byte[]> OnDataReceived;

        /// <summary>
        /// Request any runtime permissions the native stack needs (Android 12+
        /// Bluetooth/Nearby permissions). Invokes <paramref name="done"/> with
        /// true when everything required is granted; may complete synchronously
        /// when nothing is missing. Call before <see cref="Initialize"/>.
        /// </summary>
        void EnsurePermissions(Action<bool> done);

        /// <summary>Prepare the native stack (service type / display name).</summary>
        void Initialize(string serviceType, string displayName);

        /// <summary>Become host: start advertising a lobby (capped at maxPlayers).</summary>
        void StartAdvertising(int maxPlayers);

        /// <summary>Look for an existing lobby to join.</summary>
        void StartBrowsing();

        void StopAdvertising();
        void StopBrowsing();
        void StopAll();

        /// <summary>Request/establish a session with a discovered peer (client → host).</summary>
        void Connect(string peerId);

        /// <summary>Send a payload to a specific connected peer.</summary>
        void SendTo(string peerId, byte[] data, bool reliable);

        /// <summary>Tear down the session with a specific peer.</summary>
        void Disconnect(string peerId);
    }
}
