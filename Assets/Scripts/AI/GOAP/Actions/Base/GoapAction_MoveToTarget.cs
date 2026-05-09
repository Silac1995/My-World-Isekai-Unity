using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MWI.AI;

public abstract class GoapAction_MoveToTarget : GoapAction
{
    protected bool _isMoving = false;
    protected float _lastRouteRequestTime;
    protected bool _isComplete = false;
    protected Vector3 _lastTargetPos = Vector3.positiveInfinity;

    /// <summary>
    /// Phase-B safety net: an upfront NavMesh.CalculatePath guard runs the first time
    /// we try to move toward a given destination. If the path is Invalid or Partial the
    /// action is aborted BEFORE <c>movement.SetDestination</c> commits — so the worker
    /// never walks to the wrong place just because the destination is inside a tiny
    /// unreachable pocket of NavMesh. Subclasses override <see cref="OnPathUnreachable"/>
    /// to trigger domain-specific rollback (e.g. transporter cancels its TransportOrder).
    /// Stays false until an abort fires so the "arrived" branch + Exit reset work normally.
    /// </summary>
    protected bool _pathAbortedUnreachable = false;

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
    /// Phase-B hook. Called once, server-side, when the upfront
    /// <c>NavMesh.CalculatePath</c> probe reports Invalid or Partial for the worker →
    /// destination segment. Default behaviour: just log — subclass overrides (notably
    /// <c>GoapAction_MoveToItem</c> and <c>GoapAction_MoveToDestination</c>) roll
    /// back the active <c>TransportOrder</c> via <c>JobTransporter.CancelCurrentOrder</c>
    /// and report the missing reservation so the supplier recomputes logistics.
    ///
    /// <paramref name="status"/> distinguishes <c>PathInvalid</c> (nothing at all reachable)
    /// from <c>PathPartial</c> (reachable but not all the way there — e.g. item behind a
    /// locked door or on an island of NavMesh). Both abort; the log differentiates them.
    /// </summary>
    protected virtual void OnPathUnreachable(Character worker, Vector3 attemptedDestination, NavMeshPathStatus status)
    {
        Debug.LogError($"<color=red>[MoveToTarget]</color> {worker?.CharacterName} action '{ActionName}' aborted: NavMesh path {status} to {attemptedDestination}. Base class default — subclass should override with a rollback.");
    }

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

        // Short-circuit: an unreachable abort was raised during a previous Execute tick.
        // Exit sequence has likely already torn us down, but guard in case the plan was
        // re-queued by the job layer before Exit ran.
        if (_pathAbortedUnreachable)
        {
            _isComplete = true;
            return;
        }

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

                // Phase-B safety net: upfront reachability probe. Runs the first time we
                // commit to a destination (or when a moving target pulls us out of range).
                // Skipped if we're already in a fail-diversification retry (failCount > 0)
                // because Unity will replan via SetDestination anyway and we don't want to
                // false-positive a detour vector as "unreachable".
                if (failCount == 0)
                {
                    NavMeshPathStatus probeStatus = NavMeshPathStatus.PathComplete;
                    bool probed = false;
                    try
                    {
                        NavMeshPath probe = new NavMeshPath();
                        // CalculatePath can throw if called before the NavMeshSurface is baked
                        // (hibernated interior coming back online, fresh map spawn, etc.).
                        if (NavMesh.CalculatePath(worker.transform.position, finalDest, NavMesh.AllAreas, probe))
                        {
                            probeStatus = probe.status;
                            probed = true;
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                        Debug.LogError($"[MoveToTarget] {worker?.CharacterName} action '{ActionName}': NavMesh.CalculatePath threw. Skipping upfront probe and letting SetDestination run its normal retry path.");
                    }

                    if (probed && (probeStatus == NavMeshPathStatus.PathInvalid || probeStatus == NavMeshPathStatus.PathPartial))
                    {
                        int targetIdForLog = targetCol != null ? targetCol.gameObject.GetInstanceID() : rawDest.GetHashCode();
                        Debug.LogError($"<color=red>[MoveToTarget]</color> {worker?.CharacterName} action '{ActionName}': NavMesh probe returned {probeStatus} from {worker.transform.position} to {finalDest} (target id={targetIdForLog}, raw dest={rawDest}). Aborting BEFORE SetDestination — subclass rollback runs now.");

                        _pathAbortedUnreachable = true;
                        _isMoving = false;
                        _isComplete = true;
                        movement.Stop();
                        movement.ResetPath();

                        OnPathUnreachable(worker, finalDest, probeStatus);
                        return;
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
        _pathAbortedUnreachable = false;
        _lastTargetPos = Vector3.positiveInfinity;
        worker.CharacterMovement?.Stop();
        worker.CharacterMovement?.ResetPath();
    }
}
