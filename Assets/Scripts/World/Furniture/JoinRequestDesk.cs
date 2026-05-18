using UnityEngine;

/// <summary>
/// AB-preplaced furniture (Plan 4c Task 6). When a <see cref="Character"/> taps E on
/// the desk, the desk forwards a join-request submission to its parent
/// <see cref="AdministrativeBuilding"/> and raises a local toast on the interacting
/// player's HUD. **Spontaneous one-shot interaction — the character does NOT sit on
/// or occupy the desk.** Per design slip 2026-05-18: previously inherited
/// <c>OccupiableFurniture</c> and queued <c>CharacterAction_OccupyFurniture</c> to
/// make drifters "sit and wait"; reverted to a plain <see cref="Furniture"/> because
/// the seat metaphor added no value for a fire-and-forget submission and blocked the
/// actor's BT from continuing onto whatever it had to do next.
///
/// Plan 4c Task 6.
/// </summary>
public class JoinRequestDesk : Furniture
{
    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _toastChannel;

    private AdministrativeBuilding _ab;

    protected override void Awake()
    {
        base.Awake();
        _ab = GetComponentInParent<AdministrativeBuilding>();
    }

    /// <summary>
    /// Late-binding retry — covers the spawn-race where Awake fires before the AB has been
    /// registered as the parent (per rule #19b GetComponentInParent-in-Awake gotcha).
    /// Called from OnInteract on first contact; subsequent calls are no-ops once cached.
    /// </summary>
    private void TryRegisterWithAB()
    {
        if (_ab == null)
        {
            _ab = GetComponentInParent<AdministrativeBuilding>();
        }
    }

    public override bool OnInteract(Character interactor)
    {
        TryRegisterWithAB();
        if (_ab == null)
        {
            Debug.LogWarning($"<color=orange>[JoinRequestDesk]</color> '{name}' has no AdministrativeBuilding parent; cannot submit join request.");
            return false;
        }
        if (interactor == null || interactor.NetworkObject == null) return false;

        // Server-only: submit the request. Drifters are server-spawned NPCs so this runs
        // direct on the server; for completeness we also handle a player-driven path that
        // a future "request to join via dev tool / quest" entry could fire.
        var charNetObj = interactor.GetComponent<Unity.Netcode.NetworkObject>();
        bool isServer = charNetObj != null
            && charNetObj.NetworkManager != null
            && charNetObj.NetworkManager.IsServer;

        if (isServer)
        {
            _ab.SubmitJoinRequestServerRpc(interactor.NetworkObject.NetworkObjectId);
        }
        else
        {
            // Client-owned actor (rare for drifters; included for player parity). The
            // server validates everything inside the RPC; client just relays.
            _ab.SubmitJoinRequestServerRpc(interactor.NetworkObject.NetworkObjectId);
        }

        // Local-player toast feedback. Only the interacting player's peer raises the
        // toast (Cashier/CharacterEquipment pattern): IsPlayer() filters out NPCs and
        // IsOwner filters out the host-NPC trap where IsOwner is true for the host on
        // every NPC in the scene (rule #19b). The server-side ServerRpc is still the
        // authoritative gate — this is purely UX confirmation that E was registered.
        if (_toastChannel != null && interactor.IsPlayer() && interactor.IsOwner)
        {
            _toastChannel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                message: $"Join request submitted to {_ab.BuildingName}",
                type: MWI.UI.Notifications.ToastType.Info,
                duration: 3f
            ));
        }

        // Spontaneous one-shot — no occupation, no sit, no action queued. The actor
        // is free to immediately do whatever its BT / next input dictates.
        return true;
    }
}
