using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MWI.Economy;
using MWI.WorldSystem;

public class EconomySubTab : CharacterSubTab
{
    // Lazy-populated at first use. (Name, CurrencyId) pairs for every `public static readonly CurrencyId`
    // declared on the CurrencyId type. Lets the inspector auto-show new currencies without code edits here.
    private static List<(string name, CurrencyId id)> _knownCurrencies;

    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(1536);

        // ── Wallet ────────────────────────────────────────────────────
        sb.AppendLine("<b><color=#FFFFFF>Wallet</color></b>");
        var wallet = c.CharacterWallet;
        if (wallet != null)
        {
            var known = GetKnownCurrencies();
            var balances = wallet.GetAllBalances();
            var seen = new HashSet<int>(); // CurrencyId.Id values we've already rendered

            // 1. Known currencies — always shown, even at 0.
            foreach (var (name, id) in known)
            {
                int amount = wallet.GetBalance(id);
                string color = amount > 0 ? "#FFD870" : "#888888";
                sb.AppendLine($"  <color={color}>{name}:</color> {amount}");
                seen.Add(id.Id);
            }

            // 2. Any currencies present at runtime but not declared statically.
            if (balances != null)
            {
                foreach (var kv in balances)
                {
                    if (seen.Contains(kv.Key.Id)) continue;
                    sb.AppendLine($"  <color=#FFD870>Currency#{kv.Key.Id}:</color> {kv.Value}");
                }
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterWallet</color>");
        }

        // ── Job ───────────────────────────────────────────────────────
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

        // ── Work Log (per JobType) ────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Work Log — per JobType</color></b>");
        var log = c.CharacterWorkLog;
        if (log != null)
        {
            // Collect flat workplace list while we iterate (used in the next section).
            var flatPlaces = new List<(JobType jt, WorkPlaceRecord rec)>();

            foreach (JobType jt in Enum.GetValues(typeof(JobType)))
            {
                if (jt == JobType.None) continue;

                int shift = log.GetShiftUnits(jt);
                int career = log.GetCareerUnits(jt);
                var places = log.GetWorkplaces(jt);
                int placeCount = places != null ? places.Count : 0;

                bool hasData = shift > 0 || career > 0 || placeCount > 0;
                string lineColor = hasData ? "#FFFFFF" : "#888888";
                sb.AppendLine($"  <color={lineColor}>{jt}:</color> shift={shift} · career={career} · {placeCount} place(s)");

                if (places == null) continue;
                foreach (var wp in places)
                {
                    if (wp != null) flatPlaces.Add((jt, wp));
                }
            }

            // ── Workplaces (flat list with score per place) ─────────
            sb.AppendLine();
            sb.AppendLine("<b><color=#FFFFFF>Workplaces — career history</color></b>");
            if (flatPlaces.Count == 0)
            {
                sb.AppendLine("  <color=grey>No places worked yet.</color>");
            }
            else
            {
                // Sort by UnitsWorked descending so the highest-scored place comes first.
                flatPlaces.Sort((a, b) => b.rec.UnitsWorked.CompareTo(a.rec.UnitsWorked));

                foreach (var (jt, wp) in flatPlaces)
                {
                    string place = string.IsNullOrEmpty(wp.BuildingDisplayName) ? wp.BuildingId : wp.BuildingDisplayName;
                    sb.AppendLine(
                        $"  · {place} <color=#AAAAAA>({jt})</color> — " +
                        $"<color=#FFD870>score {wp.UnitsWorked}u</color> · " +
                        $"{wp.ShiftsCompleted} shift(s) · days {wp.FirstWorkedDay}–{wp.LastWorkedDay}");
                }
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterWorkLog</color>");
        }

        return sb.ToString();
    }

    private static List<(string name, CurrencyId id)> GetKnownCurrencies()
    {
        if (_knownCurrencies != null) return _knownCurrencies;

        var list = new List<(string name, CurrencyId id)>();
        foreach (var f in typeof(CurrencyId).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.FieldType != typeof(CurrencyId)) continue;
            object raw = f.GetValue(null);
            if (raw is CurrencyId id) list.Add((f.Name, id));
        }
        _knownCurrencies = list;
        return _knownCurrencies;
    }
}
