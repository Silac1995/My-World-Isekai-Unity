using UnityEngine;

public class PlayerController : CharacterGameController
{
    [SerializeField] private float rotationSpeed = 10f;
    private Vector3 inputDir = Vector3.zero;
    private bool isCrouching = false;

    private void Awake()
    {
        Initialize();

        if (animator != null)
            animator.updateMode = AnimatorUpdateMode.Fixed;

        if (character?.Rigidbody != null)
            character.Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    protected override void Update()
    {
        // Lecture des inputs
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        inputDir = new Vector3(h, 0f, v).normalized;
        isCrouching = Input.GetKey(KeyCode.C);

        if (animator != null)
        {
            animator.SetBool("isWalking", inputDir.magnitude > 0.1f && !isCrouching);
        }

        base.Update(); // appelle Move() (IA) si défini
    }

    public override void Move()
    {
        if (currentBehaviour != null)
        {
            // IA prend le contrôle
            base.Move();
            return;
        }

        // Mouvement manuel
        if (character == null || isCrouching || inputDir.magnitude < 0.1f)
            return;

        Vector3 moveDir = Quaternion.Euler(0f, Camera.main.transform.eulerAngles.y, 0f) * inputDir;
        Vector3 move = moveDir * character.MovementSpeed * Time.deltaTime;

        Rigidbody rb = character.Rigidbody;
        rb.MovePosition(rb.position + move);

        Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, rotationSpeed * Time.deltaTime));

        characterVisual?.UpdateFlip(moveDir);
    }
    protected override void UpdateAnimations()
    {
        // uniquement pour le joueur, animation basée sur input
        if (animator != null)
            animator.SetBool("isWalking", inputDir.magnitude > 0.1f && !isCrouching);
    }

}