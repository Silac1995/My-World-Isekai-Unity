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
                _character.CharacterInteraction.PerformInteraction(action);
            }
        });

        options.Add(new InteractionOption
        {
            Name = "Insult",
            Action = () =>
            {
                var action = new InteractionInsult();
                _character.CharacterInteraction.PerformInteraction(action);
            }
        });

        return options;
    }
}