using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PlayerInteractionDetector : CharacterInteractionDetector
{
    private GameObject interactionPromptPrefab;
    [SerializeField] private List<InteractableObject> nearbyInteractables = new List<InteractableObject>();
    private GameObject currentPromptUI;

    protected override void Awake()
    {
        base.Awake(); // Initialise le Character du parent
        interactionPromptPrefab = Resources.Load<GameObject>("UI/InteractionPrompt");
        if (interactionPromptPrefab == null)
            Debug.LogError("UI/InteractionPrompt not found in Resources!");
    }

    private void Update()
    {
        UpdateClosestTarget();

        if (Input.GetKeyDown(KeyCode.E) && currentTarget != null)
        {
            Debug.Log($"Interaction avec {currentTarget}.", this);
            try
            {
                if (currentTarget.TryGetComponent(out CharacterInteractable characterInteractable))
                {
                    //if (characterInteractable.Character == null)
                    //{
                    //    Debug.LogWarning($"CharacterInteractable sur {currentTarget.name} a un champ 'character' null.", this);
                    //    return;
                    //}
                    currentTarget.Interact();
                    Character.CharacterInteraction.PerformInteraction(new InteractionAskToFollow(), characterInteractable.Character);
                    Debug.Log($"Interaction avec {characterInteractable.Character.name}.", this);
                }
                else
                {
                    currentTarget.Interact();
                    Debug.Log($"Interaction avec l'objet générique {currentTarget.name}.", this);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Erreur lors de l'interaction avec {currentTarget.name} : {ex.Message}", this);
            }
        }
    }

    private void UpdateClosestTarget()
    {
        nearbyInteractables.RemoveAll(item => item == null);

        if (nearbyInteractables.Count == 0)
        {
            if (currentTarget != null)
            {
                currentTarget.OnCharacterExit();
                currentTarget = null;
            }
            if (currentPromptUI != null)
            {
                Destroy(currentPromptUI);
                currentPromptUI = null;
            }
            return;
        }

        InteractableObject closest = nearbyInteractables
            .OrderBy(interactable => Vector3.Distance(transform.position, interactable.transform.position))
            .FirstOrDefault();

        if (closest == null)
        {
            Debug.LogError("Aucune cible valide trouvée dans nearbyInteractables.", this);
            return;
        }

        if (closest != currentTarget)
        {
            if (currentTarget != null)
            {
                currentTarget.OnCharacterExit();
                if (currentPromptUI != null)
                {
                    Destroy(currentPromptUI);
                    currentPromptUI = null;
                }
            }

            currentTarget = closest;
            currentTarget.OnCharacterEnter();

            if (interactionPromptPrefab == null)
            {
                Debug.LogError("interactionPromptPrefab est null. Assigne un prefab dans l'inspecteur ou via InitializePromptUI.", this);
                return;
            }

            currentPromptUI = Instantiate(interactionPromptPrefab);
            InteractionPromptUI promptUIComponent = currentPromptUI.GetComponent<InteractionPromptUI>();
            if (promptUIComponent == null)
            {
                Debug.LogError("Le prefab interactionPromptPrefab n'a pas de composant InteractionPromptUI.", this);
                Destroy(currentPromptUI);
                currentPromptUI = null;
                return;
            }

            promptUIComponent.SetTarget(currentTarget.transform);
            string targetName = currentTarget.TryGetComponent(out CharacterInteractable characterInteractable) && characterInteractable.Character != null
                ? characterInteractable.Character.name
                : currentTarget.name;
            //Debug.Log($"Prompt affiché sur {targetName}", this);
        }
    }

    protected override void OnTriggerEnter(Collider other)
    {
        if (other.gameObject == gameObject)
        {
            Debug.Log("Collision avec soi-même ignorée.", this);
            return;
        }

        if (other.TryGetComponent(out InteractableObject interactable))
        {
            if (!nearbyInteractables.Contains(interactable))
            {
                nearbyInteractables.Add(interactable);
                Debug.Log($"Ajout de {interactable.name} à la liste des interactables.", this);
            }
        }
    }

    protected override void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent(out InteractableObject interactable))
        {
            if (nearbyInteractables.Contains(interactable))
            {
                nearbyInteractables.Remove(interactable);
                Debug.Log($"Retrait de {interactable.name} de la liste des interactables.", this);

                if (interactable == currentTarget)
                {
                    currentTarget.OnCharacterExit();
                    currentTarget = null;
                    if (currentPromptUI != null)
                    {
                        Destroy(currentPromptUI);
                        currentPromptUI = null;
                    }
                }
            }
        }
    }
}