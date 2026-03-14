using System.Collections.Generic;
using UnityEngine;

public abstract class CharacterNeed
{
    protected Character _character;

    public CharacterNeed(Character character)
    {
        _character = character;
    }

    // Le besoin est-il actif ? (Ex: IsNaked() == true)
    public abstract bool IsActive();

    // Quelle est l'urgence ? (0 = rien, 100 = vital)
    public abstract float GetUrgency();

    /// <summary>
    /// Fournit le but GOAP correspondant à ce besoin.
    /// Sera lu et injecté dynamiquement par le CharacterGoapController.
    /// </summary>
    public abstract GoapGoal GetGoapGoal();

    /// <summary>
    /// Fournit les actions GOAP capables de résoudre ce besoin.
    /// Seront lues et injectées dynamiquement par le CharacterGoapController.
    /// </summary>
    public abstract List<GoapAction> GetGoapActions();
}
