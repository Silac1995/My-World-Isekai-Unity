using System.Collections.Generic;
using UnityEngine;

public class CharacterStatusEffectInstance
{
    private string statusEffectName;
    private List<StatusEffectInstance> statusEffectInstances;
    private float remainingDuration; // 0 = permanent
    private bool isPermanent;
    private GameObject visualEffectInstance; 
    private Sprite icon;
    private string description;
    private Character caster;
    private Character target;
    private CharacterStatusEffect sourceAsset;

    public string StatusEffectName => statusEffectName;
    public CharacterStatusEffect SourceAsset => sourceAsset;
    public IReadOnlyList<StatusEffectInstance> StatusEffectInstances => statusEffectInstances.AsReadOnly();
    public float RemainingDuration => remainingDuration;
    public bool IsPermanent => isPermanent;
    public GameObject VisualEffectInstance => visualEffectInstance; 
    public Sprite Icon => icon;
    public string Description => description;
    public Character Caster => caster;
    public Character Target => target;

    public CharacterStatusEffectInstance(CharacterStatusEffect effectAsset, Character caster, Character target)
    {
        this.sourceAsset = effectAsset;
        statusEffectName = effectAsset.StatusEffectName;
        remainingDuration = effectAsset.Duration;
        isPermanent = (remainingDuration <= 0);
        icon = effectAsset.Icon;
        description = effectAsset.Description;
        this.caster = caster;
        this.target = target;

        statusEffectInstances = new List<StatusEffectInstance>();

        foreach (var effect in effectAsset.StatusEffects)
        {
            if (effect is StatModifierEffect statModifierEffect)
            {
                var instance = new StatModifierEffectInstance(statModifierEffect, caster, target);
                statusEffectInstances.Add(instance);
            }
            else if (effect is PeriodicStatEffect periodicEffect)
            {
                var instance = new PeriodicStatEffectInstance(periodicEffect, caster, target);
                statusEffectInstances.Add(instance);
            }
        }

        if (effectAsset.VisualEffectPrefab != null && target != null)
        {
            visualEffectInstance = Object.Instantiate(effectAsset.VisualEffectPrefab, target.transform);
        }
    }

    public void Apply()
    {
        foreach (var instance in statusEffectInstances)
        {
            instance.Apply();
        }
    }

    public void Remove()
    {
        foreach (var instance in statusEffectInstances)
        {
            instance.Remove();
        }

        if (visualEffectInstance != null)
        {
            Object.Destroy(visualEffectInstance);
        }
    }

    public void RefreshDuration()
    {
        remainingDuration = sourceAsset.Duration;
    }

    public bool Tick(float deltaTime)
    {
        // Internal ticks (for DOTs)
        foreach (var instance in statusEffectInstances)
        {
            instance.Tick(deltaTime);
        }

        if (isPermanent) return false;

        remainingDuration -= deltaTime;
        return remainingDuration <= 0;
    }
}
