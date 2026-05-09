/// <summary>
/// Shared interface for anything that can receive damage: Characters, doors, destructibles.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float damage, Character attacker);
    bool CanBeDamaged();
}
