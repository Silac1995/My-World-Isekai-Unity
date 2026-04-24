using System.Collections.Generic;
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
    /// Set to true in the editor to see per-plan success/failure logs. Kept off by default because
    /// the planner runs on the server once per NPC per replan and a jobless NPC would fill the
    /// Unity console in minutes (which on Windows progressively stalls the editor).
    /// </summary>
    public static bool VerboseLogging = false;

    // Scratch buffer: tracks actions currently "used" along the recursion path.
    // Single-threaded (server main thread, non-reentrant), so one static HashSet is safe
    // and eliminates the per-recursion `Where().ToList()` allocation.
    private static readonly HashSet<GoapAction> _usedActions = new HashSet<GoapAction>();

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

        _usedActions.Clear();
        bool found = BuildGraph(startNode, leaves, availableActions, goal, 0);

        if (!found || leaves.Count == 0)
        {
            if (VerboseLogging)
                Debug.Log($"<color=red>[GOAP]</color> Aucun plan trouvé pour le goal '{goal.GoalName}'.");
            return null;
        }

        // Trouver le plan le moins coûteux (scan linéaire au lieu d'OrderBy().First() pour éviter l'allocation LINQ)
        PlanNode cheapest = leaves[0];
        for (int i = 1; i < leaves.Count; i++)
        {
            if (leaves[i].RunningCost < cheapest.RunningCost)
                cheapest = leaves[i];
        }

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

        if (VerboseLogging)
        {
            var names = new string[plan.Count];
            for (int i = 0; i < plan.Count; i++) names[i] = plan[i].ActionName;
            Debug.Log($"<color=green>[GOAP]</color> Plan trouvé pour '{goal.GoalName}' : {string.Join(" → ", names)}");
        }

        return new Queue<GoapAction>(plan);
    }

    /// <summary>
    /// Construit récursivement le graphe de plans possibles (forward search).
    /// Uses a shared `_usedActions` set with backtracking to avoid allocating a filtered list
    /// at every recursive call (previously `availableActions.Where(a => a != action).ToList()`
    /// allocated up to O(actions^depth) lists during a single Plan).
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
            // Skip actions déjà utilisées sur le chemin courant de la recherche
            if (_usedActions.Contains(action))
                continue;

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
                _usedActions.Add(action);
                if (BuildGraph(node, leaves, availableActions, goal, depth + 1))
                {
                    foundPlan = true;
                }
                _usedActions.Remove(action); // backtrack
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
