using UnityEngine;

/// <summary>
/// CharacterAction for weapon-bound physical abilities (Stamina cost, no cooldown).
/// Follows the same hitbox-spawn pattern as CharacterMeleeAttackAction.
/// </summary>
public class CharacterPhysicalAbilityAction : CharacterCombatAction
{
    private readonly PhysicalAbilityInstance _ability;
    private readonly Character _target;

    public PhysicalAbilityInstance Ability => _ability;

    public CharacterPhysicalAbilityAction(Character character, PhysicalAbilityInstance ability, Character target)
        : base(character, ability.PhysicalData.AnimationDuration)
    {
        _ability = ability;
        _target = target;

        float castTime = _ability.ComputeCastTime();
        if (castTime > 0f)
        {
            Duration = castTime;
        }
        else
        {
            // Instant: use animation duration (preserve current behavior)
            var animHandler = character.CharacterVisual?.CharacterAnimator;
            if (animHandler != null)
            {
                float clipDuration = animHandler.GetMeleeAttackDuration();
                if (clipDuration > 0f)
                    Duration = clipDuration + 0.1f;
            }
        }
    }

    public override bool CanExecute()
    {
        if (_ability == null) return false;
        return _ability.CanUse(_target);
    }

    public override void OnStart()
    {
        base.OnStart();

        // Server: consume stamina
        if (character.IsServer && character.Stats?.Stamina != null)
        {
            character.Stats.Stamina.DecreaseCurrentAmount(_ability.PhysicalData.StaminaCost);

            // Check Out of Breath
            if (character.Stats.Stamina.CurrentAmount <= 0f)
                character.StatusManager?.ApplyOutOfBreathEffect();
        }

        // Play melee animation (physical abilities reuse melee animation for now)
        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler != null)
        {
            animHandler.PlayMeleeAttack();
        }

        // Apply status effects on self immediately
        if (character.IsServer && _ability.Data.StatusEffectsOnSelf != null)
        {
            foreach (var effect in _ability.Data.StatusEffectsOnSelf)
            {
                if (effect != null)
                    character.StatusManager?.ApplyEffect(effect, character);
            }
        }
    }

    public override void OnApplyEffect()
    {
        base.OnApplyEffect();
        // Hitbox damage is handled by Animation Events (SpawnCombatStyleAttackInstance)
        // with the ability's damage multiplier applied via the combat system.
        // Status effects on target are applied when the hitbox connects.

        // Apply status effects on target if we have a direct target and this is not hitbox-based
        if (character.IsServer && _target != null && _target.IsAlive() && _ability.Data.StatusEffectsOnTarget != null)
        {
            foreach (var effect in _ability.Data.StatusEffectsOnTarget)
            {
                if (effect != null)
                    _target.StatusManager?.ApplyEffect(effect, character);
            }
        }

        if (_ability.Data is IStatRestoreAbility restorer)
        {
            if (character.IsServer)
            {
                StatRestoreProcessor.ApplyRestores(restorer.StatRestoresOnTarget, _target);
                StatRestoreProcessor.ApplyRestores(restorer.StatRestoresOnSelf, character);
            }
        }
    }
}
