using System.Collections.Generic;

/// <summary>
/// Un but GOAP : un état du monde désiré.
/// Le planner cherche une séquence d'actions pour atteindre cet état.
/// Ex: { "hasDepositedResources" = true } pour un Gatherer.
/// </summary>
[System.Serializable]
public class GoapGoal
{
    public string GoalName;
    public Dictionary<string, bool> DesiredState;
    public int Priority;

    public GoapGoal(string name, Dictionary<string, bool> desiredState, int priority = 0)
    {
        GoalName = name;
        DesiredState = desiredState;
        Priority = priority;
    }

    /// <summary>
    /// Vérifie si le world state actuel satisfait déjà ce goal.
    /// </summary>
    public bool IsSatisfied(Dictionary<string, bool> worldState)
    {
        foreach (var desired in DesiredState)
        {
            if (!worldState.TryGetValue(desired.Key, out bool value) || value != desired.Value)
                return false;
        }
        return true;
    }
}
