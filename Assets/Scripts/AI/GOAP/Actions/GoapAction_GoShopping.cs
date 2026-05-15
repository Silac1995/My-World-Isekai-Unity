using System.Collections.Generic;
using System.Linq;
using MWI.Economy;
using UnityEngine;

public class GoapAction_GoShopping : GoapAction
{
    public override string ActionName => "GoShopping";

    public override Dictionary<string, bool> Preconditions => new();
    public override Dictionary<string, bool> Effects => new() { { "shoppingDone", true } };
    public override float Cost => 2f;

    private readonly ItemSO _desiredItem;
    private bool _isComplete;
    private bool _isMoving;
    private bool _actionEnqueued;
    private bool _actionFinished;
    private ShopBuilding _chosenShop;
    private Cashier _chosenCashier;
    private CharacterAction_BuyFromShop _enqueuedAction;

    public override bool IsComplete => _isComplete;

    public GoapAction_GoShopping(ItemSO desiredItem) { _desiredItem = desiredItem; }

    public override bool IsValid(Character worker)
    {
        if (_isComplete) return false;
        if (_chosenShop != null && _chosenCashier != null)
            return _chosenCashier.IsAvailableForCustomer || _actionEnqueued;

        var shop = FindShopWithItem(_desiredItem);
        if (shop == null) return false;

        var entry = shop.GetCatalogEntry(_desiredItem);
        if (!entry.HasValue) return false;

        int price = ShopBuilding.ResolvePrice(entry.Value);
        if (price > 0 && !worker.CharacterWallet.CanAfford(CurrencyId.Default, price)) return false;

        bool hasBagSpace = worker.CharacterEquipment != null && worker.CharacterEquipment.HasFreeSpaceForItemSO(_desiredItem);
        bool handsFree = worker.CharacterVisual?.BodyPartsController?.HandsController?.AreHandsFree() == true;
        if (!hasBagSpace && !handsFree) return false;

        var cashier = shop.GetFirstAvailableCashier();
        if (cashier == null) return false;

        _chosenShop = shop;
        _chosenCashier = cashier;
        return true;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;
        if (_chosenShop == null || _chosenCashier == null) { _isComplete = true; return; }

        var movement = worker.CharacterMovement;
        if (movement == null) { _isComplete = true; return; }

        // Movement gate: InteractionZone containment + softlock guard, NOT raw distance
        // to GetInteractionPosition. NavMesh.SamplePosition can pull the agent's landing
        // several metres off the interaction point (cashier mesh blocks NavMesh beneath it),
        // and a naive distance check then never resolves. See goap/SKILL.md "Interactable
        // Core Rule #1" + CLAUDE.md rule #36. Mirror of GoapAction_FetchSeed / BuyFood.
        var interactable = _chosenCashier.GetComponent<InteractableObject>();
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
                    Vector3 ip = _chosenCashier.GetInteractionPosition(worker.transform.position);
                    Vector3 wp = worker.transform.position;
                    if (Vector3.Distance(new Vector3(wp.x, 0f, wp.z),
                                         new Vector3(ip.x, 0f, ip.z)) <= 2f)
                        inZone = true;
                }
            }
        }
        else
        {
            Vector3 ip = _chosenCashier.GetInteractionPosition(worker.transform.position);
            Vector3 wp = worker.transform.position;
            inZone = Vector3.Distance(new Vector3(wp.x, 0f, wp.z),
                                      new Vector3(ip.x, 0f, ip.z)) <= 1.5f;
        }

        if (!inZone)
        {
            if (!_isMoving)
            {
                movement.SetDestination(_chosenCashier.GetInteractionPosition(worker.transform.position));
                _isMoving = true;
            }
            return;
        }
        if (_isMoving) { movement.Stop(); _isMoving = false; }

        if (!_actionEnqueued)
        {
            if (!_chosenCashier.IsAvailableForCustomer) { _isComplete = true; return; }

            _enqueuedAction = new CharacterAction_BuyFromShop(
                worker, _chosenCashier, new List<ItemSO> { _desiredItem }, CharacterAction_BuyFromShop.BuyMode.NPC);
            _enqueuedAction.OnActionFinished += () => _actionFinished = true;
            worker.CharacterActions.ExecuteAction(_enqueuedAction);
            _actionEnqueued = true;
        }

        if (_actionFinished) _isComplete = true;
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _isMoving = false;
        _actionEnqueued = false;
        _actionFinished = false;
        _chosenShop = null;
        _chosenCashier = null;
        _enqueuedAction = null;
        worker.CharacterMovement?.Stop();
    }

    private static ShopBuilding FindShopWithItem(ItemSO item)
    {
        if (BuildingManager.Instance == null) return null;
        return BuildingManager.Instance.allBuildings
            .OfType<ShopBuilding>()
            .FirstOrDefault(s =>
            {
                var entry = s.GetCatalogEntry(item);
                if (!entry.HasValue) return false;
                if (s.GetFirstAvailableCashier() == null) return false;

                // At least one matching instance must be on a sell-shelf.
                for (int i = 0; i < s.SellShelves.Count; i++)
                {
                    var shelf = s.SellShelves[i];
                    if (shelf == null) continue;
                    for (int sl = 0; sl < shelf.Capacity; sl++)
                    {
                        var slot = shelf.GetItemSlot(sl);
                        if (slot != null && !slot.IsEmpty() && slot.ItemInstance.ItemSO == item) return true;
                    }
                }
                return false;
            });
    }
}
