using MWI.UI.Notifications;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Player-facing interactable surface on a Cashier. Tap-E entry has two branches:
/// <list type="bullet">
///   <item>If the interactor is the currently-seated occupant (resolved via
///   <see cref="CashierNetSync.OccupantNetworkObjectId"/>) → request Leave through
///   <see cref="CharacterActions.RequestLeaveOccupiedFurnitureServerRpc"/>.</item>
///   <item>Otherwise → send a single "use this cashier" intent via
///   <see cref="CashierNetSync.RequestUseCashierServerRpc"/>. The server routes by role:
///   assigned vendor + seat free → <see cref="CharacterAction_OccupyFurniture"/>; else →
///   <see cref="CharacterAction_BuyFromShop"/>. Server-side routing is required because
///   <see cref="CharacterJob.CurrentJob"/> is not NetVar-replicated — remote-client owners
///   cannot determine their own role locally (player↔NPC parity per rule #22).</item>
/// </list>
///
/// Local fast-gate keeps the "vendor is busy with another customer" toast snappy
/// (CurrentCustomer is NetVar-resolved). The "no vendor on duty" toast moved server-side
/// so it doesn't misfire for a player-vendor about to take their own cashier.
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

        // Local fast-gate — only safe to fire when the player is definitely a customer (not
        // a vendor). The "vendor is busy" case is determinable because CurrentCustomer is
        // NetVar-resolved on every peer, and a *vendor* taking the cashier wouldn't be
        // blocked by it anyway. Keep this for snappy UX feedback.
        if (_cashier.CurrentCustomer != null && _cashier.CurrentCustomer != interactor)
        {
            UI_Toast.Show("Shop vendor is busy with another customer.", ToastType.Warning);
            return;
        }

        // Branches 2 + 3 unified — the client cannot read CharacterJob.CurrentJob for
        // remote-owned characters (not NetVar-replicated yet). Send the single "use cashier"
        // intent and let the server route:
        //   • assigned vendor + seat free → CharacterAction_OccupyFurniture
        //   • everyone else → CharacterAction_BuyFromShop
        // Server may send a targeted "No vendor on duty" / "busy" toast back if the customer
        // path can't proceed. Mirrors the player↔NPC parity rule — JobVendor.Execute queues
        // the same OccupyFurniture action on the NPC side.
        _cashier.NetSync.RequestUseCashierServerRpc(new NetworkBehaviourReference(interactor));
    }
}
