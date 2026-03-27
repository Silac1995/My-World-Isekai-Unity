using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[CreateAssetMenu(fileName = "CharacterStatusEffect", menuName = "Character Status Effect")]
public class CharacterStatusEffect : ScriptableObject
{
    [SerializeField]
    private string statusEffectName;
    [SerializeField]
    private List<StatusEffect> statusEffects;
    [SerializeField]
    private float duration; // if equals 0 => permanent
    [SerializeField]
    private GameObject visualEffectPrefab;
    [SerializeField]
    private Sprite icon;
    [SerializeField]
    private string description;
    [SerializeField]
    [Tooltip("Sets the maximum number of times this effect can stack. 0 or 1 means non-stackable.")]
    private int maxStacks = 1;

    [Header("Suspend Condition")]
    [SerializeField]
    [Tooltip("Enable to suspend this effect when a stat threshold is met.")]
    private bool _hasSuspendCondition = false;

    [SerializeField]
    private StatusEffectSuspendCondition _suspendCondition;

    public string StatusEffectName => statusEffectName;
    public IReadOnlyList<StatusEffect> StatusEffects => statusEffects.AsReadOnly();
    public float Duration => duration;
    public GameObject VisualEffectPrefab => visualEffectPrefab;
    public Sprite Icon => icon;
    public string Description => description;
    public int MaxStacks => maxStacks;
    public bool HasSuspendCondition => _hasSuspendCondition;
    public StatusEffectSuspendCondition SuspendCondition => _suspendCondition;

    private void OnValidate()
    {
        if (maxStacks < 1) maxStacks = 1;

        if (_hasSuspendCondition && _suspendCondition.isPercentage)
        {
            bool isPrimary = _suspendCondition.statType == StatType.Health
                          || _suspendCondition.statType == StatType.Mana
                          || _suspendCondition.statType == StatType.Stamina
                          || _suspendCondition.statType == StatType.Initiative;
            if (!isPrimary)
            {
                Debug.LogWarning($"[{statusEffectName}] isPercentage is only valid for Primary stats. Forcing to false.");
                _suspendCondition.isPercentage = false;
            }
        }
    }
}
