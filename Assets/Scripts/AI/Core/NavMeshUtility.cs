using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Centralizes distance and physics bound checks between a Character and various targets.
    /// This prevents duplicating the identical 'ClosestPoint' and 'Intersects' math across all Goap Actions and Movement scripts.
    /// </summary>
    public static class NavMeshUtility
    {
        /// <summary>
        /// Highly resilient check to see if a Character is close enough to interact with a target zone.
        /// Checks bounds intersection first (ideal case), then falls back to calculating the ClosestPoint on the bounding box (for large zones or NavMesh Obstacles).
        /// </summary>
        public static bool IsCharacterAtTargetZone(Character worker, Collider targetZone, float distanceTolerance = 1.0f)
        {
            if (worker == null) return false;
            
            var workerCol = worker.Collider;
            if (targetZone == null) return false;

            // 1. Ideal Case: The colliders actually touch/intersect.
            if (workerCol != null && targetZone.bounds.Intersects(workerCol.bounds))
            {
                return true;
            }

            // 2. Center-Point Fallback: If bounds aren't touching, verify if the worker's center is literally inside the zone.
            if (targetZone.bounds.Contains(worker.transform.position))
            {
                return true;
            }

            // 3. Margin Fallback: Calculate direct distance horizontally between worker and the nearest edge of the bounding box
            Vector3 workerPosFlat = worker.transform.position;
            workerPosFlat.y = 0;

            Vector3 closestEdgeFlat = targetZone.bounds.ClosestPoint(worker.transform.position);
            closestEdgeFlat.y = 0;

            if (Vector3.Distance(workerPosFlat, closestEdgeFlat) <= distanceTolerance)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if the Character's NavMeshAgent has completely halted and reached its internally calculated destination.
        /// </summary>
        public static bool HasAgentReachedDestination(CharacterMovement movement, float extraTolerance = 0.5f)
        {
            if (movement == null) return false;
            if (movement.PathPending) return false; // Still calculating

            // If we have no path and remaining distance is 0, we're likely stopped AT the target.
            // If we're extremely close to StoppingDistance + ExtraTolerance, the agent is considered arrived.
            return !movement.HasPath || movement.RemainingDistance <= (movement.StoppingDistance + extraTolerance);
        }

        /// <summary>
        /// Calculates safety bounds—returns true if the Agent has fundamentally failed to calculate a path or reached a dead end.
        /// Ensure to pass a time-delay (e.g., 0.2f seconds since request) to allow Unity's async NavMesh cycle to populate PathStatus.
        /// </summary>
        public static bool HasPathFailed(CharacterMovement movement, float timeSinceLastRequest, float minThresholdSeconds = 0.2f)
        {
            if (movement == null) return true;
            
            bool hasPassedThreshold = (UnityEngine.Time.unscaledTime - timeSinceLastRequest) > minThresholdSeconds;
            if (!hasPassedThreshold) return false;

            bool pathInvalid = movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid;
            bool effectivelyLost = !movement.HasPath && !movement.PathPending;

            return pathInvalid || effectivelyLost;
        }

        /// <summary>
        /// Retrieves the most appropriate target point for an Agent to walk to when heading to an Interactable or Zone.
        /// </summary>
        public static Vector3 GetOptimalDestination(Character worker, Collider targetZone)
        {
            if (targetZone == null || worker == null) return Vector3.zero;

            // Approaching the edge of the zone rather than its physical center guarantees we don't try to path inside a solid NavMeshObstacle
            return targetZone.bounds.ClosestPoint(worker.transform.position);
        }
    }
}
