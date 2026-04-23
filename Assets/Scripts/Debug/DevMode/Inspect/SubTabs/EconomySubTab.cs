using System.Text;

public class EconomySubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(1024);

        sb.AppendLine("<b><color=#FFFFFF>Wallet</color></b>");
        var wallet = c.CharacterWallet;
        if (wallet != null)
        {
            var balances = wallet.GetAllBalances();
            if (balances != null)
            {
                foreach (var kv in balances)
                {
                    sb.AppendLine($"  {kv.Key}: {kv.Value}");
                }
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterWallet</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Job</color></b>");
        var job = c.CharacterJob;
        if (job != null)
        {
            sb.AppendLine($"  Is Working: {job.IsWorking}");
            sb.AppendLine($"  Current Job: {(job.CurrentJob != null ? job.CurrentJob.ToString() : "—")}");
            var active = job.ActiveJobs;
            if (active != null && active.Count > 0)
            {
                sb.AppendLine("  Active Jobs:");
                foreach (var j in active)
                {
                    if (j == null) continue;
                    sb.AppendLine($"    {j}");
                }
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterJob</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Work Log</color></b>");
        var log = c.CharacterWorkLog;
        if (log != null)
        {
            var history = log.GetAllHistory();
            if (history != null && history.Count > 0)
            {
                sb.AppendLine($"  History entries: {history.Count}");
                foreach (var kv in history)
                {
                    sb.AppendLine($"  [{kv.Key}]");
                    foreach (var rec in kv.Value)
                    {
                        if (rec == null) continue;
                        sb.AppendLine($"    {rec}");
                    }
                }
            }
            else
            {
                sb.AppendLine("  No history yet.");
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterWorkLog</color>");
        }

        return sb.ToString();
    }
}
