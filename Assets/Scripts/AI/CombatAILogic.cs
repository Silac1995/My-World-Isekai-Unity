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

            // If the character has manually selected a PlannedTarget (player click or queued intent),
            // override the coordinator-assigned currentTarget so all movement and execution aim at it.
            Character plannedTarget = _self.CharacterCombat?.PlannedTarget;
            if (plannedTarget != null && plannedTarget.IsAlive())
            {
                currentTarget = plannedTarget;
            }

            if (currentTarget == null)
            {
                if (doLog) Debug.Log($"<color=red>[CombatAI]</color> {_self.CharacterName} target is NULL! Stopping movement.");
                // Clear stale intent so Phase 1 can re-decide with a new target
                if (_self.CharacterCombat != null && _self.CharacterCombat.HasPlannedAction)
                    _self.CharacterCombat.ClearActionIntent();
                movement.Stop();
                return true;
            }
            if (!currentTarget.IsAlive())
            {
                if (doLog) Debug.Log($"<color=red>[CombatAI]</color> {_self.CharacterName} target {currentTarget.CharacterName} is DEAD! Stopping movement.");
                // Clear stale intent so Phase 1 can re-decide with a new target
                if (_self.CharacterCombat != null && _self.CharacterCombat.HasPlannedAction)
                    _self.CharacterCombat.ClearActionIntent();
                movement.Stop();
                return true;
            }

            movement.Resume();
            float distToTarget = Vector3.Distance(_self.transform.position, currentTarget.transform.position);
            float attackRange = GetEffectiveAttackRange(_self);
            bool isRanged = IsRangedCharacter(_self);

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
                // DEBUG: Log every frame for player to diagnose why attack never executes
                if (!_autoDecideIntent)
                {
                    Debug.Log($"<color=lime>[PlayerCombat]</color> {_self.CharacterName} Phase 2 ACTIVE — dist={distToTarget:F2} range={attackRange:F2} zDist={Mathf.Abs(_self.transform.position.z - currentTarget.transform.position.z):F2} readyToAct={isReadyToAct} target={currentTarget?.CharacterName}");
                }
                _isChargingTarget = true;

                float dx = Mathf.Abs(_self.transform.position.x - currentTarget.transform.position.x);
                float zDist = Mathf.Abs(_self.transform.position.z - currentTarget.transform.position.z);

                bool isWithinRange = distToTarget <= attackRange;
                bool isZAligned = zDist <= 2.5f; // Must encompass stagger range (±1.5) + idle sway radius (±0.7)

                // Ranged characters: if already within weapon range, skip approach entirely.
                // They hold ground and fire from current position — no need to close further distance.
                if (isRanged && isWithinRange)
                {
                    _isChargingTarget = false;
                    if (doLog) Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 2] Ranged — already in weapon range ({distToTarget:F2}/{attackRange:F2}), skipping approach.");
                    // Fall through to execution block below
                }

                // Attack is allowed if within range AND Z-aligned.
                // isXTooClose is only used for repositioning, NOT for blocking attacks.
                // Ranged characters that passed the check above also enter the execution block (isWithinRange is true).
                if (!isWithinRange || (!isZAligned && !isRanged))
                {
                    // Melee: approach at target's Z (hitbox is narrow on Z axis).
                    // Ranged: approach to weapon range with Z stagger (projectiles don't need Z alignment).
                    float side = (_self.transform.position.x < currentTarget.transform.position.x) ? -1f : 1f;

                    float staggeredZ;
                    if (isRanged)
                    {
                        int staggerIndex = Mathf.Abs(_self.GetInstanceID()) % 7;
                        staggeredZ = (staggerIndex - 3) * 0.5f; // -1.5 to 1.5
                    }
                    else
                    {
                        // Melee: approach near target's Z with small margin for natural feel
                        // Hitbox is narrow so keep within ±1.5 units
                        int staggerIndex = Mathf.Abs(_self.GetInstanceID()) % 5;
                        staggeredZ = (staggerIndex - 2) * 0.6f; // -1.2 to 1.2
                    }

                    float approachRange = isRanged ? Mathf.Min(attackRange, distToTarget) : attackRange;
                    float targetHypotenuse = Mathf.Max(1.0f, approachRange - 0.2f);
                    float xSqr = Mathf.Max(0.1f, (targetHypotenuse * targetHypotenuse) - (staggeredZ * staggeredZ));
                    float calculatedX = Mathf.Sqrt(xSqr);

                    Vector3 optimalStrikePos = currentTarget.transform.position + new Vector3(side * calculatedX, 0, staggeredZ);

                    if (UnityEngine.Time.time - _lastPathUpdateTime > 0.3f && Vector3.Distance(movement.Destination, optimalStrikePos) > 0.5f)
                    {
                        if (doLog) Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 2] Moving into optimal strike pos: {optimalStrikePos} (ranged={isRanged})");
                        movement.SetDestination(optimalStrikePos);
                        _lastPathUpdateTime = UnityEngine.Time.time;
                    }
                }
                else
                {
                    // Perfectly positioned, stop and charge the action
                    movement.Stop();

                    // Don't attempt execution if a previous action (attack animation) is still playing.
                    // Without this guard, initiative refills (~0.1s) far faster than animations finish (~0.8s),
                    // causing a rapid attempt→fail→re-decide loop every tick for the entire animation duration.
                    bool isActionBusy = _self.CharacterActions != null && _self.CharacterActions.CurrentAction != null;

                    // DEBUG: Player per-frame log to diagnose blocked execution
                    if (!_autoDecideIntent)
                    {
                        Debug.Log($"<color=lime>[PlayerCombat]</color> {_self.CharacterName} IN POSITION — readyToAct={isReadyToAct} isActionBusy={isActionBusy} currentAction={_self.CharacterActions?.CurrentAction?.GetType().Name ?? "null"} PlannedAction={(_self.CharacterCombat.PlannedAction != null ? "SET" : "NULL")}");
                    }

                    if (isReadyToAct && !isActionBusy)
                    {
                        if (doLog) Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 2] Executing Action! Distance: {distToTarget:F2}/{attackRange:F2}, Z-Dist: {zDist:F2}");
                        bool success = _self.CharacterCombat.ExecuteAction(_self.CharacterCombat.PlannedAction);

                        if (success)
                        {
                            // Notify the tactical pacer so melee characters perform a post-attack step-back
                            // and the sway center resets to the new position after execution.
                            _combatPacer.NotifyAttackCompleted();
                            _combatPacer.ResetSwayCenter();
                        }
                        else
                        {
                            Debug.LogWarning($"<color=yellow>[CombatAI]</color> {_self.CharacterName} [Phase 2] ExecuteAction FAILED — possible stamina depletion or action rejected.");
                        }

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
                        if (doLog)
                        {
                            if (isActionBusy)
                                Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 2] In position, waiting for current action to finish.");
                            else
                                Debug.Log($"<color=orange>[CombatAI]</color> {_self.CharacterName} [Phase 2] In position but waiting for full initiative (isReadyToAct).");
                        }
                    }
                }
            }
            else
            {
                // 3. CLEANUP & PACING FALLBACK (Only tactical movement when NO ACTION is planned)
                // Resolve the character's current engagement so the pacer can apply leash, circling, etc.
                CombatEngagement engagement = null;
                if (_self.CharacterCombat.IsInBattle)
                {
                    engagement = _self.CharacterCombat.CurrentBattleManager?.Coordinator?.GetEngagementOf(_self);
                }

                Vector3 finalPos = _combatPacer.GetTacticalDestination(currentTarget, attackRange, engagement, _isChargingTarget);
                
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

            // Facing is handled by CharacterVisual.LateUpdate via the look target set by SetActionIntent.
            // CombatAILogic must NOT directly control facing to avoid competing flip sources.

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

        private bool IsRangedCharacter(Character character)
        {
            if (character?.CharacterCombat?.CurrentCombatStyleExpertise?.Style == null)
                return false;
            return character.CharacterCombat.CurrentCombatStyleExpertise.Style is RangedCombatStyleSO;
        }

        /// <summary>
        /// Returns the effective attack range for the character's current combat style.
        /// Ranged characters use RangedRange; melee characters use MeleeRange.
        /// </summary>
        private float GetEffectiveAttackRange(Character character)
        {
            var style = character?.CharacterCombat?.CurrentCombatStyleExpertise?.Style;
            if (style == null) return 3.5f;

            if (style is RangedCombatStyleSO rangedStyle)
                return rangedStyle.RangedRange;

            return style.MeleeRange;
        }
    }
}
