using UnityEngine;

namespace MWI.AI
{
    public class BTAction_FollowPartyLeader : BTNode
    {
        private const float FOLLOW_DISTANCE = 5f;
        private const float MIN_MEMBER_SPACING = 1.5f;
        private const float DIRECTION_SMOOTH_SPEED = 2f;
        private const float LEADER_MOVING_THRESHOLD = 0.3f;

        private Vector3 _smoothedLeaderDir;
        private bool _initialized = false;

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            Character leader = bb.Get<Character>(Blackboard.KEY_PARTY_FOLLOW);
            if (leader == null || !leader.IsAlive()) return BTNodeStatus.Failure;

            UpdateSmoothedDirection(self, leader);

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

        private void UpdateSmoothedDirection(Character self, Character leader)
        {
            Vector3 leaderVelocity = leader.CharacterMovement != null
                ? leader.CharacterMovement.Velocity
                : Vector3.zero;

            Vector3 flatVel = new Vector3(leaderVelocity.x, 0f, leaderVelocity.z);
            bool leaderIsMoving = flatVel.sqrMagnitude > LEADER_MOVING_THRESHOLD * LEADER_MOVING_THRESHOLD;

            if (!_initialized)
            {
                if (leaderIsMoving)
                {
                    // Leader is moving — initialize from their travel direction
                    _smoothedLeaderDir = flatVel.normalized;
                }
                else
                {
                    // Leader is stationary — use direction from follower toward leader
                    // so the follower just approaches naturally
                    Vector3 toLeader = leader.transform.position - self.transform.position;
                    toLeader.y = 0f;
                    _smoothedLeaderDir = toLeader.sqrMagnitude > 0.01f
                        ? toLeader.normalized
                        : Vector3.forward;
                }
                _initialized = true;
                return;
            }

            // Only update formation direction when the leader is actively moving.
            // When stationary, keep the current formation direction frozen.
            if (leaderIsMoving)
            {
                _smoothedLeaderDir = Vector3.Slerp(
                    _smoothedLeaderDir, flatVel.normalized,
                    UnityEngine.Time.deltaTime * DIRECTION_SMOOTH_SPEED
                ).normalized;
            }
        }

        private const int MAX_PER_ROW = 4;
        private const float ROW_SPACING = 3f;
        private const float LATERAL_SPACING = 2f;

        private Vector3 GetFollowPosition(Character self, Character leader)
        {
            CharacterParty leaderParty = leader.CharacterParty;
            if (leaderParty == null || leaderParty.PartyData == null)
                return leader.transform.position;

            int memberIndex = leaderParty.PartyData.MemberIds.IndexOf(self.CharacterId);
            if (memberIndex <= 0) memberIndex = 1;

            int slot = memberIndex - 1; // 0-based among followers

            // Determine which row and column this follower belongs to
            // Row 0 = closest to leader, row 1 = further back, etc.
            int row = slot / MAX_PER_ROW;
            int col = slot % MAX_PER_ROW;
            int membersInThisRow = Mathf.Min(MAX_PER_ROW, (leaderParty.PartyData.MemberCount - 1) - row * MAX_PER_ROW);

            Vector3 behind = -_smoothedLeaderDir;
            Vector3 right = Vector3.Cross(Vector3.up, behind).normalized;

            // Center the row laterally
            float halfRowWidth = (membersInThisRow - 1) * 0.5f;
            float lateralOffset = (col - halfRowWidth) * LATERAL_SPACING;
            float depthOffset = FOLLOW_DISTANCE + row * ROW_SPACING;

            Vector3 offset = behind * depthOffset + right * lateralOffset;
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
