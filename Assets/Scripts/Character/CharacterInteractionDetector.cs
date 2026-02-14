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

        // Utilise CheckSphere ou simplement la distance entre les centres si ce sont des sphères
        // Ou reste sur bounds.Intersects qui est purement mathématique et ne réveille pas la physique
        return InteractionZone.bounds.Intersects(target.InteractionZone.bounds);
    }

    protected virtual void OnTriggerEnter(Collider other)
    {
        // --- FILTRE CRUCIAL ---
        // Si ce n'est pas l'InteractionZone qui touche l'objet, on ignore.
        if (InteractionZone != null && !InteractionZone.bounds.Intersects(other.bounds)) return;
        // ----------------------

        if (other.gameObject == selfCharacter.gameObject) return;

        if (other.TryGetComponent(out InteractableObject interactable))
        {
            _currentInteractableObjectTarget = interactable;
            _currentInteractableObjectTarget.OnCharacterEnter(selfCharacter);
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