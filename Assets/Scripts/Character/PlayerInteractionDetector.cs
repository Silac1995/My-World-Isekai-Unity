using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PlayerInteractionDetector : CharacterInteractionDetector
{
    [SerializeField] private GameObject interactionPromptPrefab;
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

        if (Input.GetKeyDown(KeyCode.E) && _currentInteractableObjectTarget != null)
        {
            try
            {
                // --- CAS 1 : PERSONNAGE ---
                if (_currentInteractableObjectTarget is CharacterInteractable charInteractable)
                {
                    Character targetChar = charInteractable.Character;
                    if (targetChar == null) return;

                    // 1. Vérifier si on veut fermer une interaction existante
                    if (Character.CharacterInteraction.IsInteracting &&
                        Character.CharacterInteraction.CurrentTarget == targetChar)
                    {
                        Debug.Log($"<color=orange>[Interaction]</color> Fermeture manuelle avec {targetChar.CharacterName}");
                        Character.CharacterInteraction.EndInteraction();
                        return;
                    }

                    // 2. Lancer l'interaction
                    // C'est maintenant CharacterInteractable.Interact qui va créer 
                    // et lancer la CharacterStartInteraction.
                    _currentInteractableObjectTarget.Interact(Character);

                    Debug.Log($"<color=cyan>[Interaction]</color> Signal envoyé à l'interactable de {targetChar.CharacterName}");
                }

                // --- CAS 2 : ITEM ---
                else if (_currentInteractableObjectTarget is ItemInteractable itemInteractable)
                {
                    ItemInstance instance = itemInteractable.ItemInstance;
                    if (instance == null) return;

                    GameObject rootToDestroy = itemInteractable.RootGameObject;

                    // A. ÉQUIPEMENT PORTABLE (Vêtements, sacs...)
                    if (instance is WearableInstance wearable)
                    {
                        CharacterEquipAction equipAction = new CharacterEquipAction(Character, wearable);
                        Character.CharacterActions.ExecuteAction(equipAction);

                        // On détruit l'objet au sol car il est maintenant sur le personnage
                        if (rootToDestroy != null) Destroy(rootToDestroy);
                        Debug.Log($"[Equip] {wearable.CustomizedName} porté.");
                    }
                    // B. ARME OU OBJET DIVERS (On ramasse dans l'inventaire)
                    else
                    {
                        // On traite ici les WeaponInstance et les ItemInstance simples (nourriture, ressources...)
                        CharacterPickUpItem pickUpAction = new CharacterPickUpItem(Character, instance, rootToDestroy);
                        Character.CharacterActions.ExecuteAction(pickUpAction);

                        // Note : C'est CharacterPickUpItem.OnApplyEffect qui gérera le Destroy(rootToDestroy) 
                        // SEULEMENT si l'ajout à l'inventaire réussit.
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"<color=red>[Interaction Error]</color> Sur {_currentInteractableObjectTarget.name}: {ex.Message}");
            }
        }
    }

    private void UpdateClosestTarget()
    {
        nearbyInteractables.RemoveAll(item => item == null);

        if (nearbyInteractables.Count == 0)
        {
            if (_currentInteractableObjectTarget != null)
            {
                _currentInteractableObjectTarget.OnCharacterExit(Character);
                _currentInteractableObjectTarget = null;
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

        if (closest != _currentInteractableObjectTarget)
        {
            if (_currentInteractableObjectTarget != null)
            {
                _currentInteractableObjectTarget.OnCharacterExit(Character);
                if (currentPromptUI != null)
                {
                    Destroy(currentPromptUI);
                    currentPromptUI = null;
                }
            }

            _currentInteractableObjectTarget = closest;
            _currentInteractableObjectTarget.OnCharacterEnter(Character);

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

            promptUIComponent.SetTarget(_currentInteractableObjectTarget.transform);
            string targetName = _currentInteractableObjectTarget.TryGetComponent(out CharacterInteractable characterInteractable) && characterInteractable.Character != null
                ? characterInteractable.Character.name
                : _currentInteractableObjectTarget.name;
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

                if (interactable == _currentInteractableObjectTarget)
                {
                    _currentInteractableObjectTarget.OnCharacterExit(Character);
                    _currentInteractableObjectTarget = null;
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