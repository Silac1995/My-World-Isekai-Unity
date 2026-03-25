using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Exterior door placed on a Building that teleports the interactor into
/// the building's interior MapController. The interior is lazy-spawned
/// on first entry (server-side) via CharacterMapTracker.
///
/// Inheritance chain: BuildingInteriorDoor : MapTransitionDoor : InteractableObject : MonoBehaviour
/// </summary>
public class BuildingInteriorDoor : MapTransitionDoor
{
    [Header("Building Interior")]
    [Tooltip("The MapId of the exterior map this door sits on.")]
    [SerializeField] private string _exteriorMapId;

    private Building _parentBuilding;

    [Tooltip("Local offset within the interior prefab for the entry point.")]
    [SerializeField] private Vector3 _interiorEntryLocalOffset = new Vector3(0f, 0f, 2f);

    [Tooltip("Offset from this door's position where the character spawns when exiting back to exterior.")]
    [SerializeField] private Vector3 _returnOffset = new Vector3(0f, 0f, -2f);

    public string BuildingId => _checkBuilding() ? _parentBuilding.BuildingId : "";
    public string PrefabId => _checkBuilding() ? _parentBuilding.PrefabId : "";
    public Vector3 ReturnWorldPosition => transform.position + _returnOffset;

    /// <summary>
    /// Returns the exterior map ID. Uses the serialized value if set,
    /// otherwise auto-detects from the nearest MapController at runtime.
    /// </summary>
    public string ExteriorMapId
    {
        get
        {
            if (!string.IsNullOrEmpty(_exteriorMapId)) return _exteriorMapId;
            _exteriorMapId = ResolveExteriorMapId();
            return _exteriorMapId;
        }
    }

    private bool _checkBuilding()
    {
        if (_parentBuilding == null) _parentBuilding = GetComponentInParent<Building>();
        return _parentBuilding != null;
    }

    public override void Interact(Character interactor)
    {
        if (interactor == null || !interactor.IsAlive()) return;

        if (interactor.CharacterActions.CurrentAction is CharacterMapTransitionAction)
        {
            return;
        }

        // Auto-detect exterior map from interactor if not already set
        if (string.IsNullOrEmpty(_exteriorMapId) && interactor.TryGetComponent(out CharacterMapTracker tracker))
        {
            string currentMap = tracker.CurrentMapID.Value.ToString();
            if (!string.IsNullOrEmpty(currentMap))
            {
                _exteriorMapId = currentMap;
            }
        }

        // Compute the deterministic interior map ID
        string interiorMapId = GetInteriorMapId();

        if (string.IsNullOrEmpty(BuildingId))
        {
            Debug.LogWarning("<color=orange>[BuildingInteriorDoor]</color> Cannot enter — building has no ID yet.");
            return;
        }

        // Try to get the actual interior position from the registry (works after first spawn)
        Vector3 targetPosition = ResolveTargetPosition();

        // Set the base class fields so the action is created correctly
        TargetMapId = interiorMapId;
        TargetSpawnPoint = null;

        var transitionAction = new CharacterMapTransitionAction(
            interactor, this, interiorMapId, targetPosition, FadeDuration
        );
        interactor.CharacterActions.ExecuteAction(transitionAction);
    }

    /// <summary>
    /// Deterministic interior map ID that both client and server can compute independently.
    /// </summary>
    public string GetInteriorMapId()
    {
        return $"{ExteriorMapId}_Interior_{BuildingId}";
    }

    /// <summary>
    /// Finds the MapController this door sits inside of via physics overlap.
    /// </summary>
    private string ResolveExteriorMapId()
    {
        // Check parent MapController first
        var parentMap = GetComponentInParent<MapController>();
        if (parentMap != null) return parentMap.MapId;

        // Fallback: find the MapController whose trigger contains this door's position
        var maps = Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
        foreach (var map in maps)
        {
            if (map.IsInteriorOffset) continue;
            var col = map.GetComponent<Collider>();
            if (col != null && col.bounds.Contains(transform.position))
            {
                return map.MapId;
            }
        }

        return "World";
    }

    private Vector3 ResolveTargetPosition()
    {
        // If the registry already knows this interior (repeat visits), use the real position
        if (BuildingInteriorRegistry.Instance != null &&
            BuildingInteriorRegistry.Instance.TryGetInterior(BuildingId, out var record))
        {
            Vector3 interiorOrigin = WorldOffsetAllocator.Instance.GetInteriorOffsetVector(record.SlotIndex);
            return interiorOrigin + _interiorEntryLocalOffset;
        }

        // First visit: position is unknown until server spawns the interior.
        // The fade-to-black hides this. The server will warp to the correct position
        // in RequestTransitionServerRpc after lazy-spawning the interior.
        return Vector3.zero;
    }
}
