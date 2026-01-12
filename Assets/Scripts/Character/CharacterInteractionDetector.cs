using UnityEngine;

public abstract class CharacterInteractionDetector : MonoBehaviour
{
    protected InteractableObject _currentInteractableObjectTarget;
    [SerializeField] private Character selfCharacter; // Référence au Character local
    public Character Character => selfCharacter;

    public Collider InteractionZone => Character.CharacterInteraction.InteractionZone;

    public InteractableObject CurrentTarget => _currentInteractableObjectTarget;
    protected virtual void Awake()
    {
        selfCharacter = GetComponent<Character>();
        if (selfCharacter == null)
        {
            Debug.LogError("Le composant Character est manquant sur ce GameObject.", this);
        }
    }
    /// <summary>
    /// Vérifie si la cible fait partie de ce que le Trigger physique détecte actuellement.
    /// </summary>
    public bool IsInRange(InteractableObject target)
    {
        if (target == null) return false;

        // Si le target est celui actuellement enregistré par nos Triggers physiques
        if (target == _currentInteractableObjectTarget)
        {
            Debug.Log($"<color=green>[InRange]</color> Confirmation physique : {target.name} est à portée !");
            return true;
        }

        return false;
    }

    // Version plus précise si tu as des zones très fines (utilise la physique)
    public bool IsInPhysicalRange(InteractableObject target)
    {
        if (target == null || target.InteractionZone == null) return false;

        // Trouve le point le plus proche sur le collider de la zone
        Vector3 closestPoint = target.InteractionZone.ClosestPoint(transform.position);

        // Si la distance entre le perso et le point le plus proche du collider est quasi nulle, on est dedans
        return Vector3.Distance(transform.position, closestPoint) < 0.1f;
    }

    public bool IsInContactWith(InteractableObject target)
    {
        // 1. Cache du Trigger (OnTriggerEnter)
        if (_currentInteractableObjectTarget != null && _currentInteractableObjectTarget == target) return true;

        // 2. Ta nouvelle logique d'imbrication des zones
        if (IsOverlapping(target))
        {
            Debug.Log($"<color=green>[IA]</color> Imbrication des zones validée avec {target.name}");
            _currentInteractableObjectTarget = target;
            return true;
        }

        return false;
    }
    public bool IsOverlapping(InteractableObject target)
    {
        if (target == null || target.InteractionZone == null || InteractionZone == null)
            return false;

        // On utilise ComputePenetration pour savoir si les deux triggers s'imbriquent.
        // Cette méthode est plus précise que bounds.Intersects pour les zones rotatées ou complexes.
        bool isOverlapping = Physics.ComputePenetration(
            InteractionZone, InteractionZone.transform.position, InteractionZone.transform.rotation,
            target.InteractionZone, target.InteractionZone.transform.position, target.InteractionZone.transform.rotation,
            out Vector3 direction, out float distance
        );

        // Si la distance est positive, il y a imbrication
        return isOverlapping;
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
            _currentInteractableObjectTarget = interactable;
            if (_currentInteractableObjectTarget != null)
            {
                _currentInteractableObjectTarget.OnCharacterEnter(selfCharacter);
                //Debug.Log($"Cible détectée : {interactable.name}", this);
            }
            else
            {
                Debug.LogError("InteractableObject détecté, mais currentTarget est null.", this);
            }
        }
    }

    protected virtual void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent(out InteractableObject interactable) && interactable == _currentInteractableObjectTarget)
        {
            if (_currentInteractableObjectTarget != null)
            {
                _currentInteractableObjectTarget.OnCharacterExit(selfCharacter);
                //Debug.Log($"Cible {interactable.name} sortie.", this);
            }
            _currentInteractableObjectTarget = null;
        }
    }
}