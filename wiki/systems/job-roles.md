---
type: system
title: "Job Roles"
tags: [jobs, roles, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[jobs-and-logistics]]", "[[shops]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Jobs/"
depends_on: ["[[jobs-and-logistics]]"]
depended_on_by: ["[[jobs-and-logistics]]"]
---

# Job Roles

## Summary
Concrete `Job` subclasses. Each overrides `Execute()` for its role-specific logic:
- `JobVendor` — shop counter, customer queue.
- `JobCrafter` — demand-driven production at a `CraftingStation`.
- `JobTransporter` — physical delivery between buildings.
- `JobLogisticsManager` — restock manager (shops, crafting suppliers).
- `JobHarvester` — gathering raw resources.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md).
