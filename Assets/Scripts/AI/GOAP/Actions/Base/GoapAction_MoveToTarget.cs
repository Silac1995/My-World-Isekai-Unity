using System.Collections.Generic;
using UnityEngine;
using MWI.AI;

public abstract class GoapAction_MoveToTarget : GoapAction
{
    protected bool _isMoving = false;
    protected float _lastRouteRequestTime;
    protected bool _isComplete = false;
    protected Vector3 _lastTargetPos = Vector3.positiveInfinity;

    public override float Cost => 1f;

    public override bool IsComplete => _isComplete;

    /// <summary>
    /// Forces the child implementation to supply the current Target Collider.
    /// Can return null if moving to a raw Vector3 (DestinationPoint).
    /// </summary>
    protected abstract Collider GetTargetCollider(Character worker);

    /// <summary>
    /// Returns the exact destination transform or point. 
    /// If TargetCollider is valid, the script uses NavMeshUtility to snap to its edge automatically.
    /// </summary>
    protected abstract Vector3 GetDestinationPoint(Character worker);

    /// <summary>
    /// If true, the character will walk towards the physical center of the target zone 
    /// instead of stopping at the closest outer edge. Ideal for dropping items inside a zone.
    /// </summary>
    protected virtual bool ShouldGoInsideZone() => false;

    /// <summary>
    /// Evaluates if the agent has reached the destination criteria. Overridable for edge cases.
    /// </summary>
    protected virtual bool CheckArrival(Character worker, Collider targetZone, Vector3 targetPos)
    {
        if (targetZone != null)
        {
            return NavMeshUtility.IsCharacterAtTargetZone(worker, targetZone, 1.0f);
        }
        
        if (targetZone != null)
        {
            if (ShouldGoInsideZone())
            {
                Vector3 workerPosFlat = new Vector3(worker.transform.position.x, 0, worker.transform.position.z);
                Vector3 centerFlat = new Vector3(targetZone.bounds.center.x, 0, targetZone.bounds.center.z);
                
                if (Vector3.Distance(workerPosFlat, centerFlat) <= 2.5f) return true;
                
                // Secondary check: if the zone is huge, just being comfortably inside is enough
                if (targetZone.bounds.Contains(worker.transform.position) && Vector3.Distance(workerPosFlat, centerFlat) <= 4.0f) return true;
                
                return false;
            }
            else
            {
                float distToTarget = Vector3.Distance(worker.transform.position, targetZone.bounds.ClosestPoint(worker.transform.position));
                if (distToTarget <= 1.5f) return true;
            }
        }

        // Return false instead of relying on NavMesh completion, which triggers too early if path is blocked
        return false;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        var movement = worker.CharacterMovement;
        if (movement == null)
        {
            _isComplete = true;
            return;
        }

        Collider targetCol = GetTargetCollider(worker);
        Vector3 rawDest = GetDestinationPoint(worker);
        
        // Dynamic target updating if target character/item is moving
        bool hasTargetMoved = Vector3.Distance(_lastTargetPos, rawDest) > 1f;

        bool isCloseEnough = CheckArrival(worker, targetCol, rawDest);

        if (!isCloseEnough)
        {
            if (_isMoving)
            {
                bool hasPathFailed = NavMeshUtility.HasPathFailed(movement, _lastRouteRequestTime, 0.2f);
                if (hasPathFailed)
                {
                    int targetId = targetCol != null ? targetCol.gameObject.GetInstanceID() : rawDest.GetHashCode();
                    bool nowBlacklisted = worker.PathingMemory.RecordFailure(targetId);

                    if (nowBlacklisted)
                    {
                        // Stop moving entirely so GOAP can drop this plan
                        movement.Stop();
                        movement.ResetPath();
                        _isMoving = false;
                        _isComplete = true; // Fixes Infinite Loop
                        return; // Exit out, let GOAP fail it on IsValid or timeout
                    }
                    
                    // Force a recalculation for path diversification
                    _isMoving = false;
                }
            }

            if (!_isMoving || hasTargetMoved)
            {
                Vector3 finalDest = rawDest;
                if (targetCol != null)
                {
                    if (ShouldGoInsideZone())
                    {
                        finalDest = targetCol.bounds.center;
                    }
                    else
                    {
                        // Snap to the closest edge of the interation zone, reducing gridlock vs NavMeshObstacles (like trees)
                        finalDest = NavMeshUtility.GetOptimalDestination(worker, targetCol);
                    }
                }

                // Path Diversification logic
                int targetId = targetCol != null ? targetCol.gameObject.GetInstanceID() : rawDest.GetHashCode();
                int failCount = worker.PathingMemory.GetFailCount(targetId);
                
                if (failCount > 0)
                {
                    // Calculate a slight offset to try a different approach angle
                    // Rotate a base offset depending on the fail count (e.g. 1st fail = +90 deg, 2nd fail = -90 deg)
                    Vector3 directionToTarget = (finalDest - worker.transform.position).normalized;
                    if (directionToTarget.sqrMagnitude < 0.01f) directionToTarget = worker.transform.forward;

                    float sign = (failCount % 2 == 0) ? -1f : 1f;
                    float angle = 90f * sign;
                    Vector3 rotatedOffset = Quaternion.Euler(0, angle, 0) * directionToTarget;
                    
                    // Push the destination 1.5 units out sideways to try to walk around an obstacle
                    finalDest += rotatedOffset * 1.5f;

                    // Ensure the new offset point is valid NavMesh
                    if (UnityEngine.AI.NavMesh.SamplePosition(finalDest, out UnityEngine.AI.NavMeshHit hit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        finalDest = hit.position;
                    }
                }

                movement.SetDestination(finalDest);
                _lastTargetPos = rawDest; // Track the "real" destination for moving targets
                _lastRouteRequestTime = UnityEngine.Time.unscaledTime;
                _isMoving = true;
            }
        }
        else
        {
            // Arrived
            if (_isMoving)
            {
                movement.Stop();
                movement.ResetPath();
                _isMoving = false;
            }
            _isComplete = true;
        }
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _isMoving = false;
        _lastTargetPos = Vector3.positiveInfinity;
        worker.CharacterMovement?.Stop();
        worker.CharacterMovement?.ResetPath();
    }
}
