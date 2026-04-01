using UnityEngine;

namespace MWI.AI
{
    public class BTAction_Combat : BTNode
    {
        private BattleManager _battleManager;
        private Character _currentTarget;
        private Collider _battleZone;

        // --- TACTICAL PACER ---
        private CombatAILogic _combatAILogic;

        protected override void OnEnter(Blackboard bb)
        {
            _combatAILogic = new CombatAILogic(bb.Self, autoDecideIntent: true);
            _combatAILogic.OnEnter();
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            _battleManager = bb.Get<BattleManager>(Blackboard.KEY_BATTLE_MANAGER);
            if (_battleManager == null) return BTNodeStatus.Failure;

            Character newTarget = bb.Get<Character>(Blackboard.KEY_COMBAT_TARGET);

            // If blackboard has no target yet, ask the coordinator for one
            if (newTarget == null && _battleManager.Coordinator != null)
            {
                newTarget = _battleManager.GetBestTargetFor(self);
                if (newTarget != null)
                    bb.Set(Blackboard.KEY_COMBAT_TARGET, newTarget);
            }

            if (newTarget != _currentTarget && newTarget != null)
            {
                _currentTarget = newTarget;

                if (self.CharacterVisual != null)
                {
                    self.CharacterVisual.SetLookTarget(_currentTarget);
                }
            }

            _combatAILogic.Tick(_currentTarget);

            return BTNodeStatus.Running;
        }

        protected override void OnExit(Blackboard bb)
        {
            Character self = bb.Self;
            if (self != null)
            {
                self.CharacterMovement?.ResetPath();
                self.CharacterMovement?.Resume();
                self.CharacterVisual?.ClearLookTarget();
            }
            _currentTarget = null;
        }
    }
}