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
    /// Evaluates if the agent has reached the destination criteria. Overridable for edge cases.
    /// </summary>
    protected virtual bool CheckArrival(Character worker, Collider targetZone, Vector3 targetPos)
    {
        if (targetZone != null)
        {
            return NavMeshUtility.IsCharacterAtTargetZone(worker, targetZone, 1.0f);
        }
        
        // If no collider exists, evaluate raw Vector3
        if (Vector3.Distance(worker.transform.position, targetPos) <= 2.5f)
        {
            return true;
        }

        if (targetZone != null)
        {
            float distToTarget = Vector3.Distance(worker.transform.position, targetZone.bounds.ClosestPoint(worker.transform.position));
            if (distToTarget <= 1.5f) return true;
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
            bool hasPathFailed = NavMeshUtility.HasPathFailed(movement, _lastRouteRequestTime, 0.2f);

            if (!_isMoving || hasPathFailed || hasTargetMoved)
            {
                Vector3 finalDest = rawDest;
                if (targetCol != null)
                {
                    // Snap to the closest edge of the interation zone, reducing gridlock vs NavMeshObstacles (like trees)
                    finalDest = NavMeshUtility.GetOptimalDestination(worker, targetCol);
                }

                movement.SetDestination(finalDest);
                _lastTargetPos = rawDest; // Track the "real" destination for moving targets
                _lastRouteRequestTime = UnityEngine.Time.time;
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
