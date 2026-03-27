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
            if (_self.IsServer && _autoDecideIntent && isReadyToDecide && !_self.CharacterCombat.HasPlannedAction)
            {
                Func<bool> chosenAction = DecideAbilityOrAttack(currentTarget);
                if (doLog) Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 1] Auto-deciding action: Intent locked!");
                _self.CharacterCombat.SetActionIntent(chosenAction, currentTarget);
            }

            // 2. EXECUTION PHASE (Player/NPC when an Intent is queued)
            if (_self.CharacterCombat != null && _self.CharacterCombat.HasPlannedAction)
            {
                _isChargingTarget = true;
                
                float dx = Mathf.Abs(_self.transform.position.x - currentTarget.transform.position.x);
                float zDist = Mathf.Abs(_self.transform.position.z - currentTarget.transform.position.z);
                
                bool isWithinRange = distToTarget <= attackRange; 
                bool isZAligned = zDist <= 1.6f; // Increased from 1.2f to encompass full staggeredZ range (-1.5 to 1.5)

                // Attack is allowed if within range AND Z-aligned. 
                // isXTooClose is only used for repositioning, NOT for blocking attacks.
                if (!isWithinRange || !isZAligned)
                {
                    // Force movement into optimal valid strike position instead of target origin
                    float side = (_self.transform.position.x < currentTarget.transform.position.x) ? -1f : 1f;
                    
                    // Expanded stagger: 7 unique Z positions instead of 3 to prevent overlap
                    int staggerIndex = Mathf.Abs(_self.GetInstanceID()) % 7;
                    float staggeredZ = (staggerIndex - 3) * 0.5f; // -1.5 to 1.5
                    
                    // CRITICAL FIX: To prevent the hypotenuse from pushing the attacker outside the attack range,
                    // calculate the exact required X distance using Pythagorean theorem (X^2 = D^2 - Z^2)
                    // We target a hypotenuse slightly less than attackRange to guarantee Phase 3 is triggered.
                    float targetHypotenuse = Mathf.Max(1.0f, attackRange - 0.2f);
                    float xSqr = Mathf.Max(0.1f, (targetHypotenuse * targetHypotenuse) - (staggeredZ * staggeredZ));
                    float calculatedX = Mathf.Sqrt(xSqr);

                    Vector3 optimalStrikePos = currentTarget.transform.position + new Vector3(side * calculatedX, 0, staggeredZ);

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
                        if (doLog) Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 2] Executing Action! Distance: {distToTarget:F2}/{attackRange:F2}, Z-Dist: {zDist:F2}");
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

        private Func<bool> DecideAbilityOrAttack(Character target)
        {
            var abilities = _self.CharacterAbilities;
            if (abilities == null)
                return () => _self.CharacterCombat.Attack();

            var stats = _self.Stats;

            // 1. Scan resource pools — find most urgent need
            float hpPercent = stats.Health.CurrentAmount / Mathf.Max(stats.Health.MaxValue, 1f);
            float staminaPercent = stats.Stamina.CurrentAmount / Mathf.Max(stats.Stamina.MaxValue, 1f);
            float manaPercent = stats.Mana.CurrentAmount / Mathf.Max(stats.Mana.MaxValue, 1f);

            StatType? urgentNeed = null;
            float urgentPercent = 1f;

            if (hpPercent < urgentPercent) { urgentNeed = StatType.Health; urgentPercent = hpPercent; }
            if (staminaPercent < urgentPercent) { urgentNeed = StatType.Stamina; urgentPercent = staminaPercent; }
            if (manaPercent < urgentPercent) { urgentNeed = StatType.Mana; urgentPercent = manaPercent; }

            bool isCritical = urgentPercent < 0.20f;
            bool isLow = urgentPercent < 0.40f;

            // 2. Try to find a support ability for the urgent need
            if (urgentNeed.HasValue && isLow)
            {
                float useChance = isCritical ? 0.80f : 0.30f;
                if (UnityEngine.Random.value <= useChance)
                {
                    int supportSlot = FindSlotForStat(abilities, AbilityPurpose.Support, urgentNeed.Value, target);
                    if (supportSlot >= 0)
                    {
                        var slot = abilities.GetActiveSlot(supportSlot);
                        Character abilityTarget = (slot.Data.TargetType == AbilityTargetType.Self) ? _self : target;
                        int idx = supportSlot;
                        return () => _self.CharacterCombat.UseAbility(idx, abilityTarget);
                    }

                    if (isCritical)
                    {
                        int hybridSlot = FindSlotWithSelfRestore(abilities, urgentNeed.Value, target);
                        if (hybridSlot >= 0)
                        {
                            int idx = hybridSlot;
                            return () => _self.CharacterCombat.UseAbility(idx, target);
                        }
                    }
                }
            }

            // 3. Fallback: pick an offensive ability (30% chance) or basic attack
            for (int i = 0; i < CharacterAbilities.ACTIVE_SLOT_COUNT; i++)
            {
                var slot = abilities.GetActiveSlot(i);
                if (slot == null || !slot.CanUse(target)) continue;
                if (slot.Data.Purpose != AbilityPurpose.Offensive) continue;

                if (UnityEngine.Random.value < 0.3f)
                {
                    int slotIndex = i;
                    return () => _self.CharacterCombat.UseAbility(slotIndex, target);
                }
            }

            return () => _self.CharacterCombat.Attack();
        }

        private int FindSlotForStat(CharacterAbilities abilities, AbilityPurpose purpose, StatType need, Character target)
        {
            for (int i = 0; i < CharacterAbilities.ACTIVE_SLOT_COUNT; i++)
            {
                var slot = abilities.GetActiveSlot(i);
                if (slot == null || !slot.CanUse(target)) continue;
                if (slot.Data.Purpose != purpose) continue;
                if (slot.Data is IStatRestoreAbility restorer)
                {
                    foreach (var r in restorer.StatRestoresOnSelf)
                        if (r.stat == need && r.value > 0f) return i;
                    foreach (var r in restorer.StatRestoresOnTarget)
                        if (r.stat == need && r.value > 0f) return i;
                }
            }
            return -1;
        }

        private int FindSlotWithSelfRestore(CharacterAbilities abilities, StatType need, Character target)
        {
            for (int i = 0; i < CharacterAbilities.ACTIVE_SLOT_COUNT; i++)
            {
                var slot = abilities.GetActiveSlot(i);
                if (slot == null || !slot.CanUse(target)) continue;
                if (slot.Data is IStatRestoreAbility restorer)
                {
                    foreach (var r in restorer.StatRestoresOnSelf)
                        if (r.stat == need && r.value > 0f) return i;
                }
            }
            return -1;
        }
    }
}
