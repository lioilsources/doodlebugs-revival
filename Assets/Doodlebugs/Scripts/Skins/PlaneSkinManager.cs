using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative registry of which plane LOOK - (model, skin) combo -
/// every player has claimed. Unlike PlayerColorManager (a local, unsynced
/// round-robin - fine for a cosmetic that never needs cross-client
/// agreement), a look pick MUST be server-arbitrated: two players racing to
/// pick the same combo need one winner, everyone else needs to see it as
/// taken immediately.
///
/// Uniqueness is per SHAPE (2026-09-06, superseding plan 23 decision D1a):
/// no two players fly the same silhouette, whatever it is painted. Skins are
/// free to repeat, because two identical liveries on different shapes still
/// read apart at a glance while two identical shapes in different colours do
/// not - and telling aircraft apart mid-dogfight is what the rule is for.
/// 29 shipped shapes against an 8-player cap, so nobody is ever blocked.
///
/// Scene singleton, same shape as BackgroundManager (NetworkObject placed in
/// Scene01 by the "Doodlebugs -> Setup Plane Skin Manager" editor menu - see
/// Editor/PlaneSkinManagerSetup.cs - rather than a spawned prefab, so it
/// exists from the moment the scene loads with no spawn-order race).
/// </summary>
public class PlaneSkinManager : NetworkBehaviour
{
    public static PlaneSkinManager Instance { get; private set; }

