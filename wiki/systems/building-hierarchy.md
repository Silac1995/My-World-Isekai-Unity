---
type: system
title: "Building Hierarchy"
tags: [building, zone, room, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[building]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[building]]"]
depended_on_by: ["[[building]]"]
---

# Building Hierarchy

## Summary
Four-level inheritance chain: `Zone` → `Room` → `ComplexRoom` → `Building`. `Zone` tracks presence via trigger collider. `Room` adds owners/residents + `FurnitureGrid`. `ComplexRoom` is a multi-room container. `Building` adds state machine (`BuildingState`), delivery zone, interior link.

## See parent
Full details in [[building]].

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[building]] + [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md).
