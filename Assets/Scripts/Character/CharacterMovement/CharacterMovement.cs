using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Rigidbody))]
public class CharacterMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float _acceleration = 50f;
    [SerializeField] private float _groundCheckDistance = 0.2f;
    [SerializeField] private LayerMask _groundLayer;
    [SerializeField] Character _character;

    [Header("References")]
    [SerializeField] private Rigidbody _rb;
    [SerializeField] private NavMeshAgent _agent;

    private Vector3 _desiredDirection;
    private float _targetSpeed;
    private bool _isStopped = false;

    // Ajoute cette ligne à l'intérieur de ta classe CharacterMovement
    public NavMeshAgent Agent => _agent;

    private void Awake()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_agent == null) _agent = GetComponent<NavMeshAgent>();

        // Configuration pour éviter que le sprite 2D ne bascule dans le décor 3D
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        if (_agent != null)
        {
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
        }
    }

    private void FixedUpdate()
    {
        if (_isStopped) return;

        // SECURITÉ : On vérifie isOnNavMesh avant de lire isStopped
        if (_agent != null && _agent.isOnNavMesh && !_agent.isStopped)
        {
            // L'agent gère sa propre vélocité
        }
        else
        {
            ApplyPhysicalMovement();
        }
    }

    private void ApplyPhysicalMovement()
    {
        Vector3 targetVelocity = _desiredDirection * _targetSpeed;

        // On récupère la vitesse actuelle
        Vector3 currentVelocity = _rb.linearVelocity;

        // On ne calcule la différence que sur X et Z pour laisser la gravité gérer le Y
        float velX = (targetVelocity.x - currentVelocity.x) * _acceleration;
        float velZ = (targetVelocity.z - currentVelocity.z) * _acceleration;

        // On applique la force sans toucher à l'axe vertical
        _rb.AddForce(new Vector3(velX, 0, velZ), ForceMode.Acceleration);
    }

    // --- API PUBLIQUE POUR LE CONTROLLER ---

    public void SetDesiredDirection(Vector3 direction, float speed)
    {
        _desiredDirection = direction;
        _targetSpeed = speed;

        // Si on donne une direction manuelle, on dit à l'agent de s'arrêter
        if (_agent != null && _agent.isOnNavMesh && direction.sqrMagnitude > 0.1f)
        {
            _agent.isStopped = true;
        }
    }

    /// <summary>
    /// Définit une destination en utilisant la vitesse par défaut du personnage.
    /// </summary>
    public void SetDestination(Vector3 target)
    {
        if (_character != null)
        {
            // On force la vitesse de l'agent AVANT de définir la destination
            if (_agent != null) _agent.speed = _character.MovementSpeed;
            SetDestination(target, _character.MovementSpeed);
        }
        else
        {
            SetDestination(target, 3.5f);
        }
    }

    /// <summary>
    /// Définit une destination avec une vitesse spécifique.
    /// </summary>
    public void SetDestination(Vector3 target, float speed)
    {
        if (_agent != null && _agent.isOnNavMesh)
        {
            _isStopped = false;
            _agent.isStopped = false; // On s'assure qu'il n'est pas stoppé
            _agent.speed = speed;
            _agent.SetDestination(target);

            // Debug pour vérifier dans la console
            // Debug.Log($"{gameObject.name} se dirige vers {target} à une vitesse de {speed}");
        }
    }

    // Propriété de confort pour savoir si on bouge horizontalement
    public bool IsMovingHorizontally => GetVelocity().magnitude > 0.1f;

    public void Stop()
    {
        _isStopped = true;
        // SECURITÉ : On ne touche à l'agent que s'il est prêt
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
        }
        _rb.linearVelocity = Vector3.zero;
    }

    public void Resume()
    {
        _isStopped = false;
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
        }
    }
    public void ForceResume()
    {
        _isStopped = false;
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.ResetPath(); // On vide les vieux résidus de l'interaction
        }
    }

    public bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        return Physics.Raycast(origin, Vector3.down, _groundCheckDistance, _groundLayer);
    }

    public void Warp(Vector3 position)
    {
        if (_agent != null)
        {
            // On place l'objet
            transform.position = position;
            // On force l'agent à se reconnecter au NavMesh à cet endroit
            _agent.Warp(position);
            _agent.enabled = true; // On s'assure qu'il est actif
        }
    }

    public Vector3 GetVelocity()
    {
        // Si l'IA pilote l'agent
        if (_agent != null && _agent.isOnNavMesh && !_agent.isStopped)
        {
            return _agent.velocity;
        }

        // Si c'est le joueur ou un mouvement physique (Rigidbody)
        if (_rb != null)
        {
            return _rb.linearVelocity;
        }

        return Vector3.zero;
    }


}