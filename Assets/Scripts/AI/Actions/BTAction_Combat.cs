using UnityEngine;

namespace MWI.AI
{
    public class BTAction_Combat : BTNode
    {
        private BattleManager _battleManager;
        private Character _currentTarget;
        private Collider _battleZone;

        // Aesthetic & Natural Movement
        private float _readyStartTime;
        private bool _isChargingTarget = false; 
        
        // --- TACTICAL PACER ---
        private CombatTacticalPacer _combatPacer;

        protected override void OnEnter(Blackboard bb)
        {
            _readyStartTime = 0f;
            _isChargingTarget = false;
            _combatPacer = new CombatTacticalPacer(bb.Self);
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            _battleManager = bb.Get<BattleManager>(Blackboard.KEY_BATTLE_MANAGER);
            if (_battleManager == null) return BTNodeStatus.Failure;

            Character newTarget = bb.Get<Character>(Blackboard.KEY_COMBAT_TARGET);
            
            // Si la cible a change via le BT (BTCond_IsInCombat)
            if (newTarget != _currentTarget && newTarget != null)
            {
                _currentTarget = newTarget;
                
                if (self.CharacterVisual != null)
                {
                    self.CharacterVisual.SetLookTarget(_currentTarget);
                }
            }

            _isChargingTarget = false;
            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            if (_currentTarget == null || !_currentTarget.IsAlive())
            {
                self.CharacterMovement?.Stop();
                return BTNodeStatus.Running; // En attente du BT pour changer la cible
            }

            movement.Resume();
            float distToTarget = Vector3.Distance(self.transform.position, _currentTarget.transform.position);
            float attackRange = self.CharacterCombat?.CurrentCombatStyleExpertise?.Style?.MeleeRange ?? 3.5f;

            bool isReadyToAct = self.CharacterCombat != null && self.CharacterCombat.IsReadyToAct;
            bool isReadyToDecide = self.Stats != null && self.Stats.Initiative != null && self.Stats.Initiative.IsReadyToDecide();
            
            if (isReadyToDecide)
            {
                if (_readyStartTime <= 0) _readyStartTime = UnityEngine.Time.time;
                float timeReady = UnityEngine.Time.time - _readyStartTime;

                float dx = Mathf.Abs(self.transform.position.x - _currentTarget.transform.position.x);
                float zDist = Mathf.Abs(self.transform.position.z - _currentTarget.transform.position.z);
                bool isWithinRange = distToTarget <= (attackRange * 0.9f);
                bool isXTooClose = dx < 1.5f; // X_FLIP_SAFETY replacement local
                bool isZAligned = zDist <= 1.5f;

                if (!self.CharacterCombat.HasPlannedAction)
                {
                    if (!isWithinRange || isXTooClose || !isZAligned)
                    {
                        _isChargingTarget = !isXTooClose;
                    }
                    else
                    {
                        movement.Stop();
                    }

                    bool targetIsStationary = _currentTarget.CharacterMovement != null && _currentTarget.CharacterMovement.GetVelocity().sqrMagnitude < 0.01f;
                    bool forcedStrike = timeReady > 3.0f;
                    
                    bool targetIsAggressiveTowardsUs = false;
                    if (_currentTarget.Controller is NPCController targetNpc && targetNpc.HasBehaviourTree && targetNpc.BehaviourTree.Blackboard != null)
                    {
                        Character itsTarget = targetNpc.BehaviourTree.Blackboard.Get<Character>(Blackboard.KEY_COMBAT_TARGET);
                        if (itsTarget == self) targetIsAggressiveTowardsUs = true;
                    }
                    bool patienceThresholdMet = timeReady > 1.5f || targetIsAggressiveTowardsUs || forcedStrike;

                    if (distToTarget <= attackRange && zDist <= 1.5f && (targetIsStationary || patienceThresholdMet))
                    {
                        movement.Stop();
                        self.CharacterVisual?.FaceTarget(_currentTarget.transform.position);
                        
                        // Lock in the intent
                        self.CharacterCombat.SetActionIntent(() => self.CharacterCombat.Attack(), _currentTarget);
                    }
                }
                else
                {
                    // Action already planned at 70%, stand and wait for 100%
                    movement.Stop();
                    self.CharacterVisual?.FaceTarget(_currentTarget.transform.position);

                    if (isReadyToAct)
                    {
                        self.CharacterCombat.ExecuteAction(self.CharacterCombat.PlannedAction);
                        self.CharacterCombat.ClearActionIntent();
                        
                        _readyStartTime = 0;
                        return BTNodeStatus.Running;
                    }
                }
            }
            else
            {
                _readyStartTime = 0;
                
                if (self.CharacterCombat != null && self.CharacterCombat.HasPlannedAction)
                {
                    self.CharacterCombat.ClearActionIntent();
                }
            }

            Vector3 finalPos = _combatPacer.GetTacticalDestination(_currentTarget, attackRange, _isChargingTarget);
            
            if (Vector3.Distance(movement.Destination, finalPos) > 0.5f)
            {
                movement.SetDestination(finalPos);
            }

            Vector3 dirToTarget = _currentTarget.transform.position - self.transform.position;
            self.CharacterVisual?.UpdateFlip(dirToTarget);

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