using System.Text;

public class NeedsSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("<b><color=#FFFFFF>Needs</color></b>");

        var needsSystem = c.CharacterNeeds;
        if (needsSystem == null)
        {
            sb.AppendLine("<color=grey>No CharacterNeeds</color>");
            return sb.ToString();
        }

        var needs = needsSystem.AllNeeds;
        if (needs == null || needs.Count == 0)
        {
            sb.AppendLine("<color=grey>None registered.</color>");
            return sb.ToString();
        }

        foreach (var need in needs)
        {
            if (need == null) continue;
            float urgency = need.GetUrgency();
            bool isActive = need.IsActive();
            string colorCode = !isActive ? "#888888" : (urgency >= 100 ? "#FF4444" : "#F5B027");
            string status = isActive ? "ON" : "OFF";
            sb.AppendLine($"<color={colorCode}>  {need.GetType().Name}: {urgency:F0}% [{status}]</color>");
        }

        return sb.ToString();
    }
}
