using System;
using System.Collections.Generic;
using MWI.WorldSystem;
using UnityEngine;

/// <summary>
/// Per-character work-history log. Tracks:
///   - per-shift transient counters (not persisted),
///   - per-JobType lifetime counters (persisted),
///   - per-(JobType, BuildingId) lifetime WorkPlaceRecord entries with display-name snapshots (persisted).
///
/// The "no overtime bonus" rule is enforced inside LogShiftUnit: units logged past
/// the scheduled shift end are rolled into the lifetime record but not the shift counter.
/// </summary>
public class CharacterWorkLog : CharacterSystem, ICharacterSaveData<WorkLogSaveData>
{
    // JobType -> Dictionary<BuildingId, WorkPlaceRecord> — lifetime, persisted
    private readonly Dictionary<JobType, Dictionary<string, WorkPlaceRecord>> _careerByJob =
        new Dictionary<JobType, Dictionary<string, WorkPlaceRecord>>();

    // JobType -> shift unit count (transient, reset on punch-in)
    private readonly Dictionary<JobType, int> _shiftByJobType = new Dictionary<JobType, int>();

    // Scheduled shift end for the active shift (0..1 time-of-day). <0 = no active shift.
    private float _currentShiftScheduledEndTime01 = -1f;

    public event Action<JobType, string, int> OnShiftUnitLogged;
    public event Action<JobType, string, ShiftSummary> OnShiftFinalized;

    // --- Passthrough convenience (rule: CharacterJob is authoritative for current employment) ---
    // CharacterJob exposes its current assignments via the public ActiveJobs collection.
    public IReadOnlyList<JobAssignment> CurrentAssignments =>
        _character != null && _character.CharacterJob != null
            ? _character.CharacterJob.ActiveJobs
            : (IReadOnlyList<JobAssignment>)Array.Empty<JobAssignment>();

    // --- Shift lifecycle ---

    /// <summary>
    /// Resets the shift counter for this JobType and records the scheduled end-of-shift
    /// (in 0..1 time-of-day, compared against TimeManager.CurrentTime01).
    /// Creates the WorkPlaceRecord if this is the character's first shift at this place.
    /// </summary>
    public void OnPunchIn(JobType jobType, string buildingId, string buildingDisplayName, float scheduledEndTime01)
    {
        _shiftByJobType[jobType] = 0;
        _currentShiftScheduledEndTime01 = scheduledEndTime01;

        var places = GetOrCreateJobMap(jobType);
        if (!places.TryGetValue(buildingId, out var rec))
        {
            rec = new WorkPlaceRecord
            {
                BuildingId = buildingId,
                BuildingDisplayName = buildingDisplayName,
                UnitsWorked = 0,
                ShiftsCompleted = 0,
                FirstWorkedDay = CurrentDay(),
                LastWorkedDay = CurrentDay()
            };
            places[buildingId] = rec;
        }
    }

    /// <summary>
    /// Increments shift and lifetime counters. If called AFTER the scheduled shift end,
    /// only the lifetime counter is touched — no shift credit (no overtime bonus).
    /// </summary>
    public void LogShiftUnit(JobType jobType, string buildingId, int amount = 1)
    {
        if (amount <= 0) return;

        // Lifetime always increments (it's history).
        var places = GetOrCreateJobMap(jobType);
        if (!places.TryGetValue(buildingId, out var rec))
        {
            // Defensive: log-without-punch-in. Create a record with best-effort display name.
            rec = new WorkPlaceRecord
            {
                BuildingId = buildingId,
                BuildingDisplayName = buildingId,
                UnitsWorked = 0,
                ShiftsCompleted = 0,
                FirstWorkedDay = CurrentDay(),
                LastWorkedDay = CurrentDay()
            };
            places[buildingId] = rec;
        }
        rec.UnitsWorked += amount;
        rec.LastWorkedDay = CurrentDay();

        // Shift counter only if we are still inside the scheduled window.
        if (IsWithinScheduledShift())
        {
            _shiftByJobType.TryGetValue(jobType, out int prev);
            int next = prev + amount;
            _shiftByJobType[jobType] = next;
            OnShiftUnitLogged?.Invoke(jobType, buildingId, next);
        }
    }

    /// <summary>
    /// Finalize the shift: roll shift counter into place record's ShiftsCompleted counter,
    /// clear transient state, return the summary for the wage pipeline.
    /// </summary>
    public ShiftSummary FinalizeShift(JobType jobType, string buildingId)
    {
        _shiftByJobType.TryGetValue(jobType, out int shiftUnits);

        var places = GetOrCreateJobMap(jobType);
        places.TryGetValue(buildingId, out var rec);
        if (rec != null)
        {
            rec.ShiftsCompleted += 1;
            rec.LastWorkedDay = CurrentDay();
        }

        var summary = new ShiftSummary
        {
            ShiftUnits = shiftUnits,
            NewCareerUnitsForJob = GetCareerUnits(jobType),
            NewCareerUnitsForJobAndPlace = rec != null ? rec.UnitsWorked : 0,
            ShiftsCompletedAtPlace = rec != null ? rec.ShiftsCompleted : 0
        };

        _shiftByJobType[jobType] = 0;
        _currentShiftScheduledEndTime01 = -1f;
        OnShiftFinalized?.Invoke(jobType, buildingId, summary);
        return summary;
    }

    // --- Queries ---

    public int GetShiftUnits(JobType jobType) =>
        _shiftByJobType.TryGetValue(jobType, out int v) ? v : 0;

    public int GetCareerUnits(JobType jobType)
    {
        if (!_careerByJob.TryGetValue(jobType, out var places)) return 0;
        int total = 0;
        foreach (var rec in places.Values) total += rec.UnitsWorked;
        return total;
    }

    public int GetCareerUnits(JobType jobType, string buildingId)
    {
        if (!_careerByJob.TryGetValue(jobType, out var places)) return 0;
        return places.TryGetValue(buildingId, out var rec) ? rec.UnitsWorked : 0;
    }

    public IReadOnlyList<WorkPlaceRecord> GetWorkplaces(JobType jobType)
    {
        if (!_careerByJob.TryGetValue(jobType, out var places)) return Array.Empty<WorkPlaceRecord>();
        return new List<WorkPlaceRecord>(places.Values);
    }

    public IReadOnlyDictionary<JobType, IReadOnlyList<WorkPlaceRecord>> GetAllHistory()
    {
        var result = new Dictionary<JobType, IReadOnlyList<WorkPlaceRecord>>();
        foreach (var kv in _careerByJob)
        {
            result[kv.Key] = new List<WorkPlaceRecord>(kv.Value.Values);
        }
        return result;
    }

    // --- Internals ---

    private Dictionary<string, WorkPlaceRecord> GetOrCreateJobMap(JobType jobType)
    {
        if (!_careerByJob.TryGetValue(jobType, out var map))
        {
            map = new Dictionary<string, WorkPlaceRecord>();
            _careerByJob[jobType] = map;
        }
        return map;
    }

    private bool IsWithinScheduledShift()
    {
        if (_currentShiftScheduledEndTime01 < 0f) return false;
        var tm = MWI.Time.TimeManager.Instance;
        if (tm == null) return true; // no time manager: accept (edit-mode / tests)
        return tm.CurrentTime01 <= _currentShiftScheduledEndTime01;
    }

    private int CurrentDay()
    {
        var tm = MWI.Time.TimeManager.Instance;
        return tm != null ? tm.CurrentDay : 1;
    }

    // --- ICharacterSaveData ---

    public string SaveKey => "CharacterWorkLog";
    public int LoadPriority => 65;

    public WorkLogSaveData Serialize()
    {
        var data = new WorkLogSaveData();
        foreach (var kv in _careerByJob)
        {
            var jobEntry = new WorkLogJobEntry { jobType = kv.Key.ToString() };
            foreach (var placeKv in kv.Value)
            {
                var rec = placeKv.Value;
                jobEntry.workplaces.Add(new WorkPlaceSaveEntry
                {
                    buildingId = rec.BuildingId,
                    buildingDisplayName = rec.BuildingDisplayName,
                    unitsWorked = rec.UnitsWorked,
                    shiftsCompleted = rec.ShiftsCompleted,
                    firstWorkedDay = rec.FirstWorkedDay,
                    lastWorkedDay = rec.LastWorkedDay
                });
            }
            data.jobs.Add(jobEntry);
        }
        return data;
    }

    public void Deserialize(WorkLogSaveData data)
    {
        _careerByJob.Clear();
        _shiftByJobType.Clear();
        _currentShiftScheduledEndTime01 = -1f;
        if (data == null || data.jobs == null) return;
        foreach (var jobEntry in data.jobs)
        {
            if (!Enum.TryParse<JobType>(jobEntry.jobType, out var parsedJobType))
            {
                Debug.LogWarning($"[CharacterWorkLog] Unknown JobType '{jobEntry.jobType}' on load — skipping entry.");
                continue;
            }
            var map = GetOrCreateJobMap(parsedJobType);
            foreach (var wp in jobEntry.workplaces)
            {
                map[wp.buildingId] = new WorkPlaceRecord
                {
                    BuildingId = wp.buildingId,
                    BuildingDisplayName = wp.buildingDisplayName,
                    UnitsWorked = wp.unitsWorked,
                    ShiftsCompleted = wp.shiftsCompleted,
                    FirstWorkedDay = wp.firstWorkedDay,
                    LastWorkedDay = wp.lastWorkedDay
                };
            }
        }
    }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
