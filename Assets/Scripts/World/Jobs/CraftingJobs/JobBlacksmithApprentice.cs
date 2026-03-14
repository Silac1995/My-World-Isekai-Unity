using UnityEngine;

/// <summary>
/// Job d'Apprenti Forgeron : assiste le forgeron principal.
/// Prépare les matériaux, entretient le feu, apprend le métier.
/// </summary>
public class JobBlacksmithApprentice : JobCrafter
{
    public override string JobTitle => "Apprenti Forgeron";

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
    /// L'apprenti travaille un peu plus tôt pour préparer la forge.
    /// </summary>
    public override System.Collections.Generic.List<ScheduleEntry> GetWorkSchedule()
    {
        return new System.Collections.Generic.List<ScheduleEntry>
        {
            new ScheduleEntry(7, 17, ScheduleActivity.Work, 10)
        };
    }
}
