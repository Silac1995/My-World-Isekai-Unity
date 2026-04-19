---
type: system
title: "Item Data"
tags: [items, data, scriptable-object, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[items]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: item-inventory-specialist
owner_code_path: "Assets/Resources/Data/Item/"
depends_on: []
depended_on_by: ["[[items]]"]
---

# Item Data

## Summary
ScriptableObject hierarchy defining static item data. Base `ItemSO` holds name, description, icon, prefab, crafting recipe, `_tier`, and `SpriteLibraryAsset` parent. Subtypes add specialty fields: `WeaponSO` (damage type, max durability/sharpness/magazine), `KeySO` (lock ID), `MiscSO`, `BagSO`. **Never** store mutable state on `ItemSO`.

## Key classes / files
- `Assets/Resources/Data/Item/ItemSO.cs` (base).
- Subclasses: `WeaponSO`, `KeySO`, `MiscSO`, `BagSO`, ...

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[items]] §1.
