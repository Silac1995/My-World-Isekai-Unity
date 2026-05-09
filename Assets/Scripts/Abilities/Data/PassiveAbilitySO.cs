using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Passive Ability")]
public class PassiveAbilitySO : AbilitySO
{
    [Header("Passive Trigger")]
    [SerializeField] private PassiveTriggerCondition _triggerCondition;
    [SerializeField][Range(0f, 1f)] private float _triggerChance = 1f;
    [SerializeField] private float _internalCooldown = 3f;
    [SerializeField][Tooltip("Used for OnLowHPThreshold. 0.3 = triggers at 30% HP.")]
    private float _hpThreshold = 0.3f;

    [Header("Reaction")]
    [SerializeField] private float _reactionDamageMultiplier = 0f;
    [SerializeField][Range(0f, 1f)] private float _reflectPercentage = 0f;
    [SerializeField] private List<CharacterStatusEffect> _reactionEffects;

    public PassiveTriggerCondition TriggerCondition => _triggerCondition;
    public float TriggerChance => _triggerChance;
    public float InternalCooldown => _internalCooldown;
    public float HpThreshold => _hpThreshold;
    public float ReactionDamageMultiplier => _reactionDamageMultiplier;
    public float ReflectPercentage => _reflectPercentage;
    public IReadOnlyList<CharacterStatusEffect> ReactionEffects => _reactionEffects;

    public override AbilityCategory Category => AbilityCategory.Passive;
}
