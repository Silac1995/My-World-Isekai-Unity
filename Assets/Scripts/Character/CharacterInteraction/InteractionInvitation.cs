using UnityEngine;

/// <summary>
/// Abstract base class for all invitation-type interactions.
/// Uses the Template Method pattern: Execute sends the invitation,
/// the target's CharacterInvitation handles the delayed response.
/// </summary>
public abstract class InteractionInvitation : ICharacterInteractionAction
{
    /// <summary>
    /// Template Method: source says the invitation, then the target's
    /// CharacterInvitation component handles the delayed response.
    /// </summary>
    public void Execute(Character source, Character target)
    {
        // 1. Source says the invitation
        string question = GetInvitationMessage(source, target);
        if (source.CharacterSpeech != null)
        {
            source.CharacterSpeech.Say(question);
        }

        // 2. Send the invitation to the target's CharacterInvitation component
        // It will handle the delay + evaluation + response
        if (target.CharacterInvitation != null)
        {
            target.CharacterInvitation.ReceiveInvitation(this, source);
        }
        else
        {
            // No CharacterInvitation component → treated as a refusal
            Debug.LogWarning($"<color=orange>[Invitation]</color> {target.CharacterName} has no CharacterInvitation component, invitation ignored.");
            OnRefused(source, target);
        }
    }

    /// <summary>
    /// Checks if this invitation can be executed (preconditions).
    /// </summary>
    public abstract bool CanExecute(Character source, Character target);

    /// <summary>
    /// The message the source character says when proposing the invitation.
    /// </summary>
    public abstract string GetInvitationMessage(Character source, Character target);

    /// <summary>
    /// Called when the target accepts the invitation.
    /// </summary>
    public abstract void OnAccepted(Character source, Character target);

    /// <summary>
    /// The message the target says when accepting. Override for custom messages.
    /// </summary>
    public virtual string GetAcceptMessage() => "Sure, why not!";

    /// <summary>
    /// The message the target says when refusing. Override for custom messages.
    /// </summary>
    public virtual string GetRefuseMessage() => "No thanks.";

    /// <summary>
    /// Called when the target refuses. Override to add penalties (e.g. relation loss).
    /// </summary>
    public virtual void OnRefused(Character source, Character target) { }

    /// <summary>
    /// Optional custom evaluation logic for specific interactions. 
    /// Return null to fallback to CharacterInvitation's default sociability check.
    /// </summary>
    public virtual bool? EvaluateCustomInvitation(Character source, Character target) => null;
}
