using UnityEngine;

public abstract class InteractableObject : MonoBehaviour
{
    [Header("Interaction")]
    public string interactionPrompt = "Press E to interact";

    // Optionnel pour le survol UI
    public virtual void OnCharacterEnter() { }

    public virtual void OnCharacterExit() { }

    // Appelé quand le joueur appuie sur E
    public abstract void Interact();
}
