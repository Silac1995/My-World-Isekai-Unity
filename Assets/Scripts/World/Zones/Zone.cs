using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(NavMeshModifier))]
public class Zone : MonoBehaviour
{
    public ZoneType zoneType;
    public string zoneName;

    [Tooltip("Optionnel : Objet enfant qui contient tous les murs/visuels pour calculer la taille de la zone.")]
    [SerializeField] private Transform _boundsTarget;

    protected BoxCollider _boxCollider;
    protected List<GameObject> _charactersInside = new List<GameObject>();

    protected virtual void Awake()
    {
        _boxCollider = GetComponent<BoxCollider>();
        _boxCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Character"))
        {
            GameObject charObj = other.gameObject;
            if (!_charactersInside.Contains(charObj))
            {
                _charactersInside.Add(charObj);
            }
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

    [ContextMenu("Fit Collider To Children")]
    public void FitColliderToChildren()
    {
        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol == null) return;

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(boxCol, "Fit Collider To Children");
#endif

        Transform target = _boundsTarget != null ? _boundsTarget : transform;
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true); // Include inactive
        
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"<color=orange>[Zone]</color> Aucun Renderer trouvé sur {target.name} pour calculer la taille.");
            return;
        }

        Bounds worldBounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
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
            // Center is easy: world coordinate of the bounds center to local coordinate
            boxCol.center = transform.InverseTransformPoint(worldBounds.center);
            
            // Size needs to account for the lossy scale of the object holding the collider
            Vector3 size = worldBounds.size;
            size.x /= transform.lossyScale.x == 0 ? 1 : Mathf.Abs(transform.lossyScale.x);
            size.y /= transform.lossyScale.y == 0 ? 1 : Mathf.Abs(transform.lossyScale.y);
            size.z /= transform.lossyScale.z == 0 ? 1 : Mathf.Abs(transform.lossyScale.z);
            
            boxCol.size = size;

            Debug.Log($"<color=cyan>[Zone]</color> BoxCollider de {gameObject.name} ajusté ({renderers.Length} meshes analysés). Centre: {boxCol.center}, Taille: {boxCol.size}");

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(boxCol);
            UnityEditor.EditorUtility.SetDirty(gameObject);
#endif
        }
        else
        {
            Debug.LogWarning($"<color=orange>[Zone]</color> Impossible de calculer les limites pour {gameObject.name}.");
        }
    }
}
