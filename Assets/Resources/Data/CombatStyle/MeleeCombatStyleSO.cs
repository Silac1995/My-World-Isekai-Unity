using UnityEngine;

/// <summary>
/// Melee combat style. Holds the hitbox prefab (CombatStyleAttack).
/// </summary>
public abstract class MeleeCombatStyleSO : CombatStyleSO
{
    [Header("Melee Settings")]
    [SerializeField] private GameObject _hitboxPrefab;

    public GameObject HitboxPrefab => _hitboxPrefab;

    // Backwards compatibility: older references use Prefab
    public GameObject Prefab => _hitboxPrefab;
}
