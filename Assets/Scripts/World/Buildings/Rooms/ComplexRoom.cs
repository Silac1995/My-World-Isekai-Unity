using System.Collections.Generic;
using UnityEngine;

public class ComplexRoom : Room
{
    [Header("Complex Room Info")]
    [SerializeField] protected List<Room> _subRooms = new List<Room>();

    public IReadOnlyList<Room> SubRooms => _subRooms;

    public override bool IsResident(Character character)
    {
        if (base.IsResident(character)) return true;

        foreach (var subRoom in _subRooms)
        {
            if (subRoom.IsResident(character)) return true;
        }

        return false;
    }

    public void AddResidentToAllSubRooms(Character resident)
    {
        AddResident(resident);
        foreach (var subRoom in _subRooms)
        {
            subRoom.AddResident(resident);
        }
    }

    public void AddSubRoom(Room room)
    {
        if (room != null && !_subRooms.Contains(room))
        {
            _subRooms.Add(room);
        }
    }

    public void RemoveSubRoom(Room room)
    {
        if (room != null && _subRooms.Contains(room))
        {
            _subRooms.Remove(room);
        }
    }

    /// <summary>
    /// Gets all rooms recursively, including this ComplexRoom itself and all nested SubRooms.
    /// </summary>
    public List<Room> GetAllRooms()
    {
        List<Room> allRooms = new List<Room>();
        allRooms.Add(this);

        foreach (var subRoom in _subRooms)
        {
            if (subRoom is ComplexRoom complex)
            {
                allRooms.AddRange(complex.GetAllRooms());
            }
            else
            {
                allRooms.Add(subRoom);
            }
        }
        return allRooms;
    }

    /// <summary>
    /// Returns a specific room containing the position, prioritizing the smallest/deepest sub-rooms.
    /// </summary>
    public Room GetRoomAt(Vector3 position)
    {
        // Check subrooms first (prioritize smallest children over the parent envelope)
        foreach (var subRoom in _subRooms)
        {
            Room found = null;
            if (subRoom is ComplexRoom complex)
            {
                found = complex.GetRoomAt(position);
            }
            else if (subRoom.IsPointInsideRoom(position))
            {
                found = subRoom;
            }

            if (found != null) return found;
        }

        // If no child has it, check this room itself
        if (IsPointInsideRoom(position)) return this;

        return null;
    }

    public List<T> GetRoomsOfType<T>() where T : Room
    {
        List<T> result = new List<T>();
        
        if (this is T thisT) result.Add(thisT);

        foreach (var subRoom in _subRooms)
        {
            if (subRoom is ComplexRoom complex)
            {
                result.AddRange(complex.GetRoomsOfType<T>());
            }
            else if (subRoom is T typed)
            {
                result.Add(typed);
            }
        }

        return result;
    }

    public T FindAvailableFurniture<T>() where T : Furniture
    {
        // Check own furniture first
        T result = FurnitureManager != null ? FurnitureManager.FindAvailableFurniture<T>() : null;
        if (result != null) return result;

        // Then check subrooms
        foreach (var subRoom in _subRooms)
        {
            if (subRoom is ComplexRoom complex)
            {
                result = complex.FindAvailableFurniture<T>();
            }
            else if (subRoom.FurnitureManager != null)
            {
                result = subRoom.FurnitureManager.FindAvailableFurniture<T>();
            }

            if (result != null) return result;
        }

        return null;
    }
    
    public List<T> GetFurnitureOfType<T>() where T : Furniture
    {
        // Get from own base room first
        List<T> result = new List<T>();
        
        if (FurnitureManager != null)
        {
            foreach (var f in FurnitureManager.Furnitures)
            {
                if (f is T typed) result.Add(typed);
            }
        }

        // Add from subrooms
        foreach (var subRoom in _subRooms)
        {
            if (subRoom is ComplexRoom complex)
            {
                result.AddRange(complex.GetFurnitureOfType<T>());
            }
            else if (subRoom.FurnitureManager != null)
            {
                foreach (var f in subRoom.FurnitureManager.Furnitures)
                {
                    if (f is T typed) result.Add(typed);
                }
            }
        }

        return result;
    }
}
