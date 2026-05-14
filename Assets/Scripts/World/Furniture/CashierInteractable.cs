using MWI.UI.Notifications;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Player-facing interactable surface on a Cashier. Tap-E entry routes the
/// customer through Cashier.RequestStartBuyServerRpc (server-relay).
///
/// Pre-gates on the local client (vendor present? cashier free?) so the
/// player gets an immediate toast without an RPC roundtrip — server still
/// re-validates authoritatively in the RPC.
///
/// Mirrors the Phase 1 BuildingInteractable pattern (2026-05-06
/// construction-loop spec): InteractableObject base + ServerRpc relay +
/// no client-side authoritative state.
/// </summary>
[RequireComponent(typeof(Cashier))]
public class CashierInteractable : InteractableObject
{
    private const string PromptShop = "Press E to shop";
    private const string PromptNoVendor = "No vendor on duty";
    private const string PromptBusy = "Vendor is busy";

    private Cashier _cashier;

    protected void Awake()
    {
        _cashier = GetComponent<Cashier>();

        if (string.IsNullOrEmpty(interactionPrompt) || interactionPrompt == "Press E to interact")
        {
            interactionPrompt = PromptShop;
        }
    }

    public override void Interact(Character interactor)
    {
        if (interactor == null || _cashier == null) return;
        if (!IsCharacterInInteractionZone(interactor)) return;
        if (interactor.CharacterActions == null) return;

        // Branch 1 — already seated on THIS cashier → leave (vendor stepping away from
        // the counter). Cashier.Occupant resolves via CashierNetSync.OccupantNetworkObjectId
        // on clients, so this branch fires correctly on every peer including a fresh
        // late-joiner (rule #19b).
        if (_cashier.Occupant == interactor)
        {
            interactor.CharacterActions.RequestLeaveOccupiedFurnitureServerRpc();
            return;
        }

        // Branch 2 — this character is the assigned vendor for this shop and the seat is
        // free → take the cashier. Player↔NPC parity (rule #22): JobVendor.Execute queues
        // the same CharacterAction_OccupyFurniture for NPCs.
        if (_cashier.RequiresVendor
            && _cashier.Occupant == null
            && interactor.CharacterJob != null
            && interactor.CharacterJob.CurrentJob is JobVendor jv
            && jv.Workplace == _cashier.LinkedBuilding)
        {
            interactor.CharacterActions.RequestOccupyFurnitureServerRpc(
                new NetworkBehaviourReference(_cashier),
                _cashier.transform.position);
            return;
        }

        // Branch 3 — customer flow: existing local pre-gate + buy ServerRpc.
        if (_cashier.RequiresVendor && _cashier.Occupant == null)
        {
            UI_Toast.Show("No vendor on duty.", ToastType.Warning);
            return;
        }
        if (_cashier.CurrentCustomer != null && _cashier.CurrentCustomer != interactor)
        {
            UI_Toast.Show("Shop vendor is busy with another customer.", ToastType.Warning);
            return;
        }

        // Server-relay. The server re-validates and may still send a busy toast back if a race occurred.
        _cashier.NetSync.RequestStartBuyServerRpc(new NetworkBehaviourReference(interactor));
    }
}
