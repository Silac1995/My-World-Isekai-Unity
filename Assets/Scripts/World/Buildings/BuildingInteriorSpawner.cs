using Unity.Netcode;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Static helper that instantiates interior prefabs at their allocated spatial offsets
/// and configures the MapController and exit door.
/// </summary>
public static class BuildingInteriorSpawner
{
    /// <summary>
    /// Spawns an interior MapController prefab at the allocated offset position.
    /// Configures the interior's MapId, exit door, and spawns the NetworkObject.
    /// </summary>
    public static MapController SpawnInterior(BuildingInteriorRegistry.InteriorRecord record, GameObject interiorPrefab)
    {
        if (record == null || interiorPrefab == null)
        {
            Debug.LogError("[BuildingInteriorSpawner] Cannot spawn interior: null record or prefab.");
            return null;
        }

        Vector3 offset = WorldOffsetAllocator.Instance.GetInteriorOffsetVector(record.SlotIndex);
        GameObject instance = Object.Instantiate(interiorPrefab, offset, Quaternion.identity);
        instance.name = $"Interior_{record.BuildingId}";

        // Configure the MapController
        MapController mapController = instance.GetComponentInChildren<MapController>();
        if (mapController == null)
        {
            Debug.LogError($"[BuildingInteriorSpawner] Interior prefab for '{record.PrefabId}' is missing a MapController component!");
            Object.Destroy(instance);
            return null;
        }

        mapController.MapId = record.InteriorMapId;
        mapController.IsInteriorOffset = true;

        // Configure the exit door to point back to the exterior map
        MapTransitionDoor[] doors = instance.GetComponentsInChildren<MapTransitionDoor>(true);
        foreach (var door in doors)
        {
            // Skip BuildingInteriorDoor (entrance doors); only configure plain exit doors
            if (door is BuildingInteriorDoor) continue;

            door.TargetMapId = record.ExteriorMapId;
            door.TargetPositionOffset = record.ExteriorDoorPosition;
        }

        // Spawn on network
        NetworkObject netObj = instance.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            netObj = instance.GetComponentInChildren<NetworkObject>();
        }

        if (netObj != null)
        {
            netObj.Spawn(true);
        }
        else
        {
            Debug.LogWarning($"[BuildingInteriorSpawner] Interior '{record.InteriorMapId}' has no NetworkObject. It will not be networked.");
        }

        Debug.Log($"<color=green>[BuildingInteriorSpawner]</color> Spawned interior '{record.InteriorMapId}' at {offset}.");
        return mapController;
    }
}
