using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CharacterLocations : CharacterSystem
{
    [Header("References")]

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

    // Schedule entries injected when becoming a resident
    private ScheduleEntry _goHomeEntry;
    private ScheduleEntry _sleepEntry;

    /// <summary>
    /// Retrieves the specific Zone instance assigned to this character based on the requested ZoneType.
    /// </summary>
    public Zone GetZoneByType(ZoneType type)
    {
        switch (type)
            {
            case ZoneType.Home:
                return homeZone;
            case ZoneType.Job:
                return workZone;
            default:
                Debug.LogWarning($"[CharacterLocations] ZoneType {type} is not yet handled or assigned for {_character.name}");
                return null;
        }
    }

    // ==========================================
    // HOME BUILDING HELPERS
    // ==========================================

    /// <summary>
    /// Returns the character's home building. Checks owned residential buildings first,
    /// then falls back to the first resident room's parent building.
    /// </summary>
    public ResidentialBuilding GetHomeBuilding()
    {
        // Check owned buildings first
        foreach (var b in OwnedBuildings)
        {
            if (b is ResidentialBuilding res) return res;
        }

        // Fallback: find parent building of any resident room
        foreach (var room in ResidentRooms)
        {
            var parentBuilding = room.GetComponentInParent<ResidentialBuilding>();
            if (parentBuilding != null) return parentBuilding;
        }

        // Legacy fallback
        return homeBuilding;
    }

    /// <summary>
    /// Finds a bed (FurnitureTag.Bed) in the character's home building.
    /// Prioritizes beds in rooms where the character is a resident.
    /// </summary>
    public Furniture GetAssignedBed()
    {
        // Check resident rooms first for a free bed
        foreach (var room in ResidentRooms)
        {
            foreach (var bed in room.GetFurnitureByTag(FurnitureTag.Bed))
            {
                if (bed.IsFree()) return bed;
            }
        }

        // Fallback: search the whole home building
        var home = GetHomeBuilding();
        if (home != null)
        {
            foreach (var bed in home.GetFurnitureByTag(FurnitureTag.Bed))
            {
                if (bed.IsFree()) return bed;
            }
        }

        return null;
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
        building.AddOwner(_character);

        // If this is a residential building, set up home zone and schedule
        if (building is ResidentialBuilding residential)
        {
            homeZone = residential;
            homeBuilding = residential;
            InjectHomeSchedule();
        }

        Debug.Log($"<color=green>[CharacterLocations]</color> {_character.name} has received ownership of {building.RoomName}.");
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

        room.AddResident(_character);

        // Set home zone to the parent building if this is the first residence
        if (homeZone == null)
        {
            var parentBuilding = room.GetComponentInParent<ResidentialBuilding>();
            if (parentBuilding != null)
            {
                homeZone = parentBuilding;
                homeBuilding = parentBuilding;
                InjectHomeSchedule();
            }
        }

        Debug.Log($"<color=cyan>[CharacterLocations]</color> {_character.name} is now a resident in {room.RoomName}.");
    }

    /// <summary>
    /// This character attempts to grant residency to another character for a specific room.
    /// Fails if this character is NOT an owner of the room (or its parent building).
    /// </summary>
    public bool AddResidentToRoom(Character potentialResident, Room targetRoom)
    {
        Building parentBuilding = targetRoom.GetComponentInParent<Building>();

        bool isOwnerOfRoom = targetRoom.IsOwner(_character);
        bool isOwnerOfBuilding = (parentBuilding != null && OwnedBuildings.Contains(parentBuilding));

        if (isOwnerOfRoom || isOwnerOfBuilding)
        {
            if (potentialResident.CharacterLocations != null)
            {
                potentialResident.CharacterLocations.BecomeResident(targetRoom);
                return true;
            }
        }

        Debug.LogWarning($"<color=orange>[CharacterLocations]</color> {_character.name} cannot add a resident to {targetRoom.RoomName} because they are not an Owner!");
        return false;
    }

    /// <summary>
    /// This character attempts to grant ownership rights to another character for a specific building.
    /// Fails if this character is NOT already an owner, UNLESS the building currently has 0 owners (System Claim).
    /// </summary>
    public bool AddOwnerToBuilding(Character potentialOwner, Building targetBuilding)
    {
        bool isAlreadyOwner = OwnedBuildings.Contains(targetBuilding) || targetBuilding.IsOwner(_character);
        bool isUnownedBuilding = targetBuilding.OwnerCount == 0;

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

    // ==========================================
    // SCHEDULE INJECTION
    // ==========================================

    /// <summary>
    /// Injects GoHome (21h) and Sleep (23h-7h) schedule entries when a character gets a home.
    /// </summary>
    private void InjectHomeSchedule()
    {
        if (_character == null || _character.CharacterSchedule == null) return;

        // Avoid duplicate injection
        if (_goHomeEntry != null) return;

        _goHomeEntry = new ScheduleEntry(21, 23, ScheduleActivity.GoHome, 5);
        _sleepEntry = new ScheduleEntry(23, 7, ScheduleActivity.Sleep, 5);

        _character.CharacterSchedule.AddEntry(_goHomeEntry);
        _character.CharacterSchedule.AddEntry(_sleepEntry);

        Debug.Log($"<color=cyan>[CharacterLocations]</color> {_character.name} now has GoHome (21h) and Sleep (23h-7h) schedule.");
    }
}
