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

            bool doLog = UnityEngine.Time.frameCount % 60 == 0; // Log once a second per character

            if (currentTarget == null)
            {
                if (doLog) Debug.Log($"<color=red>[CombatAI]</color> {_self.CharacterName} target is NULL! Stopping movement.");
                movement.Stop();
                return true; 
            }
            if (!currentTarget.IsAlive())
            {
                if (doLog) Debug.Log($"<color=red>[CombatAI]</color> {_self.CharacterName} target {currentTarget.CharacterName} is DEAD! Stopping movement.");
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
            // The AI acts exactly like a Player pressing the "Attack" button -> it queues the intent immediately.
            if (_autoDecideIntent && isReadyToDecide && !_self.CharacterCombat.HasPlannedAction)
            {
                if (doLog) Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 1] Auto-deciding action: Attack Intent locked!");
                _self.CharacterCombat.SetActionIntent(() => _self.CharacterCombat.Attack(), currentTarget);
            }

            // 2. EXECUTION PHASE (Player/NPC when an Intent is queued)
            if (_self.CharacterCombat != null && _self.CharacterCombat.HasPlannedAction)
            {
                _isChargingTarget = true;
                
                float optimalXDist = Mathf.Max(1.0f, attackRange * 0.8f);
                float dx = Mathf.Abs(_self.transform.position.x - currentTarget.transform.position.x);
                float zDist = Mathf.Abs(_self.transform.position.z - currentTarget.transform.position.z);
                
                bool isWithinRange = distToTarget <= attackRange; 
                bool isXTooClose = dx < (optimalXDist * 0.7f);
                bool isZAligned = zDist <= 0.6f;

                if (!isWithinRange || isXTooClose || !isZAligned)
                {
                    // Force movement into optimal valid strike position instead of target origin
                    float side = (_self.transform.position.x < currentTarget.transform.position.x) ? -1f : 1f;
                    
                    // Expanded stagger: 7 unique Z positions instead of 3 to prevent overlap
                    int staggerIndex = Mathf.Abs(_self.GetInstanceID()) % 7;
                    float staggeredZ = (staggerIndex - 3) * 0.5f; // -1.5 to 1.5
                    float staggeredX = Mathf.Abs(staggeredZ) * 0.2f; // Step back slightly on the X-axis to avoid visual clipping
                    
                    Vector3 optimalStrikePos = currentTarget.transform.position + new Vector3(side * (optimalXDist + staggeredX), 0, staggeredZ);

                    if (UnityEngine.Time.time - _lastPathUpdateTime > 0.3f && Vector3.Distance(movement.Destination, optimalStrikePos) > 0.5f)
                    {
                        if (doLog) Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 2] Moving into optimal strike pos: {optimalStrikePos}");
                        movement.SetDestination(optimalStrikePos);
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
                        if (doLog) Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 2] Executing Action!");
                        _self.CharacterCombat.ExecuteAction(_self.CharacterCombat.PlannedAction);
                        
                        // Only clear the intent automatically if this is an AI deciding its own actions.
                        // For the player, the intent remains queued until toggled off, creating an auto-attack loop!
                        if (_autoDecideIntent)
                        {
                            _self.CharacterCombat.ClearActionIntent();
                        }
                        return true;
                    }
                    else
                    {
                        if (doLog) Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 2] In position but waiting for full initiative (isReadyToAct).");
                    }
                }
            }
            else
            {
                // 3. CLEANUP & PACING FALLBACK (Only tactical movement when NO ACTION is planned)
                // Apply Tactics safely. Use _isChargingTarget to let the pacer know if we are aggressive or retreating.
                Vector3 finalPos = _combatPacer.GetTacticalDestination(currentTarget, attackRange, _isChargingTarget);
                
                if (UnityEngine.Time.time - _lastPathUpdateTime > 0.5f && Vector3.Distance(movement.Destination, finalPos) > 0.5f)
                {
                    if (doLog) Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 3] Dispatching to Tactical Dest: {finalPos}");
                    movement.SetDestination(finalPos);
                    _lastPathUpdateTime = UnityEngine.Time.time;
                }
                else
                {
                    if (doLog) Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 3] Not enough time passed to re-path, or already near destination. Distance: {Vector3.Distance(movement.Destination, finalPos)}");
                }
            }

            Vector3 dirToTarget = currentTarget.transform.position - _self.transform.position;
            _self.CharacterVisual?.UpdateFlip(dirToTarget);

            return true;
        }
    }
}
