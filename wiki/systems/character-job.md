---
type: system
title: "Character Job"
tags: [character, jobs, employment, tier-2, stub]
created: 2026-04-19
updated: 2026-04-24
sources: []
related: ["[[character]]", "[[jobs-and-logistics]]", "[[character-schedule]]", "[[worker-wages-and-performance]]", "[[kevin]]"]
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
- `character.CharacterJob.TakeJob(job, building)` / `QuitJob(job)` / `ForceAssignJob(...)`.
- `character.CharacterJob.HasJob` / `IsOwner` / `Workplace` / `CurrentJob`.
- `character.CharacterJob.OwnedBuilding` — the first `CommercialBuilding` in the world registry where this character is listed as an owner. Derived from the replicated `Room._ownerIds` NetworkList via `Room.IsOwner(Character)`, so it is consistent on every peer with no cached field to go stale.
- `character.CharacterJob.GetInteractionOptions(interactor)` — `IInteractionProvider` hook. When this character owns a `CommercialBuilding` with vacant jobs, emits one "Apply for {JobTitle}" entry per vacancy, disabled-with-reason when the interactor already has a job.
- `character.CharacterJob.RequestJobApplicationServerRpc(ownerNetId, jobStableIndex)` — client-routed path for hold-E clicks. Server re-validates ownership, index range, and `!job.IsAssigned` before constructing `InteractionAskForJob`. `jobStableIndex` is the index in the full `CommercialBuilding.Jobs` list (stable), NOT in the volatile `GetAvailableJobs()` subset.

## Open questions
- [ ] Maximum concurrent jobs per character — unlimited or capped?
- [ ] Ownership cascade — if a Boss quits their own building, what happens?

## Change log
- 2026-04-24 — `CharacterJob` now implements `IInteractionProvider` for the "Apply for {JobTitle}" hold-E entries; `RequestJobApplicationServerRpc` added for client-routed clicks. `OwnedBuilding` refactored from a cached private field to a derived registry scan over `Room._ownerIds` (same class of fix as mentorship's `CurrentMentorNetId`: avoids stale client-side state). `IsOwner` now derives from `OwnedBuilding != null`. — claude
- 2026-04-22 — `JobAssignment` extended with wage fields (`Currency`, `PieceRate`, `MinimumShiftWage`, `FixedShiftWage`) + `SetWage` mutator; `TakeJob` now seeds defaults via `WageSystemService`; `JobAssignmentSaveEntry` round-trips wage data. See [[worker-wages-and-performance]] — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) §1.
- [.agent/skills/wage-system/SKILL.md](../../.agent/skills/wage-system/SKILL.md) — wage seeding + per-assignment overrides.
- [docs/superpowers/specs/2026-04-23-ask-mentorship-and-job-interactions-design.md](../../docs/superpowers/specs/2026-04-23-ask-mentorship-and-job-interactions-design.md) — hold-E menu design spec.
- [wiki/systems/worker-wages-and-performance.md](worker-wages-and-performance.md).
- [[jobs-and-logistics]] parent.
