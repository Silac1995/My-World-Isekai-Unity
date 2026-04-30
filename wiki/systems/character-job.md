---
type: system
title: "Character Job"
tags: [character, jobs, employment, tier-2, stub]
created: 2026-04-19
updated: 2026-04-29
sources: []
related: ["[[character]]", "[[jobs-and-logistics]]", "[[character-schedule]]", "[[worker-wages-and-performance]]", "[[tool-storage]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents: ["character-system-specialist"]
owner_code_path: "Assets/Scripts/Character/CharacterJob/"
depends_on: ["[[character]]"]
depended_on_by: ["[[jobs-and-logistics]]", "[[worker-wages-and-performance]]"]
---

# Character Job

## Summary
Per-character employment state. Holds one or more `JobAssignment` entries, ownership flag (is the character the Boss/Owner of the workplace?), and exposes `TakeJob` / `QuitJob` / `ForceAssignJob`. Calls `CharacterSchedule.InjectWorkSchedule` on success. Uses `DoesScheduleOverlap` to reject conflicts unless forced.

## Responsibilities
- Holding assignments (`JobAssignment` dictionary).
- Time safeguard: `DoesScheduleOverlap` blocks double-booking.
- Injecting work slots into the schedule.
- Force-assign path for community leaders (bypasses consent).

## Key classes / files
- `Assets/Scripts/Character/CharacterJob/CharacterJob.cs`.

## Public API (selected)
- `character.CharacterJob.CanPunchOut() : (bool canPunchOut, string reasonIfBlocked)` — server-authoritative gate. Returns `(false, reason)` when the worker still carries items stamped with any active workplace's `BuildingId` (unreturned tools). Aggregates across all `_activeJobs` so a multi-job worker is gated by tools from any of their workplaces. Called by `CharacterSchedule.EvaluateSchedule` on Work→non-Work transitions. See [[tool-storage]].
- `character.CharacterJob.TakeJob(job, building)` / `QuitJob(job)` / `ForceAssignJob(...)`. **`QuitJob` auto-returns tools** owned by the leaving workplace before clearing the assignment, via `TryAutoReturnTools` (server-side). If storage is unreachable, `OwnerBuildingId` is cleared manually so the worker isn't permanently gated.
- `character.CharacterJob.HasJob` / `IsOwner` / `Workplace` / `CurrentJob`.
- `Job.ExecuteIntervalSeconds` (virtual, default `0.1f`) — per-job execute cadence consumed by `BTAction_Work.HandleWorking`. Heavy-planning subclasses override to throttle their `Job.Execute` calls below the BT tick rate (`JobLogisticsManager` and `JobHarvester` override to `0.3f` = 3.3 Hz). The BT itself still ticks at 10 Hz so combat reaction / schedule transitions stay responsive. **`UnityEngine.Time.time` must be fully-qualified** in the call site to avoid the `MWI.Time` namespace clash. See [[performance-conventions]] Pattern 5 for the canonical shape.
- `character.CharacterJob.OwnedBuilding` — the first `CommercialBuilding` in the world registry where this character is listed as an owner. Derived from the replicated `Room._ownerIds` NetworkList via `Room.IsOwner(Character)`, so it is consistent on every peer with no cached field to go stale.
- `character.CharacterJob.GetInteractionOptions(interactor)` — `IInteractionProvider` hook. When this character owns a `CommercialBuilding` with vacant jobs, emits one "Apply for {JobTitle}" entry per vacancy, disabled-with-reason when the interactor already has a job.
- `character.CharacterJob.RequestJobApplicationServerRpc(ownerNetId, jobStableIndex)` — client-routed path for hold-E clicks. Server re-validates ownership, index range, and `!job.IsAssigned` before constructing `InteractionAskForJob`. `jobStableIndex` is the index in the full `CommercialBuilding.Jobs` list (stable), NOT in the volatile `GetAvailableJobs()` subset.

## Open questions
- [ ] Maximum concurrent jobs per character — unlimited or capped?
- [ ] Ownership cascade — if a Boss quits their own building, what happens?

## Change log
- 2026-04-30 — Section B "Manage Hiring..." menu entry now gated on `!workplace.HasManagementFurniture`. Entry only appears as a fallback when no in-world `ManagementFurniture` desk is wired. See [[help-wanted-and-hiring]] Plan 2.5. — claude
- 2026-04-29 — Added `CanPunchOut()` gate + `QuitJob` auto-return for the [[tool-storage]] primitive (Plan 1 of Farmer rollout). Gate iterates `_activeJobs` and aggregates unreturned tools across all workplaces. `QuitJob` is scoped to the leaving workplace only — tools owned by other concurrent workplaces stay stamped. Storage-unreachable fallback clears `OwnerBuildingId` so workers aren't permanently gated. — claude
- 2026-04-27 — **Performance pass: `Job.ExecuteIntervalSeconds` cadence stagger (Tier 3 Cₐ)**. New `virtual float Job.ExecuteIntervalSeconds => 0.1f` on the base class. `JobLogisticsManager` and `JobHarvester` override to `0.3f` (3.3 Hz). `BTAction_Work.HandleWorking` tracks `_lastExecuteTime` per-NPC (instance field, reset in `OnEnter`) and only calls `jobInfo.Work()` when interval elapsed. BT itself unchanged (still 10 Hz). Heavy-job Execute call rate dropped: LogisticsManager 40/sec → 13/sec; Harvester 20/sec → 7/sec. Pairs with [[building-logistics-manager]] dirty-flag gating — the throttled call usually finds the dispatcher clean and skips. — claude
- 2026-04-24 — `CharacterJob` now implements `IInteractionProvider` for the "Apply for {JobTitle}" hold-E entries; `RequestJobApplicationServerRpc` added for client-routed clicks. `OwnedBuilding` refactored from a cached private field to a derived registry scan over `Room._ownerIds` (same class of fix as mentorship's `CurrentMentorNetId`: avoids stale client-side state). `IsOwner` now derives from `OwnedBuilding != null`. — claude
- 2026-04-22 — `JobAssignment` extended with wage fields (`Currency`, `PieceRate`, `MinimumShiftWage`, `FixedShiftWage`) + `SetWage` mutator; `TakeJob` now seeds defaults via `WageSystemService`; `JobAssignmentSaveEntry` round-trips wage data. See [[worker-wages-and-performance]] — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[performance-conventions]] — Pattern 5 (cadence stagger) was extracted from this system.
- [[optimisation-backlog]] — Tier 3 Cₐ measurements.
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) §1.
- [.agent/skills/wage-system/SKILL.md](../../.agent/skills/wage-system/SKILL.md) — wage seeding + per-assignment overrides.
- [docs/superpowers/specs/2026-04-23-ask-mentorship-and-job-interactions-design.md](../../docs/superpowers/specs/2026-04-23-ask-mentorship-and-job-interactions-design.md) — hold-E menu design spec.
- [wiki/systems/worker-wages-and-performance.md](worker-wages-and-performance.md).
- [[jobs-and-logistics]] parent.
