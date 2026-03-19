using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CharacterAwareness : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private CapsuleCollider _awarenessCollider; 

    public event System.Action<Character> OnCharacterDetected;

    public float AwarenessRadius 
    {
        get 
        {
            if (_awarenessCollider == null) return 15f;
            return _awarenessCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        }
    }

    public List<InteractableObject> GetVisibleInteractables()
    {
        if (_awarenessCollider == null) return new List<InteractableObject>();

        float radius = AwarenessRadius;
        
        // On ignore les gros triggers (zone de socialisation, etc) pour ne toucher que les colliders physiques liés aux Rigidbodies (Character, WorldItem)
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius, Physics.AllLayers, QueryTriggerInteraction.Ignore);

        List<InteractableObject> found = new List<InteractableObject>();

        foreach (var hit in hitColliders)
        {
            // Le joueur veut explicitement détecter que le collider est lié à un Rigidbody (Character.cs ou WorldItem.cs)
            if (hit.attachedRigidbody == null) continue;

            // On cherche l'InteractableObject via le rigging physique
            var interactable = hit.attachedRigidbody.GetComponent<InteractableObject>() 
                            ?? hit.GetComponent<InteractableObject>() 
                            ?? hit.GetComponentInParent<InteractableObject>();

            if (interactable != null)
            {
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

    public List<T> GetVisibleInteractables<T>() where T : InteractableObject
    {
        List<T> filteredList = GetVisibleInteractables()
            .OfType<T>()
            .ToList();

        if (filteredList.Count > 0)
        {
            string names = string.Join(", ", filteredList.Select(obj => obj.name));
            Debug.Log($"<color=#42f593>[Awareness]</color> {typeof(T).Name} trouvés ({filteredList.Count}) : {names}");
        }

        return filteredList;
    }

    private void OnTriggerEnter(Collider other)
    {
        // On ne veut détecter que les objets physiques (rigidbody), pas les triggers géants d'autres personnages
        if (other.isTrigger) return;

        // Détection via CharacterInteractable (reversion du Rigidbody)
        var interactable = other.attachedRigidbody != null 
            ? other.attachedRigidbody.GetComponent<CharacterInteractable>() ?? other.GetComponentInParent<CharacterInteractable>()
            : other.GetComponentInParent<CharacterInteractable>();

        if (interactable != null && interactable.Character != null && interactable.Character != _character)
        {
            OnCharacterDetected?.Invoke(interactable.Character);
        }
    }
}
