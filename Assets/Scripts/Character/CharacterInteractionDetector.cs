using UnityEngine;

public abstract class CharacterInteractionDetector : MonoBehaviour
{
    protected InteractableObject currentTarget;
    private Character selfCharacter; // Référence au Character local
    public Character Character => selfCharacter;

    protected virtual void Awake()
    {
        selfCharacter = GetComponent<Character>();
        if (selfCharacter == null)
        {
            Debug.LogError("Le composant Character est manquant sur ce GameObject.", this);
        }
    }

    protected virtual void OnTriggerEnter(Collider other)
    {
        if (selfCharacter == null)
        {
            Debug.LogError("selfCharacter est null. Assure-toi qu'un composant Character est attaché.", this);
            return;
        }

        if (other.gameObject == selfCharacter.gameObject)
        {
            Debug.Log("Collision avec soi-même ignorée.", this);
            return;
        }

        if (other.TryGetComponent(out InteractableObject interactable))
        {
            currentTarget = interactable;
            if (currentTarget != null)
            {
                currentTarget.OnCharacterEnter();
                Debug.Log($"Cible détectée : {interactable.name}", this);
            }
            else
            {
                Debug.LogError("InteractableObject détecté, mais currentTarget est null.", this);
            }
        }
    }

    protected virtual void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent(out InteractableObject interactable) && interactable == currentTarget)
        {
            if (currentTarget != null)
            {
                currentTarget.OnCharacterExit();
                Debug.Log($"Cible {interactable.name} sortie.", this);
            }
            currentTarget = null;
        }
    }
}