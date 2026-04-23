---
type: system
title: "Worker Wages and Performance"
tags: [character, jobs, economy, wages, worklog, save, network, tier-2]
created: 2026-04-22
updated: 2026-04-23
sources:
  - "Assets/Scripts/Character/CharacterWallet/CharacterWallet.cs"
  - "Assets/Scripts/Character/CharacterWorkLog/CharacterWorkLog.cs"
  - "Assets/Scripts/World/Jobs/Wages/WageSystemService.cs"
  - "Assets/Scripts/World/Jobs/Wages/Pure/WageCalculator.cs"
  - "Assets/Scripts/World/Jobs/Wages/Pure/HarvesterCreditCalculator.cs"
  - "Assets/Scripts/Economy/CurrencyId.cs"
  - "Assets/ScriptableObjects/Jobs/WageRates.asset"
  - ".agent/skills/character-wallet/SKILL.md"
  - ".agent/skills/character-worklog/SKILL.md"
  - ".agent/skills/wage-system/SKILL.md"
  - "docs/superpowers/specs/2026-04-22-worker-wages-and-performance-design.md"
  - "docs/superpowers/smoketests/2026-04-22-worker-wages-and-performance-smoketest.md"
related:
  - "[[character]]"
  - "[[character-job]]"
  - "[[commercial-building]]"
  - "[[jobs-and-logistics]]"
  - "[[building-logistics-manager]]"
  - "[[save-load]]"
  - "[[network]]"
  - "[[world-macro-simulation]]"
  - "[[quest-system]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - npc-ai-specialist
  - save-persistence-specialist
owner_code_path: "Assets/Scripts/World/Jobs/Wages/"
depends_on:
  - "[[character]]"
  - "[[character-job]]"
  - "[[commercial-building]]"
  - "[[building-logistics-manager]]"
  - "[[jobs-and-logistics]]"
  - "[[save-load]]"
  - "[[network]]"
depended_on_by:
  - "[[jobs-and-logistics]]"
---

# Worker Wages and Performance

## Summary

The Worker Wages and Performance system pays NPCs (and players who take a job) at the end of every shift, and records every deposit / craft / delivery / shift in a per-character `CharacterWorkLog` so that work history is queryable later for reputation, quests, or promotion mechanics. Two new character subsystems (`CharacterWallet` and `CharacterWorkLog`) sit on every `Character` prefab; a `WageSystemService` singleton in `GameScene` orchestrates wage computation through a swappable `IWagePayer` (today: `MintedWagePayer`, future: `BuildingTreasuryWagePayer`).

## Purpose

Before this system, NPCs that took jobs had a place to be on schedule but no measurable output and no economic feedback — the worker was indistinguishable from a productive one, and there was no money primitive to hand to them anyway. This system closes both gaps: it introduces the project's first currency primitive (`CurrencyId` + multi-currency wallet) and the first per-character work-history record, then connects them via a wage formula that rewards attendance and productivity while explicitly refusing to reward overtime.

The architecture is shaped so the future Kingdom system can mint additional currencies and the future treasury / quest / reputation systems can plug in without refactoring this work.

## Responsibilities

- Hold per-character coin balances per `CurrencyId` (multi-currency from day one).
- Track per-shift transient counters and per-(JobType, BuildingId) lifetime work history.
- Enforce the "no overtime bonus" rule (units logged past scheduled shift end accrue to lifetime only).
- Compute shift wage = piece-rate × units + attendance-prorated minimum (or attendance-prorated fixed-wage for service jobs).
- Pay wages at punch-out via `IWagePayer` (currently `MintedWagePayer` — coins from nothing).
- Seed default wages at hire-time from a designer-tunable `WageRatesSO` asset.
- Allow owner / community-leader runtime wage edits through a server-authoritative gated API.
- Persist wallet balances + lifetime career counters + per-assignment wage overrides through `ICharacterSaveData<T>`.

**Non-responsibilities** (common misconceptions):

