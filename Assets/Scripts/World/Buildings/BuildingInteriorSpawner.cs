using Unity.Netcode;
using Unity.AI.Navigation;
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
        mapController.SetMapType(MapType.Interior);

        // Store exit info on MapController as NetworkVariables — these replicate to all clients,
        // unlike MonoBehaviour fields on the exit door which only exist on the server instance.
        // Exit doors read these via GetComponentInParent<MapController>() at interaction time.
        // NOTE: NetworkVariables can only be written AFTER Spawn(), so we defer this below.

        // Configure the exit door to point back to the exterior map (server-side only, for host)
        MapTransitionDoor[] doors = instance.GetComponentsInChildren<MapTransitionDoor>(true);
        int exitDoorCount = 0;
        Vector3 entryPosition = offset; // fallback: MapController root position
        foreach (var door in doors)
        {
            // Skip BuildingInteriorDoor (entrance doors); only configure plain exit doors
            if (door is BuildingInteriorDoor)
            {
                Debug.Log($"<color=yellow>[BuildingInteriorSpawner]</color> Skipping BuildingInteriorDoor '{door.name}' on '{door.gameObject.name}'.");
                continue;
            }

            // Capture the spawn point position BEFORE clearing it — this is where
            // players should appear when entering the interior (near the exit door).
            if (door.TargetSpawnPoint != null)
            {
                entryPosition = door.TargetSpawnPoint.position;
            }
            else
            {
                // No spawn point set — use the exit door's own position as entry point
                entryPosition = door.transform.position;
            }

            // Clear the prefab-assigned spawn point — it points inside the interior, not outside.
            // We use TargetPositionOffset instead to compute the exterior return position.
            door.TargetSpawnPoint = null;

            // TargetPositionOffset is added to the door's own position in MapTransitionDoor.Interact(),
            // so we compute the delta from the exit door's world position to the exterior return position.
            Vector3 delta = record.ExteriorDoorPosition - door.transform.position;
            door.TargetMapId = record.ExteriorMapId;
            door.TargetPositionOffset = delta;

            // Set LockId on interior exit door to match the exterior door.
            // Both derive from BuildingId so they auto-pair in the static registry.
            DoorLock exitLock = door.GetComponent<DoorLock>();
            if (exitLock != null)
            {
                exitLock.SetLockId(record.BuildingId);
            }

            exitDoorCount++;
            Debug.Log($"<color=green>[BuildingInteriorSpawner]</color> Exit door '{door.name}' configured: TargetMapId='{record.ExteriorMapId}', doorPos={door.transform.position}, exteriorReturnPos={record.ExteriorDoorPosition}, offset={delta}, entryPos={entryPosition}");
        }

        if (exitDoorCount == 0)
        {
            Debug.LogWarning($"<color=orange>[BuildingInteriorSpawner]</color> No exit doors found in interior prefab for '{record.PrefabId}'! Total MapTransitionDoors found: {doors.Length}");
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

            // Reparent under the exterior MapController for a clean scene hierarchy.
            // Exterior MapController is a NetworkObject so NGO TrySetParent is valid and
            // the parent change replicates to clients.
            MapController exteriorMap = MapController.GetByMapId(record.ExteriorMapId);
            if (exteriorMap != null)
            {
                bool parented = netObj.TrySetParent(exteriorMap.transform, worldPositionStays: true);
                if (!parented)
                {
                    Debug.LogWarning($"<color=yellow>[BuildingInteriorSpawner]</color> NGO TrySetParent failed: interior '{record.InteriorMapId}' stays at scene root but is still networked.");
                }
            }
        }
        else
        {
            Debug.LogWarning($"[BuildingInteriorSpawner] Interior '{record.InteriorMapId}' has no NetworkObject. It will not be networked.");
        }

        // Set NetworkVariables AFTER Spawn() so they replicate to remote clients.
        // This is the authoritative exit info that all clients can read.
        mapController.ExteriorMapId.Value = record.ExteriorMapId;
        mapController.ExteriorReturnPosition.Value = record.ExteriorDoorPosition;
        mapController.InteriorEntryPosition.Value = entryPosition;

        // Rebake the interior NavMeshSurface at its actual world position.
        // The prefab's baked NavMesh is relative to (0,0,0) but the interior
        // spawns at a dynamic offset (Y=5000+). Rebaking ensures correct NavMesh.
        NavMeshSurface interiorSurface = instance.GetComponentInChildren<NavMeshSurface>();
        if (interiorSurface != null)
        {
            interiorSurface.BuildNavMesh();
            Debug.Log($"<color=cyan>[BuildingInteriorSpawner]</color> NavMesh rebaked for interior '{record.InteriorMapId}'.");
        }

        // Defensive re-application of persisted door state for the interior's doors.
        // Their `OnNetworkSpawn` already pulled from `DoorStateRegistry` (single source
        // of truth for door state), but we re-apply here in case the door spawned before
        // the registry's `RestoreState` ran (timing race on world load).
        if (DoorStateRegistry.Instance != null)
        {
            DoorLock[] doorLocks = instance.GetComponentsInChildren<DoorLock>(true);
            foreach (var dl in doorLocks)
            {
                if (string.IsNullOrEmpty(dl.LockId)) continue;
                var doorRecord = DoorStateRegistry.Instance.TryGet(dl.LockId);
                if (doorRecord != null) dl.IsLocked.Value = doorRecord.IsLocked;
            }

            DoorHealth[] doorHealths = instance.GetComponentsInChildren<DoorHealth>(true);
            foreach (var dh in doorHealths)
            {
                if (string.IsNullOrEmpty(dh.LockId)) continue;
                var doorRecord = DoorStateRegistry.Instance.TryGet(dh.LockId);
                if (doorRecord != null && doorRecord.CurrentHealth >= 0f)
                {
                    dh.CurrentHealth.Value = doorRecord.CurrentHealth;
                    dh.IsBroken.Value = doorRecord.CurrentHealth <= 0f;
                }
            }
        }

        Debug.Log($"<color=green>[BuildingInteriorSpawner]</color> Spawned interior '{record.InteriorMapId}' at {offset}. ExteriorMapId='{record.ExteriorMapId}', ExteriorReturnPos={record.ExteriorDoorPosition}, EntryPos={entryPosition}");
        return mapController;
    }
}
