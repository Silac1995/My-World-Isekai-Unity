---
type: system
title: "Crafting Loop"
tags: [crafting, jobs, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[jobs-and-logistics]]", "[[building]]", "[[items]]", "[[character-skills]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[jobs-and-logistics]]"]
depended_on_by: ["[[jobs-and-logistics]]"]
---

# Crafting Loop

## Summary
`CraftingBuilding : CommercialBuilding` scans its `ComplexRoom`s for `CraftingStation`s and publishes a list of craftable items (`GetCraftableItems()`). `JobCrafter` requires a `SkillSO` at a minimum tier; it's **demand-driven** — wakes up only on an active `CraftingOrder`. Uses `BTAction_PerformCraft` to find the right station, play the animation, produce the `ItemInstance`.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) §4.