- Not responsible for currency exchange or banking — single Default currency today; cross-currency conversion is future scope.
- Not responsible for building treasury / where the money comes from — `MintedWagePayer` mints coins. A future `BuildingTreasuryWagePayer` will deduct from a treasury, but the source-of-funds system is its own design pass.
- Not responsible for firing low-performing workers — the data is available, but no automatic dismissal logic.
- Not responsible for reputation — work history is the raw data a future reputation system would derive from.
- Not responsible for hibernated NPC offline work accrual — `MacroSimulator` has a TODO marker; today only live shifts grow the WorkLog.

## Key classes / files

| File | Role |
|------|------|
| [CurrencyId.cs](../../Assets/Scripts/Economy/CurrencyId.cs) | Thin int-wrapping struct identifying a currency. Placeholder for the future Kingdom system. |
| [CharacterWallet.cs](../../Assets/Scripts/Character/CharacterWallet/CharacterWallet.cs) | Per-character multi-currency balance + ClientRpc-on-change sync + save. |
| [WalletSaveData.cs](../../Assets/Scripts/Character/CharacterWallet/WalletSaveData.cs) | DTO for wallet persistence. |
| [CharacterWorkLog.cs](../../Assets/Scripts/Character/CharacterWorkLog/CharacterWorkLog.cs) | Per-character work-history log + shift-window enforcement + save. |
| [WorkPlaceRecord.cs](../../Assets/Scripts/Character/CharacterWorkLog/WorkPlaceRecord.cs) | Runtime record of work at one (JobType, Building) pair. |
| [ShiftSummary.cs](../../Assets/Scripts/Character/CharacterWorkLog/ShiftSummary.cs) | Returned by `FinalizeShift`, consumed by the wage pipeline. |
| [WorkLogSaveData.cs](../../Assets/Scripts/Character/CharacterWorkLog/WorkLogSaveData.cs) | DTO for worklog persistence. |
| [WageRatesSO.cs](../../Assets/Scripts/World/Jobs/Wages/WageRatesSO.cs) | ScriptableObject; per-JobType default rates. |
| [WageRateEntry.cs](../../Assets/Scripts/World/Jobs/Wages/WageRateEntry.cs) | One entry in the rates asset. |
| [WageRates.asset](../../Assets/ScriptableObjects/Jobs/WageRates.asset) | Authored data — default wages per JobType. |
| [IWagePayer.cs](../../Assets/Scripts/World/Jobs/Wages/IWagePayer.cs) | Abstraction for "credit a wallet". |
| [MintedWagePayer.cs](../../Assets/Scripts/World/Jobs/Wages/MintedWagePayer.cs) | v1 implementation; mints coins from nothing. |
| [WageSystemService.cs](../../Assets/Scripts/World/Jobs/Wages/WageSystemService.cs) | Singleton orchestrator (in `GameScene`). |
| [WageCalculator.cs](../../Assets/Scripts/World/Jobs/Wages/Pure/WageCalculator.cs) | Pure-logic wage math (unit-tested). |
| [HarvesterCreditCalculator.cs](../../Assets/Scripts/World/Jobs/Wages/Pure/HarvesterCreditCalculator.cs) | Pure-logic deficit-bounded credit (unit-tested). |
| [Character.cs](../../Assets/Scripts/Character/Character.cs) | Facade exposes `CharacterWallet` and `CharacterWorkLog` properties. |
| [CharacterJob.cs](../../Assets/Scripts/Character/CharacterJob/CharacterJob.cs) | `JobAssignment` extended with wage fields + `SetWage(...)`. |
| [CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) | Punch-in / punch-out hooks + owner-gated `TrySetAssignmentWage`. |
| [Building.cs](../../Assets/Scripts/World/Buildings/Building.cs) | `BuildingId` (GUID) + `BuildingDisplayName` used as the workplace key. |

## Public API / entry points

