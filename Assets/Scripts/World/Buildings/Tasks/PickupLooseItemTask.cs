using UnityEngine;
using MWI.Quests;

/// <summary>
/// A task to pick up a loose WorldItem from the ground.
/// Provides IQuest specifics (Title / InstructionLine / Description) and a
/// strongly-typed <see cref="WorldItemTarget"/> so both player HUDs and NPC GOAP
/// can consume the same primitive.
/// </summary>
public class PickupLooseItemTask : BuildingTask
{
    private WorldItem _worldItemTarget;

    public override string Title => "Pick Up Item";

    public override string InstructionLine
    {
        get
        {
            string itemName = _worldItemTarget != null
                              && _worldItemTarget.ItemInstance != null
                              && _worldItemTarget.ItemInstance.ItemSO != null
                ? _worldItemTarget.ItemInstance.ItemSO.ItemName
                : "<destroyed>";
            return $"Pick up {itemName} and return to storage.";
        }
    }

    public override string Description => InstructionLine;

    public PickupLooseItemTask(WorldItem target) : base(target)
    {
        _worldItemTarget = target;
        QuestTarget = new WorldItemTarget(target);
    }

    public override bool IsValid()
    {
        // Valid if the item still exists and is not currently being carried
        return _worldItemTarget != null && !_worldItemTarget.IsBeingCarried;
    }
}
