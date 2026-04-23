using System.Text;

public class SkillsTraitsSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(512);

        sb.AppendLine("<b><color=#FFFFFF>Personality (Traits)</color></b>");
        var traits = c.CharacterTraits;
        if (traits != null)
        {
            sb.AppendLine($"Aggressivity: {traits.GetAggressivity():F2}");
            sb.AppendLine($"Sociability: {traits.GetSociability():F2}");
            sb.AppendLine($"Loyalty: {traits.GetLoyalty():F2}");
            sb.AppendLine($"Can Create Community: {traits.CanCreateCommunity()}");
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterTraits</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Skills</color></b>");
        var skills = c.CharacterSkills;
        if (skills != null && skills.Skills != null && skills.Skills.Count > 0)
        {
            foreach (var skill in skills.Skills)
            {
                if (skill == null) continue;
                sb.AppendLine($"  {skill}");
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No skills registered.</color>");
        }

        return sb.ToString();
    }
}
