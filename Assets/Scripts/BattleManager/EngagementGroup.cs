using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Représente l'une des deux factions au sein d'une escarmouche locale (CombatEngagement).
/// Maintient la liste des combattants actifs de ce côté et gère leur formation spatiale
/// autour du groupe adverse.
/// </summary>
public class EngagementGroup
{
    private List<Character> _members = new List<Character>();
    public IReadOnlyList<Character> Members => _members;
    
    // La formation utilisée pour placer les membres de ce groupe autour des cibles
    public CombatFormation Formation { get; private set; }

    public EngagementGroup()
    {
        // Chaque groupe possède sa propre formation, qui sera initialisée/centrée
        // par le CombatEngagement en fonction de la position du groupe adverse.
        Formation = new CombatFormation();
    }

    /// <summary>
    /// Ajoute un personnage à ce groupe d'engagement et lui réserve un slot de formation.
    /// </summary>
    public void AddMember(Character character)
    {
        if (character != null && !_members.Contains(character))
        {
            _members.Add(character);
            Formation.AddCharacter(character);
        }
    }

    /// <summary>
    /// Retire un personnage de l'engagement (changement de cible, mort, etc.)
    /// et libère son slot de formation.
    /// </summary>
    public void RemoveMember(Character character)
    {
        if (_members.Contains(character))
        {
            _members.Remove(character);
            Formation.RemoveCharacter(character);
        }
    }

    /// <summary>
    /// Vérifie si ce groupe est entièrement vide ou si tous ses membres sont hors-combat.
    /// </summary>
    public bool IsWipedOut()
    {
        if (_members.Count == 0) return true;
        
        foreach (var member in _members)
        {
            if (member != null && member.IsAlive())
            {
                return false; // Au moins un survivant actif
            }
        }
        
        return true; // Tous sont morts ou inconscients
    }

    /// <summary>
    /// Calcule le centre géométrique de ce groupe pour servir de point focal central.
    /// Renvoie false si le groupe est vide.
    /// </summary>
    public bool TryGetCenter(out Vector3 center)
    {
        center = Vector3.zero;
        int activeCount = 0;

        foreach (var member in _members)
        {
            if (member != null && member.IsAlive())
            {
                center += member.transform.position;
                activeCount++;
            }
        }

        if (activeCount > 0)
        {
            center /= activeCount;
            return true;
        }

        return false;
    }
}
