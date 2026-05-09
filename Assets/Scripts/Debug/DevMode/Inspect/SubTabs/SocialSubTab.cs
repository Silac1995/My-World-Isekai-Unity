using System.Text;

public class SocialSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(1024);

        sb.AppendLine("<b><color=#FFFFFF>Relationships</color></b>");
        var rel = c.CharacterRelation;
        if (rel != null && rel.Relationships != null && rel.Relationships.Count > 0)
        {
            foreach (var r in rel.Relationships)
            {
                if (r == null) continue;
                string otherName = r.RelatedCharacter != null ? r.RelatedCharacter.CharacterName : "<missing>";
                int value = r.RelationValue;
                string valueColor = value > 0 ? "#7FFF7F" : (value < 0 ? "#FF7F7F" : "#AAAAAA");
                string metMark = r.HasMet ? "<color=#7FFF7F>met</color>" : "<color=#888888>unmet</color>";
                sb.AppendLine($"  {otherName} — {r.RelationType} (<color={valueColor}>{value:+#;-#;0}</color>) [{metMark}]");
            }
        }
        else
        {
            sb.AppendLine("<color=grey>None.</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Community</color></b>");
        var community = c.CharacterCommunity;
        if (community != null) sb.AppendLine($"  {community}");
        else sb.AppendLine("<color=grey>No CharacterCommunity</color>");

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Mentorship</color></b>");
        var mentor = c.CharacterMentorship;
        if (mentor != null)
        {
            sb.AppendLine($"  IsCurrentlyTeaching: {mentor.IsCurrentlyTeaching}");
            sb.AppendLine($"  {mentor}");
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterMentorship</color>");
        }

        return sb.ToString();
    }
}
