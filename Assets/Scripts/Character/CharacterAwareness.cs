using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CharacterAwareness : CharacterSystem
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
        
        // On utilise Collide pour intercepter directement les InteractionZones
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius, Physics.AllLayers, QueryTriggerInteraction.Collide);

        List<InteractableObject> found = new List<InteractableObject>();

        foreach (var hit in hitColliders)
        {
            // Détection directe, beaucoup plus performante que GetComponentInChildren
            var interactable = hit.GetComponent<InteractableObject>() ?? hit.GetComponentInParent<InteractableObject>();

            if (interactable != null && interactable.RootGameObject != _character.gameObject)
            {
                if (!found.Contains(interactable))
                {
                    // Utilisation de la nouvelle propriété Rigidbody (demandée par l'utilisateur)
                    // Garantit qu'on ne cible l'objet QUE si son corps physique est bien dans notre zone !
                    if (interactable.Rigidbody != null)
                    {
                        if (Vector3.Distance(transform.position, interactable.Rigidbody.position) <= radius)
                        {
                            found.Add(interactable);
                        }
                    }
                    else
                    {
                        // Fallback pour les objets statiques sans Rigidbody (Arbres, Bâtiments)
                        if (Vector3.Distance(transform.position, interactable.transform.position) <= radius)
                        {
                            found.Add(interactable);
                        }
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
        var interactable = other.GetComponent<CharacterInteractable>() ?? other.GetComponentInParent<CharacterInteractable>();

        if (interactable != null && interactable.Character != null && interactable.Character != _character)
        {
            // On s'assure que leur Rigidbody physique vient bien de rentrer
            if (interactable.Rigidbody != null)
            {
                if (Vector3.Distance(transform.position, interactable.Rigidbody.position) > AwarenessRadius)
                    return; // Trop loin
            }

            OnCharacterDetected?.Invoke(interactable.Character);
        }
    }
}
