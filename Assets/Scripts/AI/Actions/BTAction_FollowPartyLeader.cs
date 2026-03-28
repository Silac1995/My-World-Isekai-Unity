using UnityEngine;

namespace MWI.AI
{
    public class BTAction_FollowPartyLeader : BTNode
    {
        private const float FOLLOW_DISTANCE = 5f;
        private const float MIN_MEMBER_SPACING = 1.5f;

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            Character leader = bb.Get<Character>(Blackboard.KEY_PARTY_FOLLOW);
            if (leader == null || !leader.IsAlive()) return BTNodeStatus.Failure;

            // Calculate an offset position so members don't stack on the leader
            Vector3 targetPos = GetFollowPosition(self, leader);
            float distance = Vector3.Distance(self.transform.position, targetPos);

            if (distance <= MIN_MEMBER_SPACING)
            {
                self.CharacterMovement.Stop();
                return BTNodeStatus.Running;
            }

            self.CharacterMovement.SetDestination(targetPos);
            return BTNodeStatus.Running;
        }

        /// <summary>
        /// Returns an offset position behind/around the leader based on this member's index
        /// in the party, so multiple followers spread out instead of stacking.
        /// </summary>
        private Vector3 GetFollowPosition(Character self, Character leader)
        {
            CharacterParty leaderParty = leader.CharacterParty;
            if (leaderParty == null || leaderParty.PartyData == null)
                return leader.transform.position;

            int memberIndex = leaderParty.PartyData.MemberIds.IndexOf(self.CharacterId);
            if (memberIndex <= 0) memberIndex = 1; // Leader is 0, first follower is 1

            // Spread members in a semicircle behind the leader
            float angleStep = 45f; // degrees between members
            float startAngle = 180f; // directly behind
            float angle = startAngle + (memberIndex - 1) * angleStep - ((leaderParty.PartyData.MemberCount - 2) * angleStep * 0.5f);
            float rad = angle * Mathf.Deg2Rad;

            // Use leader's forward direction to define "behind"
            Vector3 leaderForward = leader.transform.forward;
            Vector3 leaderRight = leader.transform.right;

            Vector3 offset = (leaderForward * Mathf.Cos(rad) + leaderRight * Mathf.Sin(rad)) * FOLLOW_DISTANCE;
            return leader.transform.position + offset;
        }

        protected override void OnExit(Blackboard bb)
        {
            Character self = bb.Self;
            if (self != null && self.CharacterMovement != null)
                self.CharacterMovement.Stop();

            // Do NOT remove KEY_PARTY_FOLLOW here — CharacterParty owns that key.
        }
    }
}
