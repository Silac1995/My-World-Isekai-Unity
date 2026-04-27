---
type: system
title: "World Map Hibernation"
tags: [world, hibernation, save-load, tier-2, stub]
created: 2026-04-19
updated: 2026-04-27
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

## Persistence note
`MapController._hibernationData` is in-memory only. `SaveManager` now iterates **`MapController.AllControllers`** (not just `ActiveControllers`) and, for each map, sources its `MapSnapshot_{mapId}` from `HibernationData` if hibernating, else from a fresh `SnapshotActiveNPCs`. Without this hibernated pass, NPCs and `WorldItem`s on a map the player just left (e.g. exterior when they walked into a building's interior) were silently dropped on save — buildings survived only because `Hibernate()` flushes them into `MapRegistry.CommunityData`, which is its own `ISaveable`.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-04-27 — fix: `SaveManager` now serializes hibernated maps' `HibernationData` under `MapSnapshot_{mapId}` so NPCs/WorldItems on the player's previous map survive a save-from-interior. Added `MapController.AllControllers` accessor. — claude

## Sources
- [[world]] §2.
