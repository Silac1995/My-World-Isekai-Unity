---
type: system
title: "Character Party (component)"
tags: [character, party, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[party]]", "[[character]]", "[[kevin]]"]
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

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[party]] parent.
