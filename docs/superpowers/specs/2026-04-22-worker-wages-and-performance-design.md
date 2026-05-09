# Worker Wages & Performance Design

**Date:** 2026-04-22
**Branch:** `multiplayyer`
**Status:** Approved Design

## Problem Statement

The job logistics cycle works end-to-end: NPCs take jobs at `CommercialBuilding`s, execute work via GOAP (`JobHarvester`, `JobCrafter`, `JobTransporter`, etc.), fulfill orders, and punch out. But the system has two structural gaps:

1. **Workers don't get paid.** No money/wallet/currency primitive exists in the codebase (`Wallet`, `Currency`, `Coins`, `Gold` — none of these are defined). Searches return only item descriptions and combat logs.
2. **Workers have no measurable output.** `CharacterStats` is combat-focused; `CharacterNeeds` only drives goal selection. There is no per-character record of "items harvested", "items crafted", "items delivered" — making it impossible for owners to evaluate performance, for the future quest system to query progress, or for any reputation/promotion mechanic to be added later.

This spec adds a **wallet** (multi-currency, designed for the future Kingdom system), a **work log** (per-job-type and per-workplace lifetime counters with full work history), and a **wage payment pipeline** that pays workers at punch-out using a formula that accounts for partial-shift attendance and excess hours.

It deliberately does not introduce: building treasuries, currency exchange, market economics, employee firing, reputation gain on completion, or any UI screens. The architecture is shaped so each of those can land later without refactoring this work.

### Requirements

1. **All gameplay routes through `Character`-level subsystems (rule 7).** Wallet and WorkLog are child GameObjects of the Character facade, accessed via `Character.Wallet` and `Character.WorkLog`. No NPC-only or player-only specialization.
2. **Anything an NPC can do, a player can do (rule 22).** A player character that takes a job receives wages and accumulates work-log entries via the same pipeline as an NPC.
3. **Wallet is multi-currency from day one.** Storage is `Dictionary<CurrencyId, int>`. `CurrencyId` is a placeholder thin handle today (one `Default` currency); the future Kingdom system mints additional currencies and `CommercialBuilding.PaymentCurrency` resolves from kingdom ownership without changing wallet or payer code.
4. **Wages live on `JobAssignment`, not on a global SO.** A `WageRatesSO` provides defaults at hire time; per-assignment values can be edited at runtime by the worker's employer (owner / community leader gating reuses existing authorization).
5. **Wage formulas account for early punch-out and reject overtime bonuses.** Minimum wage component is prorated by attendance; piece-work bonus only counts units produced inside the scheduled shift window; ratio caps at 1.0 (no overtime pay).
6. **Harvester credit is deficit-bounded.** A harvester only earns credit for the portion of his deposit that reduced the building's outstanding requirement (per `IStockProvider`). Excess deposits are not rewarded.
7. **Work log supports a per-job-type history view.** For each `JobType`, the UI must be able to enumerate every workplace the character has ever worked at, with cumulative units, shift count, and first/last work day. Building display name is denormalized so the history survives building destruction.
8. **Persisted via `ICharacterSaveData<T>` (rule 20).** Wallet balances and lifetime work-log records survive save/load and travel with the portable character profile. Per-shift transient counters are not persisted.
9. **Networked correctly (rules 18, 19).** Wallet balance, wage edits, and work-log totals are server-authoritative; clients read via NetworkVariables / RPCs. No silent state desync between Host, Client, and NPC views of the same character.
10. **Documentation shipped alongside code (rules 28, 29b).** New SKILL.md files for `character-wallet`, `character-worklog`, and `wage-system`; new wiki page `wiki/systems/worker-wages-and-performance.md`; existing `jobs-and-logistics`, `commercial-building`, and `character-job` wiki pages updated with cross-links.

### Non-Goals

