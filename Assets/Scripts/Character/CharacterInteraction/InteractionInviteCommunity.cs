using UnityEngine;

public class InteractionInviteCommunity : ICharacterInteractionAction
{
    public bool CanExecute(Character initiator, Character target)
    {
        // Initiator must lead a community
        if (initiator.CharacterCommunity == null || 
            initiator.CharacterCommunity.CurrentCommunity == null || 
            initiator.CharacterCommunity.CurrentCommunity.leader != initiator)
        {
            return false;
        }

        // Target must NOT be in a community
        if (target.CharacterCommunity == null || target.CharacterCommunity.CurrentCommunity != null)
        {
            return false;
        }

        // Target must be a Friend, Lover or Soulmate
        if (initiator.CharacterRelation != null)
        {
            return initiator.CharacterRelation.IsFriend(target);
        }

        return false;
    }

    public void Execute(Character initiator, Character target)
    {
        Debug.Log($"<color=yellow>[Interaction]</color> {initiator.CharacterName} invites {target.CharacterName} to join their community '{initiator.CharacterCommunity.CurrentCommunity.communityName}'!");
        
        // Initiator says the line
        if (initiator.CharacterSpeech != null)
        {
            initiator.CharacterSpeech.Say("Wanna join my community?");
        }

        // Target joins the community
        if (target.CharacterCommunity != null)
        {
            target.CharacterCommunity.JoinCommunity(initiator.CharacterCommunity.CurrentCommunity);
        }
    }
}
