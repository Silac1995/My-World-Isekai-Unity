using UnityEngine;

namespace MWI.AI
{
    public class BTAction_Combat : BTNode
    {
        private BattleManager _battleManager;
        private Character _currentTarget;
        private Collider _battleZone;

        // Aesthetic & Natural Movement
        private Vector3 _currentDestination;
        private float _lastMoveTime;
        private float _moveInterval;
        private float _readyStartTime;
        
        // Safety & Stability
        private const float PREFERRED_X_GAP = 4.0f;  
        private const float X_FLIP_SAFETY = 1.5f;    
        private const float MAX_DISTANCE = 12.0f;    

        // --- SOFT ZONE CONSTRAINT ---
        private const float SOFT_ZONE_MARGIN = 5f; 
        private float _timeOutsideZone = 0f;
        private bool _isChargingTarget = false; 
        private const float OUTSIDE_ZONE_PATIENCE = 4f;

        protected override void OnEnter(Blackboard bb)
        {
            _moveInterval = UnityEngine.Random.Range(5f, 7f);
            _lastMoveTime = UnityEngine.Time.time;
            _timeOutsideZone = 0f;
            _readyStartTime = 0f;
            _isChargingTarget = false;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            _battleManager = bb.Get<BattleManager>(Blackboard.KEY_BATTLE_MANAGER);
            if (_battleManager == null) return BTNodeStatus.Failure;

            Character newTarget = bb.Get<Character>(Blackboard.KEY_COMBAT_TARGET);
            
            // Si la cible a change via le BT (BTCond_IsInCombat)
            if (newTarget != _currentTarget && newTarget != null)
            {
                _currentTarget = newTarget;
                _lastMoveTime = UnityEngine.Time.time;

                if (self.CharacterVisual != null)
                {
                    self.CharacterVisual.SetLookTarget(_currentTarget);
                }

                _battleManager.RequestEngagement(self, _currentTarget);
                _currentDestination = _currentTarget.transform.position;
            }

            if (_currentTarget == null || !_currentTarget.IsAlive())
            {
                self.CharacterMovement?.Stop();
                return BTNodeStatus.Running; // En attente du BT pour changer la cible
            }

            _isChargingTarget = false;
            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            // --- SOFT ZONE : Tracking du temps hors zone ---
            if (_battleZone == null) _battleZone = _battleManager.GetComponent<BoxCollider>();
            bool isOutsideZone = _battleZone != null && !_battleZone.bounds.Contains(self.transform.position);
            
            if (isOutsideZone)
            {
                _timeOutsideZone += UnityEngine.Time.deltaTime;
                
                if (_timeOutsideZone > OUTSIDE_ZONE_PATIENCE)
                {
                    float distOutside = Vector3.Distance(self.transform.position, _battleZone.ClosestPoint(self.transform.position));
                    
                    if (distOutside > SOFT_ZONE_MARGIN)
                    {
                        Vector3 returnPos = _battleZone.bounds.center;
                        movement.Resume();
                        movement.SetDestination(returnPos);
                        return BTNodeStatus.Running;
                    }
                }
            }
            else
            {
                _timeOutsideZone = 0f;
            }

            movement.Resume();
            float distToTarget = Vector3.Distance(self.transform.position, _currentTarget.transform.position);
            
            if (self.CharacterCombat != null && self.CharacterCombat.IsReadyToAct)
            {
                if (_readyStartTime <= 0) _readyStartTime = UnityEngine.Time.time;
                float timeReady = UnityEngine.Time.time - _readyStartTime;

                float attackRange = self.CharacterCombat.CurrentCombatStyleExpertise?.Style?.MeleeRange ?? 3.5f;
                float dx = Mathf.Abs(self.transform.position.x - _currentTarget.transform.position.x);
                float zDist = Mathf.Abs(self.transform.position.z - _currentTarget.transform.position.z);

                bool targetIsAggressiveTowardsUs = false;
                
                // --- TARGET AGGRESSION CHECK ---
                if (_currentTarget.Controller is NPCController targetNpc)
                {
                    if (targetNpc.HasBehaviourTree && targetNpc.BehaviourTree.Blackboard != null)
                    {
                        Character itsTarget = targetNpc.BehaviourTree.Blackboard.Get<Character>(Blackboard.KEY_COMBAT_TARGET);
                        if (itsTarget == self) targetIsAggressiveTowardsUs = true;
                    }
                }

                float stopThreshold = attackRange * 0.9f;
                bool isWithinRange = distToTarget <= stopThreshold;
                bool isXTooClose = dx < X_FLIP_SAFETY;
                bool isZAligned = zDist <= 1.5f;

                if (!isWithinRange || isXTooClose || !isZAligned)
                {
                    if (isXTooClose)
                        _currentDestination = CalculateEscapeDestination(self.transform.position, _currentTarget.transform.position, self);
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
                    
                    _lastMoveTime = UnityEngine.Time.time;
                    _moveInterval = 1f; 
                    _readyStartTime = 0;
                    return BTNodeStatus.Running;
                }
            }
            else
            {
                _readyStartTime = 0;
                bool tooClose = distToTarget < PREFERRED_X_GAP * 0.75f;
                bool tooFar = distToTarget > MAX_DISTANCE;
                bool timerExpired = UnityEngine.Time.time - _lastMoveTime > _moveInterval;
                bool canUpdate = UnityEngine.Time.time - _lastMoveTime > 1.5f;

                if (canUpdate && (tooClose || tooFar || timerExpired))
                {
                    if (tooClose)
                    {
                        _currentDestination = CalculateEscapeDestination(self.transform.position, _currentTarget.transform.position, self);
                    }
                    else
                    {
                        _currentDestination = CalculateSafeDestination(self.transform.position, self);
                    }

                    _moveInterval = UnityEngine.Random.Range(5f, 7f);
                    _lastMoveTime = UnityEngine.Time.time;
                }
            }

            Vector3 finalPos = _currentDestination;
            if (_battleZone != null && !_battleZone.bounds.Contains(finalPos) && !_isChargingTarget)
            {
                Vector3 closestInZone = _battleZone.ClosestPoint(finalPos);
                float distOutsideZone = Vector3.Distance(finalPos, closestInZone);
                float biasFactor = Mathf.Clamp01(distOutsideZone / SOFT_ZONE_MARGIN);
                finalPos = Vector3.Lerp(finalPos, closestInZone, biasFactor);
            }
            
            if (Vector3.Distance(movement.Destination, finalPos) > 0.5f)
            {
                movement.SetDestination(finalPos);
            }

            Vector3 dirToTarget = _currentTarget.transform.position - self.transform.position;
            self.CharacterVisual?.UpdateFlip(dirToTarget);

            return BTNodeStatus.Running;
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

        private Vector3 CalculateEscapeDestination(Vector3 selfPos, Vector3 targetPos, Character self)
        {
            float xDir;
            if (Mathf.Abs(selfPos.x - targetPos.x) < 0.1f && _currentTarget != null) 
            {
                xDir = (self.GetInstanceID() > _currentTarget.GetInstanceID()) ? 1f : -1f;
            }
            else 
            {
                xDir = (selfPos.x > targetPos.x) ? 1f : -1f;
            }
            
            float radius = PREFERRED_X_GAP;
            Vector3 offset = new Vector3(xDir * radius, 0, UnityEngine.Random.Range(-1.5f, 1.5f));
            
            return targetPos + offset;
        }

        protected override void OnExit(Blackboard bb)
        {
            Character self = bb.Self;
            if (self != null)
            {
                self.CharacterMovement?.ResetPath();
                self.CharacterMovement?.Resume();
                self.CharacterVisual?.ClearLookTarget();
            }
            _currentTarget = null;
        }
    }
}