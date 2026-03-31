using UnityEngine;

public class CombatTacticalPacer
{
    private Character _self;
    private Character _lastBattleTarget;
    private Vector3 _currentDestination;
    private Vector3 _currentWanderJitter;
    private float _lastMoveTime;
    private float _moveInterval;
    
    private const float PREFERRED_X_GAP = 5.0f;
    private const float X_FLIP_SAFETY = 1.5f;
    private const float MAX_DISTANCE = 10.0f;
    
    // Soft zone constraint
    private const float SOFT_ZONE_MARGIN = 5f; 
    private float _timeOutsideZone = 0f;
    private const float OUTSIDE_ZONE_PATIENCE = 4f;

    public CombatTacticalPacer(Character self)
    {
        _self = self;
        _lastMoveTime = Time.time;
        _moveInterval = Random.Range(5f, 8f);
    }

    public Vector3 GetTacticalDestination(Character target, float attackRange, bool isChargingTarget)
    {
        if (target != _lastBattleTarget)
        {
            _lastBattleTarget = target;
            _lastMoveTime = Time.time;
            _moveInterval = Random.Range(5f, 8f);
            
            var bm = _self.CharacterCombat.CurrentBattleManager;
            if (target != null) 
            {
                _currentDestination = CalculateSafeDestination(target, bm);
            }
        }

        if (target == null) return _self.transform.position;

        var battleManager = _self.CharacterCombat.CurrentBattleManager;
        Collider battleZone = battleManager != null ? battleManager.GetComponent<BoxCollider>() : null;
        
        // --- SOFT ZONE: Track time outside zone ---
        bool isOutsideZone = battleZone != null && !battleZone.bounds.Contains(_self.transform.position);
        if (isOutsideZone)
        {
            _timeOutsideZone += Time.deltaTime;
            if (_timeOutsideZone > OUTSIDE_ZONE_PATIENCE)
            {
                float distOutside = Vector3.Distance(_self.transform.position, battleZone.ClosestPoint(_self.transform.position));
                if (distOutside > SOFT_ZONE_MARGIN)
                {
                    // Return heavily toward center if stranding outside
                    return battleZone.bounds.center;
                }
            }
        }
        else
        {
            _timeOutsideZone = 0f;
        }

        float distToTarget = Vector3.Distance(_self.transform.position, target.transform.position);

        // Melee vs Ranged behaviors
        float escapeRadius;
        if (attackRange <= 4.0f) 
        {
            // Melee fighters only step back to prevent model clipping, standing their ground
            escapeRadius = 2.5f;
        }
        else 
        {
            // Ranged fighters actively kite to maintain distance
            escapeRadius = attackRange * 0.6f;
        }
        
        bool tooClose = distToTarget < escapeRadius;
        bool tooFar = distToTarget > MAX_DISTANCE;
        bool timerExpired = Time.time - _lastMoveTime > _moveInterval;
        bool canUpdate = Time.time - _lastMoveTime > 1.5f;

        if (canUpdate && (tooClose || tooFar || timerExpired) && !isChargingTarget)
        {
            if (tooClose)
            {
                _currentDestination = CalculateEscapeDestination(_self.transform.position, target.transform.position, attackRange);
            }
            else
            {
                Vector3 basePos = CalculateSafeDestination(target, battleManager);
                // Inject random wander jitter to ensure they don't freeze indefinitely when timer expires
                _currentWanderJitter = new Vector3(Random.Range(-1.5f, 1.5f), 0, Random.Range(-1.5f, 1.5f));
                _currentDestination = basePos + _currentWanderJitter;
            }

            _moveInterval = Random.Range(5f, 8f);
            _lastMoveTime = Time.time;
        }
        else if (isChargingTarget)
        {
            _currentDestination = target.transform.position;
        }

        Vector3 finalPos = _currentDestination;

        // Apply soft bounds clamp if we are not actively charging to attack
        if (battleZone != null && !battleZone.bounds.Contains(finalPos) && !isChargingTarget)
        {
            Vector3 closestInZone = battleZone.ClosestPoint(finalPos);
            float distOutsideZone = Vector3.Distance(finalPos, closestInZone);
            float biasFactor = Mathf.Clamp01(distOutsideZone / SOFT_ZONE_MARGIN);
            finalPos = Vector3.Lerp(finalPos, closestInZone, biasFactor);
        }

        return finalPos;
    }

    private Vector3 CalculateSafeDestination(Character target, BattleManager battleManager)
    {
        if (battleManager == null || target == null) return target != null ? target.transform.position : _self.transform.position;

        var engagement = battleManager.GetEngagementOf(_self);
        if (engagement != null)
        {
            return engagement.GetAssignedPosition(_self);
        }

        return target.transform.position;
    }

    private Vector3 CalculateEscapeDestination(Vector3 selfPos, Vector3 targetPos, float attackRange)
    {
        float xDir;
        if (Mathf.Abs(selfPos.x - targetPos.x) < 0.1f && _lastBattleTarget != null) 
        {
            // S'ils sont parfaitement juxtaposés, on force une direction basée sur l'ID pour être déterministe
            xDir = (_self.GetInstanceID() > _lastBattleTarget.GetInstanceID()) ? 1f : -1f;
        }
        else 
        {
            xDir = (selfPos.x > targetPos.x) ? 1f : -1f;
        }
        
        float radius = Mathf.Max(PREFERRED_X_GAP, attackRange * 0.9f);
        Vector3 offset = new Vector3(xDir * radius, 0, Random.Range(-1.5f, 1.5f));
        
        return targetPos + offset;
    }
}
