using UnityEngine;

/// <summary>
/// Ranged combat style. Holds the projectile prefab and firing parameters.
/// </summary>
public abstract class RangedCombatStyleSO : CombatStyleSO
{
    [Header("Ranged Settings")]
    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField] private float _projectileSpeed = 15f;
    [SerializeField] private float _rangedRange = 20f;

    public GameObject ProjectilePrefab => _projectilePrefab;
    public float ProjectileSpeed => _projectileSpeed;
    public float RangedRange => _rangedRange;
}
