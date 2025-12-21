using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class CharacterStatusEffectInstance
{
    private string statusEffectName;
    private List<StatusEffectInstance> statusEffectInstances;
    private float duration; // 0 = permanent
    private GameObject visualEffectInstance; // instance de l’effet visuel (optionnel)
    private Sprite icon;
    private string description;
    private Character caster;
    private Character target;

    public string StatusEffectName => statusEffectName;
    public IReadOnlyList<StatusEffectInstance> StatusEffectInstances => statusEffectInstances.AsReadOnly();
    public float Duration => duration;
    public GameObject VisualEffectInstance => visualEffectInstance;
    public Sprite Icon => icon;
    public string Description => description;
    public Character Caster => caster;
    public Character Target => target;

    public CharacterStatusEffectInstance(CharacterStatusEffect effectAsset, Character caster, Character target)
    {
        statusEffectName = effectAsset.StatusEffectName;
        duration = effectAsset.Duration;
        icon = effectAsset.Icon;
        description = effectAsset.Description;

        statusEffectInstances = new List<StatusEffectInstance>();

        // Instanciation des effets enfants (stat modifiers, etc)
        foreach (var effect in effectAsset.StatusEffects)
        {
            // Exemple : si c’est un StatModifierEffect, crée une instance spécifique
            if (effect is StatModifierEffect statModifierEffect)
            {
                var instance = new StatModifierEffectInstance(statModifierEffect, caster, target);
                statusEffectInstances.Add(instance);
            }
            else
            {
                // gérer d’autres types d’effets si besoin
            }
        }

        // Instancier l'effet visuel si prefab défini
        if (effectAsset.VisualEffectPrefab != null && target != null)
        {
            visualEffectInstance = Object.Instantiate(effectAsset.VisualEffectPrefab, target.transform);
        }
    }
}
