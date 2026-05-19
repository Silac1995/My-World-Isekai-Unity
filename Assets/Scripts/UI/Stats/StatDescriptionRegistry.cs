using System.Collections.Generic;

public readonly struct StatDescription
{
    public readonly string DisplayName;
    public readonly string Description;
    public StatDescription(string displayName, string description)
    {
        DisplayName = displayName;
        Description = description;
    }
}

public static class StatDescriptionRegistry
{
    // Formula strings are intentionally NOT stored here. Every tertiary uses the same
    // body (Mathf.Max(minValue, _baseOffset + _linkedStat.CurrentValue * _multiplier))
    // where _multiplier and _baseOffset are overridden per race at runtime via
    // CharacterStats.ApplyRaceStats(). Hard-coded strings would lie to the player.
    // The orchestrator (Phase 5) renders the formula text at runtime from live
    // CharacterTertiaryStats.Multiplier / BaseOffset / LinkedStat values.
    // See docs/superpowers/plans/character-stats-rework-recon.md §5d.
    private static readonly Dictionary<StatType, StatDescription> _map = new Dictionary<StatType, StatDescription>
    {
        // Primary
        { StatType.Health,         new StatDescription("Health",         "Your life. Hit zero and you're down.") },
        { StatType.Stamina,        new StatDescription("Stamina",        "Drives weapon attacks and most physical abilities.") },
        { StatType.Mana,           new StatDescription("Mana",           "Drives spells and magical abilities.") },
        { StatType.Initiative,     new StatDescription("Initiative",     "Combat readiness. Fills toward 100, then your next turn.") },

        // Secondary
        { StatType.Strength,       new StatDescription("Strength",       "Physical might. Drives weapon damage and carry capacity.") },
        { StatType.Agility,        new StatDescription("Agility",        "Movement and reflexes. Drives speed and dodge.") },
        { StatType.Dexterity,      new StatDescription("Dexterity",      "Precision. Drives accuracy and critical chance.") },
        { StatType.Intelligence,   new StatDescription("Intelligence",   "Mental focus. Drives magical power and casting.") },
        { StatType.Endurance,      new StatDescription("Endurance",      "Toughness. Drives max health and stamina.") },
        { StatType.Charisma,       new StatDescription("Charisma",       "Social impact. Drives reputation and negotiation.") },

        // Tertiary — display names + plain descriptions only.
        // Formula strings are rendered AT RUNTIME by the tooltip from live tertiary scaling
        // (CharacterTertiaryStats.Multiplier / BaseOffset / LinkedStat).
        { StatType.PhysicalPower,  new StatDescription("Physical Power", "Multiplies your physical attack damage.") },
        { StatType.MagicalPower,   new StatDescription("Magical Power",  "Multiplies your spell damage.") },
        { StatType.Speed,          new StatDescription("Speed",          "Movement speed in and out of combat.") },
        { StatType.Accuracy,       new StatDescription("Accuracy",       "Reduces miss chance.") },
        { StatType.Dodge,          new StatDescription("Dodge",          "Chance to avoid an incoming hit.") },
        { StatType.CriticalChance, new StatDescription("Critical Chance","Chance to land a critical hit.") },
        { StatType.SpellCasting,   new StatDescription("Spell Casting",  "Faster spell cast time.") },
        { StatType.CombatCasting,  new StatDescription("Combat Casting", "Cast spells while taking attacks.") },
        { StatType.StaminaRegen,   new StatDescription("Stamina Regen",  "Stamina recovered each second.") },
        { StatType.ManaRegen,      new StatDescription("Mana Regen",     "Mana recovered each second.") },
    };

    public static bool TryGet(StatType type, out StatDescription description)
        => _map.TryGetValue(type, out description);
}