    public struct SkinClaim : INetworkSerializable, IEquatable<SkinClaim>
    {
        public ulong ClientId;
        public int LocalPlayerIndex;
        public int ModelId;
        public int SkinId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref LocalPlayerIndex);
            serializer.SerializeValue(ref ModelId);
            serializer.SerializeValue(ref SkinId);
        }

        // NetworkList.Set() silently drops a write whose value Equals() the
        // old one, so "equal" has to mean the whole claim, not just whose it
        // is. With only the player compared, a re-pick by the same player
        // (the _claims[i] = claim path in TryClaim) never reached the list:
        // the plane still changed, because NetModelId/NetSkinId are separate
        // variables, but the registry - and every TAKEN badge on every
        // client - stayed on that player's first look for good. Player
        // identity checks use IsPlayer().
        public bool Equals(SkinClaim other) =>
            ClientId == other.ClientId && LocalPlayerIndex == other.LocalPlayerIndex &&
            ModelId == other.ModelId && SkinId == other.SkinId;

        public override bool Equals(object obj) => obj is SkinClaim other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ClientId, LocalPlayerIndex, ModelId, SkinId);

        public bool IsPlayer(ulong clientId, int localPlayerIndex) =>
            ClientId == clientId && LocalPlayerIndex == localPlayerIndex;

        public bool SameLook(int modelId, int skinId) => ModelId == modelId && SkinId == skinId;
    }

    // Read-everyone / write-server, same permissions as every other synced
    // gameplay NetworkVariable in this project (NetWeaponId, NetHealth...).
    private readonly NetworkList<SkinClaim> _claims = new();

    /// <summary>Read-only view for UI (hangar picker grid, "TAKEN" badges).</summary>
    public NetworkList<SkinClaim> Claims => _claims;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        base.OnNetworkDespawn();
    }

    /// <summary>Server-side look pick, called from PlaneAppearance's ServerRpc
    /// (already running on the server by the time it gets here). Returns
    /// true if the pick was applied.</summary>
    public bool TryClaim(ulong clientId, int localPlayerIndex, int modelId, int skinId)
    {
        if (!IsServer) return false;
        if (!PlaneSkinCatalog.IsValidId(skinId)) return false;
        if (!PlaneModelCatalog.IsAvailable(modelId)) return false;

        for (int i = 0; i < _claims.Count; i++)
        {
            var c = _claims[i];
            if (c.IsPlayer(clientId, localPlayerIndex))
            {
                if (c.SameLook(modelId, skinId)) return true; // no-op re-pick
                continue; // this player's own old claim - overwritten below
            }
            if (c.ModelId == modelId) return false; // that shape is someone else's
        }

        var claim = new SkinClaim
        {
            ClientId = clientId, LocalPlayerIndex = localPlayerIndex, ModelId = modelId, SkinId = skinId
        };
        for (int i = 0; i < _claims.Count; i++)
        {
            if (_claims[i].IsPlayer(clientId, localPlayerIndex))
            {
                _claims[i] = claim;
                return true;
            }
        }

        _claims.Add(claim);
        return true;
    }

    /// <summary>Is this SHAPE flown by somebody other than the given player?
    /// Skin is deliberately not part of the question - see the class note.</summary>
    public bool IsModelTaken(int modelId, ulong byClientId, int byLocalPlayerIndex)
    {
        foreach (var c in _claims)
        {
            if (c.ModelId == modelId && !c.IsPlayer(byClientId, byLocalPlayerIndex)) return true;
        }
        return false;
    }

    public bool HasClaim(ulong clientId, int localPlayerIndex)
    {
        foreach (var c in _claims)
        {
            if (c.IsPlayer(clientId, localPlayerIndex)) return true;
        }
        return false;
    }

    /// <summary>The player's current look, or the base biplane in the
    /// starter livery when nothing is claimed yet.</summary>
    public (int modelId, int skinId) GetClaim(ulong clientId, int localPlayerIndex)
    {
        foreach (var c in _claims)
        {
            if (c.IsPlayer(clientId, localPlayerIndex)) return (c.ModelId, c.SkinId);
        }
        return (PlaneModelCatalog.BaseModelId, PlaneSkinCatalog.StarterSkinId);
    }

    /// <summary>
    /// Server, on spawn: make sure this player OWNS a look rather than merely
    /// defaulting to one.
    ///
    /// GetClaim used to be all a spawning plane called, and it registers
    /// nothing - so until someone opened the picker they held no claim, every
    /// pilot flew the same red biplane, and none of them showed as TAKEN to
    /// anybody. Claiming on spawn is what makes "taken" mean anything.
    ///
    /// A player whose default shape is already flown is walked to the next
    /// free silhouette; the livery stays the starter one, since skins no
    /// longer have to be unique.
    /// </summary>
    public (int modelId, int skinId) ServerClaimInitialLook(ulong clientId, int localPlayerIndex)
    {
        var fallback = (PlaneModelCatalog.BaseModelId, PlaneSkinCatalog.StarterSkinId);
        if (!IsServer) return fallback;

        foreach (var c in _claims)
        {
            if (c.IsPlayer(clientId, localPlayerIndex)) return (c.ModelId, c.SkinId);
        }

        foreach (int modelId in PlaneModelCatalog.Available)
        {
            if (IsModelTaken(modelId, clientId, localPlayerIndex)) continue;
            if (TryClaim(clientId, localPlayerIndex, modelId, PlaneSkinCatalog.StarterSkinId))
            {
                return (modelId, PlaneSkinCatalog.StarterSkinId);
            }
        }

        // Every shape flown - needs more pilots than the 8-player cap allows,
        // but a duplicate look beats no plane at all.
        Debug.LogWarning("[PlaneSkinManager] No free shape left");
        return fallback;
    }

    public void Release(ulong clientId, int localPlayerIndex)
    {
        if (!IsServer) return;
        for (int i = _claims.Count - 1; i >= 0; i--)
        {
            if (_claims[i].IsPlayer(clientId, localPlayerIndex))
            {
                _claims.RemoveAt(i);
            }
        }
    }

    // A disconnecting client may own more than one local plane (couch co-op)
    // - PlayerController.OnNetworkDespawn also calls Release for its own
    // (clientId, localPlayerIndex), this is the safety net for the case a
    // client drops the connection outright without a clean despawn per plane.
    private void OnClientDisconnected(ulong clientId)
    {
        for (int i = _claims.Count - 1; i >= 0; i--)
        {
            if (_claims[i].ClientId == clientId)
            {
                _claims.RemoveAt(i);
            }
        }
    }
}
