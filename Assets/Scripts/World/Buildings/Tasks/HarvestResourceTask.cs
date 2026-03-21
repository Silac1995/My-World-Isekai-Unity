using UnityEngine;

/// <summary>
/// A task to gather resources from a Harvestable.
/// </summary>
public class HarvestResourceTask : BuildingTask
{
    private Harvestable _harvestableTarget;

    public override int MaxWorkers => 10; // Allow multiple harvesters on the same resource

    public HarvestResourceTask(Harvestable target) : base(target)
    {
        _harvestableTarget = target;
    }

    public override bool IsValid()
    {
        // Valid if the target exists and can still be harvested from
        return _harvestableTarget != null && _harvestableTarget.CanHarvest();
    }
}
