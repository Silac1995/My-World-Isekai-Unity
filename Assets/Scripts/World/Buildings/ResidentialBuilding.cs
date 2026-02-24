using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bâtiment résidentiel avec un propriétaire et une liste de résidents.
/// Utilisé pour les maisons des personnages.
/// </summary>
public class ResidentialBuilding : Building
{
    public override BuildingType BuildingType => BuildingType.Residential;

    [Header("Residential")]
    [SerializeField] private Character _owner;
    [SerializeField] private List<Character> _residents = new List<Character>();
    [SerializeField] private int _maxResidents = 4;

    public Character Owner => _owner;
    public IReadOnlyList<Character> Residents => _residents;
    public bool IsFull => _residents.Count >= _maxResidents;

    public void SetOwner(Character newOwner)
    {
        _owner = newOwner;
        if (newOwner != null && !_residents.Contains(newOwner))
        {
            _residents.Add(newOwner);
        }
        Debug.Log($"<color=green>[Building]</color> {newOwner?.CharacterName} est maintenant propriétaire de {buildingName}.");
    }

    public bool AddResident(Character resident)
    {
        if (resident == null || IsFull || _residents.Contains(resident)) return false;

        _residents.Add(resident);
        Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} habite maintenant à {buildingName}.");
        return true;
    }

    public bool RemoveResident(Character resident)
    {
        if (resident == null || !_residents.Contains(resident)) return false;

        _residents.Remove(resident);

        // Si le résident retiré était le propriétaire, transférer ou vider
        if (_owner == resident)
        {
            _owner = _residents.Count > 0 ? _residents[0] : null;
        }

        Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} a quitté {buildingName}.");
        return true;
    }

    public bool IsResident(Character character)
    {
        return character != null && _residents.Contains(character);
    }
}
