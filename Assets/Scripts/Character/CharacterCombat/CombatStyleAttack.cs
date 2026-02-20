using UnityEngine;

[RequireComponent(typeof(Collider))]
public class CombatStyleAttack : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private CombatStyleSO _combatStyleSO;
    [SerializeField] private Collider _hitCollider;

    public Character Character => _character;
    public CombatStyleSO CombatStyleSO => _combatStyleSO;
    public Collider HitCollider => _hitCollider;

    public void Initialize(Character character)
    {
        _character = character;
    }
}

