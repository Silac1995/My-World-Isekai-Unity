using System.Collections.Generic;

/// <summary>
/// Serializable DTO for CharacterSkills save/load.
/// Stores all unlocked skills with their level and XP progress.
/// </summary>
[System.Serializable]
public class SkillsSaveData
{
    public List<SkillSaveEntry> skills = new List<SkillSaveEntry>();
}

[System.Serializable]
public class SkillSaveEntry
{
    public string skillId;
    public int level;
    public int currentXP;
    public int totalXP;
}
