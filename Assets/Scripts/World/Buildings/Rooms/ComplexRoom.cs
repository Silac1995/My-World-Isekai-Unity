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
    public IEnumerable<Room> GetAllRooms()
    {
        yield return this;

        foreach (var subRoom in _subRooms)
        {
            if (subRoom is ComplexRoom complex)
            {
                foreach (var r in complex.GetAllRooms())
                {
                    yield return r;
                }
            }
            else
            {
                yield return subRoom;
            }
        }
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

    public IEnumerable<T> GetRoomsOfType<T>() where T : Room
    {
        if (this is T thisT) yield return thisT;

        foreach (var subRoom in _subRooms)
        {
            if (subRoom is ComplexRoom complex)
            {
                foreach (var r in complex.GetRoomsOfType<T>())
                {
                    yield return r;
                }
            }
            else if (subRoom is T typed)
            {
                yield return typed;
            }
        }
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
    
    public IEnumerable<T> GetFurnitureOfType<T>() where T : Furniture
    {
        // Get from own base room first
        if (FurnitureManager != null)
        {
            foreach (var f in FurnitureManager.Furnitures)
            {
                if (f is T typed) yield return typed;
            }
        }

        // Add from subrooms
        foreach (var subRoom in _subRooms)
        {
            if (subRoom is ComplexRoom complex)
            {
                foreach (var f in complex.GetFurnitureOfType<T>())
                {
                    yield return f;
                }
            }
            else if (subRoom.FurnitureManager != null)
            {
                foreach (var f in subRoom.FurnitureManager.Furnitures)
                {
                    if (f is T typed) yield return typed;
                }
            }
        }
    }

    #region Furniture Tag Queries (Recursive)

    public override bool HasFurnitureWithTag(FurnitureTag tag)
    {
        // Check own room first
        if (base.HasFurnitureWithTag(tag)) return true;

        // Then check sub-rooms
        foreach (var subRoom in _subRooms)
        {
            if (subRoom.HasFurnitureWithTag(tag)) return true;
        }
        return false;
    }

    public override IEnumerable<Furniture> GetFurnitureByTag(FurnitureTag tag)
    {
        // Own room
        foreach (var f in base.GetFurnitureByTag(tag))
        {
            yield return f;
        }

        // Sub-rooms
        foreach (var subRoom in _subRooms)
        {
            foreach (var f in subRoom.GetFurnitureByTag(tag))
            {
                yield return f;
            }
        }
    }

    #endregion
}
