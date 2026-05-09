---
type: system
title: "Building Task Manager"
tags: [building, blackboard, tasks, tier-2, stub]
created: 2026-04-19
updated: 2026-04-29
sources: []
related: ["[[building]]", "[[jobs-and-logistics]]", "[[ai]]", "[[quest-system]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[building]]"]
depended_on_by: ["[[jobs-and-logistics]]", "[[ai]]", "[[quest-system]]"]
---

# Building Task Manager

## Summary
Blackboard pattern that replaces heavy `Physics.OverlapBox` scans. Resources, furniture, and other task-producing entities **register** `BuildingTask`s (`HarvestResourceTask`, `PickupLooseItemTask`) with the manager. Workers call `TaskManager.ClaimBestTask<T>()` atomically (no race conditions).

## Rule
Every `CommercialBuilding` requires a `BuildingTaskManager`. Missing it = workers idle.

## Change log
- 2026-04-29 — Added `FindClaimedTaskByWorker<T>(worker, predicate)` helper that mirrors the in-progress branch of `HasAvailableOrClaimedTask`. Closes a real-world asymmetry between the NPC GOAP claim path (`ClaimBestTask` walks only `_availableTasks`) and the quest-system auto-claim path (`CommercialBuilding.WorkerStartingShift` → `TryAutoClaimExistingQuests` → `worker.CharacterQuestLog.TryClaim` moves the task to `_inProgressTasks` *before* GOAP plans). Symptom: `GoapAction_DestroyHarvestable` / `GoapAction_HarvestResources` IsValid passed (sees in-progress claim) but `Execute`'s `ClaimBestTask` returned null → infinite Idle/Action ping-pong loop. Both GOAP actions now call `FindClaimedTaskByWorker` first, fall back to `ClaimBestTask` only if no pre-existing claim. See [[quest-system]] auto-claim hook + [[farming]]. — claude / [[kevin]]
- 2026-04-24 — Added `Manager` back-reference on `BuildingTask` (set in `RegisterTask`) and two `NotifyTaskExternally{Claimed,Unclaimed}` hooks. Player claims via `IQuest.TryJoin/TryLeave` now move the task between `Available` / `InProgress` buckets, mirroring the NPC `ClaimBestTask` path — fixes "Unknown Worker" rows in the debug HUD and prevents orphaned InProgress entries that blocked re-claim. See [[quest-system]]. — claude
- 2026-04-23 — Added `OnTaskRegistered` / `OnTaskClaimed` / `OnTaskUnclaimed` / `OnTaskCompleted` events. `BuildingTask` now implements `MWI.Quests.IQuest` directly — existing `ClaimBestTask<T>` unchanged, returned objects additionally satisfy `IQuest`. Consumed by `CommercialBuilding.PublishQuest` to surface tasks to players via `CharacterQuestLog`. See [[quest-system]]. — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[jobs-and-logistics]] + [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) §3.
