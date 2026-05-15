using System;
using System.Collections.Generic;
using MWI.Economy;
using UnityEngine;

/// <summary>
/// Triggers when the character's chest or groin slot is empty across all three
/// wearable layers (Underwear / Clothing / Armor). Provides two GOAP chains, one
/// of which is returned at a time:
/// <list type="number">
///   <item><b>Shop chain (preferred):</b> scan every <see cref="ShopBuilding"/> for
///         a <see cref="WearableSO"/> that fills the highest-urgency missing slot
///         (Pants for groin, Armor for chest), pick the cheapest in-stock entry,
///         walk to the cashier, and buy via <see cref="CharacterAction_BuyFromShop"/>
///         with <see cref="CharacterAction_BuyFromShop.BuyMode.NPC"/>. The chain
///         terminates by re-equipping the bought wearable via
///         <see cref="CharacterEquipAction"/>. Mirrors the
///         <see cref="NeedHunger"/> shop path that landed 2026-05-15.</item>
///   <item><b>Ground-pickup fallback:</b> the legacy monolithic
///         <see cref="GoapAction_WearClothing"/> — scans
///         <see cref="CharacterAwareness"/> for a loose <see cref="WorldItem"/>
///         backed by a <see cref="WearableInstance"/>. Used only when no shop
///         carries an affordable matching wearable.</item>
/// </list>
/// </summary>
public class NeedToWearClothing : CharacterNeed
{
    private GoapAction_WearClothing _wearClothingAction;

    public NeedToWearClothing(Character character) : base(character)
    {
        _wearClothingAction = new GoapAction_WearClothing();
    }

    public override bool IsActive()
    {
        bool needsClothing = _character.CharacterEquipment.IsChestExposed() || _character.CharacterEquipment.IsGroinExposed();
        if (!needsClothing) return false;
        if (_character.CharacterActions.CurrentAction is CharacterEquipAction) return false;
        return true;
    }

    public override float GetUrgency()
    {
        if (!IsActive()) return 0f;
        if (_character.CharacterEquipment.IsGroinExposed()) return 100f;
        return 60f;
    }

    public override GoapGoal GetGoapGoal()
    {
        return new GoapGoal("WearClothing", new Dictionary<string, bool> { { "isNaked", false } }, (int)GetUrgency());
    }

    /// <summary>
    /// Shop-first, ground-pickup fallback. Returns at most one chain — the planner
    /// never sees both at once, which keeps it from cross-linking
    /// <see cref="GoapAction_BuyClothing"/> with <see cref="GoapAction_WearClothing"/>
    /// despite both terminating in <c>"isNaked" = false</c>.
    /// </summary>
    public override List<GoapAction> GetGoapActions()
    {
        if (_character == null)
        {
            Debug.LogWarning("<color=orange>[NeedToWearClothing]</color> GetGoapActions: _character is null.");
            return new List<GoapAction>();
        }

        // 1. Shop path — the default route.
        var shopChain = TryFindShopClothing();
        if (shopChain != null) return shopChain;

        // 2. Ground-pickup fallback (legacy monolithic action).
        return new List<GoapAction> { _wearClothingAction };
    }

    /// <summary>
    /// Walks every <see cref="ShopBuilding"/> registered with
    /// <see cref="BuildingManager"/> and looks for a <see cref="WearableSO"/>
    /// matching the most-urgent missing slot first (Pants for groin, then Armor
    /// for chest). The cheapest in-stock affordable candidate wins. Returns the
    /// <c>[BuyClothing, EquipCarriedClothing]</c> chain on success, otherwise
    /// null so <see cref="GetGoapActions"/> can fall back to the ground path.
    /// </summary>
    private List<GoapAction> TryFindShopClothing()
    {
        var equipment = _character.CharacterEquipment;
        if (equipment == null) return null;

        // Priority order matches GetUrgency: groin (100) before chest (60).
        var missingSlots = new List<WearableType>();
        if (equipment.IsGroinExposed()) missingSlots.Add(WearableType.Pants);
        if (equipment.IsChestExposed()) missingSlots.Add(WearableType.Armor);
        if (missingSlots.Count == 0) return null;

        var bm = BuildingManager.Instance;
        if (bm == null || bm.allBuildings == null) return null;

        var wallet = _character.CharacterWallet;
        var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;

        // Walk the priority list; first matching slot with a viable shop wins.
        for (int s = 0; s < missingSlots.Count; s++)
        {
            WearableType slot = missingSlots[s];

            WearableSO bestWearable = null;
            ShopBuilding bestShop = null;
            Cashier bestCashier = null;
            int bestPrice = int.MaxValue;

            try
            {
                for (int b = 0; b < bm.allBuildings.Count; b++)
                {
                    if (bm.allBuildings[b] is not ShopBuilding shop) continue;
                    if (shop.Catalog == null || shop.Catalog.Count == 0) continue;
                    if (shop.SellShelves == null || shop.SellShelves.Count == 0) continue;

                    var cashier = shop.GetFirstAvailableCashier();
                    if (cashier == null) continue;

                    for (int c = 0; c < shop.Catalog.Count; c++)
                    {
                        var entry = shop.Catalog[c];
                        if (entry.Item is not WearableSO ws) continue;
                        if (ws.WearableType != slot) continue;

                        int price = ShopBuilding.ResolvePrice(entry);
                        if (price > 0 && (wallet == null || !wallet.CanAfford(CurrencyId.Default, price))) continue;

                        bool hasBagSpace = equipment.HasFreeSpaceForItemSO(ws);
                        bool handsFree = hands != null && hands.AreHandsFree();
                        if (!hasBagSpace && !handsFree) continue;

                        if (!ShopHasItemInStock(shop, ws)) continue;

                        // Cheapest-in-slot wins. Free items (price ≤ 0) collapse to bestPrice=0.
                        int effective = price <= 0 ? 0 : price;
                        if (effective >= bestPrice) continue;

                        bestPrice = effective;
                        bestWearable = ws;
                        bestShop = shop;
                        bestCashier = cashier;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"<color=red>[NeedToWearClothing]</color> {_character.CharacterName}: exception while scanning shops for '{slot}'.");
                continue;
            }

            if (bestWearable != null)
            {
                Debug.Log($"<color=green>[NeedToWearClothing]</color> {_character.CharacterName} chose '{bestWearable.name}' ({slot}) at '{bestShop.BuildingName}' (price {bestPrice}).");
                return new List<GoapAction>
                {
                    new GoapAction_BuyClothing(bestShop, bestCashier, bestWearable),
                    new GoapAction_EquipCarriedClothing()
                };
            }
        }

        return null;
    }

    /// <summary>
    /// True if any sell-shelf slot on <paramref name="shop"/> holds an
    /// <see cref="ItemInstance"/> whose <see cref="ItemSO"/> matches
    /// <paramref name="item"/>. Inlined (no LINQ) per rule #34. Mirror of the
    /// helper in <see cref="NeedHunger"/> / <see cref="GoapAction_BuyFood"/>.
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