- **Building treasury / source of money.** Wages are minted (created from nothing) by `MintedWagePayer`. The interface is shaped so a future `BuildingTreasuryWagePayer` can replace it without caller changes, but the treasury system itself is out of scope.
- **Currency exchange / bank / vault.** Cross-currency conversion, banking, item-for-money trade UI — all future.
- **Kingdom system itself.** This spec only introduces the `CurrencyId` handle and a single Default currency. Kingdom design (sovereignty, taxation, currency minting authority) is its own project.
- **Employee firing for low performance.** No automatic dismissal logic. Owner can still fire manually via existing `QuitJob` paths.
- **Per-employer reputation.** A worker's reputation with a specific building owner is not tracked. (The work-log history is the raw data a future reputation system would derive from.)
- **Quest system / order system (player→NPC).** These were discussed as motivation but each gets its own spec.
- **Wage-edit UI.** The setter is exposed; no screen is designed in this spec.
- **HUD for wallet display.** The wallet exposes events; no HUD is designed.
- **Tax, tip, or any wage modifier other than minimum + piece + attendance.**

---

## Architecture Overview

### Approach: Two new Character subsystems + one stateless service + one SO

Per project rule #7, each new subsystem lives on its own child GameObject under the `Character` facade.

```
Character (root facade)
  +-- [Child GO] CharacterWallet    <-- NEW (per-currency balances + save)
  +-- [Child GO] CharacterWorkLog   <-- NEW (career counters + workplace history + save)
  +-- [Child GO] CharacterJob       (existing, gains wage fields on JobAssignment)
  +-- [Child GO] CharacterNeeds     (existing)
  +-- ... other subsystems

WageSystemService (stateless, one instance on World controller / similar)
  - Holds reference to WageRatesSO (defaults)
  - Holds active IWagePayer implementation (default: MintedWagePayer)
  - Computes shift wage from JobAssignment + shift counters + attendance
  - Calls IWagePayer.PayWages(...)

WageRatesSO (ScriptableObject asset)
  - Per-JobType default rates (designer-tunable)
```

**Why stateless service, not a manager**: wage computation has no per-frame state, no lifecycle, no MonoBehaviour need. A static helper or singleton suffices and stays out of the GameObject hierarchy.

### Data ownership

| Data | Owner | Persisted? |
|---|---|---|
| Wallet balance per currency | `CharacterWallet` | Yes (profile) |
| Lifetime career units (per JobType, per Building) | `CharacterWorkLog` | Yes (profile) |
| Workplace display name (denormalized) | `CharacterWorkLog` (`WorkPlaceRecord`) | Yes (profile) |
| Per-shift transient unit counter | `CharacterWorkLog` | **No** — rebuilt at punch-in |
| Per-assignment wage rates | `JobAssignment` (extended) | Yes (profile, via existing `JobAssignmentSaveEntry`) |
| Default rates | `WageRatesSO` asset | N/A (design-time) |
| Active `IWagePayer` impl | `WageSystemService` | N/A (singleton) |

---

## Section 1: Data Model

### CurrencyId (placeholder handle)

```csharp
public readonly struct CurrencyId : IEquatable<CurrencyId>
{
    public readonly int Id;
    public CurrencyId(int id) { Id = id; }
    public static readonly CurrencyId Default = new CurrencyId(0);
    // Equals, GetHashCode, ==, != ...
}
```

Thin int-wrapping struct. One `Default` currency exists today. When the Kingdom system lands, kingdoms allocate ids; the type itself does not change.

### WalletSaveData

```csharp
[Serializable]
public class WalletSaveData
{
    public List<CurrencyBalanceEntry> Balances = new();
}

[Serializable]
public class CurrencyBalanceEntry
{
    public int CurrencyId;
    public int Amount;
}
```

### WorkLogSaveData

```csharp
[Serializable]
public class WorkLogSaveData
{
    public List<WorkLogJobEntry> Jobs = new();
}

[Serializable]
public class WorkLogJobEntry
{
    public string JobType; // enum name as string, matches existing JobAssignmentSaveEntry.jobType pattern
    public List<WorkPlaceSaveEntry> Workplaces = new();
}

[Serializable]
public class WorkPlaceSaveEntry
{
    public string BuildingId;          // canonical id (matches BuildingManager registration)
    public string BuildingDisplayName; // denormalized at first-work-time
    public int UnitsWorked;            // cumulative units
    public int ShiftsCompleted;
    public int FirstWorkedDay;         // TimeManager.CurrentDay at first shift
    public int LastWorkedDay;          // TimeManager.CurrentDay at last finalized shift
}
```

### JobAssignment — extended (existing class)

