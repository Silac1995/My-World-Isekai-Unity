---
type: system
title: "Interaction Exchanges"
tags: [social, dialogue, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[social]]", "[[character-interaction]]", "[[character-relation]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: character-social-architect
owner_code_path: "Assets/Scripts/Character/CharacterInteraction/"
depends_on: ["[[social]]", "[[character-interaction]]"]
depended_on_by: ["[[social]]"]
---

# Interaction Exchanges

## Summary
Turn-taking rules for the dynamic `DialogueSequence` coroutine inside [[character-interaction]]. Speaker/Listener roles reverse each exchange (up to `MAX_EXCHANGES = 6`); wait times are injected strictly between bubbles. If the Speaker is the Player, the sequence pauses (`WaitUntil`) for a player action (Talk / Insult / Greet via 'E' HUD).

## Rules
- Pure dynamic — distinct from [[dialogue]] scripted sequences.
- Works for Player↔NPC and NPC↔NPC.
- `[[character-relation]]` updates at the end of the exchange (bidirectional cross-link per Kevin's Q7).

## Change log
- 2026-04-19 — Stub. Cross-link to [[character-relation]] per Q7. — Claude / [[kevin]]

## Sources
- [.agent/skills/interaction-exchanges/SKILL.md](../../.agent/skills/interaction-exchanges/SKILL.md).
- [.agent/skills/dialogue-system/SKILL.md](../../.agent/skills/dialogue-system/SKILL.md) §5.
