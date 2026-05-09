using UnityEngine;
using UnityEngine.AI;

namespace MWI.AI
{
    /// <summary>
    /// Native combat action for the Behaviour Tree.
    /// Replaces "AttackTargetBehaviour". Handles movement toward the target
    /// and triggers attacks via CharacterCombat, taking range into account.
    /// </summary>
    public class BTAction_AttackTarget : BTNode
    {
        private Vector3 _lastTargetPos = Vector3.positiveInfinity;
        private float _lastRouteRequestTime = 0f;

        protected override void OnEnter(Blackboard bb)
        {
            _lastTargetPos = Vector3.positiveInfinity;
            _lastRouteRequestTime = 0f;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || !self.IsAlive() || self.CharacterCombat == null)
            {
                return BTNodeStatus.Failure;
            }

            // Retrieve the target from the Blackboard
            Character target = bb.Get<Character>(Blackboard.KEY_COMBAT_TARGET);

            // No target, or the target is dead/invalid, fail
            if (target == null || !target.IsAlive())
            {
                self.CharacterCombat.LeaveBattle();
                bb.Remove(Blackboard.KEY_COMBAT_TARGET);
                return BTNodeStatus.Failure;
            }

            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            float distance = Vector3.Distance(self.transform.position, target.transform.position);

            // TODO: Replace with self.CharacterCombat.AttackRange once implemented
            float attackRange = 1.5f;

            if (distance <= attackRange)
            {
                // In range: stop moving and attack
                movement.Stop();
                self.CharacterVisual?.FaceCharacter(target);

                // If the attack cooldown has elapsed, strike
                if (self.CharacterCombat.IsReadyToAct)
                {
                    self.CharacterCombat.Attack(target);
                }

                bb.Set(Blackboard.KEY_COMBAT_TARGET, target);
                return BTNodeStatus.Running; // Still in combat
            }
            else
            {
                // Not in range: close in while avoiding NavMesh spam
                bool hasPathFailed = (UnityEngine.Time.unscaledTime - _lastRouteRequestTime > 0.2f)
                                     && (movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid
                                     || (!movement.HasPath && !movement.PathPending));

                if (Vector3.Distance(_lastTargetPos, target.transform.position) > 1f || hasPathFailed)
                {
                    movement.SetDestination(target.transform.position);
                    _lastTargetPos = target.transform.position;
                    _lastRouteRequestTime = UnityEngine.Time.unscaledTime;
                }

                bb.Set(Blackboard.KEY_COMBAT_TARGET, target);
                return BTNodeStatus.Running; // Approaching
            }
        }

        protected override void OnExit(Blackboard bb)
        {
            bb.Self?.CharacterMovement?.Stop();
            bb.Remove(Blackboard.KEY_COMBAT_TARGET);
            _lastTargetPos = Vector3.positiveInfinity;
        }
    }
}
