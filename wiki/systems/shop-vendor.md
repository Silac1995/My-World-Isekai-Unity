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

## Seat eviction (forced) — central `Character.AutoLeaveOccupiedFurniture`

Forced seat release on combat / incapacitate / death is centralised on `Character` (see `[[character]]` §3.b and `character_core/SKILL.md`). The cashier no longer polls for these conditions.

> **Deferred refactor:** the existing `Cashier.ServerTickAutoOccupy` proximity auto-seat is architecturally wrong — vendors should occupy the cashier through a `CharacterAction` queued from `JobVendor` (and from player E-press), not by passively wandering within range. The refactor will also introduce a leave action and movement/action lockout while occupied. Tracking: separate session.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-05-14 — Document `ServerTickValidateOccupant` (vendor seat eviction half of the cashier auto-seat state machine). Fixes the "vendor in combat, customer can still open buy panel" leak. — claude
- 2026-05-14 (later) — Reverted `ServerTickValidateOccupant`. Validator's radius check fired during shopping (vendor's transform oscillated within the auto-seat radius), aborting active transactions every second. Forced eviction now goes through central `Character.AutoLeaveOccupiedFurniture` only. Auto-seat-by-proximity itself is architecturally wrong (vendor was occupying without ever interacting with the cashier) and is deferred to a `CharacterAction`-driven refactor. — claude

## Sources
- [[shops]] §3.
- [Assets/Scripts/World/Furniture/Cashier.cs](../../Assets/Scripts/World/Furniture/Cashier.cs) — `ServerTickValidateOccupant`.
- [.agent/skills/shop_system/SKILL.md](../../.agent/skills/shop_system/SKILL.md) — symmetric tick contract.
