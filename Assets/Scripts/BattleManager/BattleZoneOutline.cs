using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(LineRenderer))]
public class BattleZoneOutline : MonoBehaviour
{
    [Tooltip("Height offset above ground for the line")]
    public float lineHeightOffset = 0.05f;

    [Tooltip("Layers considered as ground")]
    public LayerMask groundLayer;

    [Tooltip("Distance between interpolated points along each edge (smaller = smoother on slopes)")]
    public float segmentLength = 0.5f;

    private MeshCollider meshCollider;
    private LineRenderer lineRenderer;

    private void Awake()
    {
        meshCollider = GetComponent<MeshCollider>();
        lineRenderer = GetComponent<LineRenderer>();

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

        Vector3[] worldVertices = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            worldVertices[i] = transform.TransformPoint(vertices[i]);
        }

        Vector3[] hullVertices = ConvexHullXZ(worldVertices);

        // Subdivide each edge so the line follows slopes and stairs
        List<Vector3> subdividedPoints = SubdivideEdges(hullVertices, segmentLength);

        // Raycast each point down to ground
        for (int i = 0; i < subdividedPoints.Count; i++)
        {
            Vector3 rayOrigin = subdividedPoints[i] + Vector3.up * 10f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 20f, groundLayer))
            {
                subdividedPoints[i] = hit.point + Vector3.up * lineHeightOffset;
            }
            else
            {
                subdividedPoints[i] += Vector3.up * lineHeightOffset;
            }
        }

        lineRenderer.positionCount = subdividedPoints.Count;
        lineRenderer.SetPositions(subdividedPoints.ToArray());
    }

    private List<Vector3> SubdivideEdges(Vector3[] hull, float maxSegmentLength)
    {
        List<Vector3> result = new List<Vector3>();

        for (int i = 0; i < hull.Length; i++)
        {
            Vector3 start = hull[i];
            Vector3 end = hull[(i + 1) % hull.Length];

            float edgeLength = Vector3.Distance(start, end);
            int divisions = Mathf.Max(1, Mathf.CeilToInt(edgeLength / maxSegmentLength));

            for (int d = 0; d < divisions; d++)
            {
                float t = (float)d / divisions;
                result.Add(Vector3.Lerp(start, end, t));
            }
        }

        return result;
    }

    private Vector3[] ConvexHullXZ(Vector3[] points)
    {
        if (points.Length < 3)
            return points;

        List<Vector3> hull = new List<Vector3>();

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

                Vector3 a = points[current];
                Vector3 b = points[next];
                Vector3 c = points[i];

                float cross = ((b.x - a.x) * (c.z - a.z)) - ((b.z - a.z) * (c.x - a.x));
                if (cross < 0)
                {
                    next = i;
                }
                else if (cross == 0)
                {
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