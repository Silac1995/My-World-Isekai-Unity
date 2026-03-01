using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A room representing a single apartment within an apartment building.
/// </summary>
public class ApartmentRoom : Room
{
    [Header("Apartment Info")]
    [SerializeField] private List<Character> _residents = new List<Character>();

    public IReadOnlyList<Character> Residents => _residents;

    public bool AddResident(Character resident)
    {
        if (resident == null || _residents.Contains(resident)) return false;

        _residents.Add(resident);
        return true;
    }

    public bool RemoveResident(Character resident)
    {
        if (resident == null || !_residents.Contains(resident)) return false;

        _residents.Remove(resident);
        return true;
    }

    public bool IsResident(Character character)
    {
        return character != null && _residents.Contains(character);
    }
}
