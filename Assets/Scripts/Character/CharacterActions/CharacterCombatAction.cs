using UnityEngine;

public abstract class CharacterCombatAction : CharacterAction
{
    public override bool ShouldPlayGenericActionAnimation => false;
    public override bool IsReplicatedInternally => true;

    /// <summary>
    /// Override to return true if this action should consume stamina via the base class.
    /// Abilities that manage their own resource cost (PhysicalAbility, SpellCast) return false.
    /// </summary>
    protected virtual bool ConsumesBaseStamina => false;

    private const float BASE_MELEE_STAMINA_COST = 3f;
    private const float STAMINA_POWER_SCALING_RATIO = 0.1f;
    private const float FLAT_RANGED_STAMINA_COST = 5f;

    protected CharacterCombatAction(Character character, float duration = 0f)
        : base(character, duration)
    {
    }

    public override bool CanExecute()
    {
        if (!ConsumesBaseStamina) return true;
        if (character.Stats?.Stamina == null) return true;

        float cost = CalculateStaminaCost();
        if (character.Stats.Stamina.CurrentAmount < cost)
        {
            Debug.LogWarning($"<color=yellow>[Combat]</color> {character.CharacterName} cannot attack — stamina {character.Stats.Stamina.CurrentAmount:F1}/{cost:F1} insufficient.");
            return false;
        }

        return true;
    }

    public override void OnStart()
    {
        if (!ConsumesBaseStamina) return;

        // Server-only: consume stamina when the action begins
        if (character.IsServer && character.Stats?.Stamina != null)
        {
            float cost = CalculateStaminaCost();
            character.Stats.Stamina.DecreaseCurrentAmount(cost);

            if (character.Stats.Stamina.CurrentAmount <= 0f)
            {
                character.StatusManager?.ApplyOutOfBreathEffect();
            }
        }
    }

    public override void OnApplyEffect()
    {
        // Damage is handled by Animation Events or subclass logic.
    }

    protected virtual float CalculateStaminaCost()
    {
        bool isRanged = character.CharacterCombat?.CurrentCombatStyleExpertise?.Style is RangedCombatStyleSO;
        if (isRanged) return FLAT_RANGED_STAMINA_COST;

        float physicalPower = character.Stats?.PhysicalPower?.CurrentValue ?? 0f;
        return BASE_MELEE_STAMINA_COST + physicalPower * STAMINA_POWER_SCALING_RATIO;
    }
}
