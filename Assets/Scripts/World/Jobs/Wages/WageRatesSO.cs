using System.Collections.Generic;
using UnityEngine;
using MWI.WorldSystem;

[CreateAssetMenu(menuName = "MWI/Jobs/Wage Rates", fileName = "WageRates")]
public class WageRatesSO : ScriptableObject
{
    [SerializeField] private List<WageRateEntry> _entries = new List<WageRateEntry>();

    /// <summary>
    /// Returns the entry for the given JobType, or null if not configured.
    /// Caller is responsible for falling back (zero wage = the asset is not yet configured for this JobType).
    /// </summary>
    public WageRateEntry GetDefaults(JobType jobType)
    {
        if (_entries == null) return null;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i] != null && _entries[i].JobType == jobType) return _entries[i];
        }
        return null;
    }

    public IReadOnlyList<WageRateEntry> Entries => _entries;
}
