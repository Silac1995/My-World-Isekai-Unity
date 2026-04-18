---
type: system
title: "World Map Transitions"
tags: [world, doors, network, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[world]]", "[[character-movement]]", "[[network]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: world-system-specialist
owner_code_path: "Assets/Scripts/World/MapSystem/"
depends_on: ["[[world]]", "[[character-movement]]"]
depended_on_by: ["[[world]]"]
---

# World Map Transitions

## Summary
Standardized via `MapTransitionDoor` (exterior-to-exterior) and `BuildingInteriorDoor` (exterior-to-interior). Transition flow: interact → `ScreenFadeManager` fades the screen (unscaled time) → `CharacterMapTracker.RequestTransitionServerRpc` → server resolves interior position (lazy-spawn if needed) → `CharacterMovement.ForceWarp` (cross-NavMesh) → `WarpClientRpc` feedback.

## Key rules
- `Warp` vs `ForceWarp` — **must** use `ForceWarp` for cross-NavMesh teleports (y=5000 interiors).
- `CharacterMapTracker.CurrentMapID.Value` is the authoritative "which map am I on".
- `MapController.NotifyPlayerTransition()` handles source/destination hibernation handoff.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[world]] §4.
