namespace MWI.AI
{
    /// <summary>
    /// Wrappe FollowTargetBehaviour dans le BT.
    /// </summary>
    public class BTAction_Follow : BTActionNode
    {
        private Character _target;
        private float _followDistance;

        public BTAction_Follow(Character target, float followDistance = 3f)
        {
            _target = target;
            _followDistance = followDistance;
        }

        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            if (_target == null) return null;
            return new FollowTargetBehaviour(_target, _followDistance);
        }
    }
}
