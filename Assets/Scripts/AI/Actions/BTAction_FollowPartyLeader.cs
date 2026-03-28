using UnityEngine;

namespace MWI.AI
{
    public class BTAction_FollowPartyLeader : BTNode
    {
        private const float FOLLOW_DISTANCE = 5f;
        private const float MIN_MEMBER_SPACING = 1.5f;

        // Cache the leader's last known travel direction so followers
        // keep their "behind" position even when the leader stops.
        private Vector3 _lastLeaderDirection = Vector3.back;

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            Character leader = bb.Get<Character>(Blackboard.KEY_PARTY_FOLLOW);
            if (leader == null || !leader.IsAlive()) return BTNodeStatus.Failure;

            // Update cached direction from leader's actual movement velocity
            UpdateLeaderDirection(leader);

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

        private void UpdateLeaderDirection(Character leader)
        {
            if (leader.CharacterMovement == null) return;

            Vector3 velocity = leader.CharacterMovement.Velocity;
            // Only update if the leader is actually moving (ignore tiny drift)
            // Flatten to XZ plane since this is a 2D-sprites-in-3D project
            Vector3 flatVel = new Vector3(velocity.x, 0f, velocity.z);
            if (flatVel.sqrMagnitude > 0.1f)
            {
                _lastLeaderDirection = flatVel.normalized;
            }
        }

        /// <summary>
        /// Returns an offset position behind the leader based on the leader's
        /// actual travel direction and this member's index in the party.
        /// </summary>
        private Vector3 GetFollowPosition(Character self, Character leader)
        {
            CharacterParty leaderParty = leader.CharacterParty;
            if (leaderParty == null || leaderParty.PartyData == null)
                return leader.transform.position;

            int memberIndex = leaderParty.PartyData.MemberIds.IndexOf(self.CharacterId);
            if (memberIndex <= 0) memberIndex = 1;

            // "Behind" = opposite of the leader's travel direction
            Vector3 behind = -_lastLeaderDirection;
            // Perpendicular axis for spreading members side to side
            Vector3 right = Vector3.Cross(Vector3.up, behind).normalized;

            // Spread: first follower directly behind, others offset to the sides
            int followersCount = leaderParty.PartyData.MemberCount - 1;
            int slot = memberIndex - 1; // 0-based slot among followers
            float halfSpread = (followersCount - 1) * 0.5f;
            float lateralOffset = (slot - halfSpread) * 2f; // 2m spacing between followers

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
