---
name: character-worklog
description: Per-character work-history log — transient per-shift counter + persisted per-(JobType, BuildingId) WorkPlaceRecord. Enforces the "no overtime bonus" rule.
---

# Character Work Log

`CharacterWorkLog` is a `CharacterSystem` (NetworkBehaviour) child of every `Character`. It tracks two flavors of work data:

1. **Per-shift transient counter** — reset on punch-in, consumed at punch-out, NOT persisted.
2. **Per-(JobType, BuildingId) lifetime career counter** — every shift's work rolls in here, persisted via `ICharacterSaveData<WorkLogSaveData>`.

The log is the data source for current wage calculation (shift counter feeds piece-rate bonus) and for the future quest system / promotion mechanic (lifetime counter answers "have you delivered 100 packages?").

## When to use this skill

- Crediting a worker for completed work (deposit / craft / delivery / shift end).
- Rendering a "work history" UI: per-JobType list of workplaces with stats.
- Querying a character's career stats from a quest / dialogue / promotion system.
- Adding a new job type whose work should count.

## Counter taxonomy

```
_shiftByJobType : Dictionary<JobType, int>                      // transient
_careerByJob    : Dictionary<JobType, Dictionary<string, WorkPlaceRecord>>  // persisted
```

`WorkPlaceRecord` holds:
- `BuildingId` — stable id (the `Building.BuildingId` NetworkVariable GUID).
- `BuildingDisplayName` — denormalized at first-work-time. Survives building destruction so UI history doesn't go blank.
- `UnitsWorked` — cumulative item count.
- `ShiftsCompleted` — finalized shift count.
- `FirstWorkedDay` / `LastWorkedDay` — `TimeManager.CurrentDay` stamps.

## "No overtime bonus" rule

`LogShiftUnit(jobType, buildingId, amount)` always increments the lifetime counter (the work happened — it's history). It only increments the **shift counter** when the call lands inside the scheduled shift window:

```csharp
if (_currentShiftScheduledEndTime01 < 0f) return false;        // no active shift
if (TimeManager.Instance.CurrentTime01 > _currentShiftScheduledEndTime01) return false; // overtime
```

So a worker who stays past their scheduled end and produces 5 more items will see `+5` in their lifetime career counter and `+0` in the shift score → no piece-rate bonus for those 5 items.

## Public API

```csharp
// Lifecycle
void OnPunchIn(JobType, string buildingId, string buildingDisplayName, float scheduledEndTime01);
void LogShiftUnit(JobType, string buildingId, int amount = 1);
ShiftSummary FinalizeShift(JobType, string buildingId);

// Query
int GetShiftUnits(JobType);
int GetCareerUnits(JobType);
int GetCareerUnits(JobType, string buildingId);
IReadOnlyList<WorkPlaceRecord> GetWorkplaces(JobType);
IReadOnlyDictionary<JobType, IReadOnlyList<WorkPlaceRecord>> GetAllHistory();

// Events
event Action<JobType, string, int> OnShiftUnitLogged;     // (jobType, buildingId, newShiftTotal)
event Action<JobType, string, ShiftSummary> OnShiftFinalized;
```

`ShiftSummary` carries `ShiftUnits`, `NewCareerUnitsForJob`, `NewCareerUnitsForJobAndPlace`, `ShiftsCompletedAtPlace` — what the wage pipeline needs to compute pay.

`CurrentAssignments` is a passthrough to `Character.CharacterJob.ActiveJobs` — no duplication of state.

## Save / load

```csharp
public string SaveKey => "CharacterWorkLog";
public int LoadPriority => 65;   // after CharacterJob (60)
```

Only the lifetime career counters persist. Shift counter and `_currentShiftScheduledEndTime01` are transient — they reset to "no active shift" on load. Loading mid-shift is treated as fresh start; the worker punches in again the next time their schedule fires.

## Integration points

| Caller | What it does |
|---|---|
| `CommercialBuilding.WorkerStartingShift` | Calls `OnPunchIn(jobType, BuildingId, BuildingDisplayName, scheduledEndTime01)`. |
| `CommercialBuilding.WorkerEndingShift` | Calls `FinalizeShift(jobType, BuildingId)`, passes the `ShiftSummary` to `WageSystemService`. |
| `GoapAction_DepositResources.TryCreditWorkLog` | For Harvester family (Woodcutter, Miner, Forager, Farmer): `LogShiftUnit(JobType.X, building.BuildingId, deficitBoundedQty)`. |
| `JobBlacksmith.TryCreditWorkLog` | After each crafted item against an active CraftingOrder: `LogShiftUnit(this.Type, _workplace.BuildingId, 1)`. |
| `JobTransporter.TryCreditWorkLog` | Per item unloaded at destination: `LogShiftUnit(JobType.Transporter, _workplace.BuildingId, amount)`. Credit goes to the **employer**, not the destination. |

## Gotchas

- **Server-only state.** No NetworkVariable / ClientRpc. Clients see an empty WorkLog locally — fine for current usage but if a player UI needs to display NPC work history across clients, this needs a sync layer added.
- **`LogShiftUnit` defensive create-on-missing.** If a unit gets logged for a `(jobType, buildingId)` pair that was never `OnPunchIn`'d, the helper creates a `WorkPlaceRecord` on the fly using `buildingId` as a fallback display name. Prevents data loss but logs are quieter than they should be — if you see a workplace whose display name is a GUID, that's why.
- **Hibernated NPCs do NOT accrue WorkLog units offline.** `MacroSimulator` has a TODO marker (Task 26) — `HibernatedNPCData` currently doesn't carry profile state, so offline yields go into community pools but the NPC's career counter is frozen until they wake up and start a new shift.
- **TimeManager.Instance can be null** (EditMode tests, early-init scenes). The internal helpers degrade gracefully: `IsWithinScheduledShift` returns `true` (accept logging), `CurrentDay()` falls back to `1`.
- **Multi-role workers at one building** — `LogShiftUnit` keys on `(jobType, buildingId)`. A worker holding both a Crafter and a Vendor assignment at the same building has two distinct WorkPlaceRecords for that building.

## Related

- `.agent/skills/character-wallet/SKILL.md` — sibling subsystem holding the coin balance.
- `.agent/skills/wage-system/SKILL.md` — consumes `ShiftSummary` to compute pay.
- `.agent/skills/job_system/SKILL.md` — `CharacterJob` (authoritative for current employment).
- `wiki/systems/worker-wages-and-performance.md` — architecture overview.

## Source files

- `Assets/Scripts/Character/CharacterWorkLog/CharacterWorkLog.cs`
- `Assets/Scripts/Character/CharacterWorkLog/WorkPlaceRecord.cs`
- `Assets/Scripts/Character/CharacterWorkLog/ShiftSummary.cs`
- `Assets/Scripts/Character/CharacterWorkLog/WorkLogSaveData.cs`
