using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Représente une "mêlée" ou une "escarmouche" locale entre deux groupes de combattants.
/// Gère la spatialisation réciproque (Group A encercle le centre de Group B, et vice versa).
/// </summary>
public class CombatEngagement
{
    public const int MAX_PARTICIPANTS_PER_SIDE = 6;

    /// <summary>
    /// Maximum distance a participant should stray from the engagement anchor point.
    /// Used by tactical pacing and formation systems to keep fighters within the engagement area.
    /// </summary>
    private const float LEASH_RADIUS = 15f;
    public float LeashRadius => LEASH_RADIUS;

    // Les deux camps qui composent cette escarmouche
    public EngagementGroup GroupA { get; private set; }
    public EngagementGroup GroupB { get; private set; }

    // On garde la notion d'équipes globales pour savoir qui va où
    private BattleTeam _teamA;
    private BattleTeam _teamB;

    public BattleTeam TeamA => _teamA;
    public BattleTeam TeamB => _teamB;

    public BattleManager Manager { get; private set; }

    /// <summary>
    /// The spatial anchor point for this engagement. Used by formations and tactical positioning.
    /// Set when the engagement is created or when characters reorganize.
    /// </summary>
    public Vector3 AnchorPoint { get; private set; }

    public void SetAnchorPoint(Vector3 point)
    {
        AnchorPoint = point;
    }

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

        bool inGroupA = GroupA.Members.Contains(participant);
        EngagementGroup myGroup = inGroupA ? GroupA : GroupB;
        EngagementGroup opponentGroup = inGroupA ? GroupB : GroupA;

        if (!opponentGroup.TryGetCenter(out Vector3 opponentCenter))
            opponentCenter = AnchorPoint;

        // GroupA on left (-1), GroupB on right (+1)
        float teamSideSign = inGroupA ? -1f : 1f;

        return myGroup.Formation.GetOrganicPosition(
            participant, myGroup.Members, opponentCenter, AnchorPoint, teamSideSign);
    }

    /// <summary>
    /// Returns the ratio of alive members on the character's side vs the opposing side.
    /// A ratio > 1 means the character's side outnumbers the opponents.
    /// Returns float.MaxValue if no opponents are alive.
    /// </summary>
    public float GetOutnumberRatio(Character character)
    {
        bool inGroupA = GroupA.Members.Contains(character);
        int myCount = inGroupA ? GroupA.AliveCount : GroupB.AliveCount;
        int theirCount = inGroupA ? GroupB.AliveCount : GroupA.AliveCount;

        if (theirCount == 0) return float.MaxValue;
        return (float)myCount / theirCount;
    }

    /// <summary>
    /// Returns the center position of the opposing group for the given character.
    /// Falls back to the engagement anchor point if the opponent group has no alive members.
    /// </summary>
    public Vector3 GetOpponentCenter(Character character)
    {
        bool inGroupA = GroupA.Members.Contains(character);
        EngagementGroup opponents = inGroupA ? GroupB : GroupA;
        return opponents.TryGetCenter(out Vector3 center) ? center : AnchorPoint;
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

