using UnityEngine;

/// <summary>
/// Proposes to start a dialogue exchange. If accepted, initiates the full Interaction sequence.
/// </summary>
public class InteractionStartDialogue : InteractionInvitation
{
    public override bool CanExecute(Character source, Character target)
    {
        // For example, they shouldn't already be interacting or fighting.
        if (target.CharacterInteraction.IsInteracting) return false;
        if (source.CharacterInteraction.IsInteracting) return false;
        return true;
    }

    public override string GetInvitationMessage(Character source, Character target)
    {
        return "Can we talk a moment?";
    }

    public override void OnAccepted(Character source, Character target)
    {
        // When accepted, directly lock them into the interaction WITHOUT another forced action,
        // so `DialogueSequence` hits Tour 0 smoothly.
        // The source initiated the dialogue, so the source starts the interaction
        source.CharacterInteraction.StartInteractionWith(target, null);
    }

    public override string GetAcceptMessage() => "Sure thing!";

    public override string GetRefuseMessage() => "I'm a bit busy right now.";

    public override void OnRefused(Character source, Character target)
    {
        Debug.Log($"<color=yellow>[Dialogue]</color> {target.CharacterName} refused to chat with {source.CharacterName}.");
    }
}
