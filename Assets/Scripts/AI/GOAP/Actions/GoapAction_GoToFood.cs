using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Walks the NPC into the interaction zone of a <see cref="StorageFurniture"/> that
/// contains a <see cref="FoodInstance"/>. Sets effect <c>"atFood" = true</c> so that
/// <see cref="GoapAction_Eat"/> can chain from it.
///
/// Successor: <see cref="GoapAction_Eat"/>.
/// Registered by: <see cref="NeedHunger.GetGoapActions"/>.
/// </summary>
public class GoapAction_GoToFood : GoapAction_MoveToTarget
{
    private readonly StorageFurniture _foodSource;

    public override string ActionName => "Go To Food";

    // Empty — this is a "first move" action with no required world-state precondition.
    // The base GoapAction.Preconditions getter is abstract, so we must implement it
    // (returning an empty dictionary makes ArePreconditionsMet always return true).
    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "atFood", true }
    };

    public GoapAction_GoToFood(StorageFurniture foodSource)
    {
        _foodSource = foodSource;
    }

    public override bool IsValid(Character worker)
    {
        if (_foodSource == null) return false;
        if (_foodSource.IsLocked) return false;

        // Verify the furniture still holds at least one FoodInstance so we don't
        // walk toward an already-emptied source (another NPC may have grabbed it).
        var slots = _foodSource.ItemSlots;
        if (slots == null) return false;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot != null && !slot.IsEmpty() && slot.ItemInstance is FoodInstance)
                return true;
        }

        Debug.Log($"<color=orange>[GoToFood]</color> {worker?.CharacterName}: food source '{_foodSource.FurnitureName}' is empty or has no FoodInstance. Action invalid.");
        return false;
    }

    protected override Collider GetTargetCollider(Character worker)
    {
        if (_foodSource == null) return null;
        var interactable = _foodSource.GetComponent<FurnitureInteractable>();
        if (interactable != null && interactable.InteractionZone != null)
            return interactable.InteractionZone;

        var col = _foodSource.GetComponent<Collider>();
        if (col == null)
        {
            Debug.LogWarning($"<color=orange>[GoapAction_GoToFood]</color> {_foodSource.name} has no FurnitureInteractable.InteractionZone or root Collider — arrival check will fail.");
        }
        return col;
    }

    protected override Vector3 GetDestinationPoint(Character worker)
    {
        if (_foodSource == null) return worker != null ? worker.transform.position : Vector3.zero;
        return _foodSource.GetInteractionPosition(worker.transform.position);
    }
}
