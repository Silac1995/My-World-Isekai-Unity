using UnityEngine;

/// <summary>
/// A room representing a single apartment within an apartment building.
/// Inherits all resident management from Room (HashSet-based).
/// </summary>
public class ApartmentRoom : ComplexRoom
{
    // Resident management is fully handled by Room base class.
    // No overrides needed since HashSet.Add/Remove already return bool.
}
