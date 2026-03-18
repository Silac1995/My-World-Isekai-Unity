using UnityEngine;

/// <summary>
/// A task to gather resources from a GatherableObject.
/// </summary>
public class GatherResourceTask : BuildingTask
{
    private GatherableObject _gatherableTarget;

    public override int MaxWorkers => 10; // Allow multiple gatherers on the same resource

    public GatherResourceTask(GatherableObject target) : base(target)
    {
        _gatherableTarget = target;
    }

    public override bool IsValid()
    {
        // Valid if the target exists and can still be gathered from
        return _gatherableTarget != null && _gatherableTarget.CanGather();
    }
}
