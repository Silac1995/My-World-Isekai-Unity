using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Netcode;

[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(NavMeshModifierVolume))]
public class Zone : NetworkBehaviour
{
    public ZoneType zoneType;
    public string zoneName;

    [Tooltip("Optionnel : Objet enfant qui contient tous les murs/visuels pour calculer la taille de la zone.")]
    [SerializeField] private Transform _boundsTarget;

    [Tooltip("Multiple bounds targets for non-rectangular zones (e.g. L-shaped rooms with separate floor planes). Used by Fit Collider if populated, otherwise falls back to _boundsTarget.")]
    [SerializeField] private List<Transform> _boundsTargets = new List<Transform>();

    protected BoxCollider _boxCollider;
    protected HashSet<GameObject> _charactersInside = new HashSet<GameObject>();

    /// <summary>World-space bounds of this zone's collider. Returns empty Bounds if no collider.</summary>
    public Bounds Bounds => _boxCollider != null ? _boxCollider.bounds : new Bounds();

    protected virtual void Awake()
    {
        _boxCollider = GetComponent<BoxCollider>();
        _boxCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Character"))
        {
            _charactersInside.Add(other.gameObject);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Character"))
        {
            _charactersInside.Remove(other.gameObject);
        }
    }

    public Vector3 GetRandomPointInZone()
    {
        if (_boxCollider == null) return transform.position;

        Bounds bounds = _boxCollider.bounds;
        
        // Try multiple times to find a valid NavMesh point
        for (int i = 0; i < 5; i++)
        {
            float randomX = Random.Range(bounds.min.x, bounds.max.x);
            float randomZ = Random.Range(bounds.min.z, bounds.max.z);
            Vector3 randomPoint = new Vector3(randomX, bounds.center.y, randomZ);

            if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        return bounds.center;
    }

    /// <summary>
    /// Checks whether a given position lies inside the collider of a zone of a specific type.
    /// </summary>
    public static bool IsPositionInZoneType(Vector3 position, ZoneType targetZoneType, float checkRadius = 0.1f)
    {
        Collider[] colliders = Physics.OverlapSphere(position, checkRadius, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var col in colliders)
        {
            var zone = col.GetComponent<Zone>() ?? col.GetComponentInParent<Zone>();
            if (zone != null && zone.zoneType == targetZoneType)
            {
                return true;
            }
        }
        return false;
    }

    [ContextMenu("Fit Collider To Children")]
    public void FitColliderToChildren()
    {
        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol == null) return;

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(boxCol, "Fit Collider To Children");
#endif

        // Gather renderers from all specified targets
        List<Renderer> allRenderers = new List<Renderer>();

        if (_boundsTargets != null && _boundsTargets.Count > 0)
        {
            foreach (Transform t in _boundsTargets)
            {
                if (t == null) continue;
                allRenderers.AddRange(t.GetComponentsInChildren<Renderer>(true));
            }
        }
        else
        {
            // Fallback: single target or self
            Transform target = _boundsTarget != null ? _boundsTarget : transform;
            allRenderers.AddRange(target.GetComponentsInChildren<Renderer>(true));
        }

        if (allRenderers.Count == 0)
        {
            Debug.LogWarning($"<color=orange>[Zone]</color> No Renderers found on bounds targets for {gameObject.name}.");
            return;
        }

        Bounds worldBounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        foreach (Renderer renderer in allRenderers)
        {
            if (renderer is ParticleSystemRenderer) continue;

            if (!hasBounds)
            {
                worldBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                worldBounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds)
        {
            boxCol.center = transform.InverseTransformPoint(worldBounds.center);

            Vector3 size = worldBounds.size;
            size.x /= transform.lossyScale.x == 0 ? 1 : Mathf.Abs(transform.lossyScale.x);
            size.y /= transform.lossyScale.y == 0 ? 1 : Mathf.Abs(transform.lossyScale.y);
            size.z /= transform.lossyScale.z == 0 ? 1 : Mathf.Abs(transform.lossyScale.z);

            boxCol.size = size;

            Debug.Log($"<color=cyan>[Zone]</color> BoxCollider on {gameObject.name} fitted to {allRenderers.Count} renderers. Center: {boxCol.center}, Size: {boxCol.size}");

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(boxCol);
            UnityEditor.EditorUtility.SetDirty(gameObject);
#endif
        }
        else
        {
            Debug.LogWarning($"<color=orange>[Zone]</color> Could not compute bounds for {gameObject.name}.");
        }
    }
}
