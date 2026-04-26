using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Walks the NPC into the interaction zone of a loose <see cref="WorldItem"/> whose
/// <see cref="ItemInstance"/> is a <see cref="FoodInstance"/>. Sets effect
/// <c>"atWorldFood" = true</c> so that <see cref="GoapAction_PickupWorldFood"/> can
/// chain from it.
///
/// Companion to (but distinct from) <see cref="GoapAction_GoToFood"/>, which targets
/// a <see cref="StorageFurniture"/>. The two paths share the <c>"isHungry"</c> goal but
/// use disjoint intermediate keys (<c>"atFood"</c> vs <c>"atWorldFood"</c>) so the
/// planner cannot accidentally cross-link a workplace eat with a world-item move.
///
/// Successor: <see cref="GoapAction_PickupWorldFood"/>.
/// Registered by: <see cref="NeedHunger.GetGoapActions"/>.
/// </summary>
public class GoapAction_GoToWorldFood : GoapAction_MoveToTarget
{
    private readonly WorldItem _worldFood;

    public override string ActionName => "Go To World Food";

    // Empty — first move action with no required world-state precondition.
    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "atWorldFood", true }
    };

    public GoapAction_GoToWorldFood(WorldItem worldFood)
    {
        _worldFood = worldFood;
    }

    public override bool IsValid(Character worker)
    {
        if (_worldFood == null) return false;
        // Another character grabbed it (CharacterPickUpItem flips IsBeingCarried on start)
        if (_worldFood.IsBeingCarried) return false;
        // Defensive: GameObject destroyed mid-walk (server-side despawn).
        if (_worldFood.gameObject == null) return false;
        // The instance must still resolve to food. WorldItem.ItemInstance is replicated
        // post-spawn; on hibernation/edge cases it can briefly be null.
        if (_worldFood.ItemInstance is not FoodInstance) return false;
        return true;
    }

    protected override Collider GetTargetCollider(Character worker)
    {
        if (_worldFood == null) return null;

        var interactable = _worldFood.ItemInteractable;
        if (interactable != null && interactable.InteractionZone != null)
            return interactable.InteractionZone;

        // Fallback to the WorldItem's own collider so arrival check doesn't dead-stop.
        var col = _worldFood.GetComponentInChildren<Collider>();
        if (col == null)
        {
            Debug.LogWarning($"<color=orange>[GoapAction_GoToWorldFood]</color> {_worldFood.name} has no ItemInteractable.InteractionZone or child Collider — arrival check will fail.");
        }
        return col;
    }

    protected override Vector3 GetDestinationPoint(Character worker)
    {
        if (_worldFood == null) return worker != null ? worker.transform.position : Vector3.zero;
        return _worldFood.transform.position;
    }
}
