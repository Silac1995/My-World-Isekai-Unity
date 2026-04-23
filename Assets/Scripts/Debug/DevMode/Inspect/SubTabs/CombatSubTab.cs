using System.Text;

public class CombatSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(512);

        sb.AppendLine("<b><color=#FFFFFF>Combat</color></b>");
        var combat = c.CharacterCombat;
        if (combat != null)
        {
            sb.AppendLine($"In Battle: {combat.IsInBattle}");
            sb.AppendLine($"Combat Mode: {combat.IsCombatMode}");
            sb.AppendLine($"Planned Target: {(combat.PlannedTarget != null ? combat.PlannedTarget.CharacterName : "—")}");
            sb.AppendLine($"Battle Manager: {(combat.CurrentBattleManager != null ? combat.CurrentBattleManager.name : "—")}");
            sb.AppendLine($"Current Style Expertise: {combat.CurrentCombatStyleExpertise}");

            var styles = combat.KnownStyles;
            if (styles != null && styles.Count > 0)
            {
                sb.AppendLine("Known Styles:");
                foreach (var style in styles)
                {
                    if (style == null) continue;
                    sb.AppendLine($"  {style}");
                }
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterCombat</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Status Effects</color></b>");
        var status = c.StatusManager;
        if (status != null)
        {
            var effects = status.ActiveEffects;
            if (effects != null && effects.Count > 0)
            {
                foreach (var effect in effects)
                {
                    if (effect == null) continue;
                    sb.AppendLine($"  {effect}");
                }
            }
            else
            {
                sb.AppendLine("<color=grey>None active.</color>");
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterStatusManager</color>");
        }

        return sb.ToString();
    }
}
