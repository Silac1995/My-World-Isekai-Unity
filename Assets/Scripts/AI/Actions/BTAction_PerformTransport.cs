using UnityEngine;
using MWI.AI;

namespace MWI.AI
{
    /// <summary>
    /// Node du Behaviour Tree pour un NPC ayant le JobTransporter et une commande en cours.
    /// Renvoie Running tant que la livraison d'un "lot" n'est pas terminée.
    /// Renvoie Success si un lot a été livré.
    /// Renvoie Failure s'il n'y a pas de commande ou de JobTransporter.
    /// </summary>
    public class BTAction_PerformTransport : BTActionNode
    {
        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || self.CharacterJob == null) return null;

            // On vérifie qu'il a bien un métier de transporteur
            JobTransporter transporterJob = null;
            foreach (var jobAssign in self.CharacterJob.ActiveJobs)
            {
                if (jobAssign.AssignedJob is JobTransporter jt)
                {
                    transporterJob = jt;
                    break;
                }
            }

            if (transporterJob == null || transporterJob.CurrentOrder == null)
            {
                // Pas de commande, on échoue (le BT passera à autre chose, ex: Wander)
                return null;
            }

            NPCController npc = self.Controller as NPCController;
            if (npc == null) return null;

            return new PerformTransportBehaviour(npc, transporterJob);
        }
    }
}
