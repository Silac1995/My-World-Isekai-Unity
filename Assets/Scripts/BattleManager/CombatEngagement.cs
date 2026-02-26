using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Représente une "mêlée" ou une "escarmouche" locale entre deux groupes de combattants.
/// Gère la spatialisation réciproque (Group A encercle le centre de Group B, et vice versa).
/// </summary>
public class CombatEngagement
{
    public const int MAX_PARTICIPANTS_PER_SIDE = 6;

    // Les deux camps qui composent cette escarmouche
    public EngagementGroup GroupA { get; private set; }
    public EngagementGroup GroupB { get; private set; }

    // On garde la notion d'équipes globales pour savoir qui va où
    private BattleTeam _teamA;
    private BattleTeam _teamB;

    public BattleTeam TeamA => _teamA;
    public BattleTeam TeamB => _teamB;

    public BattleManager Manager { get; private set; }

    public CombatEngagement(BattleManager manager, BattleTeam teamA, BattleTeam teamB)
    {
        Manager = manager;
        _teamA = teamA;
        _teamB = teamB;

        GroupA = new EngagementGroup();
        GroupB = new EngagementGroup();
    }

    /// <summary>
    /// Un personnage rejoint l'escarmouche. Il est placé dans le groupe
    /// correspondant à son équipe dans le BattleManager.
    /// </summary>
    public void JoinEngagement(Character participant)
    {
        if (_teamA.ContainsCharacter(participant))
        {
            GroupA.AddMember(participant);
            // Debug.Log($"<color=cyan>[Engagement]</color> {participant.CharacterName} rejoint le Group A de l'escarmouche.");
        }
        else if (_teamB.ContainsCharacter(participant))
        {
            GroupB.AddMember(participant);
            // Debug.Log($"<color=cyan>[Engagement]</color> {participant.CharacterName} rejoint le Group B de l'escarmouche.");
        }
    }

    /// <summary>
    /// Le personnage quitte l'engagement.
    /// </summary>
    public void LeaveEngagement(Character participant)
    {
        if (_teamA.ContainsCharacter(participant)) GroupA.RemoveMember(participant);
        else if (_teamB.ContainsCharacter(participant)) GroupB.RemoveMember(participant);
    }

    /// <summary>
    /// L'engagement a-t-il encore un sens ? (L'un des deux camps est vide ou mort)
    /// </summary>
    public bool IsFinished()
    {
        return GroupA.IsWipedOut() || GroupB.IsWipedOut();
    }

    /// <summary>
    /// Vérifie si l'engagement est plein pour une équipe spécifique.
    /// Si le camp adverse n'a qu'un seul personnage, l'engagement n'est jamais considéré comme plein
    /// car on ne peut pas le diviser.
    /// </summary>
    public bool IsFullFor(BattleTeam team)
    {
        if (team == _teamA)
        {
            if (GroupB.Members.Count <= 1) return false;
            return GroupA.Members.Count >= MAX_PARTICIPANTS_PER_SIDE;
        }
        else if (team == _teamB)
        {
            if (GroupA.Members.Count <= 1) return false;
            return GroupB.Members.Count >= MAX_PARTICIPANTS_PER_SIDE;
        }
        return false;
    }

    /// <summary>
    /// Vérifie si l'engagement doit être séparé en deux (une des deux équipes dépasse la limite,
    /// et l'autre équipe a plus d'un combattant).
    /// </summary>
    public bool NeedsSplit()
    {
        bool aIsFull = GroupA.Members.Count > MAX_PARTICIPANTS_PER_SIDE;
        bool bIsFull = GroupB.Members.Count > MAX_PARTICIPANTS_PER_SIDE;
        bool aCanSplit = GroupA.Members.Count > 1;
        bool bCanSplit = GroupB.Members.Count > 1;

        return (aIsFull && bCanSplit) || (bIsFull && aCanSplit);
    }

    /// <summary>
    /// Donne la coordonnée de front assignée à ce combattant.
    /// Il est positionné par SA formation, autour du centre du GROUPE ADVERSE.
    /// </summary>
    public Vector3 GetAssignedPosition(Character participant)
    {
        if (participant == null) return Vector3.zero;

        // Si je suis dans l'Equipe A, je me place par rapport au centre du Groupe B
        if (_teamA.ContainsCharacter(participant))
        {
            if (GroupB.TryGetCenter(out Vector3 targetCenter))
            {
                return GroupA.Formation.GetWorldPosition(participant, targetCenter);
            }
        }
        else if (_teamB.ContainsCharacter(participant))
        {
            if (GroupA.TryGetCenter(out Vector3 targetCenter))
            {
                return GroupB.Formation.GetWorldPosition(participant, targetCenter);
            }
        }

        // Fallback: Si je n'arrive pas à calculer (l'autre équipe n'a pas de centre), je reste sur place
        return participant.transform.position;
    }

    /// <summary>
    /// Renvoie l'ennemi le plus proche dans le groupe opposé.
    /// Pratique pour l'IA si sa cible meurt au sein du même engagement.
    /// </summary>
    public Character GetClosestOpponent(Character participant)
    {
        EngagementGroup opponentGroup = _teamA.ContainsCharacter(participant) ? GroupB : GroupA;

        
        float minDistance = float.MaxValue;
        Character closest = null;

        foreach (var enemy in opponentGroup.Members)
        {
            if (enemy == null || !enemy.IsAlive()) continue;

            float dist = Vector3.Distance(participant.transform.position, enemy.transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                closest = enemy;
            }
        }

        return closest;
    }
}

