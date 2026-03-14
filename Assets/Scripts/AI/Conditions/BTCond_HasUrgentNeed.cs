using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Condition : le NPC a un besoin urgent à résoudre (social, vêtements, faim...).
    /// Délègue à CharacterNeeds pour évaluer et résoudre le besoin le plus urgent.
    /// </summary>
    public class BTCond_HasUrgentNeed : BTNode
    {
        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            CharacterNeeds needs = self.CharacterNeeds;
            if (needs == null) return BTNodeStatus.Failure;

            NPCController npc = self.Controller as NPCController;
            if (npc == null) return BTNodeStatus.Failure;

            // Trouver les besoins actifs triés par urgence
            var activeNeeds = needs.AllNeeds
                .Where(n => n.IsActive())
                .OrderByDescending(n => n.GetUrgency())
                .ToList();

            if (activeNeeds.Count == 0) return BTNodeStatus.Failure;

            // Tenter de résoudre le besoin
            // SÉCURITÉ : On ne résout un besoin que si le NPC est "libre" (Wander ou Idle).
            // Si une pile de comportements spécialisés est déjà là, on attend qu'elle finisse.
            if (!(npc.CurrentBehaviour is WanderBehaviour) && npc.CurrentBehaviour != null)
            {
                return BTNodeStatus.Failure;
            }

            foreach (var need in activeNeeds)
            {
                if (need.Resolve(npc))
                {
                    bb.Set(Blackboard.KEY_URGENT_NEED, need);
                    return BTNodeStatus.Success;
                }
            }

            return BTNodeStatus.Failure;
        }
    }
}
