using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CharacterAwareness : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private CapsuleCollider _awarenessCollider; // On utilise le rayon du collider

    /// <summary>
    /// Récupère tous les objets interactables actuellement dans la zone de détection.
    /// </summary>
    public List<InteractableObject> GetVisibleInteractables()
    {
        if (_awarenessCollider == null) return new List<InteractableObject>();

        float radius = _awarenessCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);

        // On récupère TOUS les colliders dans la zone (sans exception)
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius, Physics.AllLayers, QueryTriggerInteraction.Collide);

        List<InteractableObject> found = new List<InteractableObject>();

        foreach (var hit in hitColliders)
        {
            // 1. On cherche le script sur le collider précis qu'on vient de toucher
            // 2. Si pas trouvé, on cherche dans les parents (cas de ton InteractionZone)
            var interactable = hit.GetComponent<InteractableObject>() ?? hit.GetComponentInParent<InteractableObject>();

            if (interactable != null)
            {
                // Vérification : Ce n'est pas moi ?
                if (interactable.RootGameObject != _character.gameObject)
                {
                    if (!found.Contains(interactable))
                    {
                        found.Add(interactable);
                    }
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Version filtrée par type pour simplifier la vie des Needs.
    /// </summary>
    public List<T> GetVisibleInteractables<T>() where T : InteractableObject
    {
        List<T> filteredList = GetVisibleInteractables()
            .OfType<T>()
            .ToList();

        // --- DEBUG ---
        if (filteredList.Count > 0)
        {
            string names = string.Join(", ", filteredList.Select(obj => obj.name));
            Debug.Log($"<color=#42f593>[Awareness]</color> {typeof(T).Name} trouvés ({filteredList.Count}) : {names}");
        }
        else
        {
            // Optionnel : ne logguer le "vide" que toutes les X secondes pour ne pas spammer
            // Debug.Log($"<color=#f54242>[Awareness]</color> Aucun {typeof(T).Name} dans la zone.");
        }
        // -------------

        return filteredList;
    }
}