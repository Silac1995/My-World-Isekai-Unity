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
