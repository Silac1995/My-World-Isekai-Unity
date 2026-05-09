using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Blacksmith Apprentice job: assists the main blacksmith.
/// Prepares materials, tends the fire, learns the trade.
/// </summary>
public class JobBlacksmithApprentice : JobCrafter
{
    public override string JobTitle => "Blacksmith Apprentice";
    public override JobType Type => JobType.BlacksmithApprentice;

    public JobBlacksmithApprentice(SkillSO smithingSkill, SkillTier tier = SkillTier.Novice) : base(smithingSkill, tier)
    {
    }

    public override void Execute()
    {
        if (_worker == null || _workplace == null) return;

        var movement = _worker.CharacterMovement;
        if (movement != null && !movement.HasPath)
        {
            Vector3 wanderTarget = _workplace.GetRandomPointInBuildingZone(_worker.transform.position.y);
            movement.SetDestination(wanderTarget);
        }
    }

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is ForgeBuilding;
    }

    /// <summary>
    /// The apprentice works slightly earlier to prepare the forge.
    /// </summary>
    public override System.Collections.Generic.List<ScheduleEntry> GetWorkSchedule()
    {
        return new System.Collections.Generic.List<ScheduleEntry>
        {
            new ScheduleEntry(7, 17, ScheduleActivity.Work, 10)
        };
    }
}
