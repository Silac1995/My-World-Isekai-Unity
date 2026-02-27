namespace MWI.AI
{
    /// <summary>
    /// Action Node for NodeCanvas Behavior Tree that tells a Student to attend their mentor's class if they are teaching.
    /// </summary>
    public class BTAction_AttendClass : BTActionNode
    {
        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            if (bb == null || bb.Self == null) return null;
            NPCController npc = bb.Self.GetComponent<NPCController>();
            if (npc != null)
            {
                return new AttendClassBehaviour(npc);
            }
            return null;
        }
    }
}
