using UnityEngine;

[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(LineRenderer))]
public class BattleZoneOutline : MonoBehaviour
{
    [Tooltip("Height offset above ground for the line")]
    public float lineHeightOffset = 0.05f;

    [Tooltip("Layers considered as ground")]
    public LayerMask groundLayer;

    private MeshCollider meshCollider;
    private LineRenderer lineRenderer;

    private void Awake()
    {
        meshCollider = GetComponent<MeshCollider>();
        lineRenderer = GetComponent<LineRenderer>();

        // Setup LineRenderer appearance
        lineRenderer.loop = true;
        lineRenderer.widthMultiplier = 0.1f;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = Color.red;
        lineRenderer.endColor = Color.red;
        lineRenderer.useWorldSpace = true;
    }

    private void Start()
    {
        DrawLineOnGround();
    }

    public void DrawLineOnGround()
    {
        if (meshCollider.sharedMesh == null)
        {
            Debug.LogError("MeshCollider does not have a mesh assigned.");
            return;
        }

        Vector3[] vertices = meshCollider.sharedMesh.vertices;

        // Convert mesh vertices (local space) to world space
        Vector3[] worldVertices = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            worldVertices[i] = transform.TransformPoint(vertices[i]);
        }

        // Get perimeter vertices in order — simplest approach: find convex hull on XZ plane
        Vector3[] hullVertices = ConvexHullXZ(worldVertices);

        // Raycast down from each vertex to ground to find exact ground height + offset
        for (int i = 0; i < hullVertices.Length; i++)
        {
            Vector3 rayOrigin = hullVertices[i] + Vector3.up * 10f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 20f, groundLayer))
            {
                hullVertices[i] = hit.point + Vector3.up * lineHeightOffset;
            }
            else
            {
                // fallback: keep original vertex but raise it slightly
                hullVertices[i] += Vector3.up * lineHeightOffset;
            }
        }

        // Apply positions to LineRenderer and close the loop automatically (loop=true)
        lineRenderer.positionCount = hullVertices.Length;
        lineRenderer.SetPositions(hullVertices);
    }

    // Convex Hull algorithm on XZ plane for 3D points — Gift Wrapping (Jarvis March)
    private Vector3[] ConvexHullXZ(Vector3[] points)
    {
        if (points.Length < 3)
            return points;

        System.Collections.Generic.List<Vector3> hull = new System.Collections.Generic.List<Vector3>();

        // Find leftmost point (min X)
        int leftMost = 0;
        for (int i = 1; i < points.Length; i++)
            if (points[i].x < points[leftMost].x)
                leftMost = i;

        int current = leftMost;
        int next;

        do
        {
            hull.Add(points[current]);
            next = (current + 1) % points.Length;

            for (int i = 0; i < points.Length; i++)
            {
                if (i == current) continue;

                // Calculate cross product to determine relative orientation on XZ plane
                Vector3 a = points[current];
                Vector3 b = points[next];
                Vector3 c = points[i];

                float cross = ((b.x - a.x) * (c.z - a.z)) - ((b.z - a.z) * (c.x - a.x));
                if (cross < 0) // point c is more counterclockwise than b
                {
                    next = i;
                }
                else if (cross == 0)
                {
                    // If colinear, choose the farthest point
                    float distToC = (c - a).sqrMagnitude;
                    float distToB = (b - a).sqrMagnitude;
                    if (distToC > distToB)
                        next = i;
                }
            }

            current = next;

        } while (current != leftMost);

        return hull.ToArray();
    }
}
