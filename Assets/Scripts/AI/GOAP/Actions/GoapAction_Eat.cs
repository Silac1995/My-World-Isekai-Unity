using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pulls a <see cref="FoodInstance"/> out of a <see cref="StorageFurniture"/> slot and
/// enqueues <see cref="CharacterUseConsumableAction"/> on the NPC so the same consume
/// code path as the player's E-key fires (rule #22 player/NPC parity).
///
/// Precondition: <c>"atFood" = true</c> (satisfied by <see cref="GoapAction_GoToFood"/>).
/// Effect:       <c>"isHungry" = false</c>.
///
/// Registered by: <see cref="NeedHunger.GetGoapActions"/>.
/// </summary>
public class GoapAction_Eat : GoapAction_ExecuteCharacterAction
{
    private readonly StorageFurniture _foodSource;

    // The food item extracted from the slot during PrepareAction. Captured so we can
    // return it to the furniture if ExecuteAction is rejected by CharacterActions.
    private FoodInstance _extracted;

    public override string ActionName => "Eat";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "atFood", true }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isHungry", false }
    };

    public GoapAction_Eat(StorageFurniture foodSource)
    {
        _foodSource = foodSource;
    }

    public override bool IsValid(Character worker)
    {
        // Once the CharacterAction has been queued we must ride out the animation —
        // same pattern as GoapAction_TakeFromSourceFurniture.
        if (_isActionStarted) return true;

        if (_foodSource == null || _foodSource.IsLocked) return false;

        // Verify food still exists (another NPC may have grabbed it since GoToFood ran).
        return TryFindFood(out _);
    }

    protected override CharacterAction PrepareAction(Character worker)
    {
        if (_foodSource == null)
        {
            Debug.LogError($"<color=red>[GoapAction_Eat]</color> {worker?.CharacterName}: _foodSource is null in PrepareAction.");
            return null;
        }

        if (!TryFindFood(out FoodInstance food))
        {
            Debug.LogWarning($"<color=orange>[GoapAction_Eat]</color> {worker?.CharacterName}: no FoodInstance found in '{_foodSource.FurnitureName}' at PrepareAction time (another NPC grabbed it?).");
            return null;
        }

        // Remove from slot before queuing the action. The consume action owns the item from
        // this point; if ExecuteAction is rejected (OnActionFailed) we return it to the slot.
        if (!_foodSource.RemoveItem(food))
        {
            Debug.LogWarning($"<color=orange>[GoapAction_Eat]</color> {worker?.CharacterName}: RemoveItem failed for '{food.CustomizedName}' from '{_foodSource.FurnitureName}'. Race condition?");
            return null;
        }

        _extracted = food;
        Debug.Log($"<color=cyan>[GoapAction_Eat]</color> {worker?.CharacterName} extracted {food.CustomizedName} from {_foodSource.FurnitureName}. Queuing consume action.");
        return new CharacterUseConsumableAction(worker, food);
    }

    protected override void OnActionFinished()
    {
        // CharacterUseConsumableAction.OnApplyEffect already calls character.UseConsumable(_item),
        // which in turn calls FoodInstance.ApplyEffect -> NeedHunger.IncreaseValue.
        // Nothing extra needed here; _isComplete is set to true by the base class.
        Debug.Log($"<color=green>[GoapAction_Eat]</color> Eat action finished.");
    }

    protected override void OnActionFailed(Character worker)
    {
        // Return the extracted item to the furniture so it isn't lost.
        if (_extracted != null && _foodSource != null)
        {
            _foodSource.AddItem(_extracted);
            Debug.LogWarning($"<color=orange>[GoapAction_Eat]</color> {worker?.CharacterName}: consume action failed — returned {_extracted.CustomizedName} to {_foodSource.FurnitureName}.");
        }
        _extracted = null;
    }

    public override void Exit(Character worker)
    {
        base.Exit(worker);
        _extracted = null;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private bool TryFindFood(out FoodInstance food)
    {
        food = null;
        if (_foodSource == null) return false;

        var slots = _foodSource.ItemSlots;
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
