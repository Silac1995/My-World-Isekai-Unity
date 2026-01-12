using UnityEngine;

public class CharacterInteractable : InteractableObject
{
    [SerializeField] private Character _character; // Le personnage attaché à ce script

    public Character Character => _character;

    // Mise à jour de la signature avec l'interactor
    public override void Interact(Character interactor)
    {
        if (interactor == null || _character == null) return;

        Debug.Log($"<color=cyan>[Interaction]</color> {interactor.CharacterName} lance une interaction avec {_character.CharacterName}");

        // On crée l'action en passant bien les deux protagonistes
        // interactor = celui qui a appuyé sur E
        // _character = celui qui possède ce script Interactable
        var startAction = new CharacterStartInteraction(interactor, _character);

        // On demande au système d'actions de l'initiateur d'exécuter l'interaction
        interactor.CharacterActions.ExecuteAction(startAction);
    }
}