using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Physical Ability")]
public class PhysicalAbilitySO : AbilitySO
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

    public float StaminaCost => _staminaCost;
    public WeaponType RequiredWeaponType => _requiredWeaponType;
    public float DamageMultiplier => _damageMultiplier;
    public DamageType DamageTypeOverride => _damageTypeOverride;
    public bool OverridesDamageType => _overridesDamageType;
    public float KnockbackMultiplier => _knockbackMultiplier;
    public int MaxTargets => _maxTargets;
    public GameObject HitboxPrefabOverride => _hitboxPrefabOverride;
    public float AnimationDuration => _animationDuration;

    public override AbilityCategory Category => AbilityCategory.Physical;
}
