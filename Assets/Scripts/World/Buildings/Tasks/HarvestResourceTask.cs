using UnityEngine;
using MWI.Quests;

/// <summary>
/// A task to gather resources from a Harvestable.
/// </summary>
public class HarvestResourceTask : BuildingTask
{
    private Harvestable _harvestableTarget;

    public override int MaxWorkers => 10; // Allow multiple harvesters on the same resource

    // --- IQuest specifics --------------------------------------------------
    public override string Title => "Harvest Resource";

    public override string InstructionLine
    {
        get
        {
            string itemName = _harvestableTarget != null ? _harvestableTarget.name : "<destroyed>";
            return $"Harvest {Required} {itemName}";
        }
    }

    public override string Description =>
        $"Harvest from {(_harvestableTarget != null ? _harvestableTarget.name : "<destroyed>")} until depleted.";

    public override int Required =>
        _harvestableTarget != null ? _harvestableTarget.RemainingYield : 0;
    // -----------------------------------------------------------------------

    public HarvestResourceTask(Harvestable target) : base(target)
    {
        _harvestableTarget = target;
        QuestTarget = new HarvestableTarget(target);
    }

    public override bool IsValid()
    {
        // Valid if the target exists and can still be harvested from
        return _harvestableTarget != null && _harvestableTarget.CanHarvest();
    }
}
