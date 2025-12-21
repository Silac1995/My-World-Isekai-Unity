using System.Collections.Generic;
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

        // Ici tu peux calculer dynamiquement les valeurs des modifiers selon les stats du caster
        modifiers = new List<StatsModifier>();
        foreach (var mod in sourceEffect.Modifiers)
        {
            // Exemple basique : tu pourrais multiplier mod.Value par une stat du caster
            float finalValue = mod.Value; // Par défaut
            // Exemple: si mod.StatType == Strength, tu peux faire finalValue *= caster.Stats.Strength.CurrentValue; etc.
            modifiers.Add(new StatsModifier() { StatType = mod.StatType, Value = finalValue });
        }
    }

    public override void Apply()
    {
    }

    public override void Remove()
    {
    }
}