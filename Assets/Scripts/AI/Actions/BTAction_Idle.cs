namespace MWI.AI
{
    /// <summary>
    /// Wrappe IdleBehaviour dans le BT.
    /// </summary>
    public class BTAction_Idle : BTActionNode
    {
        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            return new IdleBehaviour();
        }
    }
}
