using UnityEngine;

/// <summary>
/// Walks the actor to the target building's closest <see cref="BuildingInteriorDoor"/>
/// and triggers it. The door handles the lock check, key unlock, rattle, and queues
/// <see cref="CharacterMapTransitionAction"/> — this action is purely "navigate + tap".
///
/// No-ops if the actor is already on the building's interior map.
/// Cancels with a warning if the building has no <see cref="BuildingInteriorDoor"/> child.
/// </summary>
public class CharacterEnterBuildingAction : CharacterDoorTraversalAction
{
    private readonly Building _target;

    public CharacterEnterBuildingAction(Character actor, Building target) : base(actor)
    {
        _target = target;
    }

    public override string ActionName => "Enter Building";

    public override bool CanExecute()
    {
        if (_target == null)
        {
            Debug.LogWarning($"<color=orange>[EnterBuilding] {character?.CharacterName}: target building is null.</color>");
            return false;
        }
        return base.CanExecute();
    }

    protected override bool IsActionRedundant()
    {
        if (_target == null) return false;

        string interiorMapId = _target.GetInteriorMapId();
        if (string.IsNullOrEmpty(interiorMapId)) return false; // interior not spawned yet — definitely not inside

        var tracker = character.GetComponent<CharacterMapTracker>();
        if (tracker == null) return false;

        return tracker.CurrentMapID.Value.ToString() == interiorMapId;
    }

    protected override MapTransitionDoor ResolveDoor()
    {
        if (_target == null) return null;

        BuildingInteriorDoor[] doors = _target.GetComponentsInChildren<BuildingInteriorDoor>(includeInactive: false);
        if (doors == null || doors.Length == 0) return null;

        BuildingInteriorDoor best = null;
        float bestSqrDist = float.PositiveInfinity;
        Vector3 actorPos = character.transform.position;

        foreach (var door in doors)
        {
            if (door == null) continue;
            float d = (door.transform.position - actorPos).sqrMagnitude;
            if (d < bestSqrDist)
            {
                bestSqrDist = d;
                best = door;
            }
        }

        return best;
    }
}
