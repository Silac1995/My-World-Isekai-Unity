using System.Collections.Generic;
using UnityEngine;

public abstract class AbilitySO : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string _abilityId;
    [SerializeField] private string _abilityName;
    [SerializeField][TextArea] private string _description;
    [SerializeField] private Sprite _icon;

    [Header("Targeting")]
    [SerializeField] private AbilityTargetType _targetType = AbilityTargetType.SingleEnemy;
    [SerializeField] private float _range = 3.5f;

    [Header("Effects")]
    [SerializeField] private List<CharacterStatusEffect> _statusEffectsOnTarget;
    [SerializeField] private List<CharacterStatusEffect> _statusEffectsOnSelf;
    [SerializeField] private GameObject _visualPrefab;

    public string AbilityId => _abilityId;
    public string AbilityName => _abilityName;
    public string Description => _description;
    public Sprite Icon => _icon;
    public AbilityTargetType TargetType => _targetType;
    public float Range => _range;
    public IReadOnlyList<CharacterStatusEffect> StatusEffectsOnTarget => _statusEffectsOnTarget;
    public IReadOnlyList<CharacterStatusEffect> StatusEffectsOnSelf => _statusEffectsOnSelf;
    public GameObject VisualPrefab => _visualPrefab;

    public abstract AbilityCategory Category { get; }
}