```csharp
// Wallet
worker.CharacterWallet.AddCoins(CurrencyId, int, string source);
worker.CharacterWallet.RemoveCoins(CurrencyId, int, string reason);
worker.CharacterWallet.GetBalance(CurrencyId);
worker.CharacterWallet.OnBalanceChanged += (currency, old, next) => { ... };

// WorkLog
worker.CharacterWorkLog.OnPunchIn(JobType, buildingId, displayName, scheduledEndTime01);
worker.CharacterWorkLog.LogShiftUnit(JobType, buildingId, amount);
worker.CharacterWorkLog.FinalizeShift(JobType, buildingId);
worker.CharacterWorkLog.GetAllHistory();   // for UI: per-JobType list of WorkPlaceRecords

// Wage system
WageSystemService.Instance?.SeedAssignmentDefaults(JobAssignment);
WageSystemService.Instance?.ComputeAndPayShiftWage(worker, assignment, summary, scheduledShiftHours, hoursWorked, currency);

// Owner-side mutation
building.TrySetAssignmentWage(requester, worker, pieceRate, minimumShift, fixedShift);
```

Pure-logic helpers (testable in EditMode without scene):

```csharp
MWI.Jobs.Wages.WageCalculator.ComputePieceWorkWage(...);
MWI.Jobs.Wages.WageCalculator.ComputeFixedWage(...);
MWI.Jobs.Wages.HarvesterCreditCalculator.GetCreditedAmount(depositQty, deficitBefore);
```

## Data flow

```
[Hire]                                                                                                            │
CharacterJob.TakeJob ─────────► WageSystemService.SeedAssignmentDefaults(assignment)                              │
                                  └─► reads WageRatesSO, writes wage fields onto JobAssignment                    │
                                                                                                                  │
[Shift start]                                                                                                     │
CommercialBuilding.WorkerStartingShift                                                                            │
  ├─► _punchInTimeByWorker[worker] = TimeManager.CurrentTime01 * 24f                                              │
  └─► worker.CharacterWorkLog.OnPunchIn(jobType, BuildingId, BuildingDisplayName, scheduledEndTime01)             │
                                                                                                                  │
[During shift]                                                                                                    │
GoapAction_DepositResources / JobBlacksmith / JobTransporter                                                      │
  └─► worker.CharacterWorkLog.LogShiftUnit(jobType, BuildingId, amount)                                           │
        ├─► always increments lifetime counter (history)                                                          │
        └─► increments shift counter ONLY if currentTime01 ≤ scheduledEndTime01 (no overtime)                     │
                                                                                                                  │
[Shift end]                                                                                                       │
CommercialBuilding.WorkerEndingShift                                                                              │
  ├─► summary = worker.CharacterWorkLog.FinalizeShift(jobType, BuildingId)                                        │
  ├─► hoursWorked = min(now, scheduledEnd) - punchInTime                                                          │
  └─► WageSystemService.ComputeAndPayShiftWage(worker, assignment, summary, scheduledHours, hoursWorked, currency)│
        ├─► piece-work: (clamp01(hoursWorked/scheduled) * MinimumShiftWage) + (PieceRate * shiftUnits)            │
        ├─► fixed-wage: clamp01(hoursWorked/scheduled) * FixedShiftWage                                           │
        └─► IWagePayer.PayWages(worker, currency, wage, source)                                                   │
              └─► MintedWagePayer ─► worker.CharacterWallet.AddCoins(...)                                         │
                                       └─► [ClientRpc] BroadcastBalanceChangeClientRpc                            │
```

Server authority: all mutations (AddCoins, RemoveCoins, SetWage) check `IsServer && NetworkManager.IsListening`. In Solo / EditMode (no NetworkManager listening) calls proceed locally.

## Dependencies

### Upstream (this system needs)

- [[character]] — `Character.CharacterWallet` / `Character.CharacterWorkLog` facade access; `_character?.CharacterName` for log diagnostics.
- [[character-job]] — `JobAssignment` carries the per-character wage rates; `CharacterJob.ActiveJobs` is queried at punch-in/out and at deposit-credit time to find the matching assignment.
- [[commercial-building]] — `WorkerStartingShift` / `WorkerEndingShift` are the integration hooks; `Building.BuildingId` / `Building.BuildingDisplayName` key the WorkLog.
- [[building-logistics-manager]] — `JobTransporter.NotifyDeliveryProgress` and `JobBlacksmith` craft completion are the credit hooks.
- [[save-load]] — `ICharacterSaveData<T>` auto-discovery; `CharacterDataCoordinator` round-trip; `JobAssignmentSaveEntry` extension.
- [[network]] — `NetworkBehaviour` base, `[ClientRpc]` for wallet sync.

