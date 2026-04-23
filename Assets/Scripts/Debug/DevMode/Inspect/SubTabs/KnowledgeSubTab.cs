using System.Text;

public class KnowledgeSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(1024);

        // ── Book Knowledge ────────────────────────────────────────────
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

        // ── Schedule ──────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Schedule</color></b>");
        var sched = c.CharacterSchedule;
        if (sched == null)
        {
            sb.AppendLine("<color=grey>No CharacterSchedule</color>");
            return sb.ToString();
        }

        // Current activity + time-of-day context.
        var tm = sched.TimeManager;
        int currentHour = tm != null ? tm.CurrentHour : -1;
        string hourText = currentHour >= 0 ? $"{currentHour:D2}h" : "—";
        sb.AppendLine($"  Current Activity: <color=#FFD870>{sched.CurrentActivity}</color>  (hour: {hourText})");

        // Entries list.
        var entries = sched.Entries;
        if (entries == null || entries.Count == 0)
        {
            sb.AppendLine("  <color=grey>No schedule entries.</color>");
            return sb.ToString();
        }

        sb.AppendLine($"  Entries: {entries.Count}");
        foreach (var entry in entries)
        {
            if (entry == null) continue;

            bool activeNow = currentHour >= 0 && entry.IsActiveAtHour(currentHour);
            string lineColor = activeNow ? "#7FFF7F" : "#CCCCCC";
            string activeTag = activeNow ? " <color=#7FFF7F>◆ active</color>" : "";

            sb.AppendLine(
                $"    <color={lineColor}>{entry.startHour:D2}h–{entry.endHour:D2}h · " +
                $"{entry.activity} · priority {entry.priority}</color>{activeTag}");
        }

        return sb.ToString();
    }
}
