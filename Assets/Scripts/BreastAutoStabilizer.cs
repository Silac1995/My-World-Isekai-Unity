using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class BreastAutoReset : MonoBehaviour
{
    private Vector3 initialLocalPos;
    private Rigidbody2D rb;

    [Header("Settings")]
    public float idleTime = 2f;       // Temps d'inactivité avant reset
    public float resetSpeed = 5f;     // Vitesse du retour

    private float timeSinceMove;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        initialLocalPos = transform.localPosition;
        timeSinceMove = 0f;
    }

    void FixedUpdate()
    {
        // Vérifie si le sein bouge encore
        if (rb.linearVelocity.magnitude > 0.01f)
        {
            timeSinceMove = 0f; // reset du timer si mouvement
        }
        else
        {
            timeSinceMove += Time.fixedDeltaTime;
        }

        // Si sein immobile depuis idleTime → recentrage
        if (timeSinceMove >= idleTime)
        {
            Vector3 targetWorldPos = transform.parent.TransformPoint(initialLocalPos);
            Vector2 newPos = Vector2.Lerp(
                rb.position,
                targetWorldPos,
                Time.fixedDeltaTime * resetSpeed
            );

            rb.MovePosition(newPos);
        }
    }
}
