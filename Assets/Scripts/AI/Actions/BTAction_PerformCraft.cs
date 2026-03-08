using UnityEngine;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Node du Behaviour Tree pour un NPC ayant un JobCrafter.
    /// </summary>
    public class BTAction_PerformCraft : BTActionNode
    {
        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || self.CharacterJob == null) return null;

            // Vérifier qu'il a bien un métier de type JobCrafter
            JobCrafter crafterJob = null;
            foreach (var jobAssign in self.CharacterJob.ActiveJobs)
            {
                if (jobAssign.AssignedJob is JobCrafter jc)
                {
                    crafterJob = jc;
                    break;
                }
            }

            if (crafterJob == null) return null;

            NPCController npc = self.Controller as NPCController;
            if (npc == null) return null;

            return new PerformCraftBehaviour(npc, crafterJob);
        }
    }
}
