namespace MWI.AI
{
    public class BTCond_IsInPartyFollow : BTNode
    {
        private BTAction_FollowPartyLeader _followAction = new BTAction_FollowPartyLeader();

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            Character leader = bb.Get<Character>(Blackboard.KEY_PARTY_FOLLOW);
            if (leader == null || !leader.IsAlive()) return BTNodeStatus.Failure;

            return _followAction.Execute(bb);
        }

        protected override void OnExit(Blackboard bb)
        {
            _followAction.Abort(bb);
        }
    }
}
