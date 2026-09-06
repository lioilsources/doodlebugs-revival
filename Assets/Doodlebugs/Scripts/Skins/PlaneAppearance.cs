using System.Collections;
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

    // Effective couch-co-op index this plane currently holds a claim under;
    // -1 = none yet. Kept so a later index change releases the right key.
    private int _claimedIndex = -1;

    private int LocalIndex => _playerController != null ? Mathf.Max(_playerController.LocalPlayerIndex, 0) : 0;

    public override void OnNetworkSpawn()
    {
        _playerController = GetComponent<PlayerController>();

        if (IsServer)
        {
            StartCoroutine(ServerClaimNextFrame());
        }
    }

    private IEnumerator ServerClaimNextFrame()
    {
        // LocalPlayerManager calls SetLocalPlayerIndex immediately AFTER
        // spawning the object, so during OnNetworkSpawn a second couch pilot
        // still reads -1 and would claim under the first one's key, evicting
        // them. One frame is all it takes for the real index to land.
        yield return null;
        ServerEnsureClaim();
    }

    /// <summary>
    /// Server: hold a claim on this plane's look, moving it if the couch
    /// co-op index changed under us. Idempotent - re-entry with the same
    /// index does nothing.
    /// </summary>
    public void ServerEnsureClaim()
    {
        if (!IsServer || PlaneSkinManager.Instance == null) return;
        // The warm-up bot never holds a claim - its shape must stay pickable
        // for humans (Prompts/25, D3). BotManager sets its look directly.
        if (_playerController != null && _playerController.IsBot) return;

        int idx = LocalIndex;
        if (_claimedIndex == idx) return;
        if (_claimedIndex >= 0) PlaneSkinManager.Instance.Release(OwnerClientId, _claimedIndex);

        var (modelId, skinId) = PlaneSkinManager.Instance.ServerClaimInitialLook(OwnerClientId, idx);
        _claimedIndex = idx;
        NetModelId.Value = modelId;
        NetSkinId.Value = skinId;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && PlaneSkinManager.Instance != null && _claimedIndex >= 0)
        {
            PlaneSkinManager.Instance.Release(OwnerClientId, _claimedIndex);
            _claimedIndex = -1;
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

    /// <summary>
    /// Server: put a look on this plane WITHOUT registering a claim. Only for
    /// the warm-up bot: a claim would mark the shape TAKEN in every picker,
    /// and the bot is supposed to yield to humans, not block them. Because
    /// there is no claim, a human's TryClaim on the same shape simply
    /// succeeds and BotManager re-picks on its next tick.
    /// </summary>
    public void ServerSetLookUnclaimed(int modelId, int skinId)
    {
        if (!IsServer) return;
        NetModelId.Value = modelId;
        NetSkinId.Value = skinId;
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
