using UnityEngine;

using MWI.CharacterControllers.Commands;

public class PlayerController : CharacterGameController
{
    private Vector3 _inputDir = Vector3.zero;
    private bool _isCrouching = false;
    private bool _wasNavMeshActiveLastFrame = false;

    private IPlayerCommand _currentOrder;

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
            _character.Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    protected override void Update()
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

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        _inputDir = new Vector3(h, 0f, v).normalized;
        _isCrouching = Input.GetKey(KeyCode.C);

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

        // Right-click to move (standard RPG/MOBA)
        if (Input.GetMouseButtonDown(1) && !_character.CharacterCombat.IsInBattle)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                SetOrder(new PlayerMoveCommand(hit.point));
            }
        }

        // Auto-Trigger Combat Command when in battle. The command handles pacing and action execution.
        if (_character.CharacterCombat.IsInBattle && !(_currentOrder is PlayerCombatCommand))
        {
            Character battleTarget = _character.CharacterCombat.CurrentBattleManager?.GetBestTargetFor(_character);
            if (battleTarget != null)
            {
                SetOrder(new PlayerCombatCommand(_character, battleTarget));
            }
        }
        else if (!_character.CharacterCombat.IsInBattle && _currentOrder is PlayerCombatCommand)
        {
            // Exit combat gracefully
            SetOrder(null);
        }

        if (Input.GetKeyDown(KeyCode.J))
        {
            _character.CharacterCombat.ToggleCombatMode();
        }

        if (!_character.CharacterCombat.IsInBattle && Input.GetKeyDown(KeyCode.L))
        {
            _character.CharacterCombat.Attack(null);
        }

        base.Update();
        Move();
    }

    protected override void UpdateFlip()
    {
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
        if (_character.CharacterActions.CurrentAction != null) return;

        bool needsNavMesh = _currentOrder != null;

        if (needsNavMesh && !_wasNavMeshActiveLastFrame)
        {
            _character.ConfigureNavMesh(true);
        }
        else if (!needsNavMesh && _wasNavMeshActiveLastFrame)
        {
            _character.ConfigureNavMesh(false);
            _characterMovement.Stop();
        }
        _wasNavMeshActiveLastFrame = needsNavMesh;

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
                _characterMovement.SetDesiredDirection(moveDir, _character.MovementSpeed);
            }
            else
            {
                _characterMovement.SetDesiredDirection(Vector3.zero, 0f);
            }
        }
    }
}