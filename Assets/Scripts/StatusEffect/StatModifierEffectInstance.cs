using System.Collections.Generic;
using UnityEngine;

public class StatModifierEffectInstance : StatusEffectInstance
{
    private StatModifierEffect sourceEffect;
    private Character caster;
    private Character target;
    private List<StatsModifier> modifiers;

    public Character Caster => caster;
    public Character Target => target;
    public IReadOnlyList<StatsModifier> Modifiers => modifiers.AsReadOnly();

    public StatModifierEffectInstance(StatModifierEffect sourceEffect, Character caster, Character target)
    {
        this.sourceEffect = sourceEffect;
        this.caster = caster;
        this.target = target;

        modifiers = new List<StatsModifier>();
        foreach (var mod in sourceEffect.Modifiers)
        {
            float finalValue = mod.Value; 
            modifiers.Add(new StatsModifier() { StatType = mod.StatType, Value = finalValue });
        }
    }

    public override void Apply()
    {
        if (target == null || target.Stats == null) return;

        foreach (var mod in modifiers)
        {
            var stat = target.Stats.GetBaseStat(mod.StatType);
            if (stat != null)
            {
                stat.ApplyModifier(mod.Value);
            }
        }
        
        target.Stats.RecalculateTertiaryStats();
    }

    public override void Remove()
    {
        if (target == null || target.Stats == null) return;

        foreach (var mod in modifiers)
        {
            var stat = target.Stats.GetBaseStat(mod.StatType);
            if (stat != null)
            {
                stat.RemoveModifier(mod.Value);
            }
        }
        
        target.Stats.RecalculateTertiaryStats();
    }
}
