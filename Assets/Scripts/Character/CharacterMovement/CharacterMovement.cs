using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Rigidbody))]
public class CharacterMovement : CharacterSystem
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
    private float _knockbackTimer = 0f;
    private bool _wasKinematic = false;

    // Gestion de la stabilité du chemin
    private int _unstablePathFrames = 0;
    private const int MAX_UNSTABLE_FRAMES = 30; // ~0.6s à 50fps

    // Gestion Stuck Detection
    private float _stuckTimer = 0f;
    private float _stuckCheckTimer = 0f;
    private Vector3 _lastStuckCheckPos;
    private bool _isSliding = false;

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

    protected override void Awake()
    {
        base.Awake();
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_agent == null) _agent = GetComponent<NavMeshAgent>();

        if (_agent != null)
        {
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
            _agent.avoidancePriority = UnityEngine.Random.Range(30, 70);
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
                // Si le personnage est mort ou évanoui pendant le knockback, on ne restaure pas le mouvement NavMesh
                // MAIS on finit de désactiver sa physique propre (qu'on avait laissée active pour le vol plané)
                if (_character != null && !_character.IsAlive())
                {
                    if (_rb != null) _rb.isKinematic = true;
                    if (_character.Collider != null) _character.Collider.enabled = false;
                    return;
                }

                // Fin du knockback normal : On restaure l'état original
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

            // --- STUCK DETECTION SYSTEM ---
            if (_agent.hasPath && !_agent.pathPending && _agent.remainingDistance > (_agent.stoppingDistance + 0.1f))
            {
                _stuckCheckTimer += Time.fixedDeltaTime;
                if (_stuckCheckTimer >= 0.5f) // Eval every 0.5s
                {
                    float distMoved = Vector3.Distance(transform.position, _lastStuckCheckPos);
                    
                    if (distMoved < 0.2f) // Less than 0.2m in 0.5s = stuck
                    {
                        _stuckTimer += 0.5f;

                        if (_stuckTimer >= 1.5f && !_isSliding)
                        {
                            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                            _isSliding = true;
                        }
                    }
                    else
                    {
                        // Moving fine, cleanly reset
                        _stuckTimer = 0f;
                        if (_isSliding)
                        {
                            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                            _isSliding = false;
                        }
                    }

                    _lastStuckCheckPos = transform.position;
                    _stuckCheckTimer = 0f;
                }
            }
            else
            {
                // Reset everything if arrived or no path
                _stuckCheckTimer = 0f;
                _stuckTimer = 0f;
                if (_isSliding)
                {
                    // Restauration de l'évitement si on le désactivait pour le slide
                    _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                    _isSliding = false;
                }
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
            _agent.velocity = Vector3.zero;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance; // Empêche d'être poussé pendant les dialogues
            _isSliding = true; // Empêche notre système de resetter l'évitement par accident
        }
        if (_rb != null && !_rb.isKinematic) _rb.linearVelocity = Vector3.zero;
    }

    protected override void HandleIncapacitated(Character character)
    {
        Stop();
    }

    public void Resume()
    {
        if (_knockbackTimer > 0) return;
        _isStopped = false;
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance; // Rétablit l'évitement
            _isSliding = false;
        }
        _stuckTimer = 0f;
        _stuckCheckTimer = 0f;
        _lastStuckCheckPos = transform.position;
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

    private Vector3 _lastPosition;
    private Vector3 _empiricalVelocity;

    private void Start()
    {
        _lastPosition = transform.position;
    }

    private void LateUpdate()
    {
        if (Time.deltaTime > 0f)
        {
            _empiricalVelocity = (transform.position - _lastPosition) / Time.deltaTime;
        }
        _lastPosition = transform.position;
    }

    public Vector3 GetVelocity()
    {
        if (_agent != null && _agent.isOnNavMesh && !_agent.isStopped)
        {
            if (_agent.velocity.sqrMagnitude > 0.01f)
                return _agent.velocity;
        }

        if (_rb != null && !_rb.isKinematic)
        {
            if (_rb.linearVelocity.sqrMagnitude > 0.01f)
                return _rb.linearVelocity;
        }

        return _empiricalVelocity;
    }
}
