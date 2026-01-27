using UnityEngine;

public class GrassElement : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float _bendRotation = 20f; // L'angle d'inclinaison
    [SerializeField] private float _restoreSpeed = 5f;  // Vitesse de retour à la normale

    private Quaternion _initialRotation;
    private Quaternion _targetRotation;
    private int _collidersInside = 0;

    private void Awake()
    {
        _initialRotation = transform.rotation;
        _targetRotation = _initialRotation;
    }

    private void Update()
    {
        // On lisse le mouvement pour un effet organique (ressort)
        transform.rotation = Quaternion.Lerp(transform.rotation, _targetRotation, Time.deltaTime * _restoreSpeed);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Plus besoin de checks complexes, la matrice de collision fait le tri !
        _collidersInside++;
        CalculateBending(other.transform.position);
    }

    private void OnTriggerStay(Collider other)
    {
        // Optionnel : On peut mettre à jour l'inclinaison si le perso bouge dedans
        if (other.attachedRigidbody != null && other.attachedRigidbody.linearVelocity.magnitude > 0.1f)
        {
            CalculateBending(other.transform.position);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        _collidersInside--;
        if (_collidersInside <= 0)
        {
            _collidersInside = 0;
            _targetRotation = _initialRotation;
        }
    }

    private void CalculateBending(Vector3 actorPosition)
    {
        // On calcule la direction relative (Herbe - Acteur)
        Vector3 direction = (transform.position - actorPosition).normalized;

        // INVERSION : On ajoute un '-' pour que l'herbe se courbe dans le sens du mouvement
        // Si direction.x est positif (le perso est à gauche), l'herbe penchera à droite
        float angle = -direction.x * _bendRotation;

        _targetRotation = _initialRotation * Quaternion.Euler(0, 0, angle);
    }
}