using UnityEngine;
using UnityEngine.AI;

public class CombatBehaviour : IAIBehaviour
{
    private BattleManager _battleManager;
    private Character _currentTarget;
    private bool _isFinished = false;
    private Collider _battleZone;

    // Aesthetic & Natural Movement
    private Vector3 _currentDestination;
    private float _lastMoveTime;
    private float _moveInterval;
    private float _readyStartTime;
    
    // Safety & Stability
    private const float MIN_DISTANCE = 3f;      // Danger: Too close
    private const float MAX_DISTANCE = 15f;     // Danger: Too far
    private const float IDEAL_MIN = 6f;         // Buffer zone start
    private const float IDEAL_MAX = 12f;        // Buffer zone end

    public Character Target => _currentTarget;
    public bool IsFinished => _isFinished;
    public bool HasTarget => _currentTarget != null && _currentTarget.IsAlive();

    public CombatBehaviour(BattleManager battleManager, Character target)
    {
        _battleManager = battleManager;
        _currentTarget = target;
        _moveInterval = Random.Range(5f, 7f);
        _lastMoveTime = Time.time;
        
        if (target != null)
            _currentDestination = CalculateSafeDestination(target.transform.position, Vector3.zero);
    }

    public void SetCurrentTarget(Character target)
    {
        _currentTarget = target;
        _lastMoveTime = Time.time;
        if (target != null)
            _currentDestination = CalculateSafeDestination(target.transform.position, Vector3.zero);
    }

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        if (_battleManager == null || _isFinished) return;

        var movement = self.CharacterMovement;
        if (movement == null) return;

        if (!HasTarget)
        {
            movement.Stop();
            return;
        }

        movement.Resume();

        float distToTarget = Vector3.Distance(self.transform.position, _currentTarget.transform.position);
        
        if (self.CharacterCombat != null && self.CharacterCombat.IsReadyToAct)
        {
            if (_readyStartTime <= 0) _readyStartTime = Time.time;
            float timeReady = Time.time - _readyStartTime;

            // --- NOUVEAU : On n'approche que si la cible ne bouge plus OU si on perd patience ---
            bool targetIsStationary = _currentTarget.CharacterMovement != null && _currentTarget.CharacterMovement.GetVelocity().sqrMagnitude < 0.01f;
            bool lostPatience = timeReady > 1.0f; // On fonce après 1s d'attente

            if (targetIsStationary || lostPatience)
            {
                // On fonce sur la cible
                _currentDestination = _currentTarget.transform.position;
                
                float attackRange = self.CharacterCombat.CurrentCombatStyleExpertise?.Style?.AttackRange ?? 3.5f;
                
                // On ne frappe que si on est à portée ET (cible immobile OU grosse perte de patience)
                if (distToTarget <= attackRange && (targetIsStationary || timeReady > 2.0f))
                {
                    // On s'arrête et on tape
                    movement.Stop();
                    self.CharacterCombat.ExecuteAction(() => self.CharacterCombat.Attack());
                    
                    // On force une petite pause dans le mouvement pour éviter de glisser pendant l'anim
                    _lastMoveTime = Time.time;
                    _moveInterval = 1f; 
                    _readyStartTime = 0; // Reset
                    return;
                }
            }
        }
        else
        {
            _readyStartTime = 0; // On n'est plus prêt, on reset le timer
            // --- LOGIQUE DE DÉPLACEMENT "LAZY" (EXISTANTE) ---
            bool tooClose = distToTarget < MIN_DISTANCE;
            bool tooFar = distToTarget > MAX_DISTANCE;
            bool timerExpired = Time.time - _lastMoveTime > _moveInterval;

            // Stability check: Only update path if we aren't already moving or if it's been a while
            bool canUpdate = Time.time - _lastMoveTime > 1.5f;

            if (canUpdate && (tooClose || tooFar || timerExpired))
            {
                // If too close, we prioritize moving away in a logical direction
                if (tooClose)
                {
                    _currentDestination = CalculateEscapeDestination(self.transform.position, _currentTarget.transform.position);
                }
                else
                {
                    _currentDestination = CalculateSafeDestination(_currentTarget.transform.position, self.transform.position);
                }

                _moveInterval = Random.Range(5f, 7f); // High wait for "lazy" feel
                _lastMoveTime = Time.time;
            }
        }

        // BATTLE ZONE CONSTRAINT
        if (_battleZone == null) _battleZone = _battleManager.GetComponent<BoxCollider>();

        Vector3 finalPos = _currentDestination;
        if (_battleZone != null && !_battleZone.bounds.Contains(finalPos))
        {
            finalPos = _battleZone.ClosestPoint(finalPos);
        }
        
        // Only update movement if the destination is significantly different to avoid NavMesh jitter
        if (Vector3.Distance(movement.Destination, finalPos) > 0.5f)
        {
            movement.SetDestination(finalPos);
        }

        // Face target
        Vector3 dirToTarget = _currentTarget.transform.position - self.transform.position;
        self.CharacterVisual?.UpdateFlip(dirToTarget);
    }

    private Vector3 CalculateSafeDestination(Vector3 targetPos, Vector3 selfPos)
    {
        // Try to stay in the middle of current angle or pick new one
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float radius = Random.Range(IDEAL_MIN, IDEAL_MAX); 
        Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
        return targetPos + offset;
    }

    private Vector3 CalculateEscapeDestination(Vector3 selfPos, Vector3 targetPos)
    {
        // Move directly away from target to resolve "pushing"
        Vector3 dirAway = (selfPos - targetPos).normalized;
        if (dirAway.sqrMagnitude < 0.01f) dirAway = Vector3.forward;
        
        float radius = Random.Range(IDEAL_MIN, IDEAL_MIN + 2f); // Just enough to be safe
        return targetPos + dirAway * radius;
    }

    public void Exit(Character self)
    {
        self.CharacterMovement?.ResetPath();
        Debug.Log($"<color=orange>[AI]</color> {self.CharacterName} sort du mode Combat.");
    }
}