### Downstream (systems that need this)

- [[jobs-and-logistics]] — wage hooks live inside the logistics cycle's punch-in/out and deposit/delivery events.
- Future quest system — will query `CharacterWorkLog.GetCareerUnits(...)` for goals like "deliver 100 packages."
- Future reputation system — will derive employer-specific reputation from `WorkPlaceRecord.ShiftsCompleted` per workplace.
- Future shop UI — will call `CharacterWallet.RemoveCoins` for purchases.

## State & persistence

| Data | Owner | Persisted? | Save container |
|------|-------|-----------|----------------|
| Per-currency balance | `CharacterWallet._balances` | Yes | `WalletSaveData` (SaveKey "CharacterWallet", LoadPriority 35) |
| Lifetime career counters | `CharacterWorkLog._careerByJob` | Yes | `WorkLogSaveData` (SaveKey "CharacterWorkLog", LoadPriority 65) |
| WorkPlaceRecord display name | snapshotted at first-work-time | Yes (denormalized) | inside `WorkPlaceSaveEntry` |
| Per-shift transient counter | `CharacterWorkLog._shiftByJobType` | **No** — rebuilt on punch-in | n/a |
| Active-shift end time | `CharacterWorkLog._currentShiftScheduledEndTime01` | **No** | n/a |
| Per-assignment wage rates | `JobAssignment.PieceRate / MinimumShiftWage / FixedShiftWage / Currency` | Yes | `JobAssignmentSaveEntry` (extended) |
| Default wage rates | `WageRatesSO` asset | Design-time | `Assets/ScriptableObjects/Jobs/WageRates.asset` |
| Active `IWagePayer` impl | `WageSystemService._payer` | n/a (singleton) | n/a |
| Punch-in timestamp | `CommercialBuilding._punchInTimeByWorker` | **No** | n/a |

Loading mid-shift is treated as a fresh start — no half-shift recovery. Worker punches in again next time their schedule fires.

Network: wallet uses `[ClientRpc]` on every mutation. **v1 limitation:** no initial-state sync — late-joining clients see balance 0 until next mutation. WorkLog is server-only state (no NetworkVariable) — clients see an empty WorkLog locally, which is acceptable for current usage but blocks any future "client UI shows NPC career history" feature.

## Known gotchas / edge cases

- **Late-joining client wallet sync gap.** Wallet uses ClientRpc-on-change; no NetworkVariable. A client that joins after wage payment sees balance 0 until the next mutation. Will be fixed when Kingdom adds multiple currencies (upgrade to `NetworkList<CurrencyBalanceEntry>` with full initial sync).
- **Hibernated NPCs do not accrue WorkLog units offline.** `MacroSimulator` has a TODO marker — `HibernatedNPCData` doesn't carry profile state today, so offline yields go into `community.ResourcePools` but the NPC's career counter is frozen until they wake up.
- **Harvester deficit cap is dormant.** `HarvestingBuilding` does not implement `IStockProvider`, so `HarvesterCreditCalculator.GetCreditedAmount` always returns the full deposit qty (deficit = -1 → "no info, full credit"). The exploit ceiling is bounded by `HarvestingBuilding.IsResourceAtLimit` (workers stop depositing when at limit). Future fix: `HarvestingBuilding : IStockProvider`.
- **`JobBlacksmith.Type` was returning `JobType.None` before Task 24.** A latent bug — Type was not overridden, so blacksmiths would have been classified as fixed-wage (zero piece-bonus). Fixed by adding the override. Same fix applied to `JobBlacksmithApprentice`.
- **Multi-role workers at one building.** Wage payment and unit credit pick the FIRST matching `JobAssignment` via `foreach { if Workplace == this break; }`. v1 limitation; spec section 12 documents this.
- **`WageSystemService.Instance` can be null** in EditMode tests / non-bootstrap scenes (it lives only in `GameScene`). Call sites use `?.` to no-op silently. EditMode unit tests at `Assets/Tests/EditMode/` exercise pure logic instead.
- **`Mathf.RoundToInt` uses banker's rounding.** Test cases on `.5` boundaries (e.g., 0.5 × 15 = 7.5) round to nearest even (8). Documented in `WageCalculatorTests.HalfShift_FixedWage_ProratesHalf`.

