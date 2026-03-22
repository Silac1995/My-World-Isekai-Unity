using UnityEngine;

public class CharacterStartInteraction : CharacterAction
{
    private Character _target;
    private ICharacterInteractionAction _forcedAction;

    // On passe par base(character, 0f) car l'interaction est instantanee
    public CharacterStartInteraction(Character character, Character target, ICharacterInteractionAction forcedAction = null) : base(character, 0f)
    {
        _target = target ?? throw new System.ArgumentNullException(nameof(target));
        _forcedAction = forcedAction;
    }

    public override void OnStart()
    {
        if (_target == null)
        {
            Finish();
            return;
        }

        // 1. Visuel : Se faire face
        character.CharacterVisual?.FaceTarget(_target.transform.position);
        _target.CharacterVisual?.FaceTarget(character.transform.position);

        // 2. Logique : If the action is an invitation (e.g. InteractionStartDialogue),
        //    send it to the server. The server must handle it so the target
        //    NPC freezes and responds natively.
        if (_forcedAction is InteractionInvitation invitation)
        {
            if (character.IsOwner && !character.IsServer)
            {
                character.CharacterInteraction.RequestInvitationServerRpc(_target.NetworkObject.NetworkObjectId, invitation.GetType().AssemblyQualifiedName);
            }
            else if (character.IsServer)
            {
                if (invitation.CanExecute(character, _target))
                {
                    invitation.Execute(character, _target);
                }
            }
        }
        else
        {
            character.CharacterInteraction.StartInteractionWith(_target, _forcedAction);
        }

        Finish();
    }

    public override void OnApplyEffect()
    {
        // Rien a faire ici pour une interaction de dialogue
    }
}