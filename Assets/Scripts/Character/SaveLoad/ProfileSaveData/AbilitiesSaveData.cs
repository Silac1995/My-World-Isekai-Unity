using System.Collections.Generic;

/// <summary>
/// Serializable DTO for CharacterAbilities save/load.
/// Stores known abilities by category and equipped slot assignments.
/// </summary>
[System.Serializable]
public class AbilitiesSaveData
{
    public List<string> knownPhysicalAbilityIds = new List<string>();
    public List<string> knownSpellIds = new List<string>();
    public List<string> knownPassiveIds = new List<string>();
    public List<AbilitySlotEntry> activeSlots = new List<AbilitySlotEntry>();
    public List<AbilitySlotEntry> passiveSlots = new List<AbilitySlotEntry>();
}

[System.Serializable]
public class AbilitySlotEntry
{
    public int slotIndex;
    public string abilityId;
}
