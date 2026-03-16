using UnityEngine;

/// <summary>
/// A task to pick up a loose WorldItem from the ground.
/// </summary>
public class PickupLooseItemTask : BuildingTask
{
    private WorldItem _worldItemTarget;

    public PickupLooseItemTask(WorldItem target) : base(target)
    {
        _worldItemTarget = target;
    }

    public override bool IsValid()
    {
        // Valid if the item still exists and is not currently being carried
        return _worldItemTarget != null && !_worldItemTarget.IsBeingCarried;
    }
}
