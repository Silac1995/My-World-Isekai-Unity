---
type: system
title: "Dialogue Manager"
tags: [dialogue, ui, multiplayer, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[dialogue]]", "[[character]]", "[[network]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: character-social-architect
owner_code_path: "Assets/Scripts/Dialogue/"
depends_on: ["[[dialogue]]"]
depended_on_by: ["[[dialogue]]"]
---

# Dialogue Manager

## Summary
Per-player component. `StartDialogue(DialogueSO, participants)` enters `IsInDialogue = true`, resolves placeholders, drives `CharacterSpeech.SayScripted` per line, waits on input (Space / Left Click) **or** auto-advances 1.5s after bubble typing finishes if no players are in the participant list.

## Inspector testing
- `_currentDialogue`, `_testParticipants` (0-based, mapped to 1-based indices), "Trigger Serialized Dialogue" context menu.

## Open questions
- [ ] Multiplayer advance — any-player-input vs specific-participant-only. Flagged in [[dialogue]] and [[TODO-docs]].

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[dialogue]] §2.
