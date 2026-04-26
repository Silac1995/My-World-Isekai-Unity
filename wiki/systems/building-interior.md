---
type: system
title: "Building Interior"
tags: [building, interior, world, network, tier-2, stub]
created: 2026-04-19
updated: 2026-04-26
sources: []
related: ["[[building]]", "[[world]]", "[[character-movement]]", "[[character]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents: ["world-system-specialist"]
owner_code_path: "Assets/Scripts/World/MapSystem/"
depends_on: ["[[building]]", "[[world]]"]
depended_on_by: ["[[building]]", "[[character]]"]
---

# Building Interior

## Summary
Interior maps live high in the sky at `y=5000` (or deep underground). `BuildingInteriorDoor` links exterior footprint to interior map; `BuildingInteriorRegistry` lazy-spawns interiors on first entry. Every `Building` generates a `NetworkBuildingId` GUID on spawn so multiple instances of the same shop prefab each bind to a distinct interior map slot.

## Cross-NavMesh rule
Entering an interior requires `CharacterMovement.ForceWarp` — disable agent, teleport, re-enable after 2 frames. `Warp` silently fails.

## Programmatic NPC entry / exit

NPCs can autonomously enter and leave any building's interior via two `CharacterAction` primitives:

- `[[character|CharacterEnterBuildingAction]](actor, Building)` — walks to and triggers the closest `BuildingInteriorDoor`.
- `[[character|CharacterLeaveInteriorAction]](actor)` — walks to and triggers the closest exit `MapTransitionDoor` on the current interior `MapController`.

Both delegate to the existing `door.Interact` → `CharacterMapTransitionAction` chain. The door retains full ownership of lock/key/rattle decisions.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-04-26 — documented programmatic NPC entry / exit via CharacterEnterBuildingAction & CharacterLeaveInteriorAction — claude

## Sources
- [[world]] §4 + [[building]].
- [CharacterEnterBuildingAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterEnterBuildingAction.cs)
- [CharacterLeaveInteriorAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterLeaveInteriorAction.cs)
- [CharacterDoorTraversalAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterDoorTraversalAction.cs)
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md)
