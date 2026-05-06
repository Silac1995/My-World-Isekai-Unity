---
type: system
title: "Building State"
tags: [building, construction, tier-2, stub]
created: 2026-04-19
updated: 2026-05-06
sources: []
related: ["[[building]]", "[[items]]", "[[construction]]", "[[kevin]]"]
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
- 2026-05-06 — clarified that the gameplay loop driving `UnderConstruction → Complete` lives in [[construction]]; `Building.Finalize()` is the state-flip-first server method that runs on completion. — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[building]] parent.
- [[construction]] — full architecture for the construction loop.
