using UnityEngine;
using MWI.WorldSystem;
using MWI.UI.Notifications;

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

        // --- Door Lock / Broken Check ---
        DoorLock doorLock = GetComponent<DoorLock>();
        DoorHealth doorHealth = GetComponent<DoorHealth>();

        bool isBroken = doorHealth != null && doorHealth.IsSpawned && doorHealth.IsBroken.Value;

        if (!isBroken && doorLock != null && doorLock.IsSpawned && doorLock.IsLocked.Value)
        {
            KeyInstance key = interactor.CharacterEquipment?.FindKeyForLock(doorLock.LockId, doorLock.RequiredTier);
            if (key != null)
            {
                doorLock.RequestUnlockServerRpc();
                return;
            }
            else
            {
                doorLock.RequestJiggleServerRpc();
                if (interactor.IsOwner && interactor.IsPlayer())
                {
                    UI_Toast.Show("the door is locked");
                }
                return;
            }
        }

        // Note: do NOT cache the interactor's `CurrentMapID` into `_exteriorMapId` —
        // that path used to chain interior IDs (`Base_Interior_A_Interior_B_…`) when the
        // player triggered the door from inside another interior, eventually overflowing
        // FixedString128Bytes and silently breaking transitions. The `ExteriorMapId`
        // property's lazy `ResolveExteriorMapId()` already finds the correct outer map
        // via `GetComponentInParent<MapController>()` (the door is parented under its
        // building's exterior MapController), with a bounds-overlap fallback.

        // Compute the deterministic interior map ID
        string interiorMapId = GetInteriorMapId();

        if (string.IsNullOrEmpty(BuildingId))
        {
            Debug.LogWarning("<color=orange>[BuildingInteriorDoor]</color> Cannot enter — building has no ID yet.");
            return;
        }

        // Try to get the actual interior position from the registry (works after first spawn)
        Vector3 targetPosition = ResolveTargetPosition();

        Debug.Log($"<color=cyan>[BuildingInteriorDoor]</color> Interact: interiorMapId='{interiorMapId}', targetPos={targetPosition}, buildingId='{BuildingId}', exteriorMapId='{ExteriorMapId}'");

        // Set the base class fields (TargetMapId for consistency; position is passed directly)
        TargetMapId = interiorMapId;

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
    /// Finds the outermost exterior <see cref="MapController"/> this door sits inside of.
    /// If the door's hierarchy parent is itself an interior MapController (shouldn't normally
    /// happen but is possible with custom prefab nesting), unwinds via <c>ExteriorMapId.Value</c>
    /// instead of returning the interior's MapId — otherwise <see cref="GetInteriorMapId"/>
    /// would chain (`Base_Interior_X_Interior_Y_…`) and overflow the networked
    /// FixedString128Bytes destination.
    /// </summary>
    private string ResolveExteriorMapId()
    {
        // Check parent MapController first
        var parentMap = GetComponentInParent<MapController>();
        if (parentMap != null)
        {
            // Unwind one level if the parent is itself an interior — use its replicated exterior MapId.
            if (parentMap.Type == MWI.WorldSystem.MapType.Interior)
            {
                string outer = parentMap.ExteriorMapId.Value.ToString();
                if (!string.IsNullOrEmpty(outer)) return outer;
            }
            return parentMap.MapId;
        }

        // Fallback: find the exterior MapController whose trigger contains this door's position
        var maps = Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
        foreach (var map in maps)
        {
            if (map.Type == MWI.WorldSystem.MapType.Interior) continue;
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
        // If the interior MapController exists, use its replicated entry position
        string interiorMapId = GetInteriorMapId();
        MapController interiorMap = MapController.GetByMapId(interiorMapId);
        if (interiorMap != null && interiorMap.InteriorEntryPosition.Value != Vector3.zero)
        {
            return interiorMap.InteriorEntryPosition.Value;
        }

        // First visit: position is unknown until server spawns the interior.
        // The fade-to-black hides this. The server will warp to the correct position
        // in RequestTransitionServerRpc after lazy-spawning the interior.
        return Vector3.zero;
    }
}
