---
type: system
title: "AI Conditions"
tags: [ai, bt, conditions, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[ai]]", "[[ai-behaviour-tree]]", "[[kevin]]"]
status: stable
confidence: medium
primary_agent: npc-ai-specialist
owner_code_path: "Assets/Scripts/AI/Conditions/"
depends_on: ["[[ai-behaviour-tree]]"]
depended_on_by: ["[[ai-behaviour-tree]]"]
---

# AI Conditions

## Summary
BT condition library. Each `BTCond_*` evaluates a boolean and lets / blocks its branch of the priority selector. See [[ai-behaviour-tree]] for the root ordering.

## Full list (from SKILL)
- `BTCond_HasOrder`
- `BTCond_NeedsToPunchOut`
- `BTCond_IsInCombat`
- `BTCond_FriendInDanger`
- `BTCond_DetectedEnemy`
- `BTCond_HasScheduledActivity`
- `BTCond_WantsToSocialize`
- `BTCond_HasLegacyBehaviour` (bridge)

## Folder
- `Assets/Scripts/AI/Conditions/`.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/behaviour_tree/SKILL.md](../../.agent/skills/behaviour_tree/SKILL.md) §1.
