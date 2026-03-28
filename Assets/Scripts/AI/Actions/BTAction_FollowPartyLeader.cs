using UnityEngine;

namespace MWI.AI
{
    public class BTAction_FollowPartyLeader : BTNode
    {
        private const float FOLLOW_DISTANCE = 5f;
        private const float MIN_MEMBER_SPACING = 1.5f;
        private const float DIRECTION_SMOOTH_SPEED = 2f;

        // Smoothed direction used for positioning — prevents followers from
        // criss-crossing the leader on quick direction reversals.
        private Vector3 _smoothedLeaderDir = Vector3.forward;
        private bool _initialized = false;

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            Character leader = bb.Get<Character>(Blackboard.KEY_PARTY_FOLLOW);
            if (leader == null || !leader.IsAlive()) return BTNodeStatus.Failure;

            UpdateSmoothedDirection(leader);

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

        private void UpdateSmoothedDirection(Character leader)
        {
            Vector3 rawDir = leader.CharacterMovement != null
                ? leader.CharacterMovement.LastMoveDirection
                : Vector3.forward;

            if (!_initialized)
            {
                _smoothedLeaderDir = rawDir;
                _initialized = true;
                return;
            }

            // Slowly lerp toward the leader's current direction.
            // This means quick reversals take ~1s to fully propagate,
            // so followers drift smoothly instead of snapping across.
            _smoothedLeaderDir = Vector3.Slerp(
                _smoothedLeaderDir, rawDir,
                Time.deltaTime * DIRECTION_SMOOTH_SPEED
            ).normalized;
        }

        private Vector3 GetFollowPosition(Character self, Character leader)
        {
            CharacterParty leaderParty = leader.CharacterParty;
            if (leaderParty == null || leaderParty.PartyData == null)
                return leader.transform.position;

            int memberIndex = leaderParty.PartyData.MemberIds.IndexOf(self.CharacterId);
            if (memberIndex <= 0) memberIndex = 1;

            Vector3 behind = -_smoothedLeaderDir;
            Vector3 right = Vector3.Cross(Vector3.up, behind).normalized;

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

            _initialized = false;
        }
    }
}
