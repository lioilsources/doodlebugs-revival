using UnityEngine;
using Unity.Netcode;

// (c) GalacticKittens
public static class NetworkObjectDespawner
{
    public static void DespawnNetworkObject(NetworkObject networkObject)
    {
        // if I'm an active on the networking session, tell all clients to remove
        // the instance that owns this NetworkObject
        if (networkObject != null && networkObject.IsSpawned)
            networkObject.Despawn();
    }
}
