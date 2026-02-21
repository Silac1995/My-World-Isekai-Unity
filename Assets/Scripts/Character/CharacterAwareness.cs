using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CharacterAwareness : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private CapsuleCollider _awarenessCollider; 

    public event System.Action<Character> OnCharacterDetected;

    public List<InteractableObject> GetVisibleInteractables()
    {
        if (_awarenessCollider == null) return new List<InteractableObject>();

        float radius = _awarenessCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius, Physics.AllLayers, QueryTriggerInteraction.Collide);

        List<InteractableObject> found = new List<InteractableObject>();

        foreach (var hit in hitColliders)
        {
            var interactable = hit.GetComponent<InteractableObject>() ?? hit.GetComponentInParent<InteractableObject>();

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
        // Détection via CharacterInteractable (reversion du Rigidbody)
        var interactable = other.GetComponent<CharacterInteractable>() ?? other.GetComponentInParent<CharacterInteractable>();
        if (interactable != null && interactable.Character != null && interactable.Character != _character)
        {
            OnCharacterDetected?.Invoke(interactable.Character);
        }
    }
}
