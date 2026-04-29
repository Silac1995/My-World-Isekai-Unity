using UnityEngine;
using MWI.Quests;

/// <summary>
/// A task to destroy a Harvestable for its destruction outputs (e.g. chop a dead tree to get
/// wood, mine through a depleted ore vein to get fragments). Distinct from
/// <see cref="HarvestResourceTask"/> in two ways:
///
/// 1. It runs <see cref="CharacterAction_DestroyHarvestable"/> instead of
///    <see cref="CharacterHarvestAction"/>, which spawns the destruction outputs and despawns
///    the GameObject when complete.
/// 2. It is gated on <see cref="Harvestable.AllowNpcDestruction"/> — designers must explicitly
///    opt-in per resource node so workers don't strip-mine the world.
///
/// Registered by <see cref="HarvestingBuilding.AddToTrackedHarvestables"/> when a discovered
/// harvestable yields a wanted item only via destruction (not via the default pick path).
/// </summary>
public class DestroyHarvestableTask : BuildingTask
{
    private Harvestable _harvestableTarget;

    public override int MaxWorkers => 1; // Only one worker can destroy a given harvestable

    public Harvestable HarvestableTarget => _harvestableTarget;

    public override string Title => "Destroy Resource";

    public override string InstructionLine
    {
        get
        {
            string itemName = _harvestableTarget != null ? _harvestableTarget.name : "<destroyed>";
            return $"Destroy {itemName}";
        }
    }

    public override string Description =>
        $"Destroy {(_harvestableTarget != null ? _harvestableTarget.name : "<destroyed>")} to harvest its destruction outputs.";

    public override int Required => 1; // One destruction completes the task

    public DestroyHarvestableTask(Harvestable target) : base(target)
    {
        _harvestableTarget = target;
        QuestTarget = new HarvestableTarget(target);
    }

    /// <summary>Valid while the target exists, opts in to destruction by NPCs, and hasn't
    /// already been depleted via the yield path.</summary>
    public override bool IsValid()
    {
        return _harvestableTarget != null
            && _harvestableTarget.AllowDestruction
            && _harvestableTarget.AllowNpcDestruction
            && !_harvestableTarget.IsDepleted;
    }
}
