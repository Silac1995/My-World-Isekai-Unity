using UnityEngine;

public abstract class InteractableObject : MonoBehaviour
{
    [Header("Interaction")]
    public string interactionPrompt = "Press E to interact";

    // On passe le Character qui déclenche l'action
    public abstract void Interact(Character interactor);

    // On peut aussi mettre à jour les méthodes de survol pour savoir qui survole
    public virtual void OnCharacterEnter(Character interactor) { }
    public virtual void OnCharacterExit(Character interactor) { }
}