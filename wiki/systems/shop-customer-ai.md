---
type: system
title: "Shop Customer AI"
tags: [shops, ai, goap, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[shops]]", "[[ai]]", "[[character-needs]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: npc-ai-specialist
secondary_agents: ["building-furniture-specialist"]
owner_code_path: "Assets/Scripts/AI/Behaviours/"
depends_on: ["[[shops]]", "[[ai]]", "[[character-needs]]"]
depended_on_by: ["[[shops]]"]
---

# Shop Customer AI

## Summary
NPCs with a `NeedItem` / `NeedFood` need trigger the shopping chain: pick the nearest shop that sells it → ask `CanServeNow` → if yes walk to free vendor, if no `JoinQueue` + `WaitInQueueBehaviour`. Once called, execute `InteractionBuyItem`.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[shops]] §4.
