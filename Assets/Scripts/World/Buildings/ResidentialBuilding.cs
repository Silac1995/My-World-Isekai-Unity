using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bâtiment résidentiel avec un propriétaire et une liste de résidents.
/// Utilisé pour les maisons des personnages.
/// </summary>
public class ResidentialBuilding : Building
{
    public override BuildingType BuildingType => BuildingType.Residential;

    public List<ApartmentRoom> Apartments => GetRoomsOfType<ApartmentRoom>();

    public Character Owner => _roomOwners.Count > 0 ? _roomOwners[0] : null;

    public new IReadOnlyList<Character> Residents
    {
        get
        {
            var apts = Apartments;
            if (apts.Count > 0)
            {
                List<Character> allResidents = new List<Character>();
                foreach (var apt in apts)
                {
                    allResidents.AddRange(apt.Residents);
                }
                return allResidents;
            }
            return _roomResidents;
        }
    }

    public int GetApartmentCount()
    {
        return Apartments.Count;
    }

    public void SetOwner(Character newOwner)
    {
        // _roomOwners inherited from Room via Building/ComplexRoom
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

    public override bool AddResident(Character resident)
    {
        return AddResident(resident, null);
    }

    public bool AddResident(Character resident, ApartmentRoom targetRoom)
    {
        if (resident == null || IsResident(resident)) return false;

        var apts = Apartments;
        if (apts.Count > 0)
        {
            ApartmentRoom targetApt = targetRoom;

            if (targetApt == null || !apts.Contains(targetApt))
            {
                // Si aucune chambre n'est fournie (ou si elle n'appartient pas au batiment), 
                // trouver l'appartement avec le moins de résidents pour équilibrer.
                targetApt = apts[0];
                foreach (var apt in apts)
                {
                    if (apt.Residents.Count < targetApt.Residents.Count)
                    {
                        targetApt = apt;
                    }
                }
            }

            // Si l'appartement n'a pas de propriétaire, le premier résident devient le propriétaire par défaut
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

        var apts = Apartments;
        if (apts.Count > 0)
        {
            foreach (var apt in apts)
            {
                if (apt.IsResident(resident))
                {
                    apt.RemoveResident(resident);
                    // Remove ownership if they were owner
                    apt.RemoveOwner(resident);
                    Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} a quitté son appartement dans {buildingName}.");
                    return true;
                }
            }
            return false;
        }

        _roomResidents.Remove(resident);

        // Si le résident retiré était le propriétaire, transférer ou vider
        if (Owner == resident)
        {
            SetOwner(_roomResidents.Count > 0 ? _roomResidents[0] : null);
        }

        Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} a quitté {buildingName}.");
        return true;
    }

    public override bool IsResident(Character character)
    {
        if (character == null) return false;
        
        var apts = Apartments;
        if (apts.Count > 0)
        {
            foreach (var apt in apts)
            {
                if (apt.IsResident(character)) return true;
            }
            return false;
        }

        return _roomResidents.Contains(character);
    }
}
