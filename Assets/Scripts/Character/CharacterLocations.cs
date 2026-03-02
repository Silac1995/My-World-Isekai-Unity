using System.Collections.Generic;
using UnityEngine;

public class CharacterLocations : MonoBehaviour
{
    [Header("Assigned Zones")]
    public Zone homeZone;
    public Zone workZone;

    [Header("Legacy Direct Assignments (To be deprecated)")]
    public ResidentialBuilding homeBuilding;
    public CommercialBuilding workBuilding;
    public Job currentJob;

    [Header("Properties & Residencies")]
    public List<Building> OwnedBuildings = new List<Building>();
    public List<Room> ResidentRooms = new List<Room>();

    /// <summary>
    /// Retrieves the specific Zone instance assigned to this character based on the requested ZoneType.
    /// </summary>
    /// <param name="type">The type of zone requested.</param>
    /// <returns>The Zone instance, or null if not assigned/handled.</returns>
    public Zone GetZoneByType(ZoneType type)
    {
        switch (type)
            {
            case ZoneType.Home:
                return homeZone;
            case ZoneType.Job:
                return workZone;
            default:
                Debug.LogWarning($"[CharacterLocations] ZoneType {type} is not yet handled or assigned for {gameObject.name}");
                return null;
        }
    }

    // ==========================================
    // SYSTEM LEVEL / SYSTEM SPAWNER OWNERSHIP ASSIGNMENT
    // ==========================================
    
    /// <summary>
    /// Called by SpawnManager or Purchase System to grant global building ownership to this character.
    /// </summary>
    public void ReceiveOwnership(Building building)
    {
        if (building == null) return;
        
        if (!OwnedBuildings.Contains(building))
        {
            OwnedBuildings.Add(building);
        }
        
        // Also tell the building backend that this character is an owner
        building.AddOwner(GetComponent<Character>());
        Debug.Log($"<color=green>[CharacterLocations]</color> {gameObject.name} has received ownership of {building.RoomName}.");
    }

    // ==========================================
    // CHARACTER-DRIVEN PERMISSION LOGIC
    // ==========================================

    /// <summary>
    /// Internal method called when another owner successfully grants this character residency.
    /// </summary>
    private void BecomeResident(Room room)
    {
        if (room == null) return;

        if (!ResidentRooms.Contains(room))
        {
            ResidentRooms.Add(room);
        }
        
        room.AddResident(GetComponent<Character>());
        Debug.Log($"<color=cyan>[CharacterLocations]</color> {gameObject.name} is now a resident in {room.RoomName}.");
    }

    /// <summary>
    /// This character attempts to grant residency to another character for a specific room.
    /// Fails if this character is NOT an owner of the room (or its parent building).
    /// </summary>
    public bool AddResidentToRoom(Character potentialResident, Room targetRoom)
    {
        Character thisCharacter = GetComponent<Character>();

        // Check if THIS character has the right to assign residents
        // (Must be in the targetRoom.Owners list, or if the room is inside a Building this character owns)
        Building parentBuilding = targetRoom.GetComponentInParent<Building>();
        
        bool isOwnerOfRoom = targetRoom.Owners.Contains(thisCharacter);
        bool isOwnerOfBuilding = (parentBuilding != null && OwnedBuildings.Contains(parentBuilding));

        if (isOwnerOfRoom || isOwnerOfBuilding)
        {
            if (potentialResident.CharacterLocations != null)
            {
                potentialResident.CharacterLocations.BecomeResident(targetRoom);
                return true;
            }
        }
        
        Debug.LogWarning($"<color=orange>[CharacterLocations]</color> {gameObject.name} cannot add a resident to {targetRoom.RoomName} because they are not an Owner!");
        return false;
    }

    /// <summary>
    /// This character attempts to grant ownership rights to another character for a specific building.
    /// Fails if this character is NOT already an owner, UNLESS the building currently has 0 owners (System Claim).
    /// </summary>
    public bool AddOwnerToBuilding(Character potentialOwner, Building targetBuilding)
    {
        Character thisCharacter = GetComponent<Character>();

        bool isAlreadyOwner = OwnedBuildings.Contains(targetBuilding) || targetBuilding.Owners.Contains(thisCharacter);
        bool isUnownedBuilding = targetBuilding.Owners.Count == 0;

        if (isAlreadyOwner || isUnownedBuilding)
        {
            if (potentialOwner.CharacterLocations != null)
            {
                potentialOwner.CharacterLocations.ReceiveOwnership(targetBuilding);
                return true;
            }
        }

        Debug.LogWarning($"<color=orange>[CharacterLocations]</color> {gameObject.name} cannot make someone an Owner of {targetBuilding.RoomName} because they don't own it and it's already owned by someone else!");
        return false;
    }
}
