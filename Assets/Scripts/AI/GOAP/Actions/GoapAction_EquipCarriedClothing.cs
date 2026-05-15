using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Equips the first <see cref="WearableInstance"/> the worker is currently carrying
/// (hands take priority, then inventory slots) into the body slot it covers. Enqueues
/// <see cref="CharacterEquipAction"/> — the same path the player uses when they hit
/// the equip button (rule #22 player/NPC parity).
///
/// Sibling of <see cref="GoapAction_EatCarriedFood"/>: same shape, different
/// carried-item type and different terminator action. Companion to the monolithic
/// <see cref="GoapAction_WearClothing"/>, which scans the world for ground items
/// and never enters the inventory — this action exists specifically to close out
/// the <see cref="GoapAction_BuyClothing"/> → equip chain, because items bought
/// from a shop arrive in the customer's inventory / hands, not the ground.
///
/// Precondition: <c>"carryingClothing" = true</c> (set by <see cref="GoapAction_BuyClothing"/>).
/// Effect:       <c>"isNaked" = false</c> — terminator of the shop-fed clothing chain.
///
/// Registered by: <see cref="NeedToWearClothing.GetGoapActions"/>.
/// </summary>
public class GoapAction_EquipCarriedClothing : GoapAction_ExecuteCharacterAction
{
    public override string ActionName => "Equip Carried Clothing";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "carryingClothing", true }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isNaked", false }
    };

    public override bool IsValid(Character worker)
    {
        // Ride out the animation once the equip action has started.
        if (_isActionStarted) return true;
        if (worker == null) return false;

        // NOTE: at PLANNING time the worker has not yet bought the wearable
        // (BuyClothing runs before us in the chain), so we deliberately do NOT
        // require TryFindCarriedWearable to succeed here — that would exclude
        // us from the plan pool (CharacterGoapController.GetLifeActions filters
        // by IsValid before planning). PrepareAction performs the real check
        // at execution time and aborts cleanly if no carried wearable is found,
        // which is the standard GoapAction_ExecuteCharacterAction failure path.
        // Same pattern as GoapAction_EatCarriedFood.
        return true;
    }

    protected override CharacterAction PrepareAction(Character worker)
    {
        if (worker == null) return null;

        // Prefer a wearable that actually fills a currently-exposed slot. If two
        // wearables are carried (e.g. Pants + Armor) and both slots are missing,
        // pick whichever the worker grabs first that targets an exposed slot —
        // NeedToWearClothing.GetUrgency favours Pants first so the buy chain
        // usually only carries one piece per cycle anyway.
        var equipment = worker.CharacterEquipment;
        bool needsPants = equipment != null && equipment.IsGroinExposed();
        bool needsArmor = equipment != null && equipment.IsChestExposed();

        if (!TryFindCarriedWearable(worker, needsPants, needsArmor, out WearableInstance wearable))
        {
            Debug.LogWarning($"<color=orange>[GoapAction_EquipCarriedClothing]</color> {worker.CharacterName}: no WearableInstance in hands or inventory at PrepareAction time.");
            return null;
        }

        Debug.Log($"<color=cyan>[GoapAction_EquipCarriedClothing]</color> {worker.CharacterName} equipping carried clothing '{wearable.CustomizedName}'.");
        return new CharacterEquipAction(worker, wearable);
    }

    protected override void OnActionFinished()
    {
        // CharacterEquipAction.OnApplyEffect calls CharacterEquipment.Equip, which
        // moves the WearableInstance into the appropriate layer slot and removes
        // it from the inventory. Nothing more to do here — NeedToWearClothing
        // re-evaluates IsActive() against the new equipment state on the next BT
        // tick and the loop terminates if no slot is still exposed.
        Debug.Log($"<color=green>[GoapAction_EquipCarriedClothing]</color> Equip action finished.");
    }

    /// <summary>
    /// Finds the first <see cref="WearableInstance"/> the worker is currently carrying.
    /// Hands take priority (the buy pipeline routes through CharacterEquipment.PickUpItem
    /// which fills inventory first then hands, but either order works for our lookup).
    /// When <paramref name="needsPants"/> or <paramref name="needsArmor"/> is true,
    /// prefer wearables that fill the corresponding currently-exposed slot — fall
    /// back to the first wearable found if no slot-matching one is carried (e.g.
    /// because the NPC already had a different one in their inventory).
    /// </summary>
    private static bool TryFindCarriedWearable(Character worker, bool needsPants, bool needsArmor, out WearableInstance wearable)
    {
        wearable = null;
        if (worker == null) return false;

        WearableInstance firstAny = null;
        WearableInstance firstMatch = null;

        // 1. Hands.
        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands != null && hands.IsCarrying && hands.CarriedItem is WearableInstance handWearable)
        {
            firstAny ??= handWearable;
            if (firstMatch == null && IsSlotMatch(handWearable, needsPants, needsArmor)) firstMatch = handWearable;
        }

        // 2. Inventory.
        var equipment = worker.CharacterEquipment;
        if (equipment != null && equipment.HaveInventory())
        {
            var inventory = equipment.GetInventory();
            var slots = inventory?.ItemSlots;
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot == null || slot.IsEmpty()) continue;
                    if (slot.ItemInstance is WearableInstance wi)
                    {
                        firstAny ??= wi;
                        if (firstMatch == null && IsSlotMatch(wi, needsPants, needsArmor)) firstMatch = wi;
                        if (firstMatch != null) break;
                    }
                }
            }
        }

        wearable = firstMatch ?? firstAny;
        return wearable != null;
    }

    private static bool IsSlotMatch(WearableInstance w, bool needsPants, bool needsArmor)
    {
        if (w?.ItemSO is not WearableSO wso) return false;
        if (needsPants && wso.WearableType == WearableType.Pants) return true;
        if (needsArmor && wso.WearableType == WearableType.Armor) return true;
        return false;
    }
}
