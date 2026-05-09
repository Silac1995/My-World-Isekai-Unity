---
type: system
title: "Furniture Grid"
tags: [building, furniture, placement, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[building]]", "[[items]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Furniture/"
depends_on: ["[[building]]"]
depended_on_by: ["[[building]]"]
---

# Furniture Grid

## Summary
Discrete placement grid within a `Room`. `FurnitureGrid` initializes bounds from the room's `BoxCollider`; `FurnitureManager` is the per-room registry. `CanPlace(prefab, cell)` checks occupancy + collision rules; `Place(instance)` commits and serializes. Community-level permissions gate placement via [[building-placement-manager]].

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[building]] parent + [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md).
