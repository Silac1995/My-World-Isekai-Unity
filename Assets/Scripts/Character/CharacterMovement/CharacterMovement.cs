using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class CharacterMovement : CharacterSystem
{
    [Header("Movement Settings")]
    [SerializeField] private float _acceleration = 50f;
    [SerializeField] private float _groundCheckDistance = 0.2f;
    [SerializeField] private LayerMask _groundLayer;

    [Header("Step & Obstacle Handling")]
    [SerializeField] private float _stepHeight = 0.3f;
    [SerializeField] private float _stepSmooth = 0.08f;
    [SerializeField] private float _stepDetectDistance = 0.6f;
    [SerializeField] private CharacterObstacleAvoidance _obstacleAvoidance;

    [Header("References")]
    [SerializeField] private Rigidbody _rb;
    [SerializeField] private NavMeshAgent _agent;

    private Vector3 _desiredDirection;
    private float _targetSpeed;
    private bool _isStopped = false;
    private float _knockbackTimer = 0f;
    private bool _wasKinematic = false;
    private bool _wasAgentEnabled = false;

    // Gestion de la stabilité du chemin
    private int _unstablePathFrames = 0;
    private const int MAX_UNSTABLE_FRAMES = 30; // ~0.6s à 50fps

    // Gestion Stuck Detection
    private float _stuckTimer = 0f;
    private float _stuckCheckTimer = 0f;
    private Vector3 _lastStuckCheckPos;
    private bool _isSliding = false;

    // --- VELOCITY SMOOTHING FOR REMOTE CLIENTS ---
    // ClientNetworkTransform interpolation can stall between network ticks,
    // causing _empiricalVelocity to briefly drop to zero and flicker walk animations.
    private Vector3 _smoothedEmpiricalVelocity;
    private const float VELOCITY_SMOOTH_SPEED = 8f;

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
        if (_obstacleAvoidance == null) _obstacleAvoidance = GetComponent<CharacterObstacleAvoidance>();

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
        // Non-authoritative clients should NEVER drive their own physics
        if (IsSpawned && !IsOwner && !IsServer) return;

        if (_knockbackTimer > 0)
        {
            _knockbackTimer -= Time.fixedDeltaTime;
            
            if (_knockbackTimer <= 0)
            {
                // Si le personnage est mort ou évanoui pendant le knockback, on ne restaure pas le mouvement NavMesh
                // MAIS on finit de désactiver sa physique propre (qu'on avait laissée active pour le vol plané)
                if (_character != null && !_character.IsAlive())
                {
                    if (_rb != null) 
                    {
                        _rb.isKinematic = true;
                        _rb.useGravity = false;
                    }
                    if (_character.Collider != null) _character.Collider.enabled = false;
                    return;
                }

                // Fin du knockback normal : On restaure l'état
                // IMPORTANT: If the character has entered combat DURING the knockback window,
                // the pre-knockback state (_wasAgentEnabled=false, _wasKinematic=false) is STALE.
                // We must respect the current combat state instead of blindly restoring.
                bool isNowInCombat = _character != null && _character.CharacterCombat != null && _character.CharacterCombat.IsInBattle;
                
                if (isNowInCombat)
                {
                    // Combat requires NavMeshAgent ON and Rigidbody kinematic
                    if (_rb != null) _rb.isKinematic = true;
                    if (_agent != null) _agent.enabled = true;
                    Debug.Log($"<color=yellow>[Movement]</color> {name} knockback ended — combat active, forcing NavMesh ON.");
                }
                else
                {
                    // Restore to pre-knockback state (WASD mode)
                    if (_rb != null) _rb.isKinematic = _wasKinematic;
                    if (_agent != null) _agent.enabled = _wasAgentEnabled;
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

                        // Removed HandleStepUp for NavMeshAgent because NavMesh handles its own stepping.
            // Using MovePosition while a NavMeshAgent is active causes vertical fighting that broadcasts
            // a continuous wobble to all networked clients.
        }
        else
        {
            ApplyPhysicalMovement();
        }
    }

    private void ApplyPhysicalMovement()
    {
        Vector3 moveDir = _desiredDirection;

        // Proactive raycast steering against walls and corners
        if (_obstacleAvoidance != null && moveDir.sqrMagnitude > 0f)
        {
            moveDir = _obstacleAvoidance.GetAvoidanceDirection(moveDir);
        }

        Vector3 targetVelocity = moveDir * _targetSpeed;
        Vector3 currentVelocity = _rb.linearVelocity;

        float velX = (targetVelocity.x - currentVelocity.x) * _acceleration;
        float velZ = (targetVelocity.z - currentVelocity.z) * _acceleration;

        _rb.AddForce(new Vector3(velX, 0, velZ), ForceMode.Acceleration);

        // Assist physics engine over small vertical steps (e.g. stairs, 1-pixel height ridges)
        if (moveDir.sqrMagnitude > 0.01f)
        {
            HandleStepUp(moveDir);
        }
    }

    private void HandleStepUp(Vector3 moveDir)
    {
        if (!IsGrounded()) return;

        Vector3 forward = moveDir.normalized;
        
        // Find the absolute bottom of the physics collider to accurately trace the floor.
        Vector3 bottomOffset = transform.position;
        Collider col = _character != null ? _character.Collider : GetComponentInChildren<Collider>();
        if (col != null)
        {
            bottomOffset = new Vector3(transform.position.x, col.bounds.min.y, transform.position.z);
        }

        // Small margin from ground to detect the actual step face
        Vector3 lowerOrigin = bottomOffset + Vector3.up * 0.05f; 
        // Height clearance ray
        Vector3 upperOrigin = bottomOffset + Vector3.up * _stepHeight; 

        // Do we hit a vertical obstacle at foot level?
        if (Physics.Raycast(lowerOrigin, forward, out RaycastHit hitLower, _stepDetectDistance, _groundLayer, QueryTriggerInteraction.Ignore))
        {
            float hitAngle = Vector3.Angle(Vector3.up, hitLower.normal);
            // Only step up if the obstacle is relatively steep (not a regular ramp we can easily walk up)
            if (hitAngle > 45f) 
            {
                // Is there clearance at max step height?
                if (!Physics.Raycast(upperOrigin, forward, out RaycastHit hitUpper, _stepDetectDistance + 0.1f, _groundLayer, QueryTriggerInteraction.Ignore))
                {
                    // Use MovePosition instead of modifying position directly
                    Vector3 newPos = _rb.position + Vector3.up * _stepSmooth;
                    _rb.MovePosition(newPos);

                    // Cancel negative Y velocity to prevent gravity from immediately pushing down
                    if (_rb.linearVelocity.y < 0)
                    {
                        Vector3 vel = _rb.linearVelocity;
                        vel.y = 0f;
                        _rb.linearVelocity = vel;
                    }
                }
            }
        }
    }

    public void SetDesiredDirection(Vector3 direction, float speed)
    {
        if (_knockbackTimer > 0) return;

        // If the player provides directional input while the character is forced-stopped (e.g. from an interaction)
        // This acts as a manual override to break out of the stopped state.
        if (_isStopped && direction.sqrMagnitude > 0.1f)
        {
            Resume();
        }

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
        
        // Sécurité : On s'assure d'effacer les inputs physiques résiduels quand on force l'arrêt
        _desiredDirection = Vector3.zero;

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

    protected override void HandleWakeUp(Character character)
    {
        Resume();
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
        // Cela évite d'écraser l'état original lors d'un deuxième coup consécutif en plein vol.
        if (_knockbackTimer <= 0)
        {
            _wasKinematic = _rb.isKinematic;
            _wasAgentEnabled = _agent != null && _agent.enabled;
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

    /// <summary>
    /// Server broadcasts knockback to all clients so client-owned characters
    /// (using ClientNetworkTransform) apply the force locally on the authoritative side.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    public void ApplyKnockbackClientRpc(Vector3 force)
    {
        ApplyKnockback(force);
    }

    public bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        Collider col = _character != null ? _character.Collider : GetComponentInChildren<Collider>();
        if (col != null)
        {
            origin = new Vector3(transform.position.x, col.bounds.min.y + 0.1f, transform.position.z);
        }

        return Physics.Raycast(origin, Vector3.down, _groundCheckDistance, _groundLayer, QueryTriggerInteraction.Ignore);
    }

    public void Warp(Vector3 position)
    {
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.Warp(position);
        }
        else if (_rb != null)
        {
            _rb.position = position;
        }
        else
        {
            transform.position = position;
        }

        KillMomentum();
    }

    /// <summary>
    /// Teleports the character to a position, bypassing NavMeshAgent constraints.
    /// Used for cross-NavMesh teleports (e.g. entering/exiting building interiors)
    /// where the destination NavMesh may differ from the current one.
    /// </summary>
    public void ForceWarp(Vector3 position)
    {
        Debug.Log($"<color=magenta>[CharacterMovement]</color> ForceWarp called: target={position}, current={transform.position}");

        // Disable agent so it doesn't fight the teleport or snap back to the old NavMesh
        bool agentWasEnabled = _agent != null && _agent.enabled;
        if (_agent != null && _agent.enabled)
        {
            _agent.enabled = false;
        }

        // Disable rigidbody gravity temporarily so physics doesn't pull us during the teleport
        bool wasKinematic = false;
        if (_rb != null)
        {
            wasKinematic = _rb.isKinematic;
            _rb.isKinematic = true;
            _rb.position = position;
        }

        transform.position = position;
        KillMomentum();

        Debug.Log($"<color=magenta>[CharacterMovement]</color> ForceWarp position set: transform.position={transform.position}");

        // Re-enable agent after a delay so the destination NavMesh has time to be ready
        if (agentWasEnabled)
        {
            if (_forceWarpCoroutine != null) StopCoroutine(_forceWarpCoroutine);
            _forceWarpCoroutine = StartCoroutine(ReenableAgentDelayed(position, wasKinematic));
        }
        else if (_rb != null)
        {
            _rb.isKinematic = wasKinematic;
        }
    }

    private Coroutine _forceWarpCoroutine;

    private System.Collections.IEnumerator ReenableAgentDelayed(Vector3 position, bool wasKinematic)
    {
        // Wait 2 frames for the interior's NavMesh to be fully available
        yield return null;
        yield return null;

        if (_agent != null)
        {
            _agent.enabled = true;
            if (_agent.isOnNavMesh)
            {
                _agent.Warp(position);
                Debug.Log($"<color=magenta>[CharacterMovement]</color> Agent re-enabled and warped to {position}. isOnNavMesh={_agent.isOnNavMesh}");
            }
            else
            {
                Debug.LogWarning($"<color=orange>[CharacterMovement]</color> Agent re-enabled but NOT on NavMesh at {position}. transform.position={transform.position}");
            }
        }

        if (_rb != null)
        {
            _rb.isKinematic = wasKinematic;
        }

        _forceWarpCoroutine = null;
    }

    private void KillMomentum()
    {
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
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

            // For remote characters (non-authoritative), smooth the velocity to avoid
            // animation flickering caused by ClientNetworkTransform interpolation gaps.
            // Authoritative instances (Owner or Server) use raw velocity for responsiveness.
            bool isRemote = IsSpawned && !IsOwner && !IsServer;
            if (isRemote)
            {
                _smoothedEmpiricalVelocity = Vector3.Lerp(
                    _smoothedEmpiricalVelocity,
                    _empiricalVelocity,
                    Time.deltaTime * VELOCITY_SMOOTH_SPEED
                );
            }
            else
            {
                _smoothedEmpiricalVelocity = _empiricalVelocity;
            }
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

        // For remote characters, return the smoothed velocity to prevent
        // walk animation flickering from network interpolation gaps.
        bool isRemote = IsSpawned && !IsOwner && !IsServer;
        return isRemote ? _smoothedEmpiricalVelocity : _empiricalVelocity;
    }

    private void OnDrawGizmos()
    {
        // Scale the gizmos dynamically based on your step detect distance (which correlates to character scale)
        float vizSize = _stepDetectDistance > 0f ? _stepDetectDistance : 1f;

        // Draw the real physics position, not the visual one
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, vizSize * 1.5f); // pivot

        if (_rb != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_rb.position, vizSize * 1.25f); // rb position
        }

        if (_agent != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(_agent.nextPosition, vizSize * 1.0f); // agent position
        }
    }
}
