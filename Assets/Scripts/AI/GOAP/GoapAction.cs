using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP abstraite.
/// Chaque action définit ses préconditions (ce qui doit être vrai avant),
/// ses effets (ce qui change après), et sa logique d'exécution.
/// Le GoapPlanner utilise ces infos pour construire un plan d'actions.
/// </summary>
public abstract class GoapAction
{
    /// <summary>Nom lisible de l'action (debug/log)</summary>
    public abstract string ActionName { get; }

    /// <summary>Préconditions : états qui doivent être vrais pour que l'action soit possible</summary>
    public abstract Dictionary<string, bool> Preconditions { get; }

    /// <summary>Effets : états modifiés après exécution réussie de l'action</summary>
    public abstract Dictionary<string, bool> Effects { get; }

    /// <summary>Coût de l'action (le planner préfère les plans à moindre coût)</summary>
    public virtual float Cost => 1f;

    /// <summary>L'action est-elle terminée ? (succès ou échec)</summary>
    public abstract bool IsComplete { get; }

    /// <summary>
    /// Validation runtime : vérifie que l'action est encore faisable
    /// (ex: la cible existe encore, le chemin est accessible...).
    /// Appelé avant chaque Execute().
    /// </summary>
    public abstract bool IsValid(Character worker);

    /// <summary>
    /// Exécuté chaque tick tant que l'action n'est pas complete.
    /// </summary>
    public abstract void Execute(Character worker);

    /// <summary>
    /// Appelé quand l'action se termine (succès ou interruption).
    /// Cleanup des états temporaires.
    /// </summary>
    public virtual void Exit(Character worker) { }

    /// <summary>
    /// Vérifie si les préconditions sont satisfaites par le world state donné.
    /// </summary>
    public bool ArePreconditionsMet(Dictionary<string, bool> worldState)
    {
        foreach (var precondition in Preconditions)
        {
            if (!worldState.TryGetValue(precondition.Key, out bool value) || value != precondition.Value)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Applique les effets de cette action sur un world state (copie).
    /// Utilisé par le planner pour simuler l'exécution.
    /// </summary>
    public Dictionary<string, bool> ApplyEffects(Dictionary<string, bool> worldState)
    {
        var newState = new Dictionary<string, bool>(worldState);
        foreach (var effect in Effects)
        {
            newState[effect.Key] = effect.Value;
        }
        return newState;
    }
}
