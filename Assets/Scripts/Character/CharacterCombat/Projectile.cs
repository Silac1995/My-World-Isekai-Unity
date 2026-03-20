using UnityEngine;

/// <summary>
/// Projectile tiré par une arme à distance.
/// Se déplace vers la cible, inflige des dégâts au contact, puis se détruit.
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float _speed = 15f;
    [SerializeField] private float _lifetime = 5f;

    private Character _owner;
    private float _damage;
    private DamageType _damageType;
    private float _knockbackForce;
    private bool _hasHit = false;

    public void Initialize(Character owner, float damage, DamageType damageType, float knockbackForce, Vector3 direction, float speed)
    {
        _owner = owner;
        _damage = damage;
        _damageType = damageType;
        _knockbackForce = knockbackForce;
        _speed = speed;

        // Orienter le projectile dans la direction du tir
        if (direction != Vector3.zero)
        {
            transform.forward = direction;
        }

        // Configurer le Rigidbody pour un mouvement cinématique
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = false;
            rb.linearVelocity = direction.normalized * _speed;
        }

        // Auto-destruction après timeout
        Destroy(gameObject, _lifetime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_hasHit) return;

        Character target = other.GetComponentInParent<Character>();

        if (target == null) return;
        if (target == _owner) return;
        if (!target.IsAlive()) return;

        _hasHit = true;

        // Dégâts avec variance
        float finalDamage = _damage * Random.Range(0.7f, 1.3f);
        target.CharacterCombat.TakeDamage(finalDamage, _damageType, _owner);

        // Knockback
        if (_knockbackForce > 0 && target.CharacterMovement != null)
        {
            Vector3 knockbackDir = (target.transform.position - transform.position).normalized;
            knockbackDir.y = 0;
            if (knockbackDir == Vector3.zero) knockbackDir = transform.forward;
            target.CharacterMovement.ApplyKnockback(knockbackDir * _knockbackForce);
        }

        // Déclencher un combat si pas déjà en bataille
        if (_owner.CharacterCombat != null && !_owner.CharacterCombat.IsInBattle)
        {
            _owner.CharacterCombat.StartFight(target);
        }

        Debug.Log($"<color=red>[Projectile]</color> {_owner.CharacterName} a touché {target.CharacterName} pour {finalDamage} dégâts ({_damageType}).");

        Destroy(gameObject);
    }
}
