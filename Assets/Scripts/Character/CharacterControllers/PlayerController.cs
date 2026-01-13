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


        base.Update(); // appelle Move() (IA) si défini
    }


    public override void Move()
    {
        if (character != null && !character.IsPlayer() || CurrentBehaviour != null)
        {
            base.Move();
            return;
        }

        Rigidbody rb = character.Rigidbody;
        if (rb == null) return;

        // Calcul de la direction SANS faire tourner le transform
        Vector3 moveDir = Quaternion.Euler(0f, Camera.main.transform.eulerAngles.y, 0f) * inputDir;

        if (moveDir.magnitude > 0.1f && !isCrouching)
        {
            Vector3 targetVelocity = moveDir * character.MovementSpeed;

            // On applique la vélocité
            Vector3 newVel = Vector3.Lerp(rb.linearVelocity,
                new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z),
                Time.deltaTime * 10f);

            rb.linearVelocity = newVel;

            // C'est SEULEMENT ici qu'on gère le regard (gauche/droite) via le scale
            characterVisual?.UpdateFlip(moveDir);
        }
        else
        {
            Vector3 stopVel = rb.linearVelocity;
            stopVel.x = Mathf.Lerp(stopVel.x, 0, Time.deltaTime * 15f);
            stopVel.z = Mathf.Lerp(stopVel.z, 0, Time.deltaTime * 15f);
            rb.linearVelocity = stopVel;
        }

        // SÉCURITÉ : On force la rotation à zéro pour éviter que la physique ne le fasse pivoter
        rb.rotation = Quaternion.identity;
    }

    protected override void UpdateAnimations()
    {
        if (Animator == null) return;

        // 1. On utilise l'inputDir car c'est la source la plus fiable pour le joueur
        float moveSpeed = inputDir.magnitude * character.MovementSpeed;

        // 2. On vérifie le sol (Augmente la distance du rayon dans IsGrounded si besoin)
        bool grounded = IsGrounded();

        // 3. Zone morte
        float finalSpeed = (moveSpeed < 0.1f || isCrouching) ? 0f : moveSpeed;

        // 4. On envoie au Hash
        Animator.SetFloat(CharacterAnimator.VelocityX, finalSpeed);
        Animator.SetBool(CharacterAnimator.IsGrounded, grounded);

        // Debug pour voir ce qui bloque
         //Debug.Log($"Player Anim: Speed {finalSpeed} | Grounded {grounded}");

        // Gestion du nettoyage d'action
        if (character.CharacterActions.CurrentAction == null)
        {
            Animator.SetBool(CharacterAnimator.IsDoingAction, false);
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