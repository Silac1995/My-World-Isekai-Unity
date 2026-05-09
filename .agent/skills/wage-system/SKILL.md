---
name: wage-system
description: Orchestrates worker pay — combines WageRatesSO defaults + per-JobAssignment overrides + ShiftSummary attendance into a wage paid via IWagePayer.
---

# Wage System

The wage system pays workers at punch-out. It composes three concerns:

- **Configuration** — `WageRatesSO` asset holds per-`JobType` defaults (designer-tunable).
- **Per-character overrides** — `JobAssignment` carries the worker's actual `PieceRate` / `MinimumShiftWage` / `FixedShiftWage` / `Currency`. Owner-editable at runtime.
- **Payment** — `IWagePayer` abstraction; v1 implementation is `MintedWagePayer` (mints from nothing). Future: `BuildingTreasuryWagePayer`.

`WageSystemService` is the singleton that ties them together.

## When to use this skill

- Adding a new job type that earns wages.
- Tuning wage values for an existing job (edit `Assets/ScriptableObjects/Jobs/WageRates.asset`).
- Swapping the wage payer (v1 minted → future treasury).
- Wiring an owner-side wage-edit UI.
- Debugging "why didn't this NPC get paid?".

## Wage formulas

Both formulas use:

```
shiftRatio = clamp01(hoursWorked / scheduledShiftHours)
```

`shiftRatio` caps at 1.0 — there is **no overtime pay** for the minimum/fixed component.

### Piece-work jobs (Woodcutter, Miner, Forager, Farmer, Crafter, Transporter, Blacksmith, BlacksmithApprentice)

```
wage = (shiftRatio * MinimumShiftWage) + (PieceRate * shiftUnits)
```

- Minimum component is attendance-prorated (early punch-out = less minimum).
- Piece bonus is **not** prorated — the worker keeps what they actually produced.
- A worker who attends but produces nothing still gets the prorated minimum.

### Fixed-wage jobs (Vendor, Server, Barman, LogisticsManager)

```
wage = shiftRatio * FixedShiftWage
```

Fully attendance-prorated. No piece component.

## Pure-logic helpers

Both live in `MWI.Wages.Pure` asmdef so they can be unit-tested without Unity scenes (14 tests in `Assets/Tests/EditMode/`).

```csharp
// Wage math
int WageCalculator.ComputePieceWorkWage(hoursWorked, scheduledShiftHours, minimumShiftWage, pieceRate, shiftUnits);
int WageCalculator.ComputeFixedWage(hoursWorked, scheduledShiftHours, fixedShiftWage);
float WageCalculator.ComputeShiftRatio(hoursWorked, scheduledShiftHours);

// Deficit-bounded harvester credit
int HarvesterCreditCalculator.GetCreditedAmount(int depositQty, int deficitBefore);
// Returns clamp(depositQty, 0, deficitBefore). Excess deposits earn no bonus.
```

## WageRatesSO asset

Located at `Assets/ScriptableObjects/Jobs/WageRates.asset`. Designer-tunable list of `WageRateEntry` per JobType:

| JobType | PieceRate | MinShift | FixedShift |
|---|---|---|---|
| Woodcutter / Forager / Farmer / Transporter / BlacksmithApprentice | 2 | 10 | 0 |
| Miner / Crafter | 3 | 12 | 0 |
| Blacksmith | 4 | 15 | 0 |
| Server | 0 | 0 | 14 |
| Barman | 0 | 0 | 16 |
| Vendor | 0 | 0 | 18 |
| LogisticsManager | 0 | 0 | 22 |

Edit values in the Inspector — they take effect for **new hires** only. Existing assignments keep their per-assignment rates (which can be edited via `TrySetAssignmentWage`).

## JobAssignment per-assignment wages

`JobAssignment` (in `Assets/Scripts/Character/CharacterJob/CharacterJob.cs`) gained four wage fields in Task 16:

```csharp
public MWI.Economy.CurrencyId Currency;
public int PieceRate;
public int MinimumShiftWage;
public int FixedShiftWage;

public bool SetWage(int? pieceRate = null, int? minimumShift = null, int? fixedShift = null);
```

- Seeded at hire time by `WageSystemService.SeedAssignmentDefaults` from the SO defaults.
- Owner can edit at runtime via the gated wrapper:

```csharp
building.TrySetAssignmentWage(requester, worker, pieceRate: 5, minimumShift: 20);
// returns true if requester is the building's owner OR the community leader
// AND any field was modified.
```

The gate uses `Room.IsOwner(Character)` (existing `_ownerIds` NetworkList) and `MapRegistry.GetCommunity(...).LeaderNpcId`. Server-authoritative — clients route via ServerRpc.

Wages persist via `JobAssignmentSaveEntry` (Task 17 added the four fields). Backward-compatible: missing fields deserialize to `0`/`CurrencyId.Default` and get re-seeded next hire.

## IWagePayer

```csharp
public interface IWagePayer
{
    void PayWages(Character worker, CurrencyId currency, int coins, string source);
}
```

v1 impl `MintedWagePayer` just calls `worker.CharacterWallet.AddCoins(...)`. No treasury. Future swap to `BuildingTreasuryWagePayer` will:
- Deduct from the building's treasury before crediting.
- Fail (return without crediting) when treasury is insufficient → triggers reputation hit + potential NPC quit.
- Require zero changes to callers.

Inject via `WageSystemService.SetPayer(IWagePayer)` (singleton-level).

## WageSystemService

`MonoBehaviour` singleton. One instance in `GameScene` at scene root. Wired in Task 19.

```csharp
WageSystemService.Instance.DefaultRates;        // the SO
WageSystemService.Instance.Payer;               // current IWagePayer

WageSystemService.Instance.SeedAssignmentDefaults(JobAssignment);    // hire-time
WageSystemService.Instance.ComputeAndPayShiftWage(                   // punch-out-time
    Character worker, JobAssignment assignment, ShiftSummary summary,
    float scheduledShiftHours, float hoursWorked, CurrencyId paymentCurrency);
```

`Instance` can be null in EditMode tests / non-bootstrap scenes — call sites use `?.` to no-op silently.

## Integration points

| Caller | Hook |
|---|---|
| `CharacterJob.TakeJob` | `WageSystemService.Instance?.SeedAssignmentDefaults(assignment)` after the assignment is added. |
| `CommercialBuilding.WorkerStartingShift` | Records `_punchInTimeByWorker[worker]`, calls `worker.CharacterWorkLog.OnPunchIn`. |
| `CommercialBuilding.WorkerEndingShift` | Calls `WorkLog.FinalizeShift`, then `WageSystemService.ComputeAndPayShiftWage(...)`, then removes the punch-in entry. |
| `JobBlacksmith` / `JobTransporter` / `GoapAction_DepositResources` | Call `worker.CharacterWorkLog.LogShiftUnit(...)` to feed the shift counter that becomes piece-rate bonus. |

## Gotchas

- **Per-job-type list of piece-work jobs** lives inside `WageSystemService.IsPieceWorkJob` (private switch). When you add a new JobType, decide piece-work vs fixed-wage and add it to either the switch (piece-work) or just leave it (fixed-wage default).
- **Harvester deficit cap is dormant today.** `HarvestingBuilding` does not implement `IStockProvider`, so `HarvesterCreditCalculator` always returns the full deposit qty (deficit = -1 → "no info, full credit"). The exploit ceiling is bounded by `HarvestingBuilding.IsResourceAtLimit` (workers stop depositing). Future fix: make `HarvestingBuilding : IStockProvider`.
- **JobBlacksmith.Type and JobBlacksmithApprentice.Type were `JobType.None`** before Task 24 — no piece-work payment was reaching them. If you add a new crafter subclass, override `Type` immediately.
- **Multi-role workers at one building** — wage payment uses `foreach { if Workplace == this break; }`. First match wins. v1 limitation.
- **Currency is `CurrencyId.Default` today.** When Kingdom lands, `CommercialBuilding.PaymentCurrency` will resolve from kingdom ownership; until then everything pays in Default.
- **WageSystemService is per-scene** (no `DontDestroyOnLoad`). It lives in GameScene only; MainMenuScene has no wage system.

## Smoke testing

Manual Play Mode tests live at `docs/superpowers/smoketests/2026-04-22-worker-wages-and-performance-smoketest.md`. Eight scenarios cover full shift, partial shift, overtime cap, fixed wage, save/load, owner edit, multiplayer sync, hibernation transition.

## Related

- `.agent/skills/character-wallet/SKILL.md` — destination for paid wages.
- `.agent/skills/character-worklog/SKILL.md` — source of `ShiftSummary` and shift-window enforcement.
- `.agent/skills/job_system/SKILL.md` — `CharacterJob` / `JobAssignment` employment record.
- `.agent/skills/save-load-system/SKILL.md` — how `JobAssignmentSaveEntry` round-trips wages.
- `wiki/systems/worker-wages-and-performance.md` — architecture overview.

## Source files

- `Assets/Scripts/World/Jobs/Wages/WageRatesSO.cs`
- `Assets/Scripts/World/Jobs/Wages/WageRateEntry.cs`
- `Assets/Scripts/World/Jobs/Wages/IWagePayer.cs`
- `Assets/Scripts/World/Jobs/Wages/MintedWagePayer.cs`
- `Assets/Scripts/World/Jobs/Wages/WageSystemService.cs`
- `Assets/Scripts/World/Jobs/Wages/Pure/WageCalculator.cs`
- `Assets/Scripts/World/Jobs/Wages/Pure/HarvesterCreditCalculator.cs`
- `Assets/ScriptableObjects/Jobs/WageRates.asset`
