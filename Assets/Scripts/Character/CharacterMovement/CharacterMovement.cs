using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Rigidbody))]
public class CharacterMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float _acceleration = 50f;
    [SerializeField] private float _groundCheckDistance = 0.2f;
    [SerializeField] private LayerMask _groundLayer;

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

        // Si l'agent est actif et a une destination, on le laisse piloter
        if (_agent != null && _agent.isOnNavMesh && !_agent.isStopped)
        {
            // L'agent gère sa propre vélocité, mais on peut la lisser ici si besoin
        }
        else
        {
            // Sinon, on applique le mouvement manuel (Joueur ou Direction forcée)
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

    public void Stop()
    {
        _isStopped = true;
        if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
        _rb.linearVelocity = Vector3.zero;
    }

    public void Resume()
    {
        _isStopped = false;
        if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = false;
    }

    public bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        return Physics.Raycast(origin, Vector3.down, _groundCheckDistance, _groundLayer);
    }

    public void Warp(Vector3 position)
    {
        if (_agent != null && NavMesh.SamplePosition(position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            transform.position = hit.position;
            _agent.Warp(hit.position);
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