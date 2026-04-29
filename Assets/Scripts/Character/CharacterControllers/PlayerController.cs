using UnityEngine;
using System.Collections.Generic;
using System.Linq;

using MWI.CharacterControllers.Commands;

public class PlayerController : CharacterGameController
{
    private Vector3 _inputDir = Vector3.zero;
    private bool _isCrouching = false;
    private bool _wasNavMeshActiveLastFrame = false;

    private IPlayerCommand _currentOrder;

    // --- TAB Targeting ---
    private UI_PlayerTargeting _targeting;

    public void SetOrder(IPlayerCommand newOrder)
    {
        if (_currentOrder != null) _currentOrder.OnCancelled(this);
        _currentOrder = newOrder;

        if (newOrder == null && _character.CharacterCombat != null)
        {
            _character.CharacterCombat.ClearActionIntent();
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        if (_character.Rigidbody != null)
        {
            if (IsOwner)
                _character.Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            else
                _character.Rigidbody.interpolation = RigidbodyInterpolation.None; // Let NetworkTransform handle it
        }
    }

    /// <summary>
    /// Lazily resolves the UI_PlayerTargeting reference.
    /// </summary>
    private void EnsureTargeting()
    {
        if (_targeting == null)
            _targeting = UnityEngine.Object.FindAnyObjectByType<UI_PlayerTargeting>(FindObjectsInactive.Include);
    }

    protected override void Update()
    {
        if (IsOwner)
        {
            // --- Step 1: Sleep toggle (Z key) ---
            // Z is "lay down" when awake and "wake up" when asleep.
            // Must come BEFORE the IsSleeping early-out so it fires in both states.
            if (Input.GetKeyDown(KeyCode.Z))
            {
                if (_character.IsSleeping)
                {
                    _character.CharacterActions?.ClearCurrentAction();
                }
                else if (_character.CharacterActions != null
                         && _character.CharacterActions.CurrentAction == null
                         && _character.IsAlive())
                {
                    var action = new CharacterAction_Sleep(_character);
                    _character.CharacterActions.ExecuteAction(action);
                }
                return;  // consume the input; don't fall through to other handlers
            }

            // --- Step 2: Wake-on-movement ---
            // Any WASD or mouse click while asleep wakes the character.
            // We clear the sleep action here; the movement command routes through naturally next frame.
            if (_character.IsSleeping
                && (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A)
                    || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D)
                    || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
            {
                _character.CharacterActions?.ClearCurrentAction();
                return;  // skip the IsSleeping early-out so the movement registers next frame
            }

            // --- Step 3: Sleep re-enqueue ---
            // While asleep but no action is live (action finished its tick via Finish()),
            // re-enqueue the appropriate sleep CharacterAction so live restoration keeps firing.
            // Bed vs ground chosen via Character.OccupyingFurniture.
            if (_character.IsSleeping
                && _character.CharacterActions != null
                && _character.CharacterActions.CurrentAction == null)
            {
                CharacterAction next;
                var occupying = _character.OccupyingFurniture;
                if (occupying is BedFurniture bedFurniture)
                {
                    int slotIdx = bedFurniture.GetSlotIndexFor(_character);
                    if (slotIdx < 0)
                    {
                        // Lost the slot somehow — fall back to ground sleep.
                        next = new CharacterAction_Sleep(_character);
                    }
                    else
                    {
                        next = new CharacterAction_SleepOnFurniture(_character, bedFurniture, slotIdx);
                    }
                }
                else
                {
                    next = new CharacterAction_Sleep(_character);
                }
                _character.CharacterActions.ExecuteAction(next);
                // No early-return here — let the IsSleeping early-out below freeze other input.
            }

            // Sleeping players accept no input — bed/skip lifecycle owns position+rotation,
            // animator switches to sleep pose via Character.OnSleepStateChanged.
            if (Character != null && Character.IsSleeping) return;

            // Block player movement/action input if typing in any UI text field
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null &&
                UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null)
            {
                _inputDir = Vector3.zero;
                base.Update();
                Move();
                return;
            }

            bool devMode = DevModeManager.SuppressPlayerInput;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            _inputDir = new Vector3(h, 0f, v).normalized;
            _isCrouching = !devMode && Input.GetKey(KeyCode.C);

            if (_inputDir.sqrMagnitude > 0.1f && _currentOrder != null)
            {
                // ZQSD cancels any movement command instantly, BUT NOT if we are forced into combat AI
                if (!(_currentOrder is PlayerCombatCommand))
                {
                    SetOrder(null);
                }
                else
                {
                    // In combat, WASD is ignored for movement
                    _inputDir = Vector3.zero;
                }
            }

            // Dev mode suppresses all gameplay action inputs below — right-click move, TAB target,
            // combat command auto-assignment, and Space attack — but WASD above still drives
            // movement (at GodModeMovementSpeed, see Move()).
            if (!devMode)
            {
                // Right-click to move (standard RPG/MOBA)
                if (Input.GetMouseButtonDown(1) && !_character.CharacterCombat.IsInBattle)
                {
                    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                    {
                        SetOrder(new PlayerMoveCommand(hit.point));
                    }
                }

                // --- TAB: Cycle-select the closest interactable within awareness range ---
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    HandleTabTargeting();
                }

                // --- G: Drop the item currently carried in hands (HandsController.CarriedItem). ---
                // Mirrors the drop button in CharacterEquipmentUI. Inventory drop is right-click on UI_ItemSlot.
                if (Input.GetKeyDown(KeyCode.G))
                {
                    HandleDropCarriedItem();
                }

                // --- E key dispatch (placement-active item / consumable / tap-interact / hold-menu). ---
                // Single owner-gated dispatcher per rule #33.
                if (Input.GetKeyDown(KeyCode.E))
                {
                    HandleEKeyDown();
                }
                else if (Input.GetKey(KeyCode.E))
                {
                    HandleEKeyHeld();
                }
                else if (Input.GetKeyUp(KeyCode.E))
                {
                    HandleEKeyUp();
                }

                // Auto-Trigger Combat Command when in battle. The command handles pacing and action execution.
                if (_character.CharacterCombat.IsInBattle && !(_currentOrder is PlayerCombatCommand))
                {
                    Character battleTarget = _character.CharacterCombat.CurrentBattleManager?.GetBestTargetFor(_character);

                    // Fallback: If GetBestTargetFor returns null (e.g. the host was attacked, not the
                    // attacker, and the engagement coordinator hasn't registered them yet), pick any
                    // alive opponent from the opposing team and request an engagement.
                    if (battleTarget == null)
                    {
                        var bm = _character.CharacterCombat.CurrentBattleManager;
                        var opponentTeam = bm?.GetOpponentTeamOf(_character);
                        if (opponentTeam != null)
                        {
                            battleTarget = opponentTeam.CharacterList.Find(c => c != null && c.IsAlive());
                            if (battleTarget != null)
                            {
                                bm.SetTargeting(_character, battleTarget);
                                Debug.Log($"<color=yellow>[PlayerCtrl]</color> {_character.CharacterName} fallback targeting set against {battleTarget.CharacterName}");
                            }
                            else
                            {
                                Debug.LogWarning($"<color=red>[PlayerCtrl]</color> {_character.CharacterName} IsInBattle but no alive opponents found in opponent team!");
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"<color=red>[PlayerCtrl]</color> {_character.CharacterName} IsInBattle but GetOpponentTeamOf returned null! BM={bm}");
                        }
                    }

                    if (battleTarget != null)
                    {
                        Debug.Log($"<color=green>[PlayerCtrl]</color> {_character.CharacterName} entering combat mode vs {battleTarget.CharacterName}. NavMesh will be enabled.");
                        SetOrder(new PlayerCombatCommand(_character, battleTarget));

                        // Sync the target indicator and PlannedTarget to the initial battle target
                        EnsureTargeting();
                        if (_targeting != null)
                        {
                            var charInteractable = battleTarget.CharacterInteractable;
                            if (charInteractable != null)
                                _targeting.SelectInteractable(charInteractable);
                        }
                    }
                }
                else if (!_character.CharacterCombat.IsInBattle && _currentOrder is PlayerCombatCommand)
                {
                    // Exit combat gracefully
                    SetOrder(null);
                }

                if (!_character.CharacterCombat.IsInBattle && Input.GetKeyDown(KeyCode.Space))
                {
                    _character.CharacterCombat.Attack(null);
                }
            }
        }

