using System.Collections.Generic;
using MWI.Economy;
using UnityEngine;

/// <summary>
/// Walks the hungry NPC to a <see cref="ShopBuilding"/>'s <see cref="Cashier"/> and
/// purchases a chosen <see cref="FoodSO"/> via the shared <see cref="CharacterAction_BuyFromShop"/>
/// pipeline (<see cref="CharacterAction_BuyFromShop.BuyMode.NPC"/> path — no UI). The
/// purchased <see cref="FoodInstance"/> lands in the NPC's inventory/hands, which lets
/// the chain continue into <see cref="GoapAction_EatCarriedFood"/> exactly the same way
/// a ground pickup would.
///
/// Distinct from <see cref="GoapAction_GoShopping"/> in two ways:
/// <list type="bullet">
///   <item>The (shop, cashier, food) triple is chosen by the caller
///         (<see cref="NeedHunger.GetGoapActions"/>) so the need can score across
///         catalogs and pick the best hunger-per-coin candidate. <c>GoShopping</c>
///         takes a fixed <see cref="ItemSO"/> and rediscovers the shop itself.</item>
///   <item>Effect is <c>"carryingFood" = true</c> (not <c>"shoppingDone"</c>) so it
///         chains seamlessly into the existing hunger-terminator
///         <see cref="GoapAction_EatCarriedFood"/>.</item>
/// </list>
///
/// Precondition: empty (entry point for the shop-fed hunger chain).
/// Effect:       <c>"carryingFood" = true</c>.
///
/// Successor:   <see cref="GoapAction_EatCarriedFood"/>.
/// Registered by: <see cref="NeedHunger.GetGoapActions"/>.
/// </summary>
public class GoapAction_BuyFood : GoapAction
{
    public override string ActionName => "Buy Food";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();
    public override Dictionary<string, bool> Effects => new Dictionary<string, bool> { { "carryingFood", true } };
    public override float Cost => 2f;

    private readonly ShopBuilding _shop;
    private readonly Cashier _cashier;
    private readonly FoodSO _foodSO;

    private bool _isComplete;
    private bool _isMoving;
    private bool _actionEnqueued;
    private bool _actionFinished;
    private CharacterAction_BuyFromShop _enqueuedAction;

    public override bool IsComplete => _isComplete;

    public GoapAction_BuyFood(ShopBuilding shop, Cashier cashier, FoodSO foodSO)
    {
        _shop = shop;
        _cashier = cashier;
        _foodSO = foodSO;
    }

    public override bool IsValid(Character worker)
    {
        if (_isComplete) return false;
        if (_shop == null || _cashier == null || _foodSO == null) return false;
        if (worker == null) return false;

        // Once we've enqueued the buy CharacterAction, ride out its lifetime — the
        // cashier-availability flip on TryAcquireCustomerLock would otherwise drop us
        // mid-purchase (same pattern as GoapAction_GoShopping).
        if (_actionEnqueued) return true;

        if (!_cashier.IsAvailableForCustomer) return false;

        // Wallet check. price = 0 is legal (free sample / dev catalog) — only block when
        // the worker can't actually afford a non-free entry.
        var entry = _shop.GetCatalogEntry(_foodSO);
        if (!entry.HasValue) return false;
        int price = ShopBuilding.ResolvePrice(entry.Value);
        if (price > 0 && (worker.CharacterWallet == null ||
                          !worker.CharacterWallet.CanAfford(CurrencyId.Default, price))) return false;

        // Inventory/hands room for the food we are about to receive.
        bool hasBagSpace = worker.CharacterEquipment != null &&
                           worker.CharacterEquipment.HasFreeSpaceForItemSO(_foodSO);
        bool handsFree = worker.CharacterVisual?.BodyPartsController?.HandsController?.AreHandsFree() == true;
        if (!hasBagSpace && !handsFree) return false;

        // Stock check — the shelf may have been emptied since NeedHunger picked us.
        if (!ShopHasItemInStock(_shop, _foodSO)) return false;

        return true;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;
        if (worker == null) { _isComplete = true; return; }

        var movement = worker.CharacterMovement;
        if (movement == null) { _isComplete = true; return; }

        Vector3 dest = _cashier.GetInteractionPosition(worker.transform.position);
        if (Vector3.Distance(worker.transform.position, dest) > 1.5f)
        {
            if (!_isMoving) { movement.SetDestination(dest); _isMoving = true; }
            return;
        }
        if (_isMoving) { movement.Stop(); _isMoving = false; }

        if (!_actionEnqueued)
        {
            // Re-check at the moment of enqueue — another NPC may have just locked the cashier.
            if (!_cashier.IsAvailableForCustomer) { _isComplete = true; return; }

            _enqueuedAction = new CharacterAction_BuyFromShop(
                worker,
                _cashier,
                new List<ItemSO> { _foodSO },
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

    /// <summary>
    /// Returns true if any sell-shelf slot on <paramref name="shop"/> holds an
    /// <see cref="ItemInstance"/> whose <see cref="ItemSO"/> matches <paramref name="item"/>.
    /// Mirror of the inner loop in <see cref="GoapAction_GoShopping.FindShopWithItem"/> —
    /// kept local so we don't introduce a shared LINQ helper in a hot path (rule #34).
    /// </summary>
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
