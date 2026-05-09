using UnityEngine;
using MWI.Quests;

/// <summary>
/// A task to gather resources from a Harvestable.
/// </summary>
public class HarvestResourceTask : BuildingTask
{
    private Harvestable _harvestableTarget;
    // Required is captured at construction. _harvestableTarget.RemainingYield is dynamic
    // (decreases as harvesting depletes the resource). If we returned RemainingYield directly
    // from Required, the player's HUD would show "(0/5) → (0/4) → (0/3) ..." as they harvest,
    // because the denominator drops while progress hasn't been recorded yet.
    private readonly int _initialRequired;

    public override int MaxWorkers => 10; // Allow multiple harvesters on the same resource

    public Harvestable HarvestableTarget => _harvestableTarget;

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

    public override int Required => _initialRequired;
    // -----------------------------------------------------------------------

    public HarvestResourceTask(Harvestable target) : base(target)
    {
        _harvestableTarget = target;
        _initialRequired = target != null ? target.RemainingYield : 0;
        QuestTarget = new HarvestableTarget(target);
    }

    public override bool IsValid()
    {
        // Valid if the target exists and can still be harvested from
        return _harvestableTarget != null && _harvestableTarget.CanHarvest();
    }
}
