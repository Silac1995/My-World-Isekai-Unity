using UnityEngine;

/// <summary>
/// Provides proactive, raycast-based obstacle avoidance for manual physical movement (WASD).
/// Steers the character smoothly along walls to prevent getting caught on collision edges/corners.
/// </summary>
public class CharacterObstacleAvoidance : MonoBehaviour
{
    [Header("Avoidance Settings")]
    [SerializeField] private float _rayDistance = 1.0f;
    [SerializeField] private float _sideRayAngle = 45f;
    [SerializeField] private LayerMask _obstacleMask;

    [Header("Steering Tuning")]
    [SerializeField, Range(0f, 1f)] private float _avoidanceStrength = 0.5f;

    /// <summary>
    /// Computes a modified direction vector that steers away from upcoming obstacles.
    /// </summary>
    /// <param name="desiredDirection">The original intended movement direction.</param>
    /// <returns>The adjusted direction vector.</returns>
    public Vector3 GetAvoidanceDirection(Vector3 desiredDirection)
    {
        if (desiredDirection.sqrMagnitude < 0.01f)
            return desiredDirection;

        Vector3 bottomOffset = transform.position;
        Collider col = GetComponentInChildren<Collider>();
        if (col != null) bottomOffset = new Vector3(transform.position.x, col.bounds.min.y, transform.position.z);

        Vector3 origin = bottomOffset + Vector3.up * 0.5f; // Cast from roughly chest height relative to bottom
        Vector3 forward = desiredDirection.normalized;
        Vector3 rightOffset = Quaternion.Euler(0, _sideRayAngle, 0) * forward;
        Vector3 leftOffset = Quaternion.Euler(0, -_sideRayAngle, 0) * forward;

        bool hitForward = Physics.Raycast(origin, forward, out RaycastHit forwardHit, _rayDistance, _obstacleMask, QueryTriggerInteraction.Ignore);
        bool hitRight = Physics.Raycast(origin, rightOffset, out RaycastHit rightHit, _rayDistance, _obstacleMask, QueryTriggerInteraction.Ignore);
        bool hitLeft = Physics.Raycast(origin, leftOffset, out RaycastHit leftHit, _rayDistance, _obstacleMask, QueryTriggerInteraction.Ignore);

        Vector3 avoidanceForce = Vector3.zero;

        // Simple Steer Logic:
        // If forward hits, deflect strongly.
        if (hitForward)
        {
            // Slide along the wall normal
            avoidanceForce += forwardHit.normal * (1.0f - (forwardHit.distance / _rayDistance));
        }

        // Left/Right feelers push the character away from brushing obstacles
        if (hitRight)
        {
            avoidanceForce += rightHit.normal * (1.0f - (rightHit.distance / _rayDistance));
        }

        if (hitLeft)
        {
            avoidanceForce += leftHit.normal * (1.0f - (leftHit.distance / _rayDistance));
        }

        if (avoidanceForce.sqrMagnitude > 0f)
        {
            // Blend the desired direction with the avoidance normal
            Vector3 newDir = (desiredDirection + avoidanceForce * _avoidanceStrength).normalized;
            
            // If the new direction is directly pushing back (almost 180 degrees), project it completely along the wall
            if (Vector3.Dot(newDir, desiredDirection) < -0.1f && hitForward)
            {
                newDir = Vector3.ProjectOnPlane(desiredDirection, forwardHit.normal).normalized;
            }

            // Preserve original magnitude (speed)
            return newDir * desiredDirection.magnitude;
        }

        // Path is clear
        return desiredDirection;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        Vector3 bottomOffset = transform.position;
        Collider col = GetComponentInChildren<Collider>();
        if (col != null) bottomOffset = new Vector3(transform.position.x, col.bounds.min.y, transform.position.z);

        Vector3 origin = bottomOffset + Vector3.up * 0.5f;
        Vector3 forward = transform.forward; // fallback for visualization, but runtime uses desiredDirection

        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin, origin + forward * _rayDistance);
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin, origin + Quaternion.Euler(0, _sideRayAngle, 0) * forward * _rayDistance);
        Gizmos.DrawLine(origin, origin + Quaternion.Euler(0, -_sideRayAngle, 0) * forward * _rayDistance);
    }
}
