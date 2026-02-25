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

    // --- SOFT ZONE CONSTRAINT ---
    // Au lieu d'un mur invisible, on applique un biais doux vers le centre de la zone
    // quand le NPC est en dehors. Il peut sortir pour sa formation/attaque, mais sera
    // doucement ramene si rien ne le retient dehors.
    private const float SOFT_ZONE_MARGIN = 5f; // Distance max hors zone avant biais fort
    private float _timeOutsideZone = 0f;
    private bool _isChargingTarget = false; // True quand le NPC fonce sur sa cible pour attaquer
    private const float OUTSIDE_ZONE_PATIENCE = 4f; // Secondes avant de forcer un retour

    public Character Target => _currentTarget;
    public bool IsFinished => _isFinished;
    public bool HasTarget => _currentTarget != null && _currentTarget.IsAlive() && (_battleManager == null || _battleManager.AreOpponents(_selfCharacter, _currentTarget));

    private Character _selfCharacter;

    public CombatBehaviour(BattleManager battleManager, Character target)
    {
        _battleManager = battleManager;
        _currentTarget = target;
        _moveInterval = Random.Range(5f, 7f);
        _lastMoveTime = Time.time;
        
        if (target != null && battleManager != null)
        {
            _currentDestination = target.transform.position; 
        }
    }

    public void SetCurrentTarget(Character target)
    {
        _currentTarget = target;
        _lastMoveTime = Time.time;

        // Rejoindre/créer un engagement dès le changement de cible
        if (_battleManager != null && _selfCharacter != null && target != null)
        {
            _battleManager.RequestEngagement(_selfCharacter, target);
        }

        // Orienter le regard vers la cible de combat
        if (_selfCharacter != null && _selfCharacter.CharacterVisual != null && target != null)
        {
            _selfCharacter.CharacterVisual.SetLookTarget(target);
        }
    }

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        _selfCharacter = self;
        _isChargingTarget = false;

        // S'assurer que le look target est bien set (peut être manqué au constructeur car _selfCharacter était null)
        if (_currentTarget != null && self.CharacterVisual != null && !self.CharacterVisual.HasLookTarget)
        {
            self.CharacterVisual.SetLookTarget(_currentTarget);
        }

        if (_battleManager == null || _isFinished) return;

        var movement = self.CharacterMovement;
        if (movement == null) return;

        // --- SOFT ZONE : Tracking du temps hors zone ---
        if (_battleZone == null) _battleZone = _battleManager.GetComponent<BoxCollider>();
        bool isOutsideZone = _battleZone != null && !_battleZone.bounds.Contains(self.transform.position);
        
        if (isOutsideZone)
        {
            _timeOutsideZone += Time.deltaTime;
            
            // Si le NPC est hors zone depuis trop longtemps ET qu'il n'est pas en train d'attaquer,
            // on le ramene doucement vers le centre de la zone
            if (_timeOutsideZone > OUTSIDE_ZONE_PATIENCE)
            {
                float distOutside = Vector3.Distance(self.transform.position, _battleZone.ClosestPoint(self.transform.position));
                
                // Si vraiment trop loin (au-dela de la marge), retour prioritaire
                if (distOutside > SOFT_ZONE_MARGIN)
                {
                    Vector3 returnPos = _battleZone.bounds.center;
                    movement.Resume();
                    movement.SetDestination(returnPos);
                    Debug.Log($"<color=orange>[AI]</color> {self.CharacterName} est trop loin de la zone de combat, retour en douceur.");
                    return;
                }
            }
        }
        else
        {
            _timeOutsideZone = 0f;
        }

        if (!HasTarget)
        {
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

            bool targetIsAggressiveTowardsUs = false;
            var targetCombatBehaviour = _currentTarget.Controller.GetCurrentBehaviour<CombatBehaviour>();
            if (targetCombatBehaviour != null && targetCombatBehaviour.Target == self)
            {
                targetIsAggressiveTowardsUs = true;
            }

            float stopThreshold = attackRange * 0.9f;
            bool isWithinRange = distToTarget <= stopThreshold;
            bool isXTooClose = dx < X_FLIP_SAFETY;
            bool isZAligned = zDist <= 1.5f;

            if (!isWithinRange || isXTooClose || !isZAligned)
            {
                if (isXTooClose)
                    _currentDestination = CalculateEscapeDestination(self.transform.position, _currentTarget.transform.position);
                _isChargingTarget = true;
                    _currentDestination = _currentTarget.transform.position;
            }
            else
            {
                movement.Stop();
            }
            
            bool targetIsStationary = _currentTarget.CharacterMovement != null && _currentTarget.CharacterMovement.GetVelocity().sqrMagnitude < 0.01f;
            bool forcedStrike = timeReady > 3.0f;
            bool patienceThresholdMet = timeReady > 1.5f || targetIsAggressiveTowardsUs || forcedStrike;

            if (distToTarget <= attackRange && zDist <= 1.5f && (targetIsStationary || patienceThresholdMet))
            {
                movement.Stop();
                
                self.CharacterVisual?.FaceTarget(_currentTarget.transform.position);

                self.CharacterCombat.ExecuteAction(() => self.CharacterCombat.Attack());
                
                _lastMoveTime = Time.time;
                _moveInterval = 1f; 
                _readyStartTime = 0;
                return;
            }
        }
        else
        {
            _readyStartTime = 0;
            bool tooClose = distToTarget < PREFERRED_X_GAP * 0.75f;
            bool tooFar = distToTarget > MAX_DISTANCE;
            bool timerExpired = Time.time - _lastMoveTime > _moveInterval;

            bool canUpdate = Time.time - _lastMoveTime > 1.5f;

            if (canUpdate && (tooClose || tooFar || timerExpired))
            {
                if (tooClose)
                {
                    _currentDestination = CalculateEscapeDestination(self.transform.position, _currentTarget.transform.position);
                }
                else
                {
                    _currentDestination = CalculateSafeDestination(self.transform.position, self);
                }

                _moveInterval = Random.Range(5f, 7f);
                _lastMoveTime = Time.time;
            }
        }

        // --- SOFT ZONE BIAS (au lieu du hard clamp) ---
        // Si la destination est hors zone, on la tire doucement vers le centre
        // au lieu de la clamper brutalement sur le bord
        Vector3 finalPos = _currentDestination;
        if (_battleZone != null && !_battleZone.bounds.Contains(finalPos) && !_isChargingTarget)
        {
            Vector3 closestInZone = _battleZone.ClosestPoint(finalPos);
            float distOutsideZone = Vector3.Distance(finalPos, closestInZone);
            
            // Plus on est loin de la zone, plus le biais est fort (0% a 0m, 100% a SOFT_ZONE_MARGIN)
            float biasFactor = Mathf.Clamp01(distOutsideZone / SOFT_ZONE_MARGIN);
            
            // On interpole entre la destination originale et le point le plus proche dans la zone
            // biasFactor faible = on garde presque la destination originale (formation OK)
            // biasFactor fort = on ramene fortement vers la zone
            finalPos = Vector3.Lerp(finalPos, closestInZone, biasFactor);
        }
        
        if (Vector3.Distance(movement.Destination, finalPos) > 0.5f)
        {
            movement.SetDestination(finalPos);
        }

        Vector3 dirToTarget = _currentTarget.transform.position - self.transform.position;
        self.CharacterVisual?.UpdateFlip(dirToTarget);
    }

    private Vector3 CalculateSafeDestination(Vector3 selfPos, Character self)
    {
        if (_battleManager == null || _currentTarget == null) return _currentTarget.transform.position;

        CombatEngagement engagement = _battleManager.RequestEngagement(self, _currentTarget);
        
        if (engagement != null)
        {
            return engagement.GetAssignedPosition(self);
        }

        return _currentTarget.transform.position;
    }

    private Vector3 CalculateEscapeDestination(Vector3 selfPos, Vector3 targetPos)
    {
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
        self.CharacterVisual?.ClearLookTarget();
        Debug.Log($"<color=orange>[AI]</color> {self.CharacterName} sort du mode Combat.");
    }
}