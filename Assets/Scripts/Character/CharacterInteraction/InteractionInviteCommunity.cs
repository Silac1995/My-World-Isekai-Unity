using UnityEngine;

/// <summary>
/// Invitation action to recruit a friend into the leader's community.
/// </summary>
public class InteractionInviteCommunity : InteractionInvitation
{
    public override bool CanExecute(Character source, Character target)
    {
        // Source must lead a community
        if (source.CharacterCommunity == null || 
            source.CharacterCommunity.CurrentCommunity == null || 
            source.CharacterCommunity.CurrentCommunity.leader != source)
        {
            return false;
        }

        // Target must NOT be in a community
        if (target.CharacterCommunity == null || target.CharacterCommunity.CurrentCommunity != null)
        {
            return false;
        }

        // Target must be a Friend, Lover or Soulmate
        if (source.CharacterRelation != null)
        {
            return source.CharacterRelation.IsFriend(target);
        }

        return false;
    }

    public override string GetInvitationMessage(Character source, Character target)
    {
        return $"Hey {target.CharacterName}, wanna join my community?";
    }

    public override string GetAcceptMessage() => "Sure, I'd love to join!";
    public override string GetRefuseMessage() => "Hmm, not right now...";

    public override void OnAccepted(Character source, Character target)
    {
        if (target.CharacterCommunity != null && source.CharacterCommunity != null)
        {
            target.CharacterCommunity.JoinCommunity(source.CharacterCommunity.CurrentCommunity);
        }
    }

    public override void OnRefused(Character source, Character target)
    {
        // Small relation loss on refusal
        if (source.CharacterRelation != null)
        {
            source.CharacterRelation.UpdateRelation(target, -2);
        }
    }
}
