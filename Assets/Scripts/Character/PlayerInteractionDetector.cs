using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PlayerInteractionDetector : CharacterInteractionDetector
{
    [SerializeField] private GameObject interactionPromptPrefab;
    [SerializeField] private List<InteractableObject> nearbyInteractables = new List<InteractableObject>();
    private GameObject currentPromptUI;
    private InteractionPromptUI currentPromptComponent;
    private float eHoldTime = 0f;
    private bool isHoldingE = false;
    private const float HOLD_THRESHOLD = 0.4f;
    private PlayerUI _playerUI;

    protected override void Awake()
    {
        base.Awake(); // Initialise le Character du parent
        
        // Load dynamically only if it wasn't assigned in the Unity Inspector
        if (interactionPromptPrefab == null)
        {
            interactionPromptPrefab = Resources.Load<GameObject>("UI/Interaction/InteractionPrompt");
            if (interactionPromptPrefab == null)
                Debug.LogError("UI/Interaction/InteractionPrompt not found in Resources!");
        }

        if (Character != null && Character.CharacterInteraction != null)
        {
            Character.CharacterInteraction.OnInteractionStateChanged += HandleInteractionStateChanged;
            Character.CharacterInteraction.OnPlayerTurnStarted += HandlePlayerTurnStarted;
            Character.CharacterInteraction.OnPlayerTurnEnded += HandlePlayerTurnEnded;
            Character.CharacterInteraction.OnPlayerTurnTimerUpdated += HandlePlayerTurnTimerUpdated;
        }
    }

    private void OnDestroy()
    {
        if (Character != null && Character.CharacterInteraction != null)
        {
            Character.CharacterInteraction.OnInteractionStateChanged -= HandleInteractionStateChanged;
            Character.CharacterInteraction.OnPlayerTurnStarted -= HandlePlayerTurnStarted;
            Character.CharacterInteraction.OnPlayerTurnEnded -= HandlePlayerTurnEnded;
            Character.CharacterInteraction.OnPlayerTurnTimerUpdated -= HandlePlayerTurnTimerUpdated;
        }
    }

    /// <summary>
    /// Ensures the _playerUI reference is resolved. Called lazily by event handlers
    /// since events can fire before the first Update() call.
    /// </summary>
    private void EnsurePlayerUI()
    {
        if (_playerUI == null)
            _playerUI = UnityEngine.Object.FindAnyObjectByType<PlayerUI>(FindObjectsInactive.Include);
    }

    /// <summary>
    /// Called when the interaction formally starts or ends.
    /// Opens the menu (locked) when the interaction begins, closes it when it ends.
    /// </summary>
    private void HandleInteractionStateChanged(Character target, bool started)
    {
        EnsurePlayerUI();
        if (_playerUI == null) return;

        if (started)
        {
            // Try to get the interactable from proximity detection first
            var interactable = _currentInteractableObjectTarget;

            // Fallback: resolve from the interaction target's CharacterInteractable
            if (interactable == null && target != null)
            {
                interactable = target.GetComponent<CharacterInteractable>();
            }

            if (interactable != null)
            {
                var options = interactable.GetDialogueInteractionOptions(Character);
                if (options != null && options.Count > 0)
                {
                    _playerUI.OpenInteractionMenu(options);
                    _playerUI.SetInteractionMenuInteractable(false);
                    _playerUI.UpdateInteractionMenuTimer(1f);
                }
            }
        }
        else
        {
            _playerUI.CloseInteractionMenu();
        }
    }

    /// <summary>
    /// Called when it becomes the player's turn — unlock the buttons.
    /// </summary>
    private void HandlePlayerTurnStarted(Character listener)
    {
        EnsurePlayerUI();
        if (_playerUI != null)
        {
            _playerUI.SetInteractionMenuInteractable(true);
            _playerUI.UpdateInteractionMenuTimer(1f);
        }
    }

    /// <summary>
    /// Called when the player's turn ends — lock the buttons again.
    /// </summary>
    private void HandlePlayerTurnEnded(Character listener)
    {
        EnsurePlayerUI();
        if (_playerUI != null)
        {
            _playerUI.SetInteractionMenuInteractable(false);
        }
    }

    /// <summary>
    /// Called every frame during the player's turn with a normalized timer value.
    /// </summary>
    private void HandlePlayerTurnTimerUpdated(float normalizedValue)
    {
        EnsurePlayerUI();
        if (_playerUI != null)
        {
            _playerUI.UpdateInteractionMenuTimer(normalizedValue);
        }
    }

    private void Update()
    {
        if (Character.TryGetComponent(out Unity.Netcode.NetworkObject netObj) && netObj.IsSpawned && !netObj.IsOwner) return;

        UpdateClosestTarget();

        // Prevent interacting if the player is currently typing in an input field (e.g. chat)
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null)
        {
            return;
        }

        if (_playerUI == null)
            _playerUI = UnityEngine.Object.FindAnyObjectByType<PlayerUI>(FindObjectsInactive.Include);

        if (Input.GetKeyDown(KeyCode.E) && _currentInteractableObjectTarget != null)
        {
            isHoldingE = true;
            eHoldTime = 0f;
            if (currentPromptComponent != null) currentPromptComponent.SetFillAmount(0f);
        }

        if (Input.GetKey(KeyCode.E) && isHoldingE)
        {
            eHoldTime += Time.deltaTime;
            if (currentPromptComponent != null) currentPromptComponent.SetFillAmount(eHoldTime / HOLD_THRESHOLD);

            if (eHoldTime >= HOLD_THRESHOLD)
            {
                isHoldingE = false; // Stop tracking hold
                var options = _currentInteractableObjectTarget.GetHoldInteractionOptions(Character);
                if (options != null && options.Count > 0)
                {
                    if (_playerUI != null) _playerUI.OpenInteractionMenu(options);
                }
                else
                {
                    ExecuteNormalInteract();
                }
            }
        }

        if (Input.GetKeyUp(KeyCode.E) && isHoldingE)
        {
            isHoldingE = false; // Released before threshold
            if (currentPromptComponent != null) currentPromptComponent.SetFillAmount(0f);
            ExecuteNormalInteract();
        }
    }

    private void ExecuteNormalInteract()
    {
        if (_currentInteractableObjectTarget == null) return;
        try
        {
            // 1. SI C'EST UN PERSONNAGE, ON VÉRIFIE LES ÉTATS
            if (_currentInteractableObjectTarget is CharacterInteractable charInteractable)
            {
                Character targetChar = charInteractable.Character;
                if (targetChar == null) return;

                if (!Character.IsFree() || !targetChar.IsFree())
                {
                    Debug.LogWarning($"<color=yellow>[Interaction]</color> Interaction impossible : " +
                        $"{(!Character.IsFree() ? "Le joueur est occupé" : "La cible est en combat/interaction")}");
                    return;
                }

                if (Character.CharacterInteraction.IsInteracting &&
                    Character.CharacterInteraction.CurrentTarget == targetChar)
                {
                    Character.CharacterInteraction.EndInteraction();
                    return;
                }
            }

            // 2. TOUS LES TYPES : on délègue à l'Interact() de chaque sous-classe
            _currentInteractableObjectTarget.Interact(Character);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"<color=red>[Interaction Error]</color> Sur {_currentInteractableObjectTarget.name}: {ex.ToString()}");
        }
    }

    private void OpenInteractionMenu(List<InteractableObject.InteractionOption> options)
    {
        if (_playerUI != null)
        {
            _playerUI.OpenInteractionMenu(options);
        }
        else
        {
            ExecuteNormalInteract();
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
                currentPromptComponent = null;
            }
            if (_playerUI != null)
            {
                _playerUI.CloseInteractionMenu();
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
                    currentPromptComponent = null;
                }
                if (_playerUI != null)
                {
                    _playerUI.CloseInteractionMenu();
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
                currentPromptComponent = null;
                return;
            }

            currentPromptComponent = promptUIComponent;
            currentPromptComponent.SetTarget(_currentInteractableObjectTarget.transform, "E");
            string targetName = _currentInteractableObjectTarget.TryGetComponent(out CharacterInteractable characterInteractable) && characterInteractable.Character != null
                ? characterInteractable.Character.name
                : _currentInteractableObjectTarget.name;
            //Debug.Log($"Prompt affiché sur {targetName}", this);
        }
    }

    protected override void OnTriggerEnter(Collider other)
    {
        if (Character.TryGetComponent(out Unity.Netcode.NetworkObject netObj) && netObj.IsSpawned && !netObj.IsOwner) return;
        
        // --- LA CORRECTION EST ICI ---
        // On vérifie si le collider qui a détecté l'entrée est bien l'InteractionZone du joueur
        // Si c'est l'Awareness ou un autre collider enfant, on ignore.
        if (InteractionZone != null && !InteractionZone.bounds.Intersects(other.bounds))
        {
            return;
        }
        // -----------------------------

        if (other.gameObject == gameObject) return;

        if (other.TryGetComponent(out InteractableObject interactable))
        {
            if (!nearbyInteractables.Contains(interactable))
            {
                nearbyInteractables.Add(interactable);
            }
        }
    }

    protected override void OnTriggerExit(Collider other)
    {
        if (Character.TryGetComponent(out Unity.Netcode.NetworkObject netObj) && netObj.IsSpawned && !netObj.IsOwner) return;

        // On applique la même logique pour la sortie
        if (other.TryGetComponent(out InteractableObject interactable))
        {
            if (nearbyInteractables.Contains(interactable))
            {
                nearbyInteractables.Remove(interactable);

                if (interactable == _currentInteractableObjectTarget)
                {
                    // Nettoyage UI...
                    _currentInteractableObjectTarget.OnCharacterExit(Character);
                    _currentInteractableObjectTarget = null;
                    if (currentPromptUI != null)
                    {
                        Destroy(currentPromptUI);
                        currentPromptUI = null;
                        currentPromptComponent = null;
                    }
                    if (_playerUI != null)
                    {
                        _playerUI.CloseInteractionMenu();
                    }
                }
            }
        }
    }
}