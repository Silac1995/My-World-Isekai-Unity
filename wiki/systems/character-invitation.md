---
type: system
title: "Character Invitation"
tags: [character, social, invitation, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[social]]", "[[party]]", "[[character-interaction]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: character-social-architect
owner_code_path: "Assets/Scripts/Character/CharacterInvitation/"
depends_on: ["[[character]]", "[[social]]"]
depended_on_by: ["[[party]]", "[[social]]"]
---

# Character Invitation

## Summary
Template-method base class for initiated exchanges that need explicit consent: party invites, trade requests, lesson offers. Flows through `InteractionInvitation` as an `ICharacterInteractionAction`, so accept/decline UI is uniform and the BT + dialogue lock apply the same way.

## Responsibilities
- Expose `Offer(other, payload)` template.
- Typed subclasses per invite kind (party, trade, lesson, marriage, ...).
- Route accept/decline back to the initiator.
- Participate in [[character-relation]] deltas on outcome.

## Key classes / files
- `Assets/Scripts/Character/CharacterInvitation/CharacterInvitation.cs`.

## Open questions
- [ ] Full list of invitation subclasses — enumerate in expansion.
- [ ] Server vs client authority for decline paths — confirm.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/character_invitation/SKILL.md](../../.agent/skills/character_invitation/SKILL.md)
- [[social]] parent.
