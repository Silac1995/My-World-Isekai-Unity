---
type: system
title: "Shop Queue"
tags: [shops, queue, ai, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[shops]]", "[[ai]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[shops]]"]
depended_on_by: ["[[shops]]"]
---

# Shop Queue

## Summary
Customer FIFO queue. Customers join via `ShopBuilding.JoinQueue(customer)`, switch to `WaitInQueueBehaviour`, and wait to be called. Last vendor leaving at shift end triggers `ClearQueue()` — announces closure and kicks remaining customers.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[shops]] §3–§4.
