using UnityEngine;

/// <summary>
/// Style de combat au corps à corps. Contient le prefab de hitbox (CombatStyleAttack).
/// </summary>
public abstract class MeleeCombatStyleSO : CombatStyleSO
{
    [Header("Melee Settings")]
    [SerializeField] private GameObject _hitboxPrefab;

    public GameObject HitboxPrefab => _hitboxPrefab;

    // Compatibilité : les anciennes références utilisent Prefab
    public GameObject Prefab => _hitboxPrefab;
}
