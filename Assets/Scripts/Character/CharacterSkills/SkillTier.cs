public enum SkillTier
{
    Novice,         // Level 0 - 14
    Intermediate,   // Level 15 - 34
    Advanced,       // Level 35 - 54
    Professional,   // Level 55 - 74
    Master,         // Level 75 - 94
    Legendary       // Level 95 - 100
}

public static class SkillTierExtensions
{
    public static SkillTier GetTierForLevel(int level)
    {
        if (level >= 95) return SkillTier.Legendary;
        if (level >= 75) return SkillTier.Master;
        if (level >= 55) return SkillTier.Professional;
        if (level >= 35) return SkillTier.Advanced;
        if (level >= 15) return SkillTier.Intermediate;
        return SkillTier.Novice;
    }

    /// <summary>
    /// Retourne le multiplicateur d'XP qu'un mentor gagne lorsqu'il enseigne aux autres.
    /// Plus le maître est compétent, plus ses élèves apprennent vite.
    /// </summary>
    public static float GetMentorshipXPMultiplier(this SkillTier tier)
    {
        switch (tier)
        {
            case SkillTier.Intermediate: return 1.0f; // Ne peut normalement pas enseigner, mais au cas où
            case SkillTier.Advanced: return 1.5f;
            case SkillTier.Professional: return 2.0f;
            case SkillTier.Master: return 3.0f;
            case SkillTier.Legendary: return 5.0f;
            default: return 1.0f;
        }
    }

    /// <summary>
    /// Returns the maximum key tier a locksmith of this skill tier can copy.
    /// Legendary locksmiths can copy any tier (returns int.MaxValue).
    /// </summary>
    public static int GetMaxCopyableTier(this SkillTier tier)
    {
        switch (tier)
        {
            case SkillTier.Novice: return 1;
            case SkillTier.Intermediate: return 2;
            case SkillTier.Advanced: return 3;
            case SkillTier.Professional: return 4;
            case SkillTier.Master: return 5;
            case SkillTier.Legendary: return int.MaxValue;
            default: return 1;
        }
    }
}
