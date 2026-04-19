---
type: system
title: "Building State"
tags: [building, construction, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[building]]", "[[items]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[building]]"]
depended_on_by: ["[[building]]"]
---

# Building State

## Summary
Construction state machine: `Scaffold` → `UnderConstruction` → `Complete` → `Damaged` → `Demolished`. `ContributeMaterial(ItemInstance)` progresses the build; `BuildInstantly()` short-circuits for debug/admin. Transitioning to `Complete` opens the interior via `BuildingInteriorDoor` (lazy-spawn) and registers in [[world]] `MapController` + `CommunityManager`.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[building]] parent.
