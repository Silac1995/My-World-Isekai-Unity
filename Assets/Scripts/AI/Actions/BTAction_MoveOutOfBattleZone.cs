using UnityEngine;
using UnityEngine.AI;

namespace MWI.AI
{
    /// <summary>
    /// Native Behaviour Tree action to flee a combat zone.
    /// Replaces the wrapper of the old MoveOutOfBattleZoneBehaviour.
    /// </summary>
    public class BTAction_MoveOutOfBattleZone : BTNode
    {
        private float _fleeDuration = 2.5f;
        private float _endTime = 0f;
        private bool _isMoving = false;

        protected override void OnEnter(Blackboard bb)
        {
            _endTime = UnityEngine.Time.time + _fleeDuration;
            _isMoving = false;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || !self.IsAlive()) return BTNodeStatus.Failure;

            BattleManager bm = bb.Get<BattleManager>(Blackboard.KEY_FLEE_BATTLE_MANAGER);
            if (bm == null) return BTNodeStatus.Failure;

            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            if (UnityEngine.Time.time >= _endTime)
            {
                movement.Stop();
                bb.Remove(Blackboard.KEY_FLEE_BATTLE_MANAGER);
                return BTNodeStatus.Success;
            }

            if (!_isMoving)
            {
                Vector3 directionAway = (self.transform.position - bm.transform.position).normalized;
                Vector3 fallbackPos = self.transform.position + directionAway * 6f;

                if (NavMesh.SamplePosition(fallbackPos, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                {
                    movement.SetDestination(hit.position);
                    _isMoving = true;
                }
                else
                {
                    // Could not find a path; finish early
                    movement.Stop();
                    bb.Remove(Blackboard.KEY_FLEE_BATTLE_MANAGER);
                    return BTNodeStatus.Success;
                }
            }

            return BTNodeStatus.Running; // Still fleeing
        }

        protected override void OnExit(Blackboard bb)
        {
            bb.Self?.CharacterMovement?.Stop();
            bb.Remove(Blackboard.KEY_FLEE_BATTLE_MANAGER);
            _isMoving = false;
        }
    }
}
