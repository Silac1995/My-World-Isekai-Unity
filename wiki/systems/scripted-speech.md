---
type: system
title: "Scripted Speech"
tags: [dialogue, speech, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[dialogue]]", "[[character-speech]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: character-social-architect
owner_code_path: "Assets/Scripts/Character/CharacterSpeech/"
depends_on: ["[[dialogue]]", "[[character-speech]]"]
depended_on_by: ["[[dialogue]]"]
---

# Scripted Speech

## Summary
`ScriptedSpeech` component on the speech-bubble prefab, invoked by `CharacterSpeech.SayScripted(text)`. Displays text that **persists until advance** — distinct from ambient bubbles (which auto-timeout).

## Pacing contract
`CharacterSpeech.IsSpeaking` stays true until the bubble finishes typing. [[dialogue-manager]] waits on this before applying the 1.5s auto-advance or registering input.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[dialogue]] §3.
