using UnityEngine;

public abstract class InteractableObject : MonoBehaviour
{
    [Header("Interaction")]
    public string interactionPrompt = "Press E to interact";
    [SerializeField] private Collider _interactionZone;
    [SerializeField] private GameObject _rootgameObject;

    public GameObject RootGameObject
    {
        get
        {
            // Si la variable est vide, on prend le root par défaut
            if (_rootgameObject == null)
                _rootgameObject = transform.root.gameObject;
            return _rootgameObject;
        }
    }
    public Collider InteractionZone => _interactionZone;

    // On passe le Character qui déclenche l'action
    public abstract void Interact(Character interactor);

    // On peut aussi mettre à jour les méthodes de survol pour savoir qui survole
    public virtual void OnCharacterEnter(Character interactor) { }
    public virtual void OnCharacterExit(Character interactor) { }
}