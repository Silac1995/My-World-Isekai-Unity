---
type: system
title: "Shop Vendor"
tags: [shops, jobs, tier-2, stub]
created: 2026-04-19
updated: 2026-05-14
sources: []
related: ["[[shops]]", "[[jobs-and-logistics]]", "[[host-only-state-blindspot]]", "[[kevin]]"]
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

## Seat eviction — `ServerTickValidateOccupant`

`Cashier` runs a server-side 1 Hz state machine in two halves: `ServerTickAutoOccupy` (seats a fresh on-shift vendor in range) and `ServerTickValidateOccupant` (evicts the seated vendor when they can no longer vend). Together they prevent the seat from leaking when a vendor enters battle, dies, goes off-shift, has their job reassigned, or walks out of the seat radius. `Release()` handles replication + in-flight transaction abort in one path. See `.agent/skills/shop_system/SKILL.md` "Vendor seat is a symmetric state machine" for the full contract.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-05-14 — Document `ServerTickValidateOccupant` (vendor seat eviction half of the cashier auto-seat state machine). Fixes the "vendor in combat, customer can still open buy panel" leak. — claude

## Sources
- [[shops]] §3.
- [Assets/Scripts/World/Furniture/Cashier.cs](../../Assets/Scripts/World/Furniture/Cashier.cs) — `ServerTickValidateOccupant`.
- [.agent/skills/shop_system/SKILL.md](../../.agent/skills/shop_system/SKILL.md) — symmetric tick contract.