```csharp
// Existing fields: jobType, workplace reference, scheduled hours, etc.

// NEW fields:
public CurrencyId Currency;        // currency this assignment pays in
public int PieceRate;              // coins per shift unit (piece-work jobs)
public int MinimumShiftWage;       // floor for piece-work jobs (additive, prorated)
public int FixedShiftWage;         // shop / vendor / barman / server / logistics manager
```

For a given assignment, fields not relevant to its job type stay zero. (E.g., a Vendor assignment uses `FixedShiftWage` only; `PieceRate` and `MinimumShiftWage` are zero.)

`JobAssignmentSaveEntry` (existing — `Assets/Scripts/Character/SaveLoad/ProfileSaveData/JobSaveData.cs`) gains the same four fields. Save/load round-trips per-assignment wage edits.

### WageRatesSO — defaults asset

```csharp
[CreateAssetMenu(menuName = "MWI/Job/Wage Rates")]
public class WageRatesSO : ScriptableObject
{
    [SerializeField] private List<WageRateEntry> _entries;

    public WageRateEntry GetDefaults(JobType jobType);
}

[Serializable]
public class WageRateEntry
{
    public JobType JobType;
    public int PieceRate;          // 0 for fixed-wage jobs
    public int MinimumShiftWage;   // 0 for fixed-wage jobs
    public int FixedShiftWage;     // 0 for piece-work jobs
}
```

One asset in the project. Used at hire time to seed `JobAssignment.PieceRate / MinimumShiftWage / FixedShiftWage`.

---

## Section 2: Wage Formulas

### Job classification

| Class | Job types |
|---|---|
| **Piece-work** | Harvester, Crafter, Transporter, Blacksmith |
| **Fixed-wage** | Shop, Vendor, Barman, Server, LogisticsManager |

### Shift attendance

```
shiftRatio = clamp01(hoursWorked / scheduledShiftHours)
```

- `hoursWorked` = `min(actualPunchOutTime, scheduledShiftEnd) - punchInTime` in hours.
- `scheduledShiftHours` = `scheduledShiftEnd - scheduledShiftStart` in hours, taken from `CharacterSchedule` work slot.
- The `min(...)` cap means **hours past scheduled end do not increase ratio above 1.0**.

### Piece-work wage (per shift, paid at punch-out)

```
wage = (shiftRatio * MinimumShiftWage) + (PieceRate * shiftUnits)
```

Where `shiftUnits` = number of qualifying units logged inside the scheduled shift window.

- Minimum component is prorated by attendance.
- Piece bonus is **not** prorated — worker keeps what he produced.
- A worker who attends his full shift but produces zero units still gets the full minimum wage.

### Fixed-wage wage

```
wage = shiftRatio * FixedShiftWage
```

Prorated by attendance. No piece component.

### Excess-hours rules (strict)

Two separate rules, both apply:

