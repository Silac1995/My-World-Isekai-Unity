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

    public override AbilityCategory Category => AbilityCategory.Spell;

    /// <summary>
    /// Computes the actual cast time after Dexterity reduction.
    /// If reduced to 5% or less of the base, it becomes instant (0).
    /// </summary>
    /// <param name="castingSpeedValue">The character's CastingSpeed tertiary stat value.</param>
    public float ComputeCastTime(float castingSpeedValue)
    {
        // CastingSpeed reduces cast time. Higher = faster.
        // Formula: baseCastTime * (1 - reduction), where reduction scales with castingSpeed.
        // Clamped so it never goes negative.
        float reduction = Mathf.Clamp01(castingSpeedValue * 0.01f);
        float castTime = _baseCastTime * (1f - reduction);

        // If reduced to 5% or less of original, treat as instant
        if (castTime <= _baseCastTime * 0.05f)
            return 0f;

        return Mathf.Max(0f, castTime);
    }
}
