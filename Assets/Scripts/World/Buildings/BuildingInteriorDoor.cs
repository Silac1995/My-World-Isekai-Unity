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
    [SerializeField] private string _buildingId;
    [SerializeField] private string _prefabId;

    [Tooltip("The MapId of the exterior map this door sits on.")]
    [SerializeField] private string _exteriorMapId;

    [Tooltip("Local offset within the interior prefab for the entry point.")]
    [SerializeField] private Vector3 _interiorEntryLocalOffset = new Vector3(0f, 0f, 2f);

    [Tooltip("Offset from this door's position where the character spawns when exiting back to exterior.")]
    [SerializeField] private Vector3 _returnOffset = new Vector3(0f, 0f, -2f);

    public string BuildingId => _buildingId;
    public string PrefabId => _prefabId;
    public string ExteriorMapId => _exteriorMapId;
    public Vector3 ReturnWorldPosition => transform.position + _returnOffset;

    public override void Interact(Character interactor)
    {
        if (interactor == null || !interactor.IsAlive()) return;

        if (interactor.CharacterActions.CurrentAction is CharacterMapTransitionAction)
        {
            return;
        }

        // Compute the deterministic interior map ID
        string interiorMapId = GetInteriorMapId();

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
        return $"{_exteriorMapId}_Interior_{_buildingId}";
    }

    private Vector3 ResolveTargetPosition()
    {
        // If the registry already knows this interior (repeat visits), use the real position
        if (BuildingInteriorRegistry.Instance != null &&
            BuildingInteriorRegistry.Instance.TryGetInterior(_buildingId, out var record))
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
