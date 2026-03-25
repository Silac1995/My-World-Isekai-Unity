using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using MWI.WorldSystem;

[RequireComponent(typeof(Character))]
public class CharacterMapTracker : NetworkBehaviour
{
    private Character _character;

    [Tooltip("The ID of the Map/Region this Character is currently in.")]
    public NetworkVariable<FixedString128Bytes> CurrentMapID = new NetworkVariable<FixedString128Bytes>(
        "",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Tooltip("The ID of the Map this Character considers Home.")]
    public NetworkVariable<FixedString128Bytes> HomeMapId = new NetworkVariable<FixedString128Bytes>(
        "",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Tooltip("The specific world position the Character considers Home.")]
    public NetworkVariable<Vector3> HomePosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        _character = GetComponent<Character>();
    }

    /// <summary>
    /// Called by the Client's CharacterMapTransitionAction AFTER it predicts the Warp locally.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    public void RequestTransitionServerRpc(FixedString128Bytes targetMapId, Vector3 targetPosition)
    {
        string previousMapId = CurrentMapID.Value.ToString();
        string targetMapIdStr = targetMapId.ToString();

        // Lazy-spawn interior if this transition targets a building interior
        Vector3 resolvedPosition = ResolveInteriorPosition(targetMapIdStr, targetPosition);

        // 1. Authoritative Warp on Server
        if (TryGetComponent(out CharacterMovement movement))
        {
            movement.Warp(resolvedPosition);
        }

        // 2. Set new Map state
        SetCurrentMap(targetMapIdStr);

        // 3. Notify source and destination MapControllers for hibernation handoff
        MapController.NotifyPlayerTransition(OwnerClientId, previousMapId, targetMapIdStr);
    }

    /// <summary>
    /// Server-authoritative map assignment. Can be called directly for NPCs.
    /// </summary>
    public void SetCurrentMap(string mapId)
    {
        if (!IsServer) return;

        string previousMapId = CurrentMapID.Value.ToString();
        CurrentMapID.Value = mapId;

        // --- RULE 20: Character Decoupling ---
        // TODO: Integrate with ICharacterData to save the LastWorldID / LastCoordinates
        // to the decoupled JSON/DAT file so if the player disconnects right now, they spawn here later.
        if (_character.IsPlayer())
        {
            Debug.Log($"<color=cyan>[MapTracker]</color> Player {_character.name} transitioned to Map '{mapId}'. (Coordinates Decoupling Placeholder)");
        }
    }

    /// <summary>
    /// Server-authoritative NPC transition with MapController notifications.
    /// </summary>
    public void SetCurrentMapWithNotify(string mapId, ulong clientId)
    {
        if (!IsServer) return;

        string previousMapId = CurrentMapID.Value.ToString();
        SetCurrentMap(mapId);
        MapController.NotifyPlayerTransition(clientId, previousMapId, mapId);
    }

    /// <summary>
    /// If the target map is a building interior that hasn't been spawned yet,
    /// ensures it exists and returns the corrected spawn position.
    /// </summary>
    private Vector3 ResolveInteriorPosition(string targetMapId, Vector3 clientPosition)
    {
        if (BuildingInteriorRegistry.Instance == null) return clientPosition;

        // Check if any registered interior matches this target map
        // Interior map IDs follow the pattern: "{ExteriorMapId}_Interior_{BuildingId}"
        // We need to find the record by InteriorMapId, not BuildingId
        foreach (var buildingId in GetBuildingIdFromInteriorMapId(targetMapId))
        {
            if (BuildingInteriorRegistry.Instance.TryGetInterior(buildingId, out var record))
            {
                if (record.InteriorMapId == targetMapId)
                {
                    // Interior already exists — use the correct position
                    Vector3 interiorOrigin = WorldOffsetAllocator.Instance.GetInteriorOffsetVector(record.SlotIndex);
                    // If client sent Vector3.zero (first-visit placeholder), correct it
                    if (clientPosition == Vector3.zero)
                    {
                        return interiorOrigin;
                    }
                    return clientPosition;
                }
            }
        }

        // Check if this looks like an interior map ID that needs lazy-spawning
        if (targetMapId.Contains("_Interior_"))
        {
            string buildingId = ExtractBuildingId(targetMapId);
            string exteriorMapId = ExtractExteriorMapId(targetMapId);

            if (string.IsNullOrEmpty(buildingId))
            {
                Debug.LogError($"[CharacterMapTracker] Could not extract BuildingId from interior map ID '{targetMapId}'.");
                return clientPosition;
            }

            // Find the door to get the prefabId and exterior door position
            var door = FindDoorForBuilding(buildingId);
            if (door == null)
            {
                Debug.LogError($"[CharacterMapTracker] Could not find BuildingInteriorDoor for building '{buildingId}' to lazy-spawn interior.");
                return clientPosition;
            }

            // Use the door's exterior map ID if we couldn't extract one (e.g. fallback "World" prefix)
            if (string.IsNullOrEmpty(exteriorMapId))
            {
                exteriorMapId = door.ExteriorMapId;
            }

            var record = BuildingInteriorRegistry.Instance.RegisterInterior(
                buildingId, door.PrefabId, exteriorMapId, door.ReturnWorldPosition
            );

            if (record == null)
            {
                Debug.LogError($"[CharacterMapTracker] Failed to register interior for building '{buildingId}'.");
                return clientPosition;
            }

            WorldSettingsData settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
            if (settings == null)
            {
                Debug.LogError("[CharacterMapTracker] WorldSettingsData not found at 'Data/World/WorldSettingsData'.");
                return clientPosition;
            }

            GameObject interiorPrefab = settings.GetInteriorPrefab(record.PrefabId);
            if (interiorPrefab == null)
            {
                Debug.LogError($"[CharacterMapTracker] No InteriorPrefab found for PrefabId '{record.PrefabId}' in WorldSettingsData.");
                return clientPosition;
            }

            BuildingInteriorSpawner.SpawnInterior(record, interiorPrefab);
            Vector3 interiorOrigin = WorldOffsetAllocator.Instance.GetInteriorOffsetVector(record.SlotIndex);
            return interiorOrigin;
        }

        return clientPosition;
    }

    private static string ExtractBuildingId(string interiorMapId)
    {
        int idx = interiorMapId.LastIndexOf("_Interior_");
        if (idx < 0) return null;
        return interiorMapId.Substring(idx + "_Interior_".Length);
    }

    private static string ExtractExteriorMapId(string interiorMapId)
    {
        int idx = interiorMapId.LastIndexOf("_Interior_");
        if (idx < 0) return null;
        return interiorMapId.Substring(0, idx);
    }

    private static System.Collections.Generic.IEnumerable<string> GetBuildingIdFromInteriorMapId(string interiorMapId)
    {
        string buildingId = ExtractBuildingId(interiorMapId);
        if (!string.IsNullOrEmpty(buildingId))
        {
            yield return buildingId;
        }
    }

    private static BuildingInteriorDoor FindDoorForBuilding(string buildingId)
    {
        // Find the exterior door that references this building
        var doors = Object.FindObjectsByType<BuildingInteriorDoor>(FindObjectsSortMode.None);
        foreach (var door in doors)
        {
            if (door.BuildingId == buildingId)
            {
                return door;
            }
        }
        return null;
    }
}