## Open questions / TODO

- [ ] When does `BuildingTreasuryWagePayer` land? Needs a treasury source-of-funds system first (shop revenue / order payments / etc.).
- [ ] Does the player-as-worker UX need a HUD wallet-balance pop-up + a "I'm working" overlay? Spec leaves this for a future UI pass.
- [ ] Should hibernated NPCs accrue WorkLog units? Requires extending `HibernatedNPCData` with `WorkLogSaveData` plus a `WorkplaceBuildingId` field.
- [ ] When does the Kingdom system arrive? Determines when `CurrencyId` becomes more than just `Default` and when `CommercialBuilding.PaymentCurrency` resolves to a non-Default value.
- [ ] Should an owner who tries to drop a worker's wage below a threshold trigger a "worker quits" mechanic? (Spec section 12 lists wage negotiation as future scope.)

## Change log

- 2026-04-23 — Cross-link with new [[quest-system]] — quests share the Character facade (`CharacterQuestLog` sits alongside `CharacterWallet` + `CharacterWorkLog`). Wage payment flow unchanged; quests are orthogonal (workers can be on a quest without changing employment). — claude
- 2026-04-22 — Initial implementation (Tasks 1-27 of the plan + Tasks 29-35 docs) — claude

## Sources

- [docs/superpowers/specs/2026-04-22-worker-wages-and-performance-design.md](../../docs/superpowers/specs/2026-04-22-worker-wages-and-performance-design.md) — full design spec
- [docs/superpowers/plans/2026-04-22-worker-wages-and-performance.md](../../docs/superpowers/plans/2026-04-22-worker-wages-and-performance.md) — implementation plan
- [docs/superpowers/smoketests/2026-04-22-worker-wages-and-performance-smoketest.md](../../docs/superpowers/smoketests/2026-04-22-worker-wages-and-performance-smoketest.md) — manual Play Mode tests
- [.agent/skills/character-wallet/SKILL.md](../../.agent/skills/character-wallet/SKILL.md) — wallet procedural docs
- [.agent/skills/character-worklog/SKILL.md](../../.agent/skills/character-worklog/SKILL.md) — worklog procedural docs
- [.agent/skills/wage-system/SKILL.md](../../.agent/skills/wage-system/SKILL.md) — wage-system procedural docs
- [Assets/Scripts/Character/CharacterWallet/CharacterWallet.cs](../../Assets/Scripts/Character/CharacterWallet/CharacterWallet.cs) — primary wallet implementation
- [Assets/Scripts/Character/CharacterWorkLog/CharacterWorkLog.cs](../../Assets/Scripts/Character/CharacterWorkLog/CharacterWorkLog.cs) — primary worklog implementation
- [Assets/Scripts/World/Jobs/Wages/WageSystemService.cs](../../Assets/Scripts/World/Jobs/Wages/WageSystemService.cs) — orchestrator singleton
- [Assets/ScriptableObjects/Jobs/WageRates.asset](../../Assets/ScriptableObjects/Jobs/WageRates.asset) — designer-tuned per-JobType rates
- [Assets/Tests/EditMode/WageCalculatorTests.cs](../../Assets/Tests/EditMode/WageCalculatorTests.cs) — 8 tests
- [Assets/Tests/EditMode/HarvesterCreditCalculatorTests.cs](../../Assets/Tests/EditMode/HarvesterCreditCalculatorTests.cs) — 6 tests
