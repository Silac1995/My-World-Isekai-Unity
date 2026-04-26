using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Walks the actor to the closest exit <see cref="MapTransitionDoor"/> on their
/// current interior <see cref="MapController"/> and triggers it.
///
/// No-ops if the actor is not on an interior map (already outside).
/// Cancels with a warning if the actor's current interior has no exit door.
///
/// Exit door = any <see cref="MapTransitionDoor"/> child of the actor's current
/// MapController that is NOT a <see cref="BuildingInteriorDoor"/>. (BuildingInteriorDoors
/// are entry doors placed on the exterior building shell; the exit baked into an
/// interior prefab is a regular MapTransitionDoor.)
/// </summary>
public class CharacterLeaveInteriorAction : CharacterDoorTraversalAction
{
    public CharacterLeaveInteriorAction(Character actor) : base(actor) { }

    public override string ActionName => "Leave Interior";

    protected override bool IsActionRedundant()
    {
        var map = ResolveCurrentMap();
        return map == null || map.Type != MapType.Interior;
    }

    protected override MapTransitionDoor ResolveDoor()
    {
        var map = ResolveCurrentMap();
        if (map == null || map.Type != MapType.Interior) return null;

        MapTransitionDoor[] doors = map.GetComponentsInChildren<MapTransitionDoor>(includeInactive: false);
        if (doors == null || doors.Length == 0) return null;

        MapTransitionDoor best = null;
        float bestSqrDist = float.PositiveInfinity;
        Vector3 actorPos = character.transform.position;

        foreach (var door in doors)
        {
            if (door == null) continue;
            // Skip BuildingInteriorDoors — they're entry doors on exterior building shells,
            // not exit doors inside interiors.
            if (door is BuildingInteriorDoor) continue;

            float d = (door.transform.position - actorPos).sqrMagnitude;
            if (d < bestSqrDist)
            {
                bestSqrDist = d;
                best = door;
            }
        }

        return best;
    }

    private MapController ResolveCurrentMap()
    {
        var tracker = character.GetComponent<CharacterMapTracker>();
        if (tracker == null) return null;

        string mapId = tracker.CurrentMapID.Value.ToString();
        if (string.IsNullOrEmpty(mapId)) return null;

        return MapController.GetByMapId(mapId);
    }
}
