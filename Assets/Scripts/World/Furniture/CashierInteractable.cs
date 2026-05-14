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
        Debug.Log($"<color=magenta>[CashierInteract]</color> Interact ENTRY. cashier={(_cashier != null ? _cashier.FurnitureName : "null")}, interactor={(interactor != null ? interactor.CharacterName : "null")}, interactor.IsOwner={(interactor != null ? interactor.IsOwner.ToString() : "n/a")}.", this);
        if (interactor == null || _cashier == null) { Debug.Log("<color=magenta>[CashierInteract]</color> Early-return: null interactor or cashier."); return; }
        if (!IsCharacterInInteractionZone(interactor)) { Debug.Log($"<color=magenta>[CashierInteract]</color> Early-return: interactor not in zone (interactor pos={interactor.transform.position}, zone bounds={(InteractionZone != null ? InteractionZone.bounds.ToString() : "null")})."); return; }

        var occ = _cashier.Occupant;
        var cur = _cashier.CurrentCustomer;
        Debug.Log($"<color=magenta>[CashierInteract]</color> Pre-gate state on this peer: Occupant={(occ != null ? occ.CharacterName : "null")}, CurrentCustomer={(cur != null ? cur.CharacterName : "null")}, RequiresVendor={_cashier.RequiresVendor}, NetSync.IsServer={(_cashier.NetSync != null ? _cashier.NetSync.IsServer.ToString() : "n/a")}, NetSync.IsSpawned={(_cashier.NetSync != null ? _cashier.NetSync.IsSpawned.ToString() : "n/a")}.", this);

        // Local pre-gate (immediate toast on the offending client).
        if (_cashier.RequiresVendor && occ == null)
        {
            Debug.Log("<color=magenta>[CashierInteract]</color> Pre-gate FAILED: no vendor — showing toast.");
            UI_Toast.Show("No vendor on duty.", ToastType.Warning);
            return;
        }
        if (cur != null && cur != interactor)
        {
            Debug.Log($"<color=magenta>[CashierInteract]</color> Pre-gate FAILED: vendor busy with {cur.CharacterName} (interactor is {interactor.CharacterName}) — showing toast.");
            UI_Toast.Show("Shop vendor is busy with another customer.", ToastType.Warning);
            return;
        }

        Debug.Log("<color=magenta>[CashierInteract]</color> Pre-gate PASSED — sending RequestStartBuyServerRpc.");
        // Server-relay. The server re-validates and may still send a busy toast back if a race occurred.
        _cashier.NetSync.RequestStartBuyServerRpc(new NetworkBehaviourReference(interactor));
    }
}
