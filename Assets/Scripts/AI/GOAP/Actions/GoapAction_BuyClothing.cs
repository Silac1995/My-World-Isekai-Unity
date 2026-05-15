using System.Collections.Generic;
using MWI.Economy;
using UnityEngine;

/// <summary>
/// Walks a naked / underdressed NPC to a <see cref="ShopBuilding"/>'s <see cref="Cashier"/>
/// and purchases a chosen <see cref="WearableSO"/> via the shared
/// <see cref="CharacterAction_BuyFromShop"/> pipeline (<see cref="CharacterAction_BuyFromShop.BuyMode.NPC"/>
/// path — no UI). The purchased <see cref="WearableInstance"/> lands in the NPC's
/// inventory or hands, which lets the chain continue into
/// <see cref="GoapAction_EquipCarriedClothing"/> exactly the same way the food path
/// continues into <see cref="GoapAction_EatCarriedFood"/>.
///
/// Sibling of <see cref="GoapAction_BuyFood"/>: same shape, different SO type and
/// different terminator. <see cref="NeedToWearClothing.GetGoapActions"/> picks the
/// (shop, cashier, wearable) triple by scanning every shop's catalog for a
/// <see cref="WearableSO"/> matching the missing <see cref="WearableType"/>
/// (Pants for groin, Armor for chest), cheapest-in-slot, then constructs this action
/// against the chosen target. Movement gate follows CLAUDE.md rule #36
/// (<see cref="InteractableObject.IsCharacterInInteractionZone"/> + softlock guard +
/// path-loss recovery) — mirror of <see cref="GoapAction_BuyFood"/> /
/// <see cref="GoapAction_GoShopping"/>.
///
/// Precondition: empty (entry point for the shop-fed clothing chain).
/// Effect:       <c>"carryingClothing" = true</c>.
///
/// Successor:   <see cref="GoapAction_EquipCarriedClothing"/>.
/// Registered by: <see cref="NeedToWearClothing.GetGoapActions"/>.
/// </summary>
public class GoapAction_BuyClothing : GoapAction
{
    public override string ActionName => "Buy Clothing";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();
    public override Dictionary<string, bool> Effects => new Dictionary<string, bool> { { "carryingClothing", true } };
    public override float Cost => 2f;

    private readonly ShopBuilding _shop;
    private readonly Cashier _cashier;
    private readonly WearableSO _wearableSO;

    private bool _isComplete;
    private bool _isMoving;
    private bool _actionEnqueued;
    private bool _actionFinished;
    private CharacterAction_BuyFromShop _enqueuedAction;

    public override bool IsComplete => _isComplete;

    public GoapAction_BuyClothing(ShopBuilding shop, Cashier cashier, WearableSO wearableSO)
    {
        _shop = shop;
        _cashier = cashier;
        _wearableSO = wearableSO;
    }

    public override bool IsValid(Character worker)
    {
        if (_isComplete) return false;
        if (_shop == null || _cashier == null || _wearableSO == null) return false;
        if (worker == null) return false;

        // Once we've enqueued the buy CharacterAction, ride out its lifetime — same
        // pattern as GoapAction_BuyFood / GoapAction_GoShopping.
        if (_actionEnqueued) return true;

        if (!_cashier.IsAvailableForCustomer) return false;

        var entry = _shop.GetCatalogEntry(_wearableSO);
        if (!entry.HasValue) return false;
        int price = ShopBuilding.ResolvePrice(entry.Value);
        if (price > 0 && (worker.CharacterWallet == null ||
                          !worker.CharacterWallet.CanAfford(CurrencyId.Default, price))) return false;

        bool hasBagSpace = worker.CharacterEquipment != null &&
                           worker.CharacterEquipment.HasFreeSpaceForItemSO(_wearableSO);
        bool handsFree = worker.CharacterVisual?.BodyPartsController?.HandsController?.AreHandsFree() == true;
        if (!hasBagSpace && !handsFree) return false;

        if (!ShopHasItemInStock(_shop, _wearableSO)) return false;

        return true;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;
        if (worker == null) { _isComplete = true; return; }

        var movement = worker.CharacterMovement;
        if (movement == null) { _isComplete = true; return; }

        // ─── Movement gate (CLAUDE.md rule #36) ───
        // InteractionZone containment + softlock guard (path-landed-just-outside-zone)
        // + path-loss recovery (BT-branch-switched-away-and-back). Verbatim mirror of
        // GoapAction_BuyFood — see that file's comment block for the full rationale.
        var interactable = _cashier.GetComponent<InteractableObject>();
        bool inZone;
        if (interactable != null && interactable.InteractionZone != null)
        {
            inZone = interactable.IsCharacterInInteractionZone(worker);
            if (!inZone)
            {
                bool arrived = !movement.HasPath
                    || movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
                if (arrived)
                {
                    Vector3 ip = _cashier.GetInteractionPosition(worker.transform.position);
                    Vector3 wp = worker.transform.position;
                    if (Vector3.Distance(new Vector3(wp.x, 0f, wp.z),
                                         new Vector3(ip.x, 0f, ip.z)) <= 2f)
                        inZone = true;
                }
            }
        }
        else
        {
            Vector3 ip = _cashier.GetInteractionPosition(worker.transform.position);
            Vector3 wp = worker.transform.position;
            inZone = Vector3.Distance(new Vector3(wp.x, 0f, wp.z),
                                      new Vector3(ip.x, 0f, ip.z)) <= 1.5f;
        }

        if (!inZone)
        {
            // Re-fire SetDestination if the sticky flag was never raised OR the agent
            // dropped its path (rule #36 path-loss recovery — see JobVendor regression).
            if (!_isMoving || !movement.HasPath)
            {
                movement.SetDestination(_cashier.GetInteractionPosition(worker.transform.position));
                _isMoving = true;
            }
            return;
        }

        if (_isMoving) { movement.Stop(); _isMoving = false; }

        if (!_actionEnqueued)
        {
            if (!_cashier.IsAvailableForCustomer) { _isComplete = true; return; }

            _enqueuedAction = new CharacterAction_BuyFromShop(
                worker,
                _cashier,
                new List<ItemSO> { _wearableSO },
                CharacterAction_BuyFromShop.BuyMode.NPC);
            _enqueuedAction.OnActionFinished += HandleBuyActionFinished;
            worker.CharacterActions.ExecuteAction(_enqueuedAction);
            _actionEnqueued = true;
        }

        if (_actionFinished) _isComplete = true;
    }

    public override void Exit(Character worker)
    {
        if (_enqueuedAction != null)
        {
            _enqueuedAction.OnActionFinished -= HandleBuyActionFinished;
            _enqueuedAction = null;
        }
        _isComplete = false;
        _isMoving = false;
        _actionEnqueued = false;
        _actionFinished = false;
        worker?.CharacterMovement?.Stop();
    }

    private void HandleBuyActionFinished() => _actionFinished = true;

    private static bool ShopHasItemInStock(ShopBuilding shop, ItemSO item)
    {
        if (shop == null || item == null) return false;
        var shelves = shop.SellShelves;
        if (shelves == null) return false;
        for (int i = 0; i < shelves.Count; i++)
        {
            var shelf = shelves[i];
            if (shelf == null) continue;
            for (int sl = 0; sl < shelf.Capacity; sl++)
            {
                var slot = shelf.GetItemSlot(sl);
                if (slot == null || slot.IsEmpty()) continue;
                if (slot.ItemInstance != null && slot.ItemInstance.ItemSO == item) return true;
            }
        }
        return false;
    }
}
