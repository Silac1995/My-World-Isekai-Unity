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

        // Collect every qualifying shop, then pick one weighted by reputation
        // (2026-05-17 — customer-NPC reputation effect). Picker formula:
        //   weight = max(10, shop.Reputation)
        // floors the lowest-rep shop at weight 10 vs a top-rep shop's weight 100,
        // guaranteeing a 10:100 = 10% minimum relative chance for the worst shop.
        // Customers don't apply the B2B hard floor (ReputationB2BMinimum = 20) —
        // they're random shoppers, not procurement officers, so even a sketchy
        // shop occasionally gets a visit.
        var candidates = FindQualifyingShopsWithItem(_desiredItem);
        if (candidates == null || candidates.Count == 0) return false;
        var shop = PickShopByReputation(candidates);
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
            // Re-fire SetDestination whenever the agent dropped its path (BT branch
            // switched away and back, knockback, brief OccupyingFurniture, transient
            // NavMesh exit). The sticky _isMoving flag alone is not enough — see
            // [[interactable-proximity-distance-anti-pattern]].
            if (!_isMoving || !movement.HasPath)
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

    /// <summary>
    /// Returns every <see cref="ShopBuilding"/> that (a) sells <paramref name="item"/>
    /// in its catalog, (b) has at least one available <see cref="Cashier"/>, and
    /// (c) has at least one matching <see cref="ItemInstance"/> physically on a
    /// sell-shelf. Replaces the previous <c>FirstOrDefault</c> single-shop pick so
    /// the caller can weight the final choice by reputation (see
    /// <see cref="PickShopByReputation"/>). Returns an empty list when no shop
    /// qualifies (caller treats as "no shop sells what I need").
    /// </summary>
    private static List<ShopBuilding> FindQualifyingShopsWithItem(ItemSO item)
    {
        var result = new List<ShopBuilding>();
        if (BuildingManager.Instance == null) return result;

        var all = BuildingManager.Instance.allBuildings;
        for (int b = 0; b < all.Count; b++)
        {
            if (!(all[b] is ShopBuilding s)) continue;

            var entry = s.GetCatalogEntry(item);
            if (!entry.HasValue) continue;
            if (s.GetFirstAvailableCashier() == null) continue;

            bool hasStock = false;
            var shelves = s.SellShelves;
            for (int i = 0; i < shelves.Count && !hasStock; i++)
            {
                var shelf = shelves[i];
                if (shelf == null) continue;
                int cap = shelf.Capacity;
                for (int sl = 0; sl < cap; sl++)
                {
                    var slot = shelf.GetItemSlot(sl);
                    if (slot != null && !slot.IsEmpty() && slot.ItemInstance.ItemSO == item)
                    {
                        hasStock = true;
                        break;
                    }
                }
            }
            if (!hasStock) continue;

            result.Add(s);
        }
        return result;
    }

    /// <summary>
    /// Reputation-weighted random pick across <paramref name="candidates"/>.
    /// Weight formula: <c>max(10, shop.Reputation)</c> — guarantees the
    /// lowest-rep shop has a relative weight of 10/100 = 10% of a top-rep shop,
    /// so no shop is ever permanently invisible to customers. Customer-NPC
    /// effect (2026-05-17). Server-only — <see cref="UnityEngine.Random"/> uses
    /// shared state, but customer-NPC GOAP planning runs server-side only.
    /// </summary>
    private static ShopBuilding PickShopByReputation(List<ShopBuilding> candidates)
    {
        if (candidates == null || candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        int totalWeight = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            totalWeight += UnityEngine.Mathf.Max(10, candidates[i].Reputation);
        }
        if (totalWeight <= 0) return candidates[0]; // defensive — shouldn't happen with the 10 floor.

        int roll = UnityEngine.Random.Range(0, totalWeight);
        int accum = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            accum += UnityEngine.Mathf.Max(10, candidates[i].Reputation);
            if (roll < accum) return candidates[i];
        }
        // Floating-point safety net (Random.Range is exclusive upper, so unreachable in practice).
        return candidates[candidates.Count - 1];
    }
}
