using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Planner GOAP léger utilisant un backward search (recherche arrière).
/// Part du goal désiré, cherche des actions dont les effets satisfont le goal,
/// puis vérifie récursivement les préconditions de ces actions.
/// Retourne la séquence d'actions ordonnée (Queue) pour atteindre le goal.
/// </summary>
public static class GoapPlanner
{
    private const int MAX_DEPTH = 10;

    /// <summary>
    /// Planifie une séquence d'actions pour atteindre le goal depuis le world state actuel.
    /// Retourne null si aucun plan n'est possible.
    /// </summary>
    public static Queue<GoapAction> Plan(
        Dictionary<string, bool> worldState,
        List<GoapAction> availableActions,
        GoapGoal goal)
    {
        if (goal == null || goal.DesiredState == null || goal.DesiredState.Count == 0)
            return null;

        // Si le goal est déjà atteint, pas besoin de plan
        if (goal.IsSatisfied(worldState))
            return null;

        // Construire l'arbre de plans possibles
        var leaves = new List<PlanNode>();
        var startNode = new PlanNode(null, 0f, worldState, null);

        bool found = BuildGraph(startNode, leaves, availableActions, goal, 0);

        if (!found || leaves.Count == 0)
        {
            Debug.Log($"<color=red>[GOAP]</color> Aucun plan trouvé pour le goal '{goal.GoalName}'.");
            return null;
        }

        // Trouver le plan le moins coûteux
        PlanNode cheapest = leaves.OrderBy(n => n.RunningCost).First();

        // Reconstruire le plan en remontant les parents
        var plan = new List<GoapAction>();
        var current = cheapest;
        while (current != null)
        {
            if (current.Action != null)
                plan.Add(current.Action);
            current = current.Parent;
        }

        // Inverser (on a remonté depuis la feuille)
        plan.Reverse();

        Debug.Log($"<color=green>[GOAP]</color> Plan trouvé pour '{goal.GoalName}' : {string.Join(" → ", plan.Select(a => a.ActionName))}");

        return new Queue<GoapAction>(plan);
    }

    /// <summary>
    /// Construit récursivement le graphe de plans possibles (forward search).
    /// </summary>
    private static bool BuildGraph(
        PlanNode parent,
        List<PlanNode> leaves,
        List<GoapAction> availableActions,
        GoapGoal goal,
        int depth)
    {
        if (depth >= MAX_DEPTH) return false;

        bool foundPlan = false;

        foreach (var action in availableActions)
        {
            // L'action doit avoir ses préconditions satisfaites par l'état courant
            if (!action.ArePreconditionsMet(parent.State))
                continue;

            // Appliquer les effets de l'action pour obtenir le nouvel état
            var newState = action.ApplyEffects(parent.State);
            float newCost = parent.RunningCost + action.Cost;

            var node = new PlanNode(parent, newCost, newState, action);

            // Si le goal est atteint, c'est une feuille valide
            if (goal.IsSatisfied(newState))
            {
                leaves.Add(node);
                foundPlan = true;
            }
            else
            {
                // Sinon, continuer à chercher (sans réutiliser la même action)
                var remainingActions = availableActions.Where(a => a != action).ToList();
                if (BuildGraph(node, leaves, remainingActions, goal, depth + 1))
                {
                    foundPlan = true;
                }
            }
        }

        return foundPlan;
    }

    /// <summary>
    /// Noeud interne pour construire l'arbre de plans.
    /// </summary>
    private class PlanNode
    {
        public PlanNode Parent;
        public float RunningCost;
        public Dictionary<string, bool> State;
        public GoapAction Action;

        public PlanNode(PlanNode parent, float runningCost, Dictionary<string, bool> state, GoapAction action)
        {
            Parent = parent;
            RunningCost = runningCost;
            State = state;
            Action = action;
        }
    }
}
