using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Picks up a loose <see cref="WorldItem"/> (whose instance is a <see cref="FoodInstance"/>)
/// via <see cref="CharacterPickUpItem"/>. Same code path as the player's E-key pickup —
/// item lands in the inventory if there's space, otherwise in hands (rule #22 player/NPC parity).
///
/// Distinct from <see cref="MWI.AI.GoapAction_PickupItem"/> (transporter-coupled, requires
/// a <see cref="JobTransporter"/> with a live <c>TransportOrder</c>) and from
/// <see cref="GoapAction_PickupLooseItem"/> (harvester-coupled, claims a
/// <c>PickupLooseItemTask</c> from a <see cref="HarvestingBuilding"/>). This action is
/// purpose-built for the hunger flow and has no Job dependency.
///
/// Precondition: <c>"atWorldFood" = true</c> (set by <see cref="GoapAction_GoToWorldFood"/>).
/// Effect:       <c>"carryingFood" = true</c>.
///
/// Successor: <see cref="GoapAction_EatCarriedFood"/>.
/// Registered by: <see cref="NeedHunger.GetGoapActions"/>.
/// </summary>
public class GoapAction_PickupWorldFood : GoapAction_ExecuteCharacterAction
{
    private readonly WorldItem _worldFood;

    public override string ActionName => "Pickup World Food";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "atWorldFood", true }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "carryingFood", true }
    };

    public GoapAction_PickupWorldFood(WorldItem worldFood)
    {
        _worldFood = worldFood;
    }

    public override bool IsValid(Character worker)
    {
        // Once the CharacterAction has been queued we must ride out the animation —
        // same pattern as GoapAction_Eat / GoapAction_TakeFromSourceFurniture.
        if (_isActionStarted) return true;

        if (_worldFood == null) return false;
        if (_worldFood.IsBeingCarried) return false;
        if (_worldFood.ItemInstance is not FoodInstance) return false;

        // Worker must have somewhere to put the food (inventory slot OR free hands).
        if (worker == null) return false;
        var equipment = worker.CharacterEquipment;
        if (equipment != null && equipment.HaveInventory())
        {
            var inventory = equipment.GetInventory();
            if (inventory != null && inventory.HasFreeSpaceForItem(_worldFood.ItemInstance)) return true;
        }
        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands != null && hands.AreHandsFree()) return true;

        // No room anywhere — let GOAP fail this plan instead of busy-looping.
        return false;
    }

    protected override CharacterAction PrepareAction(Character worker)
    {
        if (_worldFood == null)
        {
            Debug.LogError($"<color=red>[GoapAction_PickupWorldFood]</color> {worker?.CharacterName}: _worldFood is null in PrepareAction.");
            return null;
        }

        if (_worldFood.IsBeingCarried)
        {
            Debug.LogWarning($"<color=orange>[GoapAction_PickupWorldFood]</color> {worker?.CharacterName}: target {_worldFood.name} is already being carried — race lost.");
            return null;
        }

        ItemInstance instance = _worldFood.ItemInstance;
        if (instance is not FoodInstance)
        {
            Debug.LogWarning($"<color=orange>[GoapAction_PickupWorldFood]</color> {worker?.CharacterName}: {_worldFood.name} is no longer a FoodInstance.");
            return null;
        }

        Debug.Log($"<color=cyan>[GoapAction_PickupWorldFood]</color> {worker?.CharacterName} picking up loose food '{instance.CustomizedName}'.");
        return new CharacterPickUpItem(worker, instance, _worldFood.gameObject);
    }

    protected override void OnActionFailed(Character worker)
    {
        Debug.LogWarning($"<color=orange>[GoapAction_PickupWorldFood]</color> {worker?.CharacterName}: pickup failed (out of range / hands+bag full / target despawned).");
    }
}
