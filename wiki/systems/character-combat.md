---
type: system
title: "Character Combat (component)"
tags: [character, combat, tier-2, stub]
created: 2026-04-19
updated: 2026-05-17
sources: []
related: ["[[combat]]", "[[character]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: combat-gameplay-architect
owner_code_path: "Assets/Scripts/Character/CharacterCombat/"
depends_on: ["[[combat]]", "[[character]]"]
depended_on_by: ["[[combat]]"]
---

# Character Combat (component)

## Summary
Per-character combat state: combat mode, planned action/target, weapon, attack execution, initiative integration. Link target for wikilinks; full documentation in [[combat]].

## Key classes / files
- [CharacterCombat.cs](../../Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs)
- See `CombatStyleAttack`, `CombatStyleExpertise`, `CombatTacticalPacer`, `Projectile` in the same folder.

## See parent
Full documentation lives in [[combat]] and its children [[combat-damage]] / [[combat-abilities]].

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-05-17 — Combat action bar additions: new `OnInitiativeChanged` (float pct 0..1) + `OnActionIntentCleared` events. New helper methods `TryQueueReload()` / `TryQueueSwapWeapon()` / `TryQueueUseItem(consumable, target)` that validate state and route through `CharacterActions.ExecuteAction`. `OnActionIntentCleared` fires from `ClearActionIntent`, `LeaveBattle`, and `ForceExitCombatMode` (every path that nulls `PlannedAction`). See [[combat]] change log + [[2026-05-17-combat-action-bar]] plan. — claude
- 2026-05-17 — `SetActionIntent` gained optional `string actionName = null` parameter; new `PlannedActionName` property exposes the human-readable label ("Melee Attack" / "Ranged Attack" / future ability + item names). Cleared in `ClearActionIntent` / `LeaveBattle` / `ForceExitCombatMode` alongside `PlannedAction`. Drives `UI_CombatQueuedLabel` so the queued chip reads "Queued: Melee Attack → Lumi" instead of the generic "Queued: Action → Lumi" placeholder. — claude

## Sources
- [[combat]].
