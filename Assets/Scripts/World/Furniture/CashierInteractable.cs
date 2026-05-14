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

        // Local pre-gate (immediate toast on the offending client). Occupant and
        // CurrentCustomer go through Cashier's NetVar-resolving overrides so this
        // mirrors the server state on every peer (rule #19).
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
