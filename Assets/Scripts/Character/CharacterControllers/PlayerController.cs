using UnityEngine;

public class PlayerController : CharacterGameController
{
    [SerializeField] private float rotationSpeed = 10f;
    private Vector3 inputDir = Vector3.zero;
    private bool isCrouching = false;

    private void Awake()
    {

        if (Animator != null)
            Animator.updateMode = AnimatorUpdateMode.Fixed;

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

        if (Animator != null)
        {
            Animator.SetBool("isWalking", inputDir.magnitude > 0.1f && !isCrouching);
        }

        base.Update(); // appelle Move() (IA) si défini
    }

    public override void Move()
    {
        if (currentBehaviour != null)
        {
            base.Move();
            return;
        }

        // 1. Si on ne bouge pas, on s'assure que la vitesse du Rigidbody est nulle
        // Cela évite la "glissade infinie"
        if (character == null || isCrouching || inputDir.magnitude < 0.1f)
        {
            if (character != null && character.Rigidbody != null)
            {
                // On stoppe les forces de mouvement mais on garde la gravité (vitesse Y)
                character.Rigidbody.linearVelocity = new Vector3(0, character.Rigidbody.linearVelocity.y, 0);
                character.Rigidbody.angularVelocity = Vector3.zero;
            }
            return;
        }

        // 2. Calcul du mouvement
        Vector3 moveDir = Quaternion.Euler(0f, Camera.main.transform.eulerAngles.y, 0f) * inputDir;

        // Utilisation de fixedDeltaTime si on est en physique
        float deltaTime = Time.inFixedTimeStep ? Time.fixedDeltaTime : Time.deltaTime;
        Vector3 move = moveDir * character.MovementSpeed * deltaTime;

        Rigidbody rb = character.Rigidbody;
        rb.MovePosition(rb.position + move);

        // 3. Rotation
        Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, rotationSpeed * deltaTime));

        characterVisual?.UpdateFlip(moveDir);
    }
    protected override void UpdateAnimations()
    {
        // On considère que le joueur bouge s'il appuie sur une touche OU si le corps bouge
        bool isMoving = inputDir.magnitude > 0.1f || character.Rigidbody.linearVelocity.magnitude > 0.1f;

        if (isMoving && character.CharacterActions.CurrentAction != null)
        {
            character.CharacterActions.ClearCurrentAction();
        }

        if (Animator != null)
        {
            // On n'oublie pas la condition de l'accroupissement que tu avais
            Animator.SetBool("isWalking", isMoving && !isCrouching);
        }
    }

    // Ajoute ceci dans ton PlayerController
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;

        // Vérifier si l'objet a un Rigidbody et n'est pas statique
        if (body == null || body.isKinematic) return;

        // Ne pas pousser les objets trop bas sous nos pieds
        if (hit.moveDirection.y < -0.3f) return;

        // Calculer la direction de la poussée (on pousse vers l'extérieur)
        Vector3 pushDir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);

        // Appliquer la force (ajuste le 'pushPower' selon tes besoins)
        float pushPower = 2.0f;
        body.linearVelocity = pushDir * pushPower;
    }

}