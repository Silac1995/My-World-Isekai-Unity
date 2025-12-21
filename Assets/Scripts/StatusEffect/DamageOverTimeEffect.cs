using UnityEngine;

[CreateAssetMenu(menuName = "Status Effects/Damage Over Time")]
public class DamageOverTimeEffect : StatusEffect
{
    [SerializeField] private float baseDamagePerSecond = 5f;

    //public override StatusEffectRuntime CreateRuntimeInstance(Character source, Character target)
    //{
    //    // Exemple de scaling : dégâts multipliés par la force du lanceur
    //    float scaledDamage = baseDamagePerSecond * source.Stats.strength.CurrentValue;
    //    return new DamageOverTimeRuntime(target, scaledDamage, duration);
    //}
}
