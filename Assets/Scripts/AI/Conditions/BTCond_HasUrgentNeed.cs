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

            // Trouver le besoin le plus urgent
            var urgentNeed = needs.AllNeeds
                .Where(n => n.IsActive())
                .OrderByDescending(n => n.GetUrgency())
                .FirstOrDefault();

            if (urgentNeed == null) return BTNodeStatus.Failure;

            // Tenter de résoudre le besoin
            if (urgentNeed.Resolve(npc))
            {
                bb.Set(Blackboard.KEY_URGENT_NEED, urgentNeed);
                return BTNodeStatus.Success;
            }

            return BTNodeStatus.Failure;
        }
    }
}
