---
type: system
title: "Character Party (component)"
tags: [character, party, tier-2, stub]
created: 2026-04-19
updated: 2026-04-26
sources: []
related: ["[[party]]", "[[character]]", "[[building-interior]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: character-social-architect
owner_code_path: "Assets/Scripts/Character/CharacterParty/"
depends_on: ["[[character]]", "[[network]]"]
depended_on_by: ["[[party]]"]
---

# Character Party (component)

## Summary
The `CharacterSystem` component that lives on every character and is the per-character face of [[party]]. Holds three `NetworkVariable`s (`_networkPartyId`, `_networkPartyState`, `_networkFollowMode`), fires events (`OnJoinedParty`, `OnLeftParty`, ...) on all clients, and mediates between `PartyData` (in the registry) and the character.

## Responsibilities
- Sync party state to clients via NetworkVariables.
- Fire client events through server-fire-plus-ClientRpc pattern.
- Expose `IsLeader`, `IsInParty`, `CurrentParty` accessors.
- Issue invitations / accept / decline via [[social]] `CharacterInvitation` pipeline.

## Key classes / files
- [CharacterParty.cs](../../Assets/Scripts/Character/CharacterParty/CharacterParty.cs).

## See parent
This is mostly documented in [[party]]. This page exists as a link target for wikilinks from other systems.

## Door-follow dispatch (leader-map-change)

When the leader transitions to a new map, each NPC follower is dispatched by `OrderFollowersThroughDoor` (server-side) and by `OnLeaderMapChanged` (per-follower NetworkVariable callback). Both dispatch sites now branch:

- If the connecting door is a `BuildingInteriorDoor` → queue `[[character|CharacterEnterBuildingAction]](member, building)` on the follower.
- Otherwise (portal / gate / outdoor↔outdoor) → run `PortalFollowRoutine`, a small dedicated coroutine inside this class that mirrors the action base class's walk loop but stays inlined here because portal-following has no other consumer.

Both sites also call `StopPortalFollow()` alongside `ClearFollowState()` before dispatching to handle rapid leader-map oscillation.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-04-26 — door-follow refactor: building branch now delegates to CharacterEnterBuildingAction; portal branch (outdoor↔outdoor / gates) kept as PortalFollowRoutine — claude

## Sources
- [[party]] parent.
- [CharacterParty.cs](../../Assets/Scripts/Character/CharacterParty/CharacterParty.cs)
- [CharacterEnterBuildingAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterEnterBuildingAction.cs)
- [.agent/skills/party-system/SKILL.md](../../.agent/skills/party-system/SKILL.md)
