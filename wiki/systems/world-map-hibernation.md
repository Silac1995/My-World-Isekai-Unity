---
type: system
title: "World Map Hibernation"
tags: [world, hibernation, save-load, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[world]]", "[[world-macro-simulation]]", "[[save-load]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: world-system-specialist
owner_code_path: "Assets/Scripts/World/MapSystem/"
depends_on: ["[[world]]", "[[save-load]]"]
depended_on_by: ["[[world]]"]
---

# World Map Hibernation

## Summary
The lifecycle mechanism that flips a map between **Active** (player present) and **Hibernating** (serialized, all prefabs despawned). `MapController` tracks active players; on reaching zero, serializes every NPC to `HibernatedNPCData`, items to `HibernatedItemData`, resource pools, and despawns NetworkObjects. First player re-entry triggers [[world-macro-simulation]] and respawn.

## See parent
Full flow in [[world]]. This is a link target.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[world]] §2.
