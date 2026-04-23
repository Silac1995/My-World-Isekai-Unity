using System.Text;

public class KnowledgeSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(512);

        sb.AppendLine("<b><color=#FFFFFF>Book Knowledge</color></b>");
        var books = c.CharacterBookKnowledge;
        if (books != null)
        {
            sb.AppendLine($"  {books}");
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterBookKnowledge</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Schedule</color></b>");
        var sched = c.CharacterSchedule;
        if (sched != null)
        {
            sb.AppendLine($"  {sched}");
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterSchedule</color>");
        }

        return sb.ToString();
    }
}
