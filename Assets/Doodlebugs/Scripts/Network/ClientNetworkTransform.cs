using Unity.Netcode.Components;

namespace Doodlebugs.Network
{
    /// <summary>
    /// Client-authoritative NetworkTransform.
    /// Use this instead of NetworkTransform when the owner (client) should control position/rotation.
    /// </summary>
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false; // Client/Owner has authority
        }
    }
}
