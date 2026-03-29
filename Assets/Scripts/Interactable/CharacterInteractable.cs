using UnityEngine;
using System.Collections.Generic;

public class CharacterInteractable : InteractableObject
{
    [SerializeField] private Character _character;
    public Character Character => _character;

    // Flag pour savoir si ce personnage est dj en train de parler/interagir
    private bool _isBusy = false;

    public override void Interact(Character interactor)
    {
        if (interactor == null || _character == null) return;

        Debug.Log($"<color=cyan>[Interaction]</color> {interactor.CharacterName} commence une interaction exclusive avec {_character.CharacterName}");

        if (interactor.IsPlayer())
        {
            var startAction = new CharacterStartInteraction(interactor, _character, new InteractionStartDialogue());
            interactor.CharacterActions.ExecuteAction(startAction);
        }
    }

    /// <summary>
    ///  appeler quand l'interaction/dialogue se termine pour librer le personnage.
    /// </summary>
    public void Release()
    {
        _isBusy = false;
        Debug.Log($"<color=grey>[Interaction]</color> {_character.CharacterName} est maintenant libre.");
    }

    public override List<InteractionOption> GetHoldInteractionOptions(Character interactor)
    {
        if (_character == null) return null;

        var options = new List<InteractionOption>();

        // For now, Follow and Spontaneous Talk are available outside of interaction.
        options.Add(new InteractionOption
        {
            Name = "Follow Me",
            Action = () =>
            {
                Debug.Log($"{interactor.CharacterName} is asking {_character.CharacterName} to follow them.");
            }
        });

        options.Add(new InteractionOption
        {
            Name = "Greet",
            Action = () =>
            {
                var action = new InteractionSpontaneousTalk();
                action.Execute(interactor, _character);
            }
        });

        // Party invitation — only if interactor is a party leader (or has Leadership and can create one)
        if (interactor.CharacterParty != null)
        {
            CharacterParty interactorParty = interactor.CharacterParty;
            bool isLeader = interactorParty.IsPartyLeader;
            bool canCreateAndInvite = !interactorParty.IsInParty
                && interactorParty.LeadershipSkill != null
                && interactor.CharacterSkills != null
                && interactor.CharacterSkills.HasSkill(interactorParty.LeadershipSkill);

            if (isLeader || canCreateAndInvite)
            {
                var invitation = new PartyInvitation(interactorParty.LeadershipSkill);
                if (invitation.CanExecute(interactor, _character))
                {
                    Character targetRef = _character;
                    options.Add(new InteractionOption
                    {
                        Name = "Invite to Party",
                        Action = () =>
                        {
                            ulong targetNetId = targetRef.NetworkObject != null
                                ? targetRef.NetworkObject.NetworkObjectId : 0;
                            interactorParty.RequestInviteToPartyServerRpc(targetNetId);
                        }
                    });
                }
            }
        }

        return options;
    }

    public override List<InteractionOption> GetDialogueInteractionOptions(Character interactor)
    {
        if (_character == null || _character.CharacterInteraction == null) return null;

        var options = new List<InteractionOption>();

        options.Add(new InteractionOption
        {
            Name = "Talk",
            Action = () =>
            {
                var action = new InteractionTalk();
                interactor.CharacterInteraction.PerformInteraction(action);
            }
        });

        options.Add(new InteractionOption
        {
            Name = "Insult",
            Action = () =>
            {
                var action = new InteractionInsult();
                interactor.CharacterInteraction.PerformInteraction(action);
            }
        });

        options.Add(new InteractionOption
        {
            Name = "Goodbye",
            Action = () =>
            {
                var action = new InteractionGoodbye();
                interactor.CharacterInteraction.PerformInteraction(action);
            }
        });

        return options;
    }
}