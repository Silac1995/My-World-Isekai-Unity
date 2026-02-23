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
    private const float PREFERRED_X_GAP = 4.0f;  
    private const float X_FLIP_SAFETY = 1.5f;    
    private const float IDEAL_MAX = 8.0f;        
    private const float MAX_DISTANCE = 12.0f;    

    public Character Target => _currentTarget;
    public bool IsFinished => _isFinished;
    public bool HasTarget => _currentTarget != null && _currentTarget.IsAlive() && (_battleManager == null || _battleManager.AreOpponents(_selfCharacter, _currentTarget));

    private Character _selfCharacter; // Cache pour HasTarget

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
        _selfCharacter = self; // Update cache
        if (_battleManager == null || _isFinished) return;

        var movement = self.CharacterMovement;
        if (movement == null) return;

        // --- NOUVEAU : DÉTECTION DE SORTIE DE ZONE ---
        // Si le personnage lui-même est en dehors, sa priorité ABSOLUE est de rerentrer
        if (_battleZone == null) _battleZone = _battleManager.GetComponent<BoxCollider>();
        if (_battleZone != null && !_battleZone.bounds.Contains(self.transform.position))
        {
            Vector3 returnPos = _battleZone.ClosestPoint(self.transform.position);
            // On ajoute une petite marge pour éviter les micro-ajustements
            if (Vector3.Distance(self.transform.position, returnPos) > 0.5f)
            {
                movement.Resume();
                movement.SetDestination(returnPos);
                Debug.Log($"<color=orange>[AI]</color> {self.CharacterName} est hors-zone ! Retour forcé vers le combat.");
                return; // On ne fait rien d'autre tant qu'on n'est pas revenu
            }
        }

        if (!HasTarget)
        {
            // Tentative de re-acquisition automatique si la cible est devenue invalide (allié ou morte)
            BattleTeam enemyTeam = _battleManager.GetOpponentTeamOf(self);
            Character nextTarget = enemyTeam?.GetClosestMember(self.transform.position);

            if (nextTarget != null)
            {
                SetCurrentTarget(nextTarget);
                Debug.Log($"<color=yellow>[AI]</color> {self.CharacterName} a change de cible pour {nextTarget.CharacterName} (cible precedente invalide).");
            }
            else
            {
                movement.Stop();
                return;
            }
        }

        movement.Resume();

        float distToTarget = Vector3.Distance(self.transform.position, _currentTarget.transform.position);
        
        if (self.CharacterCombat != null && self.CharacterCombat.IsReadyToAct)
        {
            if (_readyStartTime <= 0) _readyStartTime = Time.time;
            float timeReady = Time.time - _readyStartTime;

            float attackRange = self.CharacterCombat.CurrentCombatStyleExpertise?.Style?.AttackRange ?? 3.5f;
            float dx = Mathf.Abs(self.transform.position.x - _currentTarget.transform.position.x);
            float zDist = Mathf.Abs(self.transform.position.z - _currentTarget.transform.position.z);

            // --- NOUVEAU : DÉTECTION D'AGRESSION MUTUELLE ---
            // Si la cible nous cible aussi avec un CombatBehaviour, on considère l'agression comme mutuelle
            bool targetIsAggressiveTowardsUs = false;
            var targetCombatBehaviour = _currentTarget.Controller.GetCurrentBehaviour<CombatBehaviour>();
            if (targetCombatBehaviour != null && targetCombatBehaviour.Target == self)
            {
                targetIsAggressiveTowardsUs = true;
            }

            // --- NOUVEAU : On n'approche que si la cible ne bouge plus OU si on perd patience OU si agression mutuelle ---
            bool targetIsStationary = _currentTarget.CharacterMovement != null && _currentTarget.CharacterMovement.GetVelocity().sqrMagnitude < 0.01f;
            bool shouldStrikeFast = targetIsAggressiveTowardsUs || timeReady > 1.0f;

            if (targetIsStationary || shouldStrikeFast)
            {
                // On vise la cible mais on s'arrête un peu avant (90% de la range) pour éviter de se chevaucher
                // ET on respecte un écart X minimum pour que les hitboxes ne se chevauchent pas trop.
                float stopThreshold = attackRange * 0.9f;
                bool isWithinRange = distToTarget <= stopThreshold;
                bool isXTooClose = dx < X_FLIP_SAFETY; // Seul le risque de flip provoque une répulsion immédiate

                if (!isWithinRange || isXTooClose)
                {
                    // Si on est trop loin OU vraiment trop proche sur X, on recalcule
                    if (isXTooClose)
                    {
                        _currentDestination = CalculateEscapeDestination(self.transform.position, _currentTarget.transform.position);
                    }
                    else
                    {
                        _currentDestination = _currentTarget.transform.position;
                    }
                }
                else
                {
                    movement.Stop(); // On est à portée et bien positionné sur X
                }
                
                // On ne frappe que si on est à portée (3D) et aligné en Z.
                // L'écart X de PREFERRED_X_GAP (4.0) est un objectif de placement, pas un pré-requis strict pour frapper.
                bool patienceThresholdMet = timeReady > 2.0f || targetIsAggressiveTowardsUs;

                if (distToTarget <= attackRange && zDist <= 1.5f && (targetIsStationary || patienceThresholdMet))
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
            bool tooClose = distToTarget < PREFERRED_X_GAP * 0.75f;
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
        // On essaie de garder une distance X de confort (PREFERRED_X_GAP)
        float angle = Random.Range(30f, 60f) * Mathf.Deg2Rad; // Angle diagonal pour forcer un décalage X
        if (Random.value > 0.5f) angle = -angle;
        if (selfPos.x < targetPos.x) angle = Mathf.PI - angle;

        float radius = Random.Range(PREFERRED_X_GAP, IDEAL_MAX); 
        Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
        return targetPos + offset;
    }

    private Vector3 CalculateEscapeDestination(Vector3 selfPos, Vector3 targetPos)
    {
        // Déterminisme sur l'axe X : on s'éloigne jusqu'au gap de confort
        float xDir = (selfPos.x >= targetPos.x) ? 1f : -1f;
        
        float radius = PREFERRED_X_GAP;
        Vector3 offset = new Vector3(xDir * radius, 0, Random.Range(-1.5f, 1.5f));
        
        return targetPos + offset;
    }

    public void Exit(Character self)
    {
        self.CharacterMovement?.ResetPath();
        Debug.Log($"<color=orange>[AI]</color> {self.CharacterName} sort du mode Combat.");
    }
}
