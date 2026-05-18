using UnityEngine;

/// <summary>
/// AB-preplaced furniture (Plan 4c Task 6). When a drifter <see cref="Character"/>
/// interacts with the desk, the desk forwards a join-request submission to its parent
/// <see cref="AdministrativeBuilding"/> and queues a <see cref="CharacterAction_OccupyFurniture"/>
/// so the applicant "sits and waits" at the desk (visual feedback for the player).
///
/// Inherits <see cref="OccupiableFurniture"/> so the seat machinery (reserve/use/release +
/// the canonical OnInteract → CharacterAction_OccupyFurniture queueing) is reused.
///
/// Plan 4c Task 6.
/// </summary>
public class JoinRequestDesk : OccupiableFurniture
{
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

        // Reuse the base Occupiable seat-wait machinery so the drifter visually waits at
        // the desk until the leader processes the request.
        return base.OnInteract(interactor);
    }
}
