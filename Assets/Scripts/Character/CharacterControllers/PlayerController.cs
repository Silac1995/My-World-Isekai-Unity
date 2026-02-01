using UnityEngine;

public class PlayerController : CharacterGameController
{
    private Vector3 _inputDir = Vector3.zero;
    private bool _isCrouching = false;

    public override void Initialize()
    {
        base.Initialize();

        if (_character.Rigidbody != null)
            _character.Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    protected override void Update()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        _inputDir = new Vector3(h, 0f, v).normalized;
        _isCrouching = Input.GetKey(KeyCode.C);

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