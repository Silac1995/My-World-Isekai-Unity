---
type: system
title: "AI Actions"
tags: [ai, goap, actions, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[ai]]", "[[ai-goap]]", "[[jobs-and-logistics]]", "[[kevin]]"]
status: stable
confidence: medium
primary_agent: npc-ai-specialist
owner_code_path: "Assets/Scripts/AI/Actions/"
depends_on: ["[[ai-goap]]"]
depended_on_by: ["[[ai-goap]]"]
---

# AI Actions

## Summary
The GOAP action library. Concrete `GoapAction` subclasses that do things — move, socialize, place order, load/unload transport, harvest, fight, sleep, eat. Each defines preconditions, effects, cost, and frame-loop execution. SKILL lists 19 total.

## Examples
- `GoapAction_MoveTo`
- `GoapAction_Socialize`
- `GoapAction_PlaceOrder`
- `GoapAction_LoadTransport`, `GoapAction_UnloadTransport`
- `GoapAction_Harvest`, `GoapAction_Deposit`

## Folder
- `Assets/Scripts/AI/Actions/`.

## Open questions
- [ ] Enumerate the full 19. Tracked in [[TODO-skills]].

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/goap/SKILL.md](../../.agent/skills/goap/SKILL.md)
- [[ai]] and [[ai-goap]] parents.