        base.Update();
        
        if (IsOwner)
        {
            Move();
        }
    }

    /// <summary>
    /// Drops the item currently carried in the player's hands via CharacterDropItem.
    /// No-op if hands are empty or another action is already running. Networking is handled
    /// by CharacterDropItem itself (server spawns directly, client routes via ServerRpc).
    /// </summary>
    private void HandleDropCarriedItem()
    {
        var hands = _character?.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.IsCarrying) return;

        if (_character.CharacterActions == null) return;
        if (_character.CharacterActions.CurrentAction != null) return;

        _character.CharacterActions.ExecuteAction(new CharacterDropItem(_character, hands.CarriedItem));
    }

    private const float E_HOLD_THRESHOLD = 0.4f;
    private float _eHeldStartTime;
    private bool _eMenuOpened;

    /// <summary>
    /// Owner-gated E-key down handler (rule #33). Resolves the immediate intent that doesn't
    /// need hold-tracking: placement-active items take E unconditionally; consumables consume.
    /// Anything else starts the hold timer for the harvestable tap-vs-hold dispatch.
    /// </summary>
    private void HandleEKeyDown()
    {
        _eHeldStartTime = UnityEngine.Time.unscaledTime;
        _eMenuOpened = false;

        var hands = _character?.CharacterVisual?.BodyPartsController?.HandsController;
        var heldItemSO = hands != null && hands.CarriedItem != null ? hands.CarriedItem.ItemSO : null;

        // Priority 1 + 2: placement-active item — E starts placement, no tap/hold distinction.
        if (heldItemSO is MWI.Farming.SeedSO)
        {
            if (_character.CropPlacement != null) _character.CropPlacement.StartPlacement(hands.CarriedItem);
            _eMenuOpened = true;   // suppress hold-menu while placement-active item handles E.
            return;
        }
        if (heldItemSO is MWI.Farming.WateringCanSO)
        {
            if (_character.CropPlacement != null) _character.CropPlacement.StartWatering();
            _eMenuOpened = true;
            return;
        }

        // Priority 3: if placement is already active, the manager owns LMB/RMB/ESC. E is a no-op.
        if (_character.CropPlacement != null && _character.CropPlacement.IsActive)
        {
            _eMenuOpened = true;
            return;
        }

        // Priority 4: consumable in hand. Existing behaviour preserved.
        if (hands != null && hands.IsCarrying && hands.CarriedItem is ConsumableInstance consumable)
        {
            if (_character.CharacterActions == null || _character.CharacterActions.CurrentAction != null) return;
            _character.CharacterActions.ExecuteAction(new CharacterUseConsumableAction(_character, consumable));
            _eMenuOpened = true;
            return;
        }
        // Else: defer to KeyHeld / KeyUp for tap-vs-hold harvestable dispatch.
    }

    /// <summary>While E is held, open the interaction menu once the hold threshold is crossed.</summary>
    private void HandleEKeyHeld()
    {
        if (_eMenuOpened) return;
        if (UnityEngine.Time.unscaledTime - _eHeldStartTime < E_HOLD_THRESHOLD) return;

        var nearest = GetNearestVisibleHarvestable();
        if (nearest != null)
        {
            MWI.UI.Interaction.UI_HarvestInteractionMenu.Open(_character, nearest, OnInteractionMenuClosed);
            _eMenuOpened = true;
        }
    }

    /// <summary>On E release, if the menu wasn't opened (tap), run the immediate Interact path.</summary>
    private void HandleEKeyUp()
    {
        if (_eMenuOpened) return;

        var nearest = GetNearestVisibleInteractable();
        if (nearest != null) nearest.Interact(_character);
    }

    private void OnInteractionMenuClosed() => _eMenuOpened = false;

    private Harvestable GetNearestVisibleHarvestable()
    {
        var awareness = _character.CharacterAwareness;
        if (awareness == null) return null;
        var visible = awareness.GetVisibleInteractables<Harvestable>();
        if (visible == null || visible.Count == 0) return null;
        Harvestable best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < visible.Count; i++)
        {
            var h = visible[i];
            if (h == null) continue;
            float d = Vector3.Distance(_character.transform.position, h.transform.position);
            if (d < bestDist) { bestDist = d; best = h; }
        }
        return best;
    }

    private InteractableObject GetNearestVisibleInteractable()
    {
        var awareness = _character.CharacterAwareness;
        if (awareness == null) return null;
        var visible = awareness.GetVisibleInteractables();
        if (visible == null || visible.Count == 0) return null;
        InteractableObject closest = null;
        float closestDist = float.MaxValue;
        for (int i = 0; i < visible.Count; i++)
        {
            var obj = visible[i];
            if (obj == null) continue;
            float d = Vector3.Distance(_character.transform.position, obj.transform.position);
            if (d < closestDist) { closestDist = d; closest = obj; }
        }
        return closest;
    }

    /// <summary>
    /// Handles TAB key press to cycle-select interactables within the awareness zone.
    /// Sorts visible interactables by distance and selects the closest one,
    /// or cycles to the next if the closest is already selected.
    /// </summary>
    private void HandleTabTargeting()
    {
        EnsureTargeting();
        if (_targeting == null) return;

        var awareness = _character.CharacterAwareness;
        if (awareness == null) return;

        List<InteractableObject> visible = awareness.GetVisibleInteractables();
        if (visible == null || visible.Count == 0)
        {
            _targeting.ClearSelection();
            return;
        }

        // Sort by distance from the player
        visible = visible
            .OrderBy(i => Vector3.Distance(transform.position, 
                i.Rigidbody != null ? i.Rigidbody.position : i.transform.position))
            .ToList();

        InteractableObject currentSelection = _targeting.SelectedInteractable;

        if (currentSelection == null || !visible.Contains(currentSelection))
        {
            // Nothing selected or current selection left awareness range — pick closest
            _targeting.SelectInteractable(visible[0]);
        }
        else
        {
            // Current selection is in the list — cycle to the next one
            int currentIndex = visible.IndexOf(currentSelection);
            int nextIndex = (currentIndex + 1) % visible.Count;
            _targeting.SelectInteractable(visible[nextIndex]);
        }
    }

    protected override void UpdateFlip()
    {
        // CombatAILogic strictly handles facing the target during combat. Do not override it.
        if (_currentOrder is PlayerCombatCommand) return;

        if (_inputDir.x != 0)
        {
            _characterVisual?.UpdateFlip(_inputDir);
        }
        else if (_currentOrder != null && _characterMovement.HasPath)
        {
            Vector3 dir = _characterMovement.Destination - transform.position;
            if (dir.sqrMagnitude > 0.1f)
            {
                _characterVisual?.UpdateFlip(dir);
            }
        }
    }

    public void Move()
    {
        bool needsNavMesh = _currentOrder != null;

        if (needsNavMesh && !_wasNavMeshActiveLastFrame)
        {
            _character.ConfigureNavMesh(true);
        }
        else if (needsNavMesh && _wasNavMeshActiveLastFrame)
        {
            // Safety: If something externally disabled our NavAgent (e.g. knockback recovery
            // restoring pre-combat WASD state), re-enable it immediately.
            // BUT: do NOT override during active knockback — physics must stay in control.
            var agent = _character.CharacterMovement?.Agent;
            if (agent != null && !agent.enabled && !_character.CharacterMovement.IsKnockedBack)
            {
                Debug.Log($"<color=yellow>[PlayerCtrl]</color> NavAgent was externally disabled while in combat. Re-enabling.");
                _character.ConfigureNavMesh(true);
            }
        }
        else if (!needsNavMesh && _wasNavMeshActiveLastFrame)
        {
            _character.ConfigureNavMesh(false);
            _characterMovement.Stop();
        }
        _wasNavMeshActiveLastFrame = needsNavMesh;

        // Allow CombatAILogic to keep ticking during hit reactions — it handles its own
        // action gating via initiative checks. Only block non-combat orders during actions.
        if (_character.CharacterActions.CurrentAction != null && !(_currentOrder is PlayerCombatCommand))
            return;

        if (_currentOrder != null)
        {
            bool isFinished = _currentOrder.Tick(this, _characterMovement);
            if (isFinished)
            {
                SetOrder(null);
            }
        }
        else
        {
            // Manual WASD Control (Physical)
            Vector3 cameraForward = Vector3.Scale(Camera.main.transform.forward, new Vector3(1, 0, 1)).normalized;
            Vector3 moveDir = _inputDir.z * cameraForward + _inputDir.x * Camera.main.transform.right;

            if (moveDir.magnitude > 0.1f && !_isCrouching)
            {
                // God-mode speed override while dev mode is active.
                float speed = DevModeManager.SuppressPlayerInput
                    ? DevModeManager.GodModeMovementSpeed
                    : _character.MovementSpeed;
                _characterMovement.SetDesiredDirection(moveDir, speed);
            }
            else
            {
                _characterMovement.SetDesiredDirection(Vector3.zero, 0f);
            }
        }
    }
}
