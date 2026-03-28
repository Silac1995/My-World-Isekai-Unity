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
        /// Returns an offset position behind the leader based on the leader's
        /// LastMoveDirection and this member's index in the party.
        /// </summary>
        private Vector3 GetFollowPosition(Character self, Character leader)
        {
            CharacterParty leaderParty = leader.CharacterParty;
            if (leaderParty == null || leaderParty.PartyData == null)
                return leader.transform.position;

            int memberIndex = leaderParty.PartyData.MemberIds.IndexOf(self.CharacterId);
            if (memberIndex <= 0) memberIndex = 1;

            // "Behind" = opposite of the leader's travel direction (from CharacterMovement)
            Vector3 leaderDir = leader.CharacterMovement != null
                ? leader.CharacterMovement.LastMoveDirection
                : Vector3.forward;

            Vector3 behind = -leaderDir;
            Vector3 right = Vector3.Cross(Vector3.up, behind).normalized;

            // Spread followers laterally: first one directly behind, others offset to the sides
            int followersCount = leaderParty.PartyData.MemberCount - 1;
            int slot = memberIndex - 1;
            float halfSpread = (followersCount - 1) * 0.5f;
            float lateralOffset = (slot - halfSpread) * 2f;

            Vector3 offset = behind * FOLLOW_DISTANCE + right * lateralOffset;
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
