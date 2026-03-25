using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Residential building with an owner and a list of residents.
/// Used for character homes.
/// </summary>
public class ResidentialBuilding : Building
{
    public override BuildingType BuildingType => BuildingType.Residential;
    public IEnumerable<ApartmentRoom> Apartments => GetRoomsOfType<ApartmentRoom>();

    public Character Owner => _ownerIds.Count > 0 ? Character.FindByUUID(_ownerIds[0].ToString()) : null;

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
                for (int i = 0; i < _residentIds.Count; i++)
                {
                    Character c = Character.FindByUUID(_residentIds[i].ToString());
                    if (c != null) yield return c;
                }
            }
        }
    }

    public int GetApartmentCount() => Apartments.Count();

    /// <summary>
    /// Sets the primary owner. Server-only.
    /// </summary>
    public void SetOwner(Character newOwner)
    {
        if (!IsServer) return;

        // Clear all existing owners
        while (_ownerIds.Count > 0) _ownerIds.RemoveAt(0);

        if (newOwner != null)
        {
            AddOwner(newOwner);
            if (!IsResident(newOwner))
            {
                AddResident(newOwner);
            }
        }
        Debug.Log($"<color=green>[Building]</color> {newOwner?.CharacterName} is now the owner of {buildingName}.");
    }

    /// <summary>
    /// Owner-validated resident assignment. The owner grants residency to another character.
    /// </summary>
    public bool AddResidentAsOwner(Character owner, Character resident, Room targetRoom = null)
    {
        if (owner == null || resident == null) return false;

        if (!IsOwner(owner))
        {
            Debug.LogWarning($"<color=orange>[Building]</color> {owner.CharacterName} cannot add residents to {buildingName} — not the owner.");
            return false;
        }

        return AddResident(resident, targetRoom as ApartmentRoom);
    }

    /// <summary>
    /// System-level resident assignment (no owner check). Server-only.
    /// </summary>
    public override bool AddResident(Character resident) => AddResident(resident, null);

    public bool AddResident(Character resident, ApartmentRoom targetRoom)
    {
        if (resident == null || !IsServer || IsResident(resident)) return false;

        var apts = Apartments.ToList();
        if (apts.Count > 0)
        {
            ApartmentRoom targetApt = targetRoom;

            if (targetApt == null || !apts.Contains(targetApt))
            {
                targetApt = apts.OrderBy(a => a.ResidentCount).First();
            }

            if (targetApt.OwnerCount == 0) targetApt.AddOwner(resident);

            if (targetApt.AddResident(resident))
            {
                Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} now lives in an apartment of {buildingName}.");
                return true;
            }
            return false;
        }

        if (base.AddResident(resident))
        {
            Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} now lives at {buildingName}.");
            return true;
        }
        return false;
    }

    public override bool RemoveResident(Character resident)
    {
        if (resident == null || !IsServer || !IsResident(resident)) return false;

        foreach (var apt in Apartments)
        {
            if (apt.IsResident(resident))
            {
                apt.RemoveResident(resident);
                apt.RemoveOwner(resident);
                Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} left their apartment in {buildingName}.");
                return true;
            }
        }

        if (base.RemoveResident(resident))
        {
            if (Owner == resident)
            {
                // Transfer ownership to next resident, or clear
                Character nextOwner = Residents.FirstOrDefault();
                SetOwner(nextOwner);
            }
            Debug.Log($"<color=green>[Building]</color> {resident.CharacterName} left {buildingName}.");
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

        return ContainsId(_residentIds, character.CharacterId);
    }
}
