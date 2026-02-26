using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewSkill", menuName = "Character/SkillSO")]
public class SkillSO : ScriptableObject
{
    [Header("Basic Info")]
    public string SkillID;
    public string SkillName;
    [TextArea] public string Description;
    public Sprite Icon;

    [Header("Proficiency Config")]
    [Tooltip("Base proficiency gained per level of the skill itself.")]
    public float BaseProficiencyPerLevel = 1.0f;

    [Header("Stat Scaling (Proficiency)")]
    [Tooltip("Defines how much proficiency each stat point adds to this skill.")]
    public List<SkillStatScaling> StatInfluences = new List<SkillStatScaling>();

    [Header("Level Up Bonuses (Passive Stats)")]
    [Tooltip("Defines passive stat bonuses granted when the character reaches specific levels in this skill.")]
    public List<SkillLevelBonus> LevelBonuses = new List<SkillLevelBonus>();
}

[Serializable]
public struct SkillStatScaling
{
    public SecondaryStatType StatType; // Only linking SecondaryStats for now (Strength, Agility, etc.)
    [Tooltip("Amount of Efficacité/Proficiency gained per 1 point in this stat.")]
    public float ProficiencyPerPoint; 
}

[Serializable]
public struct SkillLevelBonus
{
    public int RequiredLevel;
    public StatType StatToBoost;
    [Tooltip("The flat amount of stat points granted at this level.")]
    public float BonusValue;
}
