namespace MWI.AI
{
    /// <summary>
    /// Wrappe WanderBehaviour dans le BT.
    /// Retourne toujours Running (le wander est un fallback infini).
    /// </summary>
    public class BTAction_Wander : BTActionNode
    {
        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            Character self = bb.Self;
            NPCController npc = self?.Controller as NPCController;
            if (npc == null) return null;
            return new WanderBehaviour(npc);
        }
    }
}
