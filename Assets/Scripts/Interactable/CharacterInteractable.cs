using UnityEngine;

public class CharacterInteractable : InteractableObject
{
    [SerializeField] private Character _character;
    public Character Character => _character;

    // Flag pour savoir si ce personnage est dj en train de parler/interagir
    private bool _isBusy = false;

    public override void Interact(Character interactor)
    {
        if (interactor == null || _character == null) return;

        // --- VRIFICATION ATOMIQUE ---
        if (_isBusy)
        {
            Debug.Log($"<color=orange>[Interaction]</color> {interactor.CharacterName} essaie de parler  {_character.CharacterName} mais il est dj occup !");
            return;
        }

        // On bloque l'accs immdiatement
        _isBusy = true;

        Debug.Log($"<color=cyan>[Interaction]</color> {interactor.CharacterName} commence une interaction exclusive avec {_character.CharacterName}");

        ICharacterInteractionAction firstAction = null;
        if (interactor.IsPlayer())
        {
            firstAction = new InteractionTalk();
        }

        var startAction = new CharacterStartInteraction(interactor, _character, firstAction);

        // On excute l'action
        interactor.CharacterActions.ExecuteAction(startAction);
    }

    /// <summary>
    ///  appeler quand l'interaction/dialogue se termine pour librer le personnage.
    /// </summary>
    public void Release()
    {
        _isBusy = false;
        Debug.Log($"<color=grey>[Interaction]</color> {_character.CharacterName} est maintenant libre.");
    }

    public override System.Collections.Generic.List<InteractionOption> GetHoldInteractionOptions(Character interactor)
    {
        var options = new System.Collections.Generic.List<InteractionOption>();

        options.Add(new InteractionOption {
            Name = "Talk",
            Action = () => {
                ICharacterInteractionAction talkAction = new InteractionTalk();
                interactor.CharacterInteraction.PerformInteraction(talkAction);
            }
        });

        options.Add(new InteractionOption {
            Name = "Follow",
            Action = () => {
                ICharacterInteractionAction followAction = new InteractionAskToFollow();
                interactor.CharacterInteraction.PerformInteraction(followAction);
            }
        });

        options.Add(new InteractionOption {
            Name = "Insult",
            Action = () => {
                ICharacterInteractionAction insultAction = new InteractionInsult();
                interactor.CharacterInteraction.PerformInteraction(insultAction);
            }
        });

        options.Add(new InteractionOption {
            Name = "Fight",
            Action = () => {
                interactor.CharacterCombat.StartFight(_character);
            }
        });

        return options;
    }
}