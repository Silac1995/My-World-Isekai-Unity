---
type: system
title: "Building State"
tags: [building, construction, tier-2, stub]
created: 2026-04-19
updated: 2026-05-08
sources: []
related: ["[[building]]", "[[items]]", "[[construction]]", "[[kevin]]", "[[buildinstantly-pre-start-lifecycle-race]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[building]]"]
depended_on_by: ["[[building]]", "[[construction]]"]
---

# Building State

## Summary
Construction state machine: `Scaffold` → `UnderConstruction` → `Complete` → `Damaged` → `Demolished`. `ContributeMaterial(ItemSO, int)` progresses the server-side ledger; `BuildInstantly()` short-circuits for debug/admin. The actual gameplay loop driving `UnderConstruction → Complete` lives in [[construction]]: a continuous-tick `CharacterAction_FinishConstruction` consumes items from `Building.BuildingZone` and calls `Building.Finalize()` on completion. Transitioning to `Complete` opens the interior via `BuildingInteriorDoor` (lazy-spawn) and registers in [[world]] `MapController` + `CommunityManager`.

## Change log
- 2026-05-08 — `BuildInstantly` defers its state flip via a coroutine that waits for `Start` to wire the `OnValueChanged` subscription, then writes `_currentState.Value = Complete` so `HandleStateChanged` drives the unified post-completion cascade — same code path as `Building.Finalize` from the construction loop. Replaced an earlier same-day fix that duplicated the cascade inline (split logic, fragile ordering). See [[buildinstantly-pre-start-lifecycle-race]]. — claude
- 2026-05-07 — Phase 1 polish: cooperative finalize (placer-only gate dropped), 2D X-Z proximity check, save/load progress restoration through hibernation/snapshot refresh, `_spawnAsComplete` designer checkbox for scene-authored already-built buildings. Full details in [[construction]]. — claude
- 2026-05-06 — clarified that the gameplay loop driving `UnderConstruction → Complete` lives in [[construction]]; `Building.Finalize()` is the state-flip-first server method that runs on completion. — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[building]] parent.
- [[construction]] — full architecture for the construction loop.
