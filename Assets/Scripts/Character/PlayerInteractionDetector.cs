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
            try
            {
                // --- CAS 1 : PERSONNAGE ---
                if (currentTarget.TryGetComponent(out CharacterInteractable charInteractable))
                {
                    Character targetChar = charInteractable.Character;
                    if (targetChar == null) return;

                    // Vérifier si on est déjà en interaction avec ce personnage précis
                    if (Character.CharacterInteraction.IsInteracting &&
                        Character.CharacterInteraction.CurrentTarget == targetChar)
                    {
                        // On annule l'interaction
                        Character.CharacterInteraction.EndInteraction();

                        Debug.Log($"Interaction avec {targetChar.CharacterName} annulée.");
                    }
                    else
                    {
                        // Sinon, on démarre l'interaction normalement
                        currentTarget.Interact();
                        var startAction = new CharacterStartInteraction(Character, targetChar);
                        Character.CharacterActions.PerformAction(startAction);

                        // Si tu veux toujours que "E" fasse suivre par défaut après le start :
                        // Character.CharacterInteraction.PerformInteraction(new InteractionAskToFollow(), targetChar);
                    }
                }

                // --- CAS 2 : ITEM ---
                else if (currentTarget is ItemInteractable itemInteractable)
                {
                    ItemInstance instance = itemInteractable.ItemInstance;

                    currentTarget.Interact();
                    // Sécurités
                    if (instance?.ItemSO == null) return;

                    if (instance is EquipmentInstance equipment)
                    {
                        if (Character?.CharacterEquipment != null)
                        {
                            // 1. On équipe
                            Character.CharacterEquipment.Equip(equipment);

                            // 2. On détruit le prefab complet (la racine)
                            // On utilise itemInteractable.transform.root pour être sûr de supprimer 
                            // l'objet parent WorldItem_NomItem et pas juste l'enfant.
                            Destroy(itemInteractable.transform.root.gameObject);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Interaction] Erreur sur {currentTarget.name}: {ex.Message}");
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

            // 1. Trouver l'objet parent dans la scène
            GameObject parentObj = GameObject.Find("WorldUIManager");

            if (parentObj != null)
            {
                // 2. Instancier en passant le Transform du parent
                // Le prefab sera automatiquement placé comme enfant de WorldUIManager
                currentPromptUI = Instantiate(interactionPromptPrefab, parentObj.transform);
            }
            else
            {
                Debug.LogError("WorldUIManager non trouvé dans la scène !");
            }

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
                //Debug.Log($"Ajout de {interactable.name} à la liste des interactables.", this);
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
                //Debug.Log($"Retrait de {interactable.name} de la liste des interactables.", this);

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