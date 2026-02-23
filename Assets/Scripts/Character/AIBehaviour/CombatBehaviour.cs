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
        
        if (target != null && battleManager != null)
        {
            // Initialisation simple, la vraie destination sera demandée au manager dans Act()
            _currentDestination = target.transform.position; 
        }
    }

    public void SetCurrentTarget(Character target)
    {
        _currentTarget = target;
        _lastMoveTime = Time.time;
        // On laisse Act gérer le premier CalculateSafeDestination avec l'ID correct
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

            // --- NOUVEAU : On s'approche TOUJOURS si on est prêt (on ne reste pas planté) ---
            // On vise la cible mais on s'arrête un peu avant (90% de la range) pour éviter de se chevaucher
            float stopThreshold = attackRange * 0.9f;
            bool isWithinRange = distToTarget <= stopThreshold;
            bool isXTooClose = dx < X_FLIP_SAFETY;
            bool isZAligned = zDist <= 1.5f;

            if (!isWithinRange || isXTooClose || !isZAligned)
            {
                // Si on n'est pas à portée OU trop proche sur X OU pas aligné sur Z -> On bouge
                if (isXTooClose)
                    _currentDestination = CalculateEscapeDestination(self.transform.position, _currentTarget.transform.position);
                else
                    // Quand on est PRÊT à frapper, on FONCE sur la cible, on ne reste pas dans notre slot de formation (qui est trop loin)
                    _currentDestination = _currentTarget.transform.position;
            }
            else
            {
                // On n'arrête le perso QUE s'il est à portée, ok sur X et aligné sur Z
                movement.Stop();
            }
            
            // --- LOGIQUE DE FRAPPE ---
            // On ne frappe que si on est à portée (3D) et aligné en Z.
            // On ajoute un failsafe de 3s pour forcer la frappe même si la cible bouge
            bool targetIsStationary = _currentTarget.CharacterMovement != null && _currentTarget.CharacterMovement.GetVelocity().sqrMagnitude < 0.01f;
            bool forcedStrike = timeReady > 3.0f;
            bool patienceThresholdMet = timeReady > 1.5f || targetIsAggressiveTowardsUs || forcedStrike;

            if (distToTarget <= attackRange && zDist <= 1.5f && (targetIsStationary || patienceThresholdMet))
            {
                movement.Stop();
                self.CharacterCombat.ExecuteAction(() => self.CharacterCombat.Attack());
                
                _lastMoveTime = Time.time;
                _moveInterval = 1f; 
                _readyStartTime = 0;
                return;
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
                    _currentDestination = CalculateSafeDestination(self.transform.position, self);
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
            // On trouve le point le plus proche autorisé
            finalPos = _battleZone.ClosestPoint(finalPos);
            
            // On repousse très légèrement (0.2m) vers le centre de la zone pour être sûr
            // que le point soit bien accessible sur le NavMesh sans "gratter" la bordure
            Vector3 pushBackDir = (_battleZone.bounds.center - finalPos).normalized;
            finalPos += pushBackDir * 0.2f;
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

    private Vector3 CalculateSafeDestination(Vector3 selfPos, Character self)
    {
        if (_battleManager == null || _currentTarget == null) return _currentTarget.transform.position;

        // On demande au BattleManager notre place dans l'escarmouche en cours
        CombatEngagement engagement = _battleManager.RequestEngagement(self, _currentTarget);
        
        if (engagement != null)
        {
            return engagement.GetAssignedPosition(self);
        }

        // Fallback ultime (ne devrait jamais arriver si le BattleManager fonctionne)
        return _currentTarget.transform.position;
    }

    private Vector3 CalculateEscapeDestination(Vector3 selfPos, Vector3 targetPos)
    {
        // Déterminisme : si parfaitement alignés, on force une séparation basée sur l'ID
        // Sinon, on s'éloigne vers le point le plus dégagé de la cible sur l'axe X
        float xDir;
        if (Mathf.Abs(selfPos.x - targetPos.x) < 0.1f && _currentTarget != null) 
        {
            xDir = (_selfCharacter.GetInstanceID() > _currentTarget.GetInstanceID()) ? 1f : -1f;
        }
        else 
        {
            xDir = (selfPos.x > targetPos.x) ? 1f : -1f;
        }
        
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
