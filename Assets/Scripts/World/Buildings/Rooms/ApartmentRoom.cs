using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A room representing a single apartment within an apartment building.
/// </summary>
public class ApartmentRoom : ComplexRoom
{
    // _roomResidents and Residents inherited from Room

    public override bool AddResident(Character resident)
    {
        if (resident == null || _roomResidents.Contains(resident)) return false;

        _roomResidents.Add(resident);
        return true;
    }

    public override bool RemoveResident(Character resident)
    {
        if (resident == null || !_roomResidents.Contains(resident)) return false;

        _roomResidents.Remove(resident);
        return true;
    }
}
