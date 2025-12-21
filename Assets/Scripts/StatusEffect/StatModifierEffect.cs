using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Status Effects/Stat Modifier Effect")]
public class StatModifierEffect : StatusEffect
{
    [SerializeField]
    private List<StatsModifier> modifiers = new List<StatsModifier>();

    public List<StatsModifier> Modifiers => modifiers;
}
