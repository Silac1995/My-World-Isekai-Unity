---
type: system
title: "Character Speech"
tags: [character, speech, dialogue, visuals, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[dialogue]]", "[[social]]", "[[visuals]]", "[[kevin]]"]
status: stable
confidence: medium
primary_agent: character-system-specialist
owner_code_path: "Assets/Scripts/Character/CharacterSpeech/"
depends_on: ["[[character]]"]
depended_on_by: ["[[dialogue]]", "[[social]]"]
---

# Character Speech

## Summary
Speech bubble rendering and pacing. Two modes: `SayAmbient` for dynamic NPC-to-NPC exchanges (auto-timeout) and `SayScripted` via `ScriptedSpeech` for `DialogueSO` sequences (persist-until-advance). `IsSpeaking` is the signal [[character-interaction]] and [[dialogue]] wait on before advancing.

## Responsibilities
- Instantiating / updating the speech bubble prefab.
- Managing typing speed, timeout (ambient) vs persistence (scripted).
- Exposing `IsSpeaking` for pacing.

## Key classes / files
- `Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs`.
- `ScriptedSpeech` (component on the bubble prefab).

## Open questions
- [ ] Networking — how do speech bubbles appear for observers? Confirm NetworkVariable vs ClientRpc.
- [ ] No SKILL.md — tracked in [[TODO-skills]].

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/dialogue-system/SKILL.md](../../.agent/skills/dialogue-system/SKILL.md) §3.
- [.agent/skills/social_system/SKILL.md](../../.agent/skills/social_system/SKILL.md) §1.
