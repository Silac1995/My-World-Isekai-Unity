using UnityEngine;

public class CharacterInteractable : InteractableObject
{
    [SerializeField] private Character _character;
    public Character Character => _character;

    // Flag pour savoir si ce personnage est déjà en train de parler/interagir
    private bool _isBusy = false;

    public override void Interact(Character interactor)
    {
        if (interactor == null || _character == null) return;

        // --- VÉRIFICATION ATOMIQUE ---
        if (_isBusy)
        {
            Debug.Log($"<color=orange>[Interaction]</color> {interactor.CharacterName} essaie de parler à {_character.CharacterName} mais il est déjà occupé !");
            return;
        }

        // On bloque l'accès immédiatement
        _isBusy = true;

        Debug.Log($"<color=cyan>[Interaction]</color> {interactor.CharacterName} commence une interaction exclusive avec {_character.CharacterName}");

        var startAction = new CharacterStartInteraction(interactor, _character);

        // On exécute l'action
        interactor.CharacterActions.ExecuteAction(startAction);
    }

    /// <summary>
    /// À appeler quand l'interaction/dialogue se termine pour libérer le personnage.
    /// </summary>
    public void Release()
    {
        _isBusy = false;
        Debug.Log($"<color=grey>[Interaction]</color> {_character.CharacterName} est maintenant libre.");
    }
}