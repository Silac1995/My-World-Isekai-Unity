using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Représente une "mêlée" ou une "escarmouche" locale entre deux groupes de combattants.
/// Gère la spatialisation réciproque (Group A encercle le centre de Group B, et vice versa).
/// </summary>
public class CombatEngagement
{
    // Les deux camps qui composent cette escarmouche
    public EngagementGroup GroupA { get; private set; }
    public EngagementGroup GroupB { get; private set; }

    // On garde la notion d'équipes globales pour savoir qui va où
    private BattleTeam _teamA;
    private BattleTeam _teamB;

    public CombatEngagement(BattleTeam teamA, BattleTeam teamB)
    {
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

