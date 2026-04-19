---
type: system
title: "Combat Battle Manager"
tags: [combat, battle, orchestration, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[combat]]", "[[network]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: combat-gameplay-architect
owner_code_path: "Assets/Scripts/BattleManager/"
depends_on: ["[[combat]]"]
depended_on_by: ["[[combat]]"]
---

# Combat Battle Manager

## Summary
`NetworkBehaviour` that orchestrates a single battle. Owns two `BattleTeam`s, delegates spatial grouping to `CombatEngagementCoordinator`, physical zone to `BattleZoneController`, and sets the pace via `PerformBattleTick`. Wraps `LeaveBattle` in try/catch on teardown to isolate UI exceptions; unsubscribes all events in `OnDestroy`.

## Key classes / files
- [BattleManager.cs](../../Assets/Scripts/BattleManager/BattleManager.cs)
- `BattleTeam.cs`, `BattleZoneController.cs`, `BattleZoneOutline.cs`.

## See parent
Full documentation is in [[combat]]. This page is a link target.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[combat]].
