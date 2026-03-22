using UnityEngine;
using System;

namespace MWI.AI
{
    public class CombatAILogic
    {
        private Character _self;
        private CombatTacticalPacer _combatPacer;
        
        private float _readyStartTime;
        private bool _isChargingTarget;
        private bool _autoDecideIntent;
        private float _lastPathUpdateTime;

        public CombatAILogic(Character self, bool autoDecideIntent)
        {
            _self = self;
            _autoDecideIntent = autoDecideIntent;
            _combatPacer = new CombatTacticalPacer(self);
        }

        public void OnEnter()
        {
            _readyStartTime = 0f;
            _isChargingTarget = false;
        }

        public bool Tick(Character currentTarget)
        {
            if (_self == null) return false;

            var movement = _self.CharacterMovement;
            if (movement == null) return false;

            if (currentTarget == null || !currentTarget.IsAlive())
            {
                movement.Stop();
                return true; 
            }

            movement.Resume();
            float distToTarget = Vector3.Distance(_self.transform.position, currentTarget.transform.position);
            float attackRange = _self.CharacterCombat?.CurrentCombatStyleExpertise?.Style?.MeleeRange ?? 3.5f;

            bool isReadyToAct = _self.CharacterCombat != null && _self.CharacterCombat.IsReadyToAct;
            bool isReadyToDecide = _self.Stats != null && _self.Stats.Initiative != null && _self.Stats.Initiative.IsReadyToDecide();

            _isChargingTarget = false;
            
            // 1. AUTO-DECIDE INTENT (NPC at 70%)
            if (_autoDecideIntent && isReadyToDecide && !_self.CharacterCombat.HasPlannedAction)
            {
                if (_readyStartTime <= 0) _readyStartTime = UnityEngine.Time.time;
                float timeReady = UnityEngine.Time.time - _readyStartTime;

                float dx = Mathf.Abs(_self.transform.position.x - currentTarget.transform.position.x);
                float zDist = Mathf.Abs(_self.transform.position.z - currentTarget.transform.position.z);
                bool isWithinRange = distToTarget <= (attackRange * 0.9f);
                bool isXTooClose = dx < 1.5f;
                bool isZAligned = zDist <= 1.5f;

                if (!isWithinRange || isXTooClose || !isZAligned)
                {
                    _isChargingTarget = !isXTooClose;
                }
                else
                {
                    movement.Stop();
                }

                bool targetIsStationary = currentTarget.CharacterMovement != null && currentTarget.CharacterMovement.GetVelocity().sqrMagnitude < 0.01f;
                bool forcedStrike = timeReady > 3.0f;
                
                bool targetIsAggressiveTowardsUs = false;
                if (currentTarget.Controller is NPCController targetNpc && targetNpc.HasBehaviourTree && targetNpc.BehaviourTree.Blackboard != null)
                {
                    Character itsTarget = targetNpc.BehaviourTree.Blackboard.Get<Character>(Blackboard.KEY_COMBAT_TARGET);
                    if (itsTarget == _self) targetIsAggressiveTowardsUs = true;
                }
                bool patienceThresholdMet = timeReady > 1.5f || targetIsAggressiveTowardsUs || forcedStrike;

                if (distToTarget <= attackRange && zDist <= 1.5f && (targetIsStationary || patienceThresholdMet))
                {
                    movement.Stop();
                    _self.CharacterVisual?.FaceTarget(currentTarget.transform.position);
                    
                    // Lock in the intent automatically!
                    _self.CharacterCombat.SetActionIntent(() => _self.CharacterCombat.Attack(), currentTarget);
                }
            }

            // 2. EXECUTION PHASE (Player/NPC when an Intent is queued)
            if (_self.CharacterCombat != null && _self.CharacterCombat.HasPlannedAction)
            {
                float dx = Mathf.Abs(_self.transform.position.x - currentTarget.transform.position.x);
                float zDist = Mathf.Abs(_self.transform.position.z - currentTarget.transform.position.z);
                bool isWithinRange = distToTarget <= (attackRange * 0.9f); // Slightly inner range to ensure solid hit
                bool isXTooClose = dx < 1.5f;
                bool isZAligned = zDist <= 1.5f;

                if (!isWithinRange || isXTooClose || !isZAligned)
                {
                    // Force movement into valid strike position
                    if (UnityEngine.Time.time - _lastPathUpdateTime > 0.3f && Vector3.Distance(movement.Destination, currentTarget.transform.position) > 0.5f)
                    {
                        movement.SetDestination(currentTarget.transform.position);
                        _lastPathUpdateTime = UnityEngine.Time.time;
                    }
                }
                else
                {
                    // Perfectly positioned, stop and charge the action
                    movement.Stop();
                    _self.CharacterVisual?.FaceTarget(currentTarget.transform.position);

                    if (isReadyToAct)
                    {
                        _self.CharacterCombat.ExecuteAction(_self.CharacterCombat.PlannedAction);
                        _self.CharacterCombat.ClearActionIntent();
                        _readyStartTime = 0;
                        return true;
                    }
                }
            }
            else
            {
                // 3. CLEANUP & PACING FALLBACK (Only tactical movement when NO ACTION is planned)
                if (!isReadyToDecide || !_autoDecideIntent)
                {
                    _readyStartTime = 0;
                }

                // Apply Tactics safely
                Vector3 finalPos = _combatPacer.GetTacticalDestination(currentTarget, attackRange, false);
                
                if (UnityEngine.Time.time - _lastPathUpdateTime > 0.5f && Vector3.Distance(movement.Destination, finalPos) > 0.5f)
                {
                    movement.SetDestination(finalPos);
                    _lastPathUpdateTime = UnityEngine.Time.time;
                }
            }

            Vector3 dirToTarget = currentTarget.transform.position - _self.transform.position;
            _self.CharacterVisual?.UpdateFlip(dirToTarget);

            return true;
        }
    }
}
