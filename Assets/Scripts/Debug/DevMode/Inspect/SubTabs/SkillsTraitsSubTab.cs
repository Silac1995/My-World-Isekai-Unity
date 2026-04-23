using System.Text;

public class SkillsTraitsSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(1024);

        // ── Behavioural Traits ────────────────────────────────────────
        sb.AppendLine("<b><color=#FFFFFF>Behavioural Traits</color></b>");
        var traits = c.CharacterTraits;
        if (traits != null)
        {
            var profile = traits.behavioralTraitsProfile;
            string profileName = profile != null ? profile.name : "<color=#888888>none</color>";
            sb.AppendLine($"Profile: {profileName}");
            sb.AppendLine($"  Aggressivity: {traits.GetAggressivity():F2}");
            sb.AppendLine($"  Sociability: {traits.GetSociability():F2}");
            sb.AppendLine($"  Loyalty: {traits.GetLoyalty():F2}");
            sb.AppendLine($"  Can Create Community: {traits.CanCreateCommunity()}");
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterTraits</color>");
        }

        // ── Personality ───────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Personality</color></b>");
        var profileSys = c.CharacterProfile;
        if (profileSys != null && profileSys.Personality != null)
        {
            var p = profileSys.Personality;
            string name = string.IsNullOrEmpty(p.PersonalityName) ? p.name : p.PersonalityName;
            sb.AppendLine($"Name: {name}");

            if (!string.IsNullOrEmpty(p.Description))
                sb.AppendLine($"  {p.Description}");

            if (p.CompatiblePersonalities != null && p.CompatiblePersonalities.Count > 0)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var other in p.CompatiblePersonalities)
                {
                    if (other == null) continue;
                    names.Add(string.IsNullOrEmpty(other.PersonalityName) ? other.name : other.PersonalityName);
                }
                sb.AppendLine($"  <color=#7FFF7F>Compatible with:</color> {string.Join(", ", names)}");
            }

            if (p.IncompatiblePersonalities != null && p.IncompatiblePersonalities.Count > 0)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var other in p.IncompatiblePersonalities)
                {
                    if (other == null) continue;
                    names.Add(string.IsNullOrEmpty(other.PersonalityName) ? other.name : other.PersonalityName);
                }
                sb.AppendLine($"  <color=#FF7F7F>Incompatible with:</color> {string.Join(", ", names)}");
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No personality assigned</color>");
        }

        // ── Skills ────────────────────────────────────────────────────
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
