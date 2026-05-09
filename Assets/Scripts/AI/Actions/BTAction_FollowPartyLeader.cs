using UnityEngine;

namespace MWI.AI
{
    public class BTAction_FollowPartyLeader : BTNode
    {
        // --- Formation ---
        private const float FOLLOW_DISTANCE = 5f;
        private const int MAX_PER_ROW = 4;
        private const float ROW_SPACING = 3f;
        private const float LATERAL_SPACING = 2f;

        // --- Movement feel ---
        private const float STOP_DISTANCE = 1.5f;         // Close enough to target — stop
        private const float REPATH_DISTANCE = 2.5f;        // Only repath when target drifts this far from last destination
        private const float DIRECTION_SMOOTH_SPEED = 2f;
        private const float LEADER_MOVING_THRESHOLD = 0.3f;

        // --- Reaction delay (stagger per follower) ---
        private const float BASE_REACTION_DELAY = 0.3f;    // First follower waits this long
        private const float REACTION_DELAY_PER_SLOT = 0.15f;// Each subsequent follower adds this

        // --- State ---
        private Vector3 _smoothedLeaderDir;
        private Vector3 _lastDestination;
        private float _reactionTimer;
        private bool _initialized;
        private bool _reacting;
        private int _cachedSlot = -1;

        protected override void OnEnter(Blackboard bb)
        {
            _initialized = false;
            _reacting = false;
            _reactionTimer = 0f;
            _lastDestination = Vector3.zero;
            _cachedSlot = -1;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            Character leader = bb.Get<Character>(Blackboard.KEY_PARTY_FOLLOW);
            if (leader == null || !leader.IsAlive()) return BTNodeStatus.Failure;

            UpdateSmoothedDirection(self, leader);

            // Cache the slot index once (avoids IndexOf every tick)
            if (_cachedSlot < 0)
            {
                CharacterParty leaderParty = leader.CharacterParty;
                if (leaderParty?.PartyData != null)
                {
                    int idx = leaderParty.PartyData.MemberIds.IndexOf(self.CharacterId);
                    _cachedSlot = idx > 0 ? idx - 1 : 0;
                }
                else
                {
                    _cachedSlot = 0;
                }
            }

            Vector3 targetPos = GetFollowPosition(self, leader);
            float distToTarget = Vector3.Distance(self.transform.position, targetPos);

            // --- Close enough: idle ---
            if (distToTarget <= STOP_DISTANCE)
            {
                self.CharacterMovement.Stop();
                _reacting = false;
                return BTNodeStatus.Running;
            }

            // --- Reaction delay: stagger start of movement ---
            // When the leader starts moving and we're not yet reacting, wait a bit.
            // Later slots wait longer, so followers peel off in sequence.
            if (!_reacting)
            {
                float myDelay = BASE_REACTION_DELAY + _cachedSlot * REACTION_DELAY_PER_SLOT;
                _reactionTimer += UnityEngine.Time.deltaTime;
                if (_reactionTimer < myDelay)
                {
                    return BTNodeStatus.Running; // Wait before moving
                }
                _reacting = true;
            }

            // --- Dead zone: only repath when target has drifted significantly ---
            float driftFromLastDest = Vector3.Distance(targetPos, _lastDestination);
            if (driftFromLastDest > REPATH_DISTANCE || _lastDestination == Vector3.zero)
            {
                self.CharacterMovement.SetDestination(targetPos);
                _lastDestination = targetPos;
            }

            // --- Reset reaction timer when leader stops (so next move triggers a new stagger) ---
            Vector3 leaderVel = leader.CharacterMovement != null ? leader.CharacterMovement.Velocity : Vector3.zero;
            Vector3 flatVel = new Vector3(leaderVel.x, 0f, leaderVel.z);
            if (flatVel.sqrMagnitude < LEADER_MOVING_THRESHOLD * LEADER_MOVING_THRESHOLD && distToTarget <= STOP_DISTANCE + 1f)
            {
                _reacting = false;
                _reactionTimer = 0f;
            }

            return BTNodeStatus.Running;
        }

        // =========================================================
        //  DIRECTION SMOOTHING
        // =========================================================

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
                    _smoothedLeaderDir = flatVel.normalized;
                }
                else
                {
                    Vector3 toLeader = leader.transform.position - self.transform.position;
                    toLeader.y = 0f;
                    _smoothedLeaderDir = toLeader.sqrMagnitude > 0.01f
                        ? toLeader.normalized
                        : Vector3.forward;
                }
                _initialized = true;
                return;
            }

            if (leaderIsMoving)
            {
                _smoothedLeaderDir = Vector3.Slerp(
                    _smoothedLeaderDir, flatVel.normalized,
                    UnityEngine.Time.deltaTime * DIRECTION_SMOOTH_SPEED
                ).normalized;
            }
        }

        // =========================================================
        //  FORMATION POSITION
        // =========================================================

        private Vector3 GetFollowPosition(Character self, Character leader)
        {
            CharacterParty leaderParty = leader.CharacterParty;
            if (leaderParty == null || leaderParty.PartyData == null)
                return leader.transform.position;

            int slot = _cachedSlot >= 0 ? _cachedSlot : 0;

            int row = slot / MAX_PER_ROW;
            int col = slot % MAX_PER_ROW;
            int totalFollowers = leaderParty.PartyData.MemberCount - 1;
            int membersInThisRow = Mathf.Min(MAX_PER_ROW, totalFollowers - row * MAX_PER_ROW);

            Vector3 behind = -_smoothedLeaderDir;
            Vector3 right = Vector3.Cross(Vector3.up, behind).normalized;

            float halfRowWidth = (membersInThisRow - 1) * 0.5f;
            float lateralOffset = (col - halfRowWidth) * LATERAL_SPACING;
            float depthOffset = FOLLOW_DISTANCE + row * ROW_SPACING;

            Vector3 offset = behind * depthOffset + right * lateralOffset;
            return leader.transform.position + offset;
        }

        // =========================================================
        //  CLEANUP
        // =========================================================

        protected override void OnExit(Blackboard bb)
        {
            Character self = bb.Self;
            if (self != null && self.CharacterMovement != null)
                self.CharacterMovement.Stop();

            _initialized = false;
            _reacting = false;
            _reactionTimer = 0f;
            _cachedSlot = -1;
            _lastDestination = Vector3.zero;
        }
    }
}
