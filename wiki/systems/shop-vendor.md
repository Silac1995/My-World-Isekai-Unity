---
type: system
title: "Shop Vendor"
tags: [shops, jobs, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[shops]]", "[[jobs-and-logistics]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Jobs/"
depends_on: ["[[shops]]"]
depended_on_by: ["[[shops]]"]
---

# Shop Vendor

## Summary
`JobVendor` is the face of the shop. BT keeps them behind the counter. Constantly scans the queue; when non-empty, calls the next customer via `CallNextCustomer`. Customer walks up, executes `InteractionBuyItem`, money + item transfer server-side.

## Shift end
If the vendor is the **last** one working, `ShopBuilding.ClearQueue()` kicks remaining customers. Vendor then `WorkerEndingShift`.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[shops]] §3.