1. **`shiftRatio` caps at 1.0** — minimum/fixed wage component cannot exceed full-shift value even if worker stays late.
2. **Piece units only count inside the scheduled shift window** — `LogShiftUnit` rejects increments where `currentTime > scheduledShiftEnd`. Late units still increment the **lifetime career counter** (the work happened — it's history) but contribute nothing to shift wage.

### Unit-credit rules

| Job | Credit rule |
|---|---|
| Harvester | Each item deposited that **reduces an outstanding required-resource deficit** of the building (queried via `IStockProvider` / `BuildingLogisticsManager`). If deposit qty exceeds remaining need, only the deficit-reducing portion counts. |
| Crafter | Each item completed against an active `CraftingOrder`. Items crafted outside any order do not count. |
| Transporter | Each item successfully unloaded at destination as part of an active `TransportOrder` (hooked into existing `NotifyDeliveryProgress`). |
| Blacksmith | Same rule as Crafter. |
| Shop / Vendor / Barman / Server / LogisticsManager | No unit counter. Career counter increments by `+1` per finalized shift. |

---

## Section 3: Components & Public API

### CharacterWallet (NEW)

```csharp
public class CharacterWallet : CharacterSystem, ICharacterSaveData<WalletSaveData>
{
    // Public read-only access
    public int GetBalance(CurrencyId currency);
    public IReadOnlyDictionary<CurrencyId, int> GetAllBalances();

    // Mutation (server-authoritative; client calls route via RPC)
    public void AddCoins(CurrencyId currency, int amount, string source);
    public bool RemoveCoins(CurrencyId currency, int amount, string reason);
    public bool CanAfford(CurrencyId currency, int amount);

    // Events
    public event Action<CurrencyId, int, int> OnBalanceChanged; // currency, oldValue, newValue
    public event Action<CurrencyId, int, string> OnCoinsReceived; // currency, amount, source

    // Save/Load
    public int LoadPriority => /* after Character core, before Job restore */;
    public WalletSaveData Serialize();
    public void Deserialize(WalletSaveData data);
}
```

- `AddCoins` / `RemoveCoins` log the source/reason for diagnostics (telemetry, future audit log).
- Negative amounts are rejected with `Debug.LogError` (defensive, per rule 31).
- Events fire after the balance change is committed.

### CharacterWorkLog (NEW)

```csharp
public class CharacterWorkLog : CharacterSystem, ICharacterSaveData<WorkLogSaveData>
{
    // Per-shift (transient, not persisted)
    public int GetShiftUnits(JobType jobType);

    // Lifetime (persisted)
    public int GetCareerUnits(JobType jobType);
    public int GetCareerUnits(JobType jobType, string buildingId);

    // History (UI consumption)
    public IReadOnlyList<WorkPlaceRecord> GetWorkplaces(JobType jobType);
    public IReadOnlyDictionary<JobType, IReadOnlyList<WorkPlaceRecord>> GetAllHistory();

    // Mutation (server-authoritative)
    public void OnPunchIn(JobType jobType, string buildingId, string buildingDisplayName);
    public void LogShiftUnit(JobType jobType, string buildingId, int amount = 1);
    public ShiftSummary FinalizeShift(JobType jobType, string buildingId);

    // Events
    public event Action<JobType, string, int> OnShiftUnitLogged; // jobType, buildingId, newShiftTotal
    public event Action<JobType, string, ShiftSummary> OnShiftFinalized;

    // Passthrough convenience (no duplication of CharacterJob state)
    public IReadOnlyList<JobAssignment> CurrentAssignments => Character.Job.ActiveAssignments;
}

public class ShiftSummary
{
    public int ShiftUnits;
    public int NewCareerUnitsForJob;
    public int NewCareerUnitsForJobAndPlace;
}

public class WorkPlaceRecord
{
    public string BuildingId;
    public string BuildingDisplayName;
    public int UnitsWorked;
    public int ShiftsCompleted;
    public int FirstWorkedDay;
    public int LastWorkedDay;
}
```

- `OnPunchIn` resets the transient shift counter for `(jobType, buildingId)` and creates the `WorkPlaceRecord` if it's the worker's first shift here (denormalizing the building display name now).
- `LogShiftUnit` enforces the **scheduled-shift-window** rule internally: if `TimeManager.CurrentTime01 > scheduledShiftEnd`, the increment is added to lifetime only, **not** to the shift counter.
- `FinalizeShift` rolls the shift counter into both `_careerByJobType` and the `WorkPlaceRecord`, increments `ShiftsCompleted`, updates `LastWorkedDay`, and returns the `ShiftSummary` for the wage system to consume.

### IWagePayer + MintedWagePayer (NEW)

```csharp
public interface IWagePayer
{
    void PayWages(Character worker, CurrencyId currency, int coins, string source);
}

public class MintedWagePayer : IWagePayer
{
    public void PayWages(Character worker, CurrencyId currency, int coins, string source)
    {
        worker.Wallet.AddCoins(currency, coins, source);
    }
}
```

Stateless. Future `BuildingTreasuryWagePayer` would deduct from a building treasury before crediting the wallet (and reject if insufficient — leading to "unpaid" status).

### WageSystemService (NEW)

`MonoBehaviour` singleton — one instance in the scene, on a world-level controller GameObject. Holds the `WageRatesSO` reference and the active `IWagePayer`. Computation is stateless (no per-frame state, no per-character state).

```csharp
public class WageSystemService : MonoBehaviour
{
    public static WageSystemService Instance { get; private set; } // set in Awake

    [SerializeField] private WageRatesSO _defaultRates;
    [SerializeField] private bool _useMintedPayer = true;

    private IWagePayer _payer;

    public WageRatesSO DefaultRates => _defaultRates;

    // Called by CommercialBuilding.OnWorkerPunchOut
    public int ComputeAndPayShiftWage(
        Character worker,
        JobAssignment assignment,
        ShiftSummary summary,
        float scheduledShiftHours,
        float hoursWorked);

    // Hire-time helper
    public void SeedAssignmentDefaults(JobAssignment assignment);
}
```

`ComputeAndPayShiftWage`:
1. Compute `shiftRatio = clamp01(hoursWorked / scheduledShiftHours)`.
2. Branch on `JobType` class (piece-work vs fixed-wage).
3. Compute `wage` using the formula above.
4. If `wage > 0`, call `_payer.PayWages(worker, assignment.Currency, wage, sourceTag)`.
5. Return `wage` (caller may log / display).

`SeedAssignmentDefaults`: copies `WageRatesSO` defaults into a freshly-created `JobAssignment`.

### CommercialBuilding — extended (existing class)

```csharp
// New runtime field per active worker assignment:
private Dictionary<Character, float> _punchInTimeByWorker;

// EXISTING hooks, augmented:
protected virtual void OnWorkerPunchIn(Character worker, JobAssignment assignment)
{
    _punchInTimeByWorker[worker] = /* TimeManager.CurrentTime01 in hours */;
    worker.WorkLog.OnPunchIn(assignment.JobType, BuildingId, BuildingDisplayName);
    /* existing logic */
}

protected virtual void OnWorkerPunchOut(Character worker, JobAssignment assignment)
{
    var summary = worker.WorkLog.FinalizeShift(assignment.JobType, BuildingId);
    var hoursWorked = /* now - punchInTime, capped at scheduledShiftEnd */;
    var scheduled = /* scheduledShiftEnd - scheduledShiftStart */;
    WageSystemService.Instance.ComputeAndPayShiftWage(
        worker, assignment, summary, scheduled, hoursWorked);
    _punchInTimeByWorker.Remove(worker);
    /* existing logic */
}
```

Plus a new resolver for `PaymentCurrency`:

```csharp
public CurrencyId PaymentCurrency => /* placeholder: CurrencyId.Default; future: kingdom-derived */;
```

### JobAssignment — runtime mutation (NEW method on existing class)

```csharp
// Owner / community leader only (gating reuses existing CommercialBuilding ownership check)
public void SetWage(int? pieceRate = null, int? minimumShift = null, int? fixedShift = null);
```

Server-authoritative. Validates non-negative. Fires `OnWageChanged` event so future UI can refresh.

---

## Section 4: Integration Points

Where the new code attaches to existing systems:

| Hook | File | Change |
|---|---|---|
| Punch-in | `CommercialBuilding.OnWorkerPunchIn` (line ~444) | Record `punchInTime`; call `worker.WorkLog.OnPunchIn`. |
| Punch-out | `CommercialBuilding.OnWorkerPunchOut` (line ~493) | `FinalizeShift` → `ComputeAndPayShiftWage`. |
| Hire | `CommercialBuilding.AssignWorker` / `CharacterJob.TakeJob` | Call `WageSystemService.SeedAssignmentDefaults` on the new assignment. |
| Harvester deposit | `JobHarvester` deposit flow (likely inside a `GoapAction_DepositResource` or equivalent) | After `BuildingLogisticsManager` records the deposit, query the **deficit-reducing portion** and call `worker.WorkLog.LogShiftUnit(JobType.Harvester, buildingId, deficitReducingQty)`. |
| Crafter completion | `JobCrafter` craft completion (active `CraftingOrder` decrement) | Call `worker.WorkLog.LogShiftUnit(JobType.Crafter, buildingId, 1)` per completed item. |
| Transporter delivery | `JobTransporter.NotifyDeliveryProgress` (per `BuildingLogisticsManager`) | Per item unloaded, call `worker.WorkLog.LogShiftUnit(JobType.Transporter, workerWorkplaceBuildingId, 1)`. The `WorkPlaceRecord` always tracks the **worker's employer** (`assignment.WorkplaceBuildingId`), never the delivery destination — "where I worked" means "who employs me". |
| Blacksmith | Same as Crafter. |
| Save profile | `CharacterDataCoordinator` priority chain | Add `CharacterWallet` and `CharacterWorkLog` to the export/import order (after `Character` core, before / after job — exact order during implementation). |

For each integration site, the implementation plan must include a **scheduled-shift-window check** at the call site (or rely on `LogShiftUnit`'s internal check — preferred, single point of truth).

---

## Section 5: Save / Load

Both new components implement `ICharacterSaveData<T>` — the established pattern (see `CharacterJob` for reference: `Assets/Scripts/Character/CharacterJob/CharacterJob.cs:269-401`).

```csharp
// CharacterWallet
public WalletSaveData Serialize() => new WalletSaveData {
    Balances = _balances.Select(kv => new CurrencyBalanceEntry {
        CurrencyId = kv.Key.Id, Amount = kv.Value
    }).ToList()
};

public void Deserialize(WalletSaveData data) { /* clear + restore */ }

// CharacterWorkLog
public WorkLogSaveData Serialize() => /* enumerate _careerByJob → save entries */;

public void Deserialize(WorkLogSaveData data) { /* clear + rebuild dictionaries */ }
```

`ProfileSaveData` aggregator gains two fields:

```csharp
public WalletSaveData Wallet;
public WorkLogSaveData WorkLog;
```

`JobAssignmentSaveEntry` (existing) gains the four wage fields + currency id. Two-sided resolver behavior (existing in `CharacterJob.Deserialize`) is unchanged — wages travel with the assignment entry.

**Transient state** (per-shift counter, punch-in timestamp) is not persisted. Loading mid-shift is treated as "fresh start" — worker punches in again the next time their schedule fires, no half-shift state to restore.

---

## Section 6: Networking

Per project rule #18 / #19 — server-authoritative; clients receive state through NetworkVariables or RPCs. No silent server-only state.

### CharacterWallet

- `_balances` is server-authoritative. Each `CurrencyId` → `NetworkVariable<int>` (or single `NetworkList<CurrencyBalanceEntry>` for dynamic currencies — implementation choice during planning).
- `AddCoins` / `RemoveCoins` execute on the server. Client requests route through a `[ServerRpc]`.
- Events fire on every machine via `OnValueChanged` callbacks of the NetworkVariable.

### CharacterWorkLog

- Career counters are server-authoritative; clients see updated values via NetworkVariable or `ClientRpc` push from `FinalizeShift`.
- `WorkPlaceRecord` collection: server-authoritative; server pushes the full list (or deltas) on change.
- Per-shift transient counter: server-side only (never sent — not persisted, not displayed historically).

### JobAssignment wage fields

- Already inside the existing `CharacterJob` net-sync model. Wage fields piggyback on the same channel (extend whatever already syncs `JobAssignment` state).
- `SetWage` ServerRpc; broadcast back via existing assignment-change pipeline.

### Validation matrix (per the network-validator agent's standard)

| Scenario | Expected behavior |
|---|---|
| Host pays NPC | Server (Host) computes wage, calls `AddCoins`, NetworkVariable propagates to all clients. |
| Client triggers payment (via player action) | ServerRpc → server validates → applies → propagates. |
| Late-joiner | Receives current wallet balance + current work-log records via NetworkVariable initial sync. |
| Wage edit by Host owner | ServerRpc on `JobAssignment.SetWage` → server validates ownership → applies → propagates. |
| NPC punches out on hibernating map | Macro-simulation path runs offline catch-up (see Section 7). No live network — wallet/worklog updated via deserialization on map wake. |

---

## Section 7: Hibernation & Macro-Simulation

Per project rule #30, any system that changes over time needs an offline catch-up formula in `MacroSimulator`.

- **Wallet:** balances are static between events; nothing to simulate offline. No catch-up.
- **WorkLog:** lifetime career counters need offline accrual when `MacroSimulator` runs the "Inventory Yields" pass.
  - For each hibernated NPC with an assigned job, for each simulated workday:
    - Determine shift units from the existing macro-sim yield calculation (already produced by `JobYieldRecipe` / `JobYieldRegistry`).
    - Append units to `_careerByJobType` and the relevant `WorkPlaceRecord` (incrementing `ShiftsCompleted` and `LastWorkedDay`).
- **Wages:** macro-sim uses the same `WageSystemService.ComputeAndPayShiftWage` — but with a synthetic `ShiftSummary` (full attendance assumed during hibernation; partial-shift only matters for live workers who get interrupted).

This means `MacroSimulator` gains a small bridge call into `WageSystemService` and `CharacterWorkLog` — the actual wage math and log mutation stay in the components.

**Open question:** for hibernated workers, should attendance always be 1.0 (workers always show up off-screen) or should we model occasional absences? Recommendation: **always 1.0 for v1**. Absence simulation is a separate concern.

---

## Section 8: Configuration & Authoring

### WageRatesSO asset

- One asset under `Assets/ScriptableObjects/Jobs/WageRates.asset`.
- Designer fills in `WageRateEntry` per `JobType`.
- Loaded by `WageSystemService` via Inspector reference.
- Editing the asset only affects **new hires** (existing assignments keep their current per-assignment rates).

### Default values (placeholder — designer to tune)

Concrete numbers chosen during implementation; the spec only asserts the structure. Suggested order of magnitude: minimum shift wage = ~10 coins, piece rate = ~2 coins/unit, fixed shift wage = ~15 coins.

---

## Section 9: UI Surface (Data Contract)

This spec **does not** design UI screens, but it asserts what data the UI can rely on:

### Wallet HUD (future)

- `Character.Wallet.GetBalance(currency)` — current balance per currency.
- `Character.Wallet.OnBalanceChanged` — push updates.
- `Character.Wallet.OnCoinsReceived` — toast / floating text on payment.

### Work history view (future)

- `Character.WorkLog.GetAllHistory()` returns `Dictionary<JobType, IReadOnlyList<WorkPlaceRecord>>`.
- For each `WorkPlaceRecord`: BuildingDisplayName, UnitsWorked, ShiftsCompleted, FirstWorkedDay, LastWorkedDay.
- Group by JobType in the UI. Sub-list per JobType = workplaces with stats.
- `Character.WorkLog.OnShiftFinalized` — refresh UI on shift completion.

### Owner wage-edit panel (future)

- Iterate `building.Workers` (existing).
- For each `(worker, JobAssignment)`: display current `PieceRate`, `MinimumShiftWage`, `FixedShiftWage`.
- On submit: `assignment.SetWage(...)` (server-authoritative; gated to owner / community leader).

---

## Section 10: Edge Cases & Defensive Behavior

Per project rule #31 — wrap fallible paths and log; never swallow.

| Case | Handling |
|---|---|
| Worker's wallet has no entry for the building's payment currency | Create entry on first credit. |
| `ComputeAndPayShiftWage` produces 0 (zero shift units, zero attendance) | Skip payment call entirely. No event noise. |
| Hours worked > scheduled shift hours | `clamp01` returns 1.0. Late units don't count to shift (already enforced in `LogShiftUnit`). |
| Worker punches out before punching in (shouldn't happen but defensive) | Log error; skip wage; do not crash. |
| `JobAssignment` missing wage fields after deserialization (old save) | `Deserialize` initializes missing fields from `WageRatesSO` defaults. |
| Building destroyed mid-shift | Existing punch-out path fires (worker auto-quits); wage paid for partial shift; `WorkPlaceRecord` retains `BuildingDisplayName` so history survives. |
| Currency mismatch between wallet and payment | Wallet stores per-currency; mismatch is impossible — each currency is its own bucket. |
| Negative `AddCoins` amount | Reject + `Debug.LogError`. |
| `RemoveCoins` insufficient balance | Returns `false`; balance unchanged; no event. |

---

## Section 11: Documentation Deliverables

Per rules #28, #29, #29b — every system change updates SKILL.md, evaluates agents, updates the wiki.

### New SKILL.md files

- `.agent/skills/character-wallet/SKILL.md` — wallet API, multi-currency model, save/load, network surface.
- `.agent/skills/character-worklog/SKILL.md` — counter taxonomy (shift vs career), workplace history, integration hooks.
- `.agent/skills/wage-system/SKILL.md` — formulas, payer interface, hire-time defaults, edit-time mutation, hibernation bridge.

### Updated SKILL.md files

- `.agent/skills/job_system/SKILL.md` — add wage hooks at punch-in / punch-out, `JobAssignment.SetWage`.
- `.agent/skills/logistics_cycle/SKILL.md` — add deficit-reducing-deposit credit hook for harvester scoring.
- `.agent/skills/save-load-system/SKILL.md` — add wallet + work-log to ProfileSaveData chain.

### Wiki

- **NEW:** `wiki/systems/worker-wages-and-performance.md` — architecture overview, components, formulas, links to SKILL.md procedures.
- **UPDATE:** `wiki/systems/jobs-and-logistics.md`, `wiki/systems/commercial-building.md`, `wiki/systems/character-job.md` — bump `updated:` date, append to `## Change log`, refresh `depends_on` / `depended_on_by`.

### Agent evaluation (rule #29)

Existing `npc-ai-specialist` and `building-furniture-specialist` agents both touch this domain. Update both:
- `npc-ai-specialist` — gains awareness of WorkLog and wage payment as side-effects of job execution.
- `building-furniture-specialist` — gains awareness of `CommercialBuilding` punch-in/out wage hooks and `JobAssignment` wage editing.

No new agent needed. Wage system is small enough to be covered by existing agents armed with the new SKILL.md files.

---

## Section 12: Open Questions / Future Work

Captured here so they aren't lost; **not** in scope for this spec:

1. **Building treasury** — replace `MintedWagePayer` with `BuildingTreasuryWagePayer`. Where does building income come from (sales? client-building order payments?)? What happens when a treasury empties (workers go unpaid, reputation hit, quit)?
2. **Currency exchange / Kingdom system** — when Kingdom lands, define how currencies are minted, how exchange rates work, whether a player needs a `Bank` to convert.
3. **Player-as-worker UX** — HUD for "I'm currently working", live shift-units counter, punch-out shortcut, wage notification toast.
4. **Owner wage-edit UI** — building-management panel listing workers and editable wage fields.
5. **Reputation derived from work history** — automatic reputation gain based on `ShiftsCompleted` per workplace.
6. **Performance-based firing / hiring priority** — owner sees worker's career stats when hiring; auto-fire on N consecutive zero-output shifts.
7. **Wage negotiation** — worker can refuse a wage edit if it drops below a threshold; quits the job.
8. **Tax / tip / overtime opt-in** — tax taken by kingdom; tips for service jobs; opt-in overtime that *does* pay.
9. **Order system (player → NPC commands)** — its own brainstorming cycle.
10. **Quest system** — its own brainstorming cycle; will likely query `CharacterWorkLog.GetCareerUnits(...)` for "deliver 100 packages" goals.

---

## References — Existing Code

- `Assets/Scripts/Character/CharacterJob/CharacterJob.cs:17-425` — assignment container, save/load resolver pattern.
- `Assets/Scripts/World/Jobs/Job.cs:10-118` — base class, `Execute()`, `OnWorkerPunchOut()`.
- `Assets/Scripts/World/Jobs/JobType.cs` — enum.
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs:13-100,444,493` — `OnWorkerPunchIn` / `OnWorkerPunchOut` hooks.
- `Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs` — order book, deficit queries.
- `Assets/Scripts/Character/SaveLoad/ICharacterSaveData.cs` — save contract.
- `Assets/Scripts/Character/SaveLoad/ProfileSaveData/ProfileSaveData.cs` — aggregator.
- `Assets/Scripts/Character/SaveLoad/ProfileSaveData/JobSaveData.cs` — `JobAssignmentSaveEntry` (extending).
- `Assets/Scripts/DayNightCycle/TimeManager.cs` — `CurrentDay`, `CurrentTime01` for first/last-worked-day stamping.

## References — Existing Documentation

- `.agent/skills/job_system/SKILL.md`
- `.agent/skills/logistics_cycle/SKILL.md`
- `.agent/skills/character-stats/SKILL.md`
- `.agent/skills/save-load-system/SKILL.md`
- `wiki/systems/jobs-and-logistics.md`
- `wiki/systems/commercial-building.md`
- `wiki/systems/character-job.md`
