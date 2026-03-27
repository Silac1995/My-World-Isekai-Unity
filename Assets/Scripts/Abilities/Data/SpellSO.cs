using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Spell")]
public class SpellSO : AbilitySO
{
    [Header("Spell")]
    [SerializeField] private float _manaCost = 15f;
    [SerializeField] private float _baseCastTime = 2f;
    [SerializeField] private float _cooldown = 5f;
    [SerializeField] private float _baseDamage = 10f;
    [SerializeField] private DamageType _damageType = DamageType.Fire;
    [SerializeField] private SecondaryStatType _scalingStat = SecondaryStatType.Intelligence;
    [SerializeField] private float _statMultiplier = 1.0f;
    [SerializeField] private float _aoeRadius = 0f;
    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField] private float _projectileSpeed = 15f;
    [SerializeField, Range(0f, 1f)]
    [Tooltip("If reduced cast time falls to this fraction of base or below, ability becomes instant. Default 5%.")]
    private float _instantCastThreshold = 0.05f;

    public float ManaCost => _manaCost;
    public float BaseCastTime => _baseCastTime;
    public float Cooldown => _cooldown;
    public float BaseDamage => _baseDamage;
    public DamageType SpellDamageType => _damageType;
    public SecondaryStatType ScalingStat => _scalingStat;
    public float StatMultiplier => _statMultiplier;
    public float AoeRadius => _aoeRadius;
    public bool IsProjectile => _projectilePrefab != null;
    public GameObject ProjectilePrefab => _projectilePrefab;
    public float ProjectileSpeed => _projectileSpeed;
    public float InstantCastThreshold => _instantCastThreshold;

    public override AbilityCategory Category => AbilityCategory.Spell;

    public float ComputeCastTime(float spellCastingValue)
    {
        if (_baseCastTime <= 0f) return 0f;

        float reducedTime = _baseCastTime / (1f + spellCastingValue);

        if (reducedTime <= _baseCastTime * _instantCastThreshold)
            return 0f;

        return reducedTime;
    }
}
