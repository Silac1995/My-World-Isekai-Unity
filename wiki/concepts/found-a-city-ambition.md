---
type: concept
title: "Found a City Ambition"
tags: [ambition, community, city-founding, npc-autonomy]
created: 2026-05-17
updated: 2026-05-17
sources:
  - "[Task_CreateCommunity.cs](../../Assets/Scripts/Character/Ambition/Tasks/Task_CreateCommunity.cs)"
  - "[Task_PromoteCommunity.cs](../../Assets/Scripts/Character/Ambition/Tasks/Task_PromoteCommunity.cs)"
  - "[Ambition_FoundACity.cs](../../Assets/Scripts/Character/Ambition/AmbitionSOs/Ambition_FoundACity.cs)"
related:
  - "[[character-ambition]]"
  - "[[character-community]]"
  - "[[world-community]]"
  - "[[building-grid]]"
  - "[[citizenship]]"
status: draft
confidence: medium
---

# Found a City Ambition

## Summary
**Ambition_FoundACity** is the top-level NPC (or player) ambition for the city-founding
flow. Its quest chain spans the full arc from "found a community" through
"reach the Empire tier", with one explicit AB-construction step in the middle.

```
Ambition_FoundACity
 ├─ Quest_CreateCommunity      (Plan 3 — Task_CreateCommunity)
 ├─ Quest_BuildCapital         (Plan 4 — Task_PlaceBuilding(ABSO) + Task_FinishConstruction)
 ├─ Quest_PromoteCamp          (Plan 3 — Task_PromoteCommunity(Camp))
 ├─ Quest_PromoteVillage       (Plan 3 — Task_PromoteCommunity(Village))
 ├─ Quest_PromoteTown          (Plan 3 — Task_PromoteCommunity(Town))
 ├─ Quest_PromoteCity          (Plan 3 — Task_PromoteCommunity(City))
 ├─ Quest_PromoteKingdom       (Plan 3 — Task_PromoteCommunity(Kingdom))
 └─ Quest_PromoteEmpire        (Plan 3 — Task_PromoteCommunity(Empire))
```

Plan 3 ships the AB-independent task scaffolding (`Task_CreateCommunity`,
`Task_PromoteCommunity`, the `Ambition_FoundACity` AmbitionSO subclass).
Plan 4 ships the AB-coupled tasks and the actual `.asset` files that wire
the chain end-to-end.

## Why the actor-as-anchor design (no `Community` in `AmbitionContext`)

`AmbitionContext.Set<T>` ([Pure/AmbitionContext.cs:36](../../Assets/Scripts/Character/Ambition/Pure/AmbitionContext.cs)) rejects values whose runtime type isn't on
its `IsSerializableValueKind` allow-list. The allow-list includes `Character`,
`ScriptableObject` subclasses, primitives, enums, and `IWorldZone` — but NOT
plain Assembly-CSharp classes like `Community`.

Storing the founded `Community` in context would require extending the allow-list,
which has knock-on serialization consequences (the save layer's `ContextEntryDTO`
switch would need a new arm for Community). Cheaper to skip context altogether
and let downstream tasks read `actor.CharacterCommunity.CurrentCommunity` —
that's the server-side authoritative pointer, set by Plan 1's
`SetCurrentCommunity`, and it's already what `Task_PromoteCommunity` uses.

This mirrors how `Task_FinishConstruction` (Plan 4) will resolve the AB by
scanning `BuildingManager.allBuildings.Where(b => b.BuildingSO == targetSO &&
b.PlacedByCharacterId == actor.CharacterId)` rather than stashing the live
Building in context.

## Why `Task_PromoteCommunity` is passive

Plan 4 owns `Community.TryPromoteLevel()` — the actual tier-up mutator that
checks treasury + required buildings + population + tier requirements. The
ambition task SHOULD NOT duplicate that logic. Instead it acts as a barrier:
the ambition pauses on Quest_PromoteCamp until SOMETHING (the player clicking
a Promote button via the admin console — Plan 5, OR an NPC leader autonomously
calling TryPromoteLevel — Plan 4 BTAction) actually raises the community level.
Once level >= TargetLevel, the task reports Completed and the ambition advances.

This decoupling means the same task definition supports both NPC and player
leadership styles without per-style branches.

## NPC vs. Player completion

- **NPC leader**: BT ticks `Task_CreateCommunity` which calls `CheckAndCreateCommunity()`
  server-side. Task completes the same tick. NPC then walks to a viable AB-placement
  spot (Plan 4's `Task_PlaceBuilding` GOAP wiring) and finishes the AB via the
  cooperative construction loop. Tier-up tasks complete passively as the NPC
  leader reaches required treasury / population.
- **Player leader**: The ambition appears in the quest log. Player clicks a
  (Plan 5) "Create Community" button → `Task_CreateCommunity.Tick` fires → completes.
  Player places the AB via the normal `BuildingPlacementManager` ghost flow →
  `Task_PlaceBuilding` (Plan 4) reports Completed once a matching building exists.
  Tier-up tasks complete when the player hits the Promote button in the admin
  console (Plan 5).

The same tasks; different driver patterns. Rule #22 (player↔NPC parity) holds.

## Open questions / TODO
- *Plan 4 — `Quest_BuildCapital` failure recovery*: if the AB is destroyed
  mid-construction, the task may resurrect via re-Tick; should it cancel and
  retry, or bail out of the ambition entirely?
- *Plan 4 — TryPromoteLevel autonomy*: should the NPC leader autonomously fire
  TryPromoteLevel as soon as criteria are met, or wait for explicit BTAction
  scheduling?
- *Plan Next — variant ambitions*: Ambition_FoundACity vs. Ambition_BuildVillage
  vs. Ambition_BuildEmpire — do we want sub-variants with shorter quest chains?
- *confidence: medium* — Plans 4 and 5 are not yet implemented; the quest-chain
  wiring described here is based on the design spec and Plan 3 implementation only.
  The AB-coupled tasks and `.asset` files do not yet exist.

## Links
- [[character-ambition]] — system page for the ambition system (not yet created; flagged as concern)
- [[character-community]] — system page for the community system
- [[world-community]] — world-side community data
- [[building-grid]] — building placement used by Quest_BuildCapital
- [[citizenship]] — citizenship model that community tier unlocks

## Sources
- [Task_CreateCommunity.cs](../../Assets/Scripts/Character/Ambition/Tasks/Task_CreateCommunity.cs)
- [Task_PromoteCommunity.cs](../../Assets/Scripts/Character/Ambition/Tasks/Task_PromoteCommunity.cs)
- [Ambition_FoundACity.cs](../../Assets/Scripts/Character/Ambition/AmbitionSOs/Ambition_FoundACity.cs)
- [AmbitionContext.cs](../../Assets/Scripts/Character/Ambition/Pure/AmbitionContext.cs) — context serialization allow-list
- [docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md](../../docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md) §`AmbitionSO` chain — design source
- [docs/superpowers/plans/2026-05-17-ambition-found-a-city.md](../../docs/superpowers/plans/2026-05-17-ambition-found-a-city.md) — Plan 3 implementation
