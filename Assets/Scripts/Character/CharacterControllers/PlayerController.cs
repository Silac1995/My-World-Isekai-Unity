using UnityEngine;

public class PlayerController : CharacterGameController
{
    private Vector3 _inputDir = Vector3.zero;
    private bool _isCrouching = false;

    private bool _wasInBattleLastFrame = false;

    // --- Combat AI State (Mimicking NPC Behavior) ---
    private MWI.AI.CombatAILogic _combatAILogic;

    private Vector3? _clickDestination = null;

    public override void Initialize()
    {
        base.Initialize();
        
        _combatAILogic = new MWI.AI.CombatAILogic(_character, autoDecideIntent: false);

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

        if (_inputDir.sqrMagnitude > 0.1f)
        {
            _clickDestination = null; // ZQSD cancels click-to-move
        }

        // Right-click to move (standard RPG/MOBA)
        if (Input.GetMouseButtonDown(1) && !_character.CharacterCombat.IsInBattle)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                _clickDestination = hit.point;
            }
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

        // Appeler explicitement Move() ici si tu veux que l'input soit traité à chaque frame
        Move();
    }

    protected override void UpdateFlip()
    {
        if (_inputDir.x != 0)
        {
            _characterVisual?.UpdateFlip(_inputDir);
        }
        else if (_clickDestination.HasValue)
        {
            Vector3 dir = _clickDestination.Value - transform.position;
            if (dir.sqrMagnitude > 0.1f)
            {
                _characterVisual?.UpdateFlip(dir);
            }
        }
    }

    public void Move()
    {
        if (_character.CharacterActions.CurrentAction != null) return;

        bool currentInBattle = _character.CharacterCombat != null && _character.CharacterCombat.IsInBattle;

        // Transition logic for NavMesh (Battle or Click-To-Move)
        bool needsNavMesh = currentInBattle || _clickDestination.HasValue;

        if (needsNavMesh && !_wasInBattleLastFrame)
        {
            _character.ConfigureNavMesh(true);
            if (_character.Rigidbody != null) 
            {
                _character.Rigidbody.isKinematic = true; // Prevent physics stuttering while NavMesh controls movement
            }
        }
        else if (!needsNavMesh && _wasInBattleLastFrame)
        {
            _character.ConfigureNavMesh(false);
            if (_character.Rigidbody != null) 
            {
                _character.Rigidbody.isKinematic = false; // Restore physical movement for WASD
            }
            _characterMovement.Stop();
        }
        _wasInBattleLastFrame = needsNavMesh;

        if (currentInBattle)
        {
            _clickDestination = null; // Combat mode overrides click-to-move
            
            // Shared autonomous pacing logic (stutter-free, intent-driven)
            Character battleTarget = _character.CharacterCombat.CurrentBattleManager?.GetBestTargetFor(_character);
            
            if (battleTarget != null)
            {
                _combatAILogic.Tick(battleTarget);
            }
            else
            {
                _characterMovement.Stop();
            }
        }
        else if (_clickDestination.HasValue)
        {
            // Click-to-move execution
            if (Vector3.Distance(transform.position, _clickDestination.Value) > _characterMovement.StoppingDistance + 0.1f)
            {
                _characterMovement.Resume();
                if (Vector3.Distance(_characterMovement.Destination, _clickDestination.Value) > 0.5f)
                {
                    _characterMovement.SetDestination(_clickDestination.Value, _character.MovementSpeed);
                }
            }
            else
            {
                _characterMovement.Stop();
                _clickDestination = null; // Reached destination, turn off NavMesh automatically next frame
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