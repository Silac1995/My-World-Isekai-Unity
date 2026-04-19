---
type: system
title: "Building Interior"
tags: [building, interior, world, network, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[building]]", "[[world]]", "[[character-movement]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents: ["world-system-specialist"]
owner_code_path: "Assets/Scripts/World/MapSystem/"
depends_on: ["[[building]]", "[[world]]"]
depended_on_by: ["[[building]]"]
---

# Building Interior

## Summary
Interior maps live high in the sky at `y=5000` (or deep underground). `BuildingInteriorDoor` links exterior footprint to interior map; `BuildingInteriorRegistry` lazy-spawns interiors on first entry. Every `Building` generates a `NetworkBuildingId` GUID on spawn so multiple instances of the same shop prefab each bind to a distinct interior map slot.

## Cross-NavMesh rule
Entering an interior requires `CharacterMovement.ForceWarp` — disable agent, teleport, re-enable after 2 frames. `Warp` silently fails.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[world]] §4 + [[building]].
