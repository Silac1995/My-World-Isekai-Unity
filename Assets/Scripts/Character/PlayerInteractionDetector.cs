using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PlayerInteractionDetector : CharacterInteractionDetector
{
    [SerializeField] private GameObject interactionPromptPrefab;
    [SerializeField] private List<InteractableObject> nearbyInteractables = new List<InteractableObject>();
    private GameObject currentPromptUI;
    private InteractionPromptUI currentPromptComponent;
    // _playerUI is read by dialogue handlers (HandleInteractionStateChanged etc.) and
    // by TriggerHoldMenu. _targeting is read by UpdateClosestTarget's SELECTION-MODE
    // branch to lock the prompt to a TAB-selected target when in range. Neither
    // reads Input.* — both are pure data lookups, so they don't violate rule #33.
    private PlayerUI _playerUI;
    private UI_PlayerTargeting _targeting;

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
    /// Ensures the _targeting reference is resolved.
    /// </summary>
    private void EnsureTargeting()
    {
        if (_targeting == null)
            _targeting = UnityEngine.Object.FindAnyObjectByType<UI_PlayerTargeting>(FindObjectsInactive.Include);
    }

    /// <summary>
    /// True only if this detector's Character is the LOCAL player's character.
    /// Remote player Characters also have a PlayerController (so IsPlayer() returns true on every
    /// machine), which would otherwise cause the interaction HUD to open for every player when
    /// any player starts an interaction. In solo play the NetworkObject is not spawned, so we
    /// fall back to IsPlayer() alone.
    /// </summary>
    private bool IsLocalPlayerCharacter()
    {
        if (!Character.IsPlayer()) return false;
        return !Character.IsSpawned || Character.IsOwner;
    }

    /// <summary>
    /// Called when the interaction formally starts or ends.
    /// Opens the menu (locked) when the interaction begins, closes it when it ends.
    /// </summary>
    private void HandleInteractionStateChanged(Character target, bool started)
    {
        if (!IsLocalPlayerCharacter()) return;

        EnsurePlayerUI();
        if (_playerUI == null) return;

        Debug.Log($"<color=cyan>[PlayerInteractionDetector]</color> HandleInteractionStateChanged called for Character: {Character.name}, Target: {(target != null ? target.name : "null")}, Started: {started}");

        if (started)
        {
            // Try to get the interactable from proximity detection first
            var interactable = _currentInteractableObjectTarget;

            // Fallback: resolve from the interaction target's CharacterInteractable
            if (interactable == null && target != null)
            {
                interactable = target.CharacterInteractable;
            }

            if (interactable != null)
            {
                var options = interactable.GetDialogueInteractionOptions(Character);
                if (options != null && options.Count > 0)
                {
                    // persistAcrossClicks: dialogue menu must stay open for the whole
                    // CharacterInteraction — clicking an option re-locks the buttons but
                    // does NOT close the menu. Closure happens only when the interaction
                    // ends (the `else` branch below calls CloseInteractionMenu()).
                    _playerUI.OpenInteractionMenu(options, persistAcrossClicks: true);
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
        if (!IsLocalPlayerCharacter()) return;

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
        if (!IsLocalPlayerCharacter()) return;

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
        if (!IsLocalPlayerCharacter()) return;

        EnsurePlayerUI();
        if (_playerUI != null)
        {
            _playerUI.UpdateInteractionMenuTimer(normalizedValue);
        }
    }

    /// <summary>
    /// Proximity tracking only. All E-key input dispatch lives in
    /// <see cref="PlayerController"/> per rule #33 — this class no longer reads
    /// any keyboard input. <see cref="UpdateClosestTarget"/> runs in LateUpdate
    /// so PlayerController's input read in Update sees the previous frame's
    /// proximity snapshot — stable, no input/render race.
    /// </summary>
    private void LateUpdate()
    {
        if (Character.TryGetComponent(out Unity.Netcode.NetworkObject netObj) && netObj.IsSpawned && !netObj.IsOwner) return;
        UpdateClosestTarget();
    }

    /// <summary>
    /// Checks whether the given interactable is currently in the player's nearbyInteractables list,
    /// meaning the player's rigidbody is inside the target's InteractionZone.
    /// Called by PlayerInteractCommand to determine arrival.
    /// </summary>
    public bool IsTargetInRange(InteractableObject target)
    {
        if (target == null) return false;
        return nearbyInteractables.Contains(target);
    }

    /// <summary>
    /// Canonical tap-E entry point. Called by <see cref="PlayerController"/>'s
    /// HandleEKeyUp dispatch (rule #33 — input owner) and by
    /// <see cref="MWI.CharacterControllers.Commands.PlayerInteractCommand"/> on auto-nav arrival.
    /// Wraps the dialogue-NPC freeness gate and the
    /// <see cref="InteractableObject.Interact"/> dispatch.
    /// </summary>
    public void TriggerTapInteract(InteractableObject target)
    {
        if (target == null) return;

        // Temporarily set the current target so ExecuteNormalInteract picks it up
        _currentInteractableObjectTarget = target;
        ExecuteNormalInteract();
    }

    /// <summary>
    /// Opens the generic hold-interaction menu for a target if it has any
    /// <see cref="InteractableObject.GetHoldInteractionOptions"/>. Returns true
    /// if a menu was opened (so the caller can flip its E-menu-opened latch).
    /// Called by <see cref="PlayerController"/>'s HandleEKeyHeld threshold branch
    /// (rule #33 — input owner).
    /// </summary>
    public bool TriggerHoldMenu(InteractableObject target)
    {
        if (target == null) return false;
        var options = target.GetHoldInteractionOptions(Character);
        if (options == null || options.Count == 0) return false;
        if (_playerUI == null) _playerUI = UnityEngine.Object.FindAnyObjectByType<PlayerUI>(FindObjectsInactive.Include);
        if (_playerUI == null) return false;
        _playerUI.OpenInteractionMenu(options);
        return true;
    }

    /// <summary>
    /// Drives the prompt-fill bar from <c>0..1</c>. Called every frame from
    /// <see cref="PlayerController"/>.HandleEKeyHeld — the input owner ticks the
    /// hold timer and pushes progress here.
    /// </summary>
    public void SetPromptHoldProgress(float t01)
    {
        if (currentPromptComponent != null) currentPromptComponent.SetFillAmount(Mathf.Clamp01(t01));
    }

    private void ExecuteNormalInteract()
    {
        if (_currentInteractableObjectTarget == null) return;
        try
        {
            // 1. IF IT'S A CHARACTER, CHECK THE STATES
            if (_currentInteractableObjectTarget is CharacterInteractable charInteractable)
            {
                Character targetChar = charInteractable.Character;
                if (targetChar == null) return;

                if (!Character.IsFree() || !targetChar.IsFree())
                {
                    Debug.LogWarning($"<color=yellow>[Interaction]</color> Interaction not possible: " +
                        $"{(!Character.IsFree() ? "the player is busy" : "the target is in combat/interaction")}");
                    return;
                }

                if (Character.CharacterInteraction.IsInteracting &&
                    Character.CharacterInteraction.CurrentTarget == targetChar)
                {
                    Character.CharacterInteraction.EndInteraction();
                    return;
                }
            }

            // 2. ALL TYPES: delegate to each subclass's Interact()
            _currentInteractableObjectTarget.Interact(Character);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"<color=red>[Interaction Error]</color> On {_currentInteractableObjectTarget.name}: {ex.ToString()}");
        }
    }

    private void OpenInteractionMenu(List<InteractionOption> options)
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

        EnsureTargeting();
        InteractableObject selectedTarget = _targeting != null ? _targeting.SelectedInteractable : null;

        // --- SELECTION MODE: If a target is selected via click/TAB, lock to it ---
        if (selectedTarget != null)
        {
            bool selectedIsInRange = nearbyInteractables.Contains(selectedTarget);

            if (selectedIsInRange)
            {
                // Selected target is in InteractionZone — lock to it
                if (_currentInteractableObjectTarget != selectedTarget)
                {
                    // Clean up old target
                    if (_currentInteractableObjectTarget != null)
                    {
                        _currentInteractableObjectTarget.OnCharacterExit(Character);
                        DestroyPrompt();
                        CloseMenuIfSafe();
                    }

                    _currentInteractableObjectTarget = selectedTarget;
                    _currentInteractableObjectTarget.OnCharacterEnter(Character);
                    CreatePrompt();
                }
                return; // Skip proximity-based auto-targeting
            }
            else
            {
                // Selected target is NOT in range — clear the prompt but keep the selection
                if (_currentInteractableObjectTarget != null)
                {
                    _currentInteractableObjectTarget.OnCharacterExit(Character);
                    _currentInteractableObjectTarget = null;
                    DestroyPrompt();
                    CloseMenuIfSafe();
                }
                return; // Don't fall through to proximity mode while a target is selected
            }
        }

        // --- PROXIMITY MODE: No selection, use closest interactable in range ---
        if (nearbyInteractables.Count == 0)
        {
            if (_currentInteractableObjectTarget != null)
            {
                _currentInteractableObjectTarget.OnCharacterExit(Character);
                _currentInteractableObjectTarget = null;
                DestroyPrompt();
                CloseMenuIfSafe();
            }
            return;
        }

        InteractableObject closest = nearbyInteractables
            .OrderBy(interactable => Vector3.Distance(transform.position, interactable.transform.position))
            .FirstOrDefault();

        if (closest == null)
        {
            Debug.LogError("No valid target found in nearbyInteractables.", this);
            return;
        }

        if (closest != _currentInteractableObjectTarget)
        {
            if (_currentInteractableObjectTarget != null)
            {
                _currentInteractableObjectTarget.OnCharacterExit(Character);
                DestroyPrompt();
                CloseMenuIfSafe();
            }

            _currentInteractableObjectTarget = closest;
            _currentInteractableObjectTarget.OnCharacterEnter(Character);
            CreatePrompt();
        }
    }

    #region Prompt Helpers

    private void CreatePrompt()
    {
        if (interactionPromptPrefab == null)
        {
            Debug.LogError("interactionPromptPrefab is null. Assign a prefab in the inspector or via InitializePromptUI.", this);
            return;
        }

        GameObject parentObj = GameObject.Find("WorldUIManager");
        if (parentObj != null)
        {
            currentPromptUI = Instantiate(interactionPromptPrefab, parentObj.transform);
        }
        else
        {
            Debug.LogError("WorldUIManager not found in the scene!");
            return;
        }

        InteractionPromptUI promptUIComponent = currentPromptUI.GetComponent<InteractionPromptUI>();
        if (promptUIComponent == null)
        {
            Debug.LogError("The interactionPromptPrefab prefab has no InteractionPromptUI component.", this);
            Destroy(currentPromptUI);
            currentPromptUI = null;
            currentPromptComponent = null;
            return;
        }

        currentPromptComponent = promptUIComponent;
        currentPromptComponent.SetTarget(_currentInteractableObjectTarget.transform, "E");
    }

    private void DestroyPrompt()
    {
        if (currentPromptUI != null)
        {
            Destroy(currentPromptUI);
            currentPromptUI = null;
            currentPromptComponent = null;
        }
    }

    private void CloseMenuIfSafe()
    {
        if (_playerUI != null && !Character.CharacterInteraction.IsInteracting && !_playerUI.IsInteractionMenuLocked())
        {
            _playerUI.CloseInteractionMenu();
        }
    }

    #endregion

    protected override void OnTriggerEnter(Collider other)
    {
        if (Character.TryGetComponent(out Unity.Netcode.NetworkObject netObj) && netObj.IsSpawned && !netObj.IsOwner) return;
        
        // Check that the collider event is from our InteractionZone, not from Awareness or other child colliders
        if (InteractionZone != null && !InteractionZone.bounds.Intersects(other.bounds))
        {
            return;
        }

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

        if (other.TryGetComponent(out InteractableObject interactable))
        {
            if (nearbyInteractables.Contains(interactable))
            {
                nearbyInteractables.Remove(interactable);

                if (interactable == _currentInteractableObjectTarget)
                {
                    _currentInteractableObjectTarget.OnCharacterExit(Character);
                    _currentInteractableObjectTarget = null;
                    DestroyPrompt();
                    if (_playerUI != null && !Character.CharacterInteraction.IsInteracting)
                    {
                        _playerUI.CloseInteractionMenu();
                    }
                }
            }
        }
    }
}
