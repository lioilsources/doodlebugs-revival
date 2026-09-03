using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-plane look state - the visual sibling of Shooting's weapon state.
/// NetModelId (shape, PlaneModelCatalog) and NetSkinId (livery,
/// PlaneSkinCatalog) are server-write so every client (and late joiners)
/// agree on what every plane looks like; PlayerController applies them to
/// the sprite. Shape never touches the hitbox - see PlaneModelCatalog.
/// </summary>
public class PlaneAppearance : NetworkBehaviour
{
    public NetworkVariable<int> NetModelId = new NetworkVariable<int>(
        PlaneModelCatalog.BaseModelId,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> NetSkinId = new NetworkVariable<int>(
        PlaneSkinCatalog.StarterSkinId,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private PlayerController _playerController;

    private int LocalIndex => _playerController != null ? Mathf.Max(_playerController.LocalPlayerIndex, 0) : 0;

    public override void OnNetworkSpawn()
    {
        _playerController = GetComponent<PlayerController>();

        if (IsServer && PlaneSkinManager.Instance != null)
        {
            // Reconnecting mid-session client (or a fresh one with no claim
            // yet) starts from whatever it already holds in the registry so
            // a late-join snapshot and a fresh spawn agree.
            var (modelId, skinId) = PlaneSkinManager.Instance.GetClaim(OwnerClientId, LocalIndex);
            NetModelId.Value = modelId;
            NetSkinId.Value = skinId;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && PlaneSkinManager.Instance != null && _playerController != null)
        {
            PlaneSkinManager.Instance.Release(OwnerClientId, LocalIndex);
        }
        base.OnNetworkDespawn();
    }

    /// <summary>Server-side: apply a validated hangar/intro-screen pick. The
    /// claim is atomic on the (model, skin) combo - see PlaneSkinManager.</summary>
    public bool ServerSetAppearance(int modelId, int skinId)
    {
        if (!IsServer) return false;
        if (PlaneSkinManager.Instance == null) return false;
        if (!PlaneSkinManager.Instance.TryClaim(OwnerClientId, LocalIndex, modelId, skinId)) return false;

        NetModelId.Value = modelId;
        NetSkinId.Value = skinId;
        Debug.Log($"[PlaneAppearance] Player {OwnerClientId}_{LocalIndex} equipped " +
                  $"{PlaneModelCatalog.Get(modelId).DisplayName} / {PlaneSkinCatalog.Get(skinId).DisplayName}");
        return true;
    }

    public bool ServerSetSkin(int skinId) => ServerSetAppearance(NetModelId.Value, skinId);
    public bool ServerSetModel(int modelId) => ServerSetAppearance(modelId, NetSkinId.Value);

    /// <summary>Owner asks the server to equip a skin picked in the hangar or
    /// on the intro screen (keeps the current shape). Ownership of premium
    /// skins is enforced locally by the platform store (see IAPManager)
    /// before the picker even offers the button - the server here only
    /// enforces "not already taken", the same trust boundary every other
    /// hangar pick in this project already has (RequestSelectWeaponServerRpc
    /// trusts its caller the same way).</summary>
    [ServerRpc]
    public void RequestSelectSkinServerRpc(int skinId)
    {
        ServerSetSkin(skinId);
    }

    /// <summary>Owner asks the server to equip a plane shape (keeps the
    /// current skin). All shapes are free (plan 23, D4) - only "not taken"
    /// and "actually shipped" are checked, server-side.</summary>
    [ServerRpc]
    public void RequestSelectModelServerRpc(int modelId)
    {
        ServerSetModel(modelId);
    }
}
