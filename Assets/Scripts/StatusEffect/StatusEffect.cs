using System.Collections.Generic;
using UnityEngine;

public abstract class StatusEffect : ScriptableObject
{
    private string statusName;
    private List<StatsModifier> statsModifier;
}
