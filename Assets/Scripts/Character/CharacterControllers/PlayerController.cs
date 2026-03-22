using UnityEngine;

public class PlayerController : CharacterGameController
{
    private Vector3 _inputDir = Vector3.zero;
    private bool _isCrouching = false;

    private float _nextPathUpdateTime = 0f;
    private const float PATH_UPDATE_INTERVAL = 0.2f;
    private bool _wasInBattleLastFrame = false;

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

    // Changé de public à protected pour correspondre au parent
    protected override void UpdateFlip()
    {
        if (_inputDir.x != 0)
        {
            _characterVisual?.UpdateFlip(_inputDir);
        }
    }

    public void Move()
    {
        if (_character.CharacterActions.CurrentAction != null) return;

        bool currentInBattle = _character.CharacterCombat != null && _character.CharacterCombat.IsInBattle;

        // Transition logic for NavMesh
        if (currentInBattle && !_wasInBattleLastFrame)
        {
            _character.ConfigureNavMesh(true);
        }
        else if (!currentInBattle && _wasInBattleLastFrame)
        {
            _character.ConfigureNavMesh(false);
            _characterMovement.Stop();
        }
        _wasInBattleLastFrame = currentInBattle;

        if (currentInBattle)
        {
            // AI-like autonomous movement
            Character battleTarget = _character.CharacterCombat.CurrentBattleManager?.GetBestTargetFor(_character);
            
            if (battleTarget != null)
            {
                float distance = Vector3.Distance(transform.position, battleTarget.transform.position);
                float engagementDistance = 2.5f; // Hardcoded default engagement distance

                if (distance > engagementDistance)
                {
                    if (Time.time >= _nextPathUpdateTime)
                    {
                        _nextPathUpdateTime = Time.time + PATH_UPDATE_INTERVAL;
                        _characterMovement.ForceResume();
                        _characterMovement.SetDestination(battleTarget.transform.position);
                    }
                }
                else
                {
                    _characterMovement.Stop();
                    // Face target
                    Vector3 direction = battleTarget.transform.position - transform.position;
                    _characterVisual?.UpdateFlip(direction);
                }
            }
            else
            {
                _characterMovement.Stop();
            }
        }
        else
        {
            // Manual WASD Control
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