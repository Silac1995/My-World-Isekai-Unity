---
type: system
title: "Building Task Manager"
tags: [building, blackboard, tasks, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[building]]", "[[jobs-and-logistics]]", "[[ai]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[building]]"]
depended_on_by: ["[[jobs-and-logistics]]", "[[ai]]"]
---

# Building Task Manager

## Summary
Blackboard pattern that replaces heavy `Physics.OverlapBox` scans. Resources, furniture, and other task-producing entities **register** `BuildingTask`s (`HarvestResourceTask`, `PickupLooseItemTask`) with the manager. Workers call `TaskManager.ClaimBestTask<T>()` atomically (no race conditions).

## Rule
Every `CommercialBuilding` requires a `BuildingTaskManager`. Missing it = workers idle.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[jobs-and-logistics]] + [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) §3.
