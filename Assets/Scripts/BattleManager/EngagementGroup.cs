using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents one of the two factions inside a local skirmish (CombatEngagement).
/// Maintains the list of active fighters on this side and manages their spatial formation
/// around the opposing group.
/// </summary>
public class EngagementGroup
{
    private List<Character> _members = new List<Character>();
    public IReadOnlyList<Character> Members => _members;
    
    // The formation used to place this group's members around the targets
    public CombatFormation Formation { get; private set; }

    public EngagementGroup()
    {
        // Each group owns its own formation, which will be initialized/centered
        // by the CombatEngagement based on the opposing group's position.
        Formation = new CombatFormation();
    }

    /// <summary>
    /// Adds a character to this engagement group. Formation positions are calculated
    /// dynamically by GetOrganicPosition — no slot reservation needed.
    /// </summary>
    public void AddMember(Character character)
    {
        if (character != null && !_members.Contains(character))
        {
            _members.Add(character);
        }
    }

    /// <summary>
    /// Removes a character from the engagement (target change, death, etc.)
    /// and releases their formation slot.
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
    /// Returns the number of alive members in this group.
    /// </summary>
    public int AliveCount
    {
        get
        {
            int count = 0;
            foreach (Character member in _members)
            {
                if (member != null && member.IsAlive())
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Checks whether this group is entirely empty or whether all of its members are out of combat.
    /// </summary>
    public bool IsWipedOut()
    {
        if (_members.Count == 0) return true;

        foreach (var member in _members)
        {
            if (member != null && member.IsAlive())
            {
                return false; // At least one active survivor
            }
        }

        return true; // All are dead or unconscious
    }

    /// <summary>
    /// Computes the geometric center of this group to act as a central focal point.
    /// Returns false if the group is empty.
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
