namespace MWI.AI
{
    /// <summary>
    /// Wrappe WorkBehaviour dans le BT.
    /// </summary>
    public class BTAction_Work : BTActionNode
    {
        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            Character self = bb.Self;
            NPCController npc = self?.Controller as NPCController;
            if (npc == null) return null;
            return new WorkBehaviour(npc);
        }
    }
}
