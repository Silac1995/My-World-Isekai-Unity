using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Physical Ability")]
public class PhysicalAbilitySO : AbilitySO, IStatRestoreAbility
{
    [Header("Physical Ability")]
    [SerializeField] private float _staminaCost = 10f;
    [SerializeField] private WeaponType _requiredWeaponType = WeaponType.Sword;
    [SerializeField] private float _damageMultiplier = 1.0f;
    [SerializeField] private DamageType _damageTypeOverride = DamageType.Slashing;
    [SerializeField] private bool _overridesDamageType = false;
    [SerializeField] private float _knockbackMultiplier = 1.0f;
    [SerializeField] private int _maxTargets = 1;
    [SerializeField] private GameObject _hitboxPrefabOverride;
    [SerializeField] private float _animationDuration = 0.8f;

    [Header("Cast Time")]
    [SerializeField]
    [Tooltip("Base cast time in seconds. 0 = instant (no channel).")]
    private float _baseCastTime = 0f;

    [SerializeField, Range(0f, 1f)]
    [Tooltip("If reduced cast time falls to this fraction of base or below, ability becomes instant. Default 10%.")]
    private float _instantCastThreshold = 0.10f;

    [Header("Stat Restores")]
    [SerializeField] private List<StatRestoreEntry> _statRestoresOnTarget = new List<StatRestoreEntry>();
    [SerializeField] private List<StatRestoreEntry> _statRestoresOnSelf = new List<StatRestoreEntry>();

    public float StaminaCost => _staminaCost;
    public WeaponType RequiredWeaponType => _requiredWeaponType;
    public float DamageMultiplier => _damageMultiplier;
    public DamageType DamageTypeOverride => _damageTypeOverride;
    public bool OverridesDamageType => _overridesDamageType;
    public float KnockbackMultiplier => _knockbackMultiplier;
    public int MaxTargets => _maxTargets;
    public GameObject HitboxPrefabOverride => _hitboxPrefabOverride;
    public float AnimationDuration => _animationDuration;
    public float BaseCastTime => _baseCastTime;
    public float InstantCastThreshold => _instantCastThreshold;

    public IReadOnlyList<StatRestoreEntry> StatRestoresOnTarget => _statRestoresOnTarget.AsReadOnly();
    public IReadOnlyList<StatRestoreEntry> StatRestoresOnSelf => _statRestoresOnSelf.AsReadOnly();

    public override AbilityCategory Category => AbilityCategory.Physical;

    public float ComputeCastTime(float combatCastingValue)
    {
        if (_baseCastTime <= 0f) return 0f;

        float reducedTime = _baseCastTime / (1f + combatCastingValue);

        if (reducedTime <= _baseCastTime * _instantCastThreshold)
            return 0f;

        return reducedTime;
    }
}
