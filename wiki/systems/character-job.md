---
type: system
title: "Character Job"
tags: [character, jobs, employment, tier-2, stub]
created: 2026-04-19
updated: 2026-04-22
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

## Open questions
- [ ] Maximum concurrent jobs per character — unlimited or capped?
- [ ] Ownership cascade — if a Boss quits their own building, what happens?

## Change log
- 2026-04-22 — `JobAssignment` extended with wage fields (`Currency`, `PieceRate`, `MinimumShiftWage`, `FixedShiftWage`) + `SetWage` mutator; `TakeJob` now seeds defaults via `WageSystemService`; `JobAssignmentSaveEntry` round-trips wage data. See [[worker-wages-and-performance]] — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) §1.
- [.agent/skills/wage-system/SKILL.md](../../.agent/skills/wage-system/SKILL.md) — wage seeding + per-assignment overrides.
- [wiki/systems/worker-wages-and-performance.md](worker-wages-and-performance.md).
- [[jobs-and-logistics]] parent.
