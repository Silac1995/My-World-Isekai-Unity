using UnityEngine;
using MWI.AI;

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
        if (_worker == null) return;

        var npcController = _worker.GetComponent<NPCController>();
        if (npcController != null && _worker.IsFree())
        {
            if (_workplace is CommercialBuilding cb)
            {
                // Priorité 1 : Ranger les objets qui traînent
                if (!npcController.HasBehaviour<StoreItemsBehaviour>() && !npcController.HasBehaviour<WanderBehaviour>())
                {
                    npcController.PushBehaviour(new StoreItemsBehaviour(npcController, cb));
                }
            }

            // L'apprenti flâne dans le bâtiment s'il n'a rien à faire
            if (_workplace != null && !npcController.HasBehaviour<WanderBehaviour>() && !npcController.HasBehaviour<StoreItemsBehaviour>())
            {
                npcController.PushBehaviour(new WanderBehaviour(npcController, _workplace));
            }
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
