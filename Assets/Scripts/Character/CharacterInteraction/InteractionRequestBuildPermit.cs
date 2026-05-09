using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// A non-leader character requests permission from a community leader to place buildings
/// inside the community zone. On acceptance, a BuildPermit is granted with the approved count.
/// </summary>
public class InteractionRequestBuildPermit : InteractionInvitation
{
    private readonly CommunityData _community;
    private readonly int _requestedCount;

    public InteractionRequestBuildPermit(CommunityData community, int requestedCount = 1)
    {
        _community = community;
        _requestedCount = Mathf.Max(1, requestedCount);
    }

    public override bool CanExecute(Character source, Character target)
    {
        if (_community == null) return false;

        // Target must be a leader of the community
        if (!_community.IsLeader(target.CharacterId)) return false;

        // Source must NOT already be a leader (leaders don't need permits)
        if (_community.IsLeader(source.CharacterId)) return false;

        // Source must not already have a permit for this zone
        if (_community.HasPermit(source.CharacterId)) return false;

        return true;
    }

    public override string GetInvitationMessage(Character source, Character target)
    {
        return $"I'd like to build in your community. May I place {_requestedCount} building{(_requestedCount > 1 ? "s" : "")} here?";
    }

    public override bool? EvaluateCustomInvitation(Character source, Character target)
    {
        // NPC leaders evaluate based on relationship
        if (target.CharacterRelation != null)
        {
            var relation = target.CharacterRelation.GetRelationshipWith(source);
            float relationValue = relation?.RelationValue ?? 0f;
            // Positive relationship = approve, negative = reject
            return relationValue >= 0f;
        }

        // Fallback to default sociability check
        return null;
    }

    public override void OnAccepted(Character source, Character target)
    {
        _community.GrantPermit(source.CharacterId, target.CharacterId, _requestedCount);
        Debug.Log($"<color=green>[BuildPermit]</color> {target.CharacterName} granted {source.CharacterName} a permit for {_requestedCount} building(s) in '{_community.MapId}'.");
    }

    public override string GetAcceptMessage()
    {
        return $"Of course, you may build here. I'm granting you {_requestedCount} placement{(_requestedCount > 1 ? "s" : "")}.";
    }

    public override string GetRefuseMessage()
    {
        return "I'm sorry, but I can't allow you to build here right now.";
    }

    public override void OnRefused(Character source, Character target)
    {
        Debug.Log($"<color=orange>[BuildPermit]</color> {target.CharacterName} denied {source.CharacterName}'s request to build in '{_community.MapId}'.");
    }
}
