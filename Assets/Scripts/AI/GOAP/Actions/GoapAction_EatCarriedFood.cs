using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Eats the first <see cref="FoodInstance"/> the worker is carrying — checks the hands
/// first, then falls through to the inventory slots. Enqueues
/// <see cref="CharacterUseConsumableAction"/> so the same consume path the player uses
/// fires on the NPC (rule #22 player/NPC parity). The use-action's
/// <see cref="Character.UseConsumable"/> branch already scrubs the item from hands and
/// inventory after the effect lands, so we don't have to.
///
/// Companion to <see cref="GoapAction_Eat"/>, which pulls food directly from a
/// <see cref="StorageFurniture"/> slot and never enters the worker's inventory.
///
/// Precondition: <c>"carryingFood" = true</c> (set by <see cref="GoapAction_PickupWorldFood"/>).
/// Effect:       <c>"isHungry" = false</c> — terminator of the world-item hunger chain.
///
/// Registered by: <see cref="NeedHunger.GetGoapActions"/>.
/// </summary>
public class GoapAction_EatCarriedFood : GoapAction_ExecuteCharacterAction
{
    public override string ActionName => "Eat Carried Food";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "carryingFood", true }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isHungry", false }
    };

    public override bool IsValid(Character worker)
    {
        // Ride out the animation once the consume action has started.
        if (_isActionStarted) return true;
        if (worker == null) return false;

        // NOTE: at PLANNING time the worker has not yet picked up the food (the pickup
        // action runs before us in the chain), so we deliberately do NOT require
        // TryFindCarriedFood to succeed here — that would exclude us from the plan pool
        // (CharacterGoapController.GetLifeActions filters by IsValid before planning).
        // PrepareAction performs the real check at execution time and aborts cleanly
        // (returning null) if no carried food is found, which is the standard
        // GoapAction_ExecuteCharacterAction failure path.
        return true;
    }

    protected override CharacterAction PrepareAction(Character worker)
    {
        if (worker == null) return null;

        if (!TryFindCarriedFood(worker, out FoodInstance food))
        {
            Debug.LogWarning($"<color=orange>[GoapAction_EatCarriedFood]</color> {worker.CharacterName}: no FoodInstance in hands or inventory at PrepareAction time.");
            return null;
        }

        Debug.Log($"<color=cyan>[GoapAction_EatCarriedFood]</color> {worker.CharacterName} consuming carried food '{food.CustomizedName}'.");
        return new CharacterUseConsumableAction(worker, food);
    }

    protected override void OnActionFinished()
    {
        // CharacterUseConsumableAction.OnApplyEffect calls Character.UseConsumable,
        // which fires FoodInstance.ApplyEffect → NeedHunger.IncreaseValue and then
        // strips the item from hands/inventory. Nothing more to do here.
        Debug.Log($"<color=green>[GoapAction_EatCarriedFood]</color> Carried-food consume action finished.");
    }

    /// <summary>
    /// Finds the first FoodInstance the worker is currently carrying. Hands take priority
    /// (the pickup action puts items in the inventory first, falling back to hands; either
    /// way we just need any FoodInstance the worker owns).
    /// </summary>
    private static bool TryFindCarriedFood(Character worker, out FoodInstance food)
    {
        food = null;
        if (worker == null) return false;

        // 1. Hands.
        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands != null && hands.IsCarrying && hands.CarriedItem is FoodInstance handFood)
        {
            food = handFood;
            return true;
        }

        // 2. Inventory.
        var equipment = worker.CharacterEquipment;
        if (equipment == null || !equipment.HaveInventory()) return false;

        var inventory = equipment.GetInventory();
        if (inventory == null) return false;

        var slots = inventory.ItemSlots;
        if (slots == null) return false;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.IsEmpty()) continue;
            if (slot.ItemInstance is FoodInstance fi)
            {
                food = fi;
                return true;
            }
        }
        return false;
    }
}
