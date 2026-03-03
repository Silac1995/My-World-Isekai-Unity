using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Bâtiment résidentiel avec un propriétaire et une liste de résidents.
/// Utilisé pour les maisons des personnages.
/// </summary>
public class ResidentialBuilding : Building
{
    public override BuildingType BuildingType => BuildingType.Residential;
    public IEnumerable<ApartmentRoom> Apartments => GetRoomsOfType<ApartmentRoom>();

    public Character Owner => _roomOwners.Count > 0 ? _roomOwners[0] : null;

    public new IEnumerable<Character> Residents
    {
        get
        {
            if (Apartments.Any())
            {
                foreach (var apt in Apartments)
                {
                    foreach (var res in apt.Residents)
                    {
                        yield return res;
                    }
                }
            }
            else
            {
                foreach (var res in _roomResidents)
                {
                    yield return res;
                }
            }
        }
    }

    public int GetApartmentCount() => Apartments.Count();

    public void SetOwner(Character newOwner)
    {
        _roomOwners.Clear();
        if (newOwner != null)
        {
            AddOwner(newOwner);
            if (!_roomResidents.Contains(newOwner))
            {
                _roomResidents.Add(newOwner);
            }
        }
        Debug.Log($"<color=green>[Building]</color> {newOwner?.CharacterName} est maintenant propriétaire de {buildingName}.");
    }

    public override bool AddResident(Character resident) => AddResident(resident, null);

    public bool AddResident(Character resident, ApartmentRoom targetRoom)
    {
        if (resident == null || IsResident(resident)) return false;

        var apts = Apartments.ToList();
        if (apts.Count > 0)
        {
            ApartmentRoom targetApt = targetRoom;

            if (targetApt == null || !apts.Contains(targetApt))
            {
                // Find apartment with the fewest residents
                targetApt = apts.OrderBy(a => a.Residents.Count).First();
            }

            // First resident becomes owner if unowned
            if (targetApt.Owners.Count == 0) targetApt.AddOwner(resident);
            
            if (targetApt.AddResident(resident))
            {
                Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} habite maintenant dans un appartement de {buildingName}.");
                return true;
            }
            return false;
        }

        _roomResidents.Add(resident);
        Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} habite maintenant à {buildingName}.");
        return true;
    }

    public override bool RemoveResident(Character resident)
    {
        if (resident == null || !IsResident(resident)) return false;

        foreach (var apt in Apartments)
        {
            if (apt.IsResident(resident))
            {
                apt.RemoveResident(resident);
                apt.RemoveOwner(resident);
                Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} a quitté son appartement dans {buildingName}.");
                return true;
            }
        }

        if (_roomResidents.Remove(resident))
        {
            if (Owner == resident)
            {
                SetOwner(_roomResidents.Count > 0 ? _roomResidents[0] : null);
            }
            Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} a quitté {buildingName}.");
            return true;
        }

        return false;
    }

    public override bool IsResident(Character character)
    {
        if (character == null) return false;
        
        if (Apartments.Any())
        {
            return Apartments.Any(apt => apt.IsResident(character));
        }

        return _roomResidents.Contains(character);
    }
}
