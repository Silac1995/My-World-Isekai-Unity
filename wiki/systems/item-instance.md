---
type: system
title: "Item Instance"
tags: [items, runtime, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[items]]", "[[character-equipment]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: item-inventory-specialist
owner_code_path: "Assets/Scripts/Item/"
depends_on: ["[[items]]"]
depended_on_by: ["[[items]]", "[[character-equipment]]"]
---

# Item Instance

## Summary
Per-owner in-memory wrapper around `ItemSO`. Holds unique state: `Color_Primary`, `Color_Secondary`, `_customizedName`. Subtypes: `WeaponInstance` (sharpness / charge / ammo), `KeyInstance` (runtime `_runtimeLockId`), `BagInstance`, `MiscInstance`. `InitializePrefab` / `InitializeWorldPrefab` injects per-instance colors into visual children tagged `Color_Primary`.

## Key classes / files
- [ItemInstance.cs](../../Assets/Scripts/Item/ItemInstance.cs)
- `WeaponInstance`, `MeleeWeaponInstance`, `ChargingWeaponInstance`, `MagazineWeaponInstance`.
- [KeyInstance.cs](../../Assets/Scripts/Item/KeyInstance.cs).

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[items]] §2.
