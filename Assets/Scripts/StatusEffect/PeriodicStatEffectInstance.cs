using UnityEngine;

public class PeriodicStatEffectInstance : StatusEffectInstance
{
    private PeriodicStatEffect sourceEffect;
    private Character caster;
    private Character target;
    
    private float valuePerSecond;
    private float timer;

    public PeriodicStatEffectInstance(PeriodicStatEffect sourceEffect, Character caster, Character target)
    {
        this.sourceEffect = sourceEffect;
        this.caster = caster;
        this.target = target;
        
        // Potential scaling logic here
        valuePerSecond = sourceEffect.ValuePerSecond;
        Debug.Log($"<color=white>[Status]</color> Effet périodique créé sur {target.name} pour {sourceEffect.TargetStat} (Valeur: {valuePerSecond}, IsPercentage: {sourceEffect.IsPercentage})");
    }

    public override void Apply() { }
    public override void Remove() { }

    public override void Tick(float deltaTime)
    {
        timer += deltaTime;
        
        int ticksToProcess = Mathf.FloorToInt(timer);
        if (ticksToProcess > 0)
        {
            timer %= 1f;

            if (target != null && target.Stats != null)
            {
                var stat = target.Stats.GetBaseStat(sourceEffect.TargetStat);
                
                if (stat is CharacterPrimaryStats primaryStat)
                {
                    float singleTickAmount = valuePerSecond;
                    if (sourceEffect.IsPercentage)
                    {
                        singleTickAmount = (valuePerSecond / 100f) * primaryStat.MaxValue;
                    }

                    for (int i = 0; i < ticksToProcess; i++)
                    {
                        if (singleTickAmount > 0)
                        {
                            primaryStat.IncreaseCurrentAmount(singleTickAmount);
                            Debug.Log($"<color=green>[Status]</color> Regénération périodique sur {target.name} : +{singleTickAmount:F2} {sourceEffect.TargetStat}. Nouveau montant : {primaryStat.CurrentAmount:F1}/{primaryStat.CurrentValue}");
                        }
                        else if (singleTickAmount < 0)
                        {
                            primaryStat.DecreaseCurrentAmount(-singleTickAmount);
                            Debug.Log($"<color=red>[Status]</color> Dégâts périodiques sur {target.name} : {singleTickAmount:F2} {sourceEffect.TargetStat}. Nouveau montant : {primaryStat.CurrentAmount:F1}/{primaryStat.CurrentValue}");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"<color=yellow>[Status]</color> Stat {sourceEffect.TargetStat} trouvée mais n'est pas une CharacterPrimaryStats sur {target.name}");
                }
            }
        }
    }
}
