---
type: system
title: "World Biome & Region"
tags: [world, biome, region, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[world]]", "[[terrain-and-weather]]", "[[jobs-and-logistics]]", "[[kevin]]"]
status: stable
confidence: medium
primary_agent: world-system-specialist
owner_code_path: "Assets/Scripts/World/"
depends_on: ["[[world]]"]
depended_on_by: ["[[jobs-and-logistics]]", "[[terrain-and-weather]]"]
---

# World Biome & Region

## Summary
Subdivides a map into biome-typed regions. `BiomeDefinition` (ScriptableObject) holds per-biome resource lists, yield curves, and climate profile hooks. `BiomeRegion` is the runtime placement of a biome on a specific map. Feeds `JobYieldRegistry` and the macro simulator's offline inventory pass.

## Key classes / files
- `Assets/Scripts/World/BiomeRegion.cs` (conceptual).
- `Assets/Scripts/World/BiomeDefinition.cs` (conceptual ScriptableObject).

## Related (feature branch)
The [[terrain-and-weather]] system places `BiomeRegion.cs` under `Assets/Scripts/Weather/` on the feature branch — boundary ownership to reconcile post-merge.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[world]] §3.
- [[terrain-and-weather]] stub.
