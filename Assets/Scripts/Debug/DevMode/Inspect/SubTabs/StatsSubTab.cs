using System.Text;

public class StatsSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine("<b><color=#FFFFFF>Combat Level</color></b>");

        var lvl = c.CharacterCombatLevel;
        if (lvl != null)
        {
            sb.AppendLine($"Level: {lvl.CurrentLevel}");
            sb.AppendLine($"XP: {lvl.CurrentExperience}");
            sb.AppendLine($"Unassigned Stat Points: {lvl.UnassignedStatPoints}");
            var history = lvl.LevelHistory;
            if (history != null && history.Count > 0)
            {
                sb.AppendLine($"Level History entries: {history.Count}");
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterCombatLevel</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Stats</color></b>");

        var s = c.Stats;
        if (s != null)
        {
            AppendStat(sb, "Health", s.Health);
            AppendStat(sb, "Stamina", s.Stamina);
            AppendStat(sb, "Mana", s.Mana);
            AppendStat(sb, "Initiative", s.Initiative);
            AppendStat(sb, "Strength", s.Strength);
            AppendStat(sb, "Agility", s.Agility);
            AppendStat(sb, "Dexterity", s.Dexterity);
            AppendStat(sb, "Intelligence", s.Intelligence);
            AppendStat(sb, "Endurance", s.Endurance);
            AppendStat(sb, "Charisma", s.Charisma);
            AppendStat(sb, "PhysicalPower", s.PhysicalPower);
            AppendStat(sb, "Speed", s.Speed);
            AppendStat(sb, "DodgeChance", s.DodgeChance);
            AppendStat(sb, "Accuracy", s.Accuracy);
            AppendStat(sb, "ManaRegenRate", s.ManaRegenRate);
            AppendStat(sb, "StaminaRegenRate", s.StaminaRegenRate);
            AppendStat(sb, "CriticalHitChance", s.CriticalHitChance);
            AppendStat(sb, "MoveSpeed", s.MoveSpeed);
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterStats</color>");
        }

        return sb.ToString();
    }

    private static void AppendStat(System.Text.StringBuilder sb, string label, CharacterBaseStats stat)
    {
        if (stat == null) { sb.AppendLine($"  {label}: —"); return; }
        sb.AppendLine($"  {label}: {stat.CurrentValue:F2}");
    }
}
