namespace MWI.AI
{
    /// <summary>
    /// Wrappe AttackTargetBehaviour dans le BT.
    /// </summary>
    public class BTAction_Attack : BTActionNode
    {
        private Character _target;

        public BTAction_Attack(Character target)
        {
            _target = target;
        }

        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            if (_target == null) return null;
            return new AttackTargetBehaviour(_target);
        }
    }
}
