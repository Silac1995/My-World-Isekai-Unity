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
    private float _knockbackTimer = 0f;
    private bool _wasKinematic = false;

    // Gestion de la stabilité du chemin
    private int _unstablePathFrames = 0;
    private const int MAX_UNSTABLE_FRAMES = 30; // ~0.6s à 50fps

    // --- ENCAPSULATION DE L'AGENT ---
    public NavMeshAgent Agent => _agent;
    public bool PathPending => _agent != null && _agent.pathPending;
    public bool HasPath => _agent != null && _agent.hasPath;
    public float RemainingDistance => _agent != null ? _agent.remainingDistance : 0f;
    public float StoppingDistance => _agent != null ? _agent.stoppingDistance : 0f;
    public bool IsOnNavMesh => _agent != null && _agent.isOnNavMesh;
    public NavMeshPathStatus PathStatus => _agent != null ? _agent.pathStatus : NavMeshPathStatus.PathInvalid;
    public Vector3 Destination => _agent != null ? _agent.destination : transform.position;
    public Vector3 Velocity => GetVelocity();
    public bool IsKnockedBack => _knockbackTimer > 0;

    private void Awake()
    {
        if (_character == null) _character = GetComponent<Character>();
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_agent == null) _agent = GetComponent<NavMeshAgent>();

        if (_agent != null)
        {
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
        }

        if (_rb != null) _wasKinematic = _rb.isKinematic;
    }

    private void FixedUpdate()
    {
        if (_knockbackTimer > 0)
        {
            _knockbackTimer -= Time.fixedDeltaTime;
            
            if (_knockbackTimer <= 0)
            {
                // Fin du knockback : On restaure l'état original
                if (_rb != null) _rb.isKinematic = _wasKinematic;
                
                // On ne réactive l'agent QUE pour ceux qui en ont besoin (NPCs cinématiques)
                // Le joueur (non-cinématique) reste libre de ses mouvements physiques.
                if (_agent != null && _wasKinematic)
                {
                    _agent.enabled = true;
                }
            }
            return;
        }

        if (_isStopped) return;

        if (_agent != null && _agent.isOnNavMesh && !_agent.isStopped)
        {
            // SECURITE BORD DE NAVMESH : Moins agressive
            if (_agent.hasPath && _agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                _unstablePathFrames++;
                if (_unstablePathFrames > MAX_UNSTABLE_FRAMES)
                {
                    Debug.LogWarning($"<color=orange>[Movement]</color> {name} : Chemin instable prolongé. Reset.");
                    Stop();
                    _unstablePathFrames = 0;
                }
            }
            else
            {
                _unstablePathFrames = 0;
            }
        }
        else
        {
            ApplyPhysicalMovement();
        }
    }

    private void ApplyPhysicalMovement()
    {
        Vector3 targetVelocity = _desiredDirection * _targetSpeed;
        Vector3 currentVelocity = _rb.linearVelocity;

        float velX = (targetVelocity.x - currentVelocity.x) * _acceleration;
        float velZ = (targetVelocity.z - currentVelocity.z) * _acceleration;

        _rb.AddForce(new Vector3(velX, 0, velZ), ForceMode.Acceleration);
    }

    public void SetDesiredDirection(Vector3 direction, float speed)
    {
        if (_knockbackTimer > 0) return;

        _desiredDirection = direction;
        _targetSpeed = speed;

        if (_agent != null && _agent.isOnNavMesh && direction.sqrMagnitude > 0.1f)
        {
            _agent.isStopped = true;
        }
    }

    public void SetDestination(Vector3 target)
    {
        float speed = _character != null ? _character.MovementSpeed : 3.5f;
        SetDestination(target, speed);
    }

    public void SetDestination(Vector3 target, float speed)
    {
        if (_knockbackTimer > 0) return;
        if (_agent == null || !_agent.isOnNavMesh) return;

        // --- SECURITE BORD DE NAVMESH ---
        // Augmentation du rayon de recherche à 5m pour plus de souplesse
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
        {
            _isStopped = false;
            _agent.isStopped = false;
            _agent.speed = speed;
            _agent.SetDestination(hit.position);
            _unstablePathFrames = 0;
        }
        else
        {
            // On n'appelle plus Stop() ici pour éviter de tout bloquer si un clic/ordre est imprécis
            Debug.LogWarning($"<color=yellow>[Movement]</color> Destination ignorée (hors NavMesh/Loin) pour {name} : {target}");
        }
    }

    public void ResetPath()
    {
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.ResetPath();
            _unstablePathFrames = 0;
        }
    }

    public void Stop()
    {
        // SÉCURITÉ : On ne réduit pas la vitesse à zéro si on est en plein knockback physique !
        if (_knockbackTimer > 0) return;

        _isStopped = true;
        _unstablePathFrames = 0;
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
        }
        if (_rb != null) _rb.linearVelocity = Vector3.zero;
    }

    public void Resume()
    {
        if (_knockbackTimer > 0) return;
        _isStopped = false;
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
        }
    }

    public void ForceResume()
    {
        _isStopped = false;
        _unstablePathFrames = 0;
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.ResetPath();
        }
    }

    public void ApplyKnockback(Vector3 force)
    {
        if (_rb == null) return;

        // --- MÉMOIRE ROBUSTE ---
        // On ne sauvegarde l'état que si on n'est pas déjà en knockback.
        // Cela évite d'écraser _wasKinematic par "false" (NPC en vol) lors d'un deuxième coup.
        if (_knockbackTimer <= 0)
        {
            _wasKinematic = _rb.isKinematic;
        }

        // --- INTERRUPTION DES ACTIONS ---
        if (_character != null && _character.CharacterActions != null)
        {
            _character.CharacterActions.ClearCurrentAction();
        }

        // --- PHYSIQUE & NAVIGATION ---
        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
            _agent.enabled = false; // Désactivation pour laisser la physique agir
        }

        _rb.isKinematic = false;
        _rb.AddForce(force, ForceMode.Impulse);
        
        _knockbackTimer = 0.4f;
        _isStopped = false; 
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
            transform.position = position;
            _agent.Warp(position);
            _agent.enabled = true;
        }
    }

    public Vector3 GetVelocity()
    {
        if (_agent != null && _agent.isOnNavMesh && !_agent.isStopped)
        {
            return _agent.velocity;
        }

        if (_rb != null)
        {
            return _rb.linearVelocity;
        }

        return Vector3.zero;
    }
}
