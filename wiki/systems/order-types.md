---
type: system
title: "Order Types"
tags: [jobs, logistics, orders, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[jobs-and-logistics]]", "[[building-logistics-manager]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Jobs/"
depends_on: ["[[jobs-and-logistics]]"]
depended_on_by: ["[[jobs-and-logistics]]"]
---

# Order Types

## Summary
Four order flavors in the logistics pipeline:
- **`BuyOrder`** — inter-building commercial contract (client ↔ supplier).
- **`CraftingOrder`** — internal production request for a `JobCrafter`.
- **`TransportOrder`** — physical delivery via `JobTransporter`.
- **`PendingOrder`** — the to-do list for `GoapAction_PlaceOrder` (physical handshake phase).

## Rules
- `IsPlaced` flag: `BuyOrder` isn't live until `InteractionPlaceOrder` succeeds.
- `RemainingDays` counter triggers expiration → reputation penalty.
- Cancellation cascades to counterparty.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[jobs-and-logistics]] + [[building-logistics-manager]].
