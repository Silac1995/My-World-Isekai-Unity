using UnityEngine;

/// <summary>
/// Style de combat à distance. Contient le prefab de projectile et les paramètres de tir.
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
