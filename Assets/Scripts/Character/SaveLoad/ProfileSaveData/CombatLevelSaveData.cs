using System.Collections.Generic;

/// <summary>
/// Serializable DTO for CharacterCombatLevel save/load.
/// Stores the character-progression state: accumulated XP into the current level,
/// any unspent stat points, and the per-level history (which stat each level-up bumped).
/// CurrentLevel is derived from levelHistory.Count and is therefore not stored explicitly.
/// </summary>
[System.Serializable]
public class CombatLevelSaveData
{
    public int currentExperience;
    public int unassignedStatPoints;
    public List<CombatLevelEntry> levelHistory = new List<CombatLevelEntry>();
}
