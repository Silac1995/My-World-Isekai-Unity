using UnityEngine;

namespace MWI.AI
{
    public class BTAction_FollowPartyLeader : BTNode
    {
        private const float FOLLOW_DISTANCE = 3f;

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            Character leader = bb.Get<Character>(Blackboard.KEY_PARTY_FOLLOW);
            if (leader == null || !leader.IsAlive()) return BTNodeStatus.Failure;

            float distance = Vector3.Distance(self.transform.position, leader.transform.position);

            if (distance <= FOLLOW_DISTANCE)
            {
                self.CharacterMovement.Stop();
                return BTNodeStatus.Running;
            }

            if (distance > FOLLOW_DISTANCE)
            {
                self.CharacterMovement.SetDestination(leader.transform.position);
            }

            return BTNodeStatus.Running;
        }

        protected override void OnExit(Blackboard bb)
        {
            Character self = bb.Self;
            if (self != null && self.CharacterMovement != null)
                self.CharacterMovement.Stop();

            // Do NOT remove KEY_PARTY_FOLLOW here — CharacterParty owns that key.
            // Removing it would prevent the node from activating again on the next tick
            // after being preempted by a higher-priority node (combat, etc.).
        }
    }
}
