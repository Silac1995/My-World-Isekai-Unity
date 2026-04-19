---
type: system
title: "Commercial Building"
tags: [building, commercial, jobs, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[building]]", "[[jobs-and-logistics]]", "[[shops]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[building]]"]
depended_on_by: ["[[jobs-and-logistics]]", "[[shops]]"]
---

# Commercial Building

## Summary
`Building` subclass adding a **`BuildingTaskManager`** (blackboard for GOAP workers) and a **`BuildingLogisticsManager`** (order queue brain). `InitializeJobs()` instantiates the abstract `Job[]`; `AskForJob(character)` handles volunteer employment (gated by HasOwner or HasCommunityLeader); `GetWorkPosition(character)` returns unique per-InstanceID offset so workers don't stack.

## Key classes / files
- `CommercialBuilding.cs`.
- [[building-task-manager]] — blackboard.
- [[building-logistics-manager]] — order queues.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[building]] + [[jobs-and-logistics]].
