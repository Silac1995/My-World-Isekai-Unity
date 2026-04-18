---
type: system
title: "Terrain & Weather"
tags: [terrain, weather, vegetation, biome, tier-1, stub]
created: 2026-04-18
updated: 2026-04-18
sources: []
related:
  - "[[world]]"
  - "[[ai]]"
  - "[[character]]"
  - "[[kevin]]"
status: wip
confidence: low
primary_agent: world-system-specialist
secondary_agents:
  - character-system-specialist
owner_code_path: "Assets/Scripts/Terrain/"
depends_on:
  - "[[world]]"
  - "[[save-load]]"
depended_on_by:
  - "[[world]]"
  - "[[ai]]"
---

# Terrain & Weather

> **⚠ STUB PAGE.** The code for this system lives on `feature/character-archetype-system` (folders `Assets/Scripts/Terrain/`, `Assets/Scripts/Weather/`, `Assets/Scripts/Character/CharacterTerrain/`, `Assets/Scripts/Audio/`). None of it has landed on `multiplayyer` yet. This page exists so sibling pages can wikilink to it; it will be filled out fully after the feature branch merges. Tracked in [[TODO-post-merge]].

## Summary
Terrain is a per-cell grid (`TerrainCellGrid`, `TerrainCell`, `TerrainPatch`) with typed surfaces (`TerrainType`, `TerrainTypeRegistry`) that drive footstep audio, visual transitions, and vegetation growth. Weather is event-driven via `WeatherFront` and `WeatherType` objects, with per-biome climate profiles (`BiomeClimateProfile`) and a global wind controller. Vegetation has growth stages and drought-death via `VegetationGrowthSystem`. Footsteps resolve at runtime via `FootstepAudioResolver` reading `FootstepAudioProfile` ScriptableObjects.

## Purpose (provisional)
Ground the living world in visible environmental change. Biomes, terrain type, weather, and vegetation are all persistent inputs into macro-simulation and real-time gameplay (footsteps, visibility, resource yields). When the player returns to a map, they should see the season and weather progress the world actually experienced.

## Responsibilities (provisional)
- Per-cell terrain type + transition rules (`TerrainTransitionRule`).
- Terrain cell save data round-trip (`TerrainCellSaveData`).
- Weather fronts moving across the map (`WeatherFront`, `WeatherFrontSnapshot`).
- Global wind driving ambient motion (`GlobalWindController`).
- Biome-typed climate (`BiomeClimateProfile`, `BiomeRegion`) — boundary with [[world]].
- Vegetation growth stages + drought death (`VegetationGrowthSystem`).
- Terrain weather processor for per-cell humidity/temperature updates (`TerrainWeatherProcessor`).
- Character-side footstep effects and visual terrain interaction (`CharacterTerrainEffects`, `FootstepAudioResolver`, `FootstepAudioProfile`).

**Non-responsibilities** (provisional):
- Does **not** own biome definition data — lives in [[world]] (`BiomeDefinition`, `BiomeRegion`).
- Does **not** own map hibernation — see [[world]].
- Does **not** own NPC schedule reactions to weather — see [[ai]].

## Key classes / files (from feature branch — not present on this branch)

Terrain:
- `Assets/Scripts/Terrain/TerrainCell.cs`
- `Assets/Scripts/Terrain/TerrainCellGrid.cs`
- `Assets/Scripts/Terrain/TerrainCellSaveData.cs`
- `Assets/Scripts/Terrain/TerrainPatch.cs`
- `Assets/Scripts/Terrain/TerrainType.cs`
- `Assets/Scripts/Terrain/TerrainTypeRegistry.cs`
- `Assets/Scripts/Terrain/TerrainTransitionRule.cs`
- `Assets/Scripts/Terrain/TerrainWeatherProcessor.cs`
- `Assets/Scripts/Terrain/VegetationGrowthSystem.cs`

Weather:
- `Assets/Scripts/Weather/WeatherType.cs`
- `Assets/Scripts/Weather/WeatherFront.cs`
- `Assets/Scripts/Weather/WeatherFrontSnapshot.cs`
- `Assets/Scripts/Weather/GlobalWindController.cs`
- `Assets/Scripts/Weather/BiomeClimateProfile.cs`
- `Assets/Scripts/Weather/BiomeRegion.cs`

Character-side:
- `Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs`
- `Assets/Scripts/Character/CharacterTerrain/FootstepAudioResolver.cs`
- `Assets/Scripts/Audio/FootstepAudioProfile.cs`

## Data flow (provisional)

```
TerrainCellGrid owns a 2D grid of TerrainCell
       │
       ├── Each cell holds a TerrainType (sand, grass, stone, snow, ...)
       ├── Transitions interpolate visually via TerrainTransitionRule
       └── Cells serialize via TerrainCellSaveData
                                │
                                ▼
                   MapSaveData — round-trips through hibernation

WeatherFront moves across the map (directed by GlobalWindController)
       │
       ▼
TerrainWeatherProcessor updates per-cell humidity / temperature
       │
       ▼
VegetationGrowthSystem ticks growth stages or drought-death per cell

Character step event
       │
       ▼
FootstepAudioResolver reads cell.TerrainType
       │
       ▼
Selects an AudioClip from matching FootstepAudioProfile
```

## Dependencies (provisional)

### Upstream
- [[world]] — `BiomeRegion` / `BiomeDefinition` sit between World and this system; `MacroSimulator` reads terrain/weather state.
- [[save-load]] — `TerrainCellSaveData`, `WeatherFrontSnapshot` persist to map save data.

### Downstream
- [[ai]] — weather/terrain conditions may influence schedule decisions (shelter-seeking in storms, etc.) — to verify.
- [[character]] — footstep sounds resolve on character movement.

## State & persistence (provisional)

- `TerrainCellSaveData` per cell — saved in `MapSaveData`.
- `WeatherFrontSnapshot` per active front — saved in `MapSaveData`.
- Vegetation growth stages per cell — saved with terrain.
- All consumed by the `MacroSimulator` for offline catch-up (weather continues during hibernation).

## Known gotchas / edge cases

- _(To be filled in when code lands on `multiplayyer`.)_

## Open questions / TODO

- [ ] **Entire page is a stub.** Code lives on `feature/character-archetype-system`; re-run `/document-system terrain-and-weather` after that branch merges. Tracked in [[TODO-post-merge]].
- [ ] SKILL.md for `terrain-weather` and `character-terrain` exists on the feature branch (referenced in the task context) — pull into Sources after merge.
- [ ] `BiomeRegion` placement — lives under `Assets/Scripts/Weather/` in the feature branch; but `world-system/SKILL.md` lists biome data there too. Clarify ownership split when merging.

## Change log
- 2026-04-18 — Stub created during wiki bootstrap. Confidence **low** because code is on a feature branch not yet merged. — Claude / [[kevin]]

## Sources
- Conversation with [[kevin]] on 2026-04-18 — Q3 answer: stub now, full pass post-merge.
- Recent commits on `feature/character-archetype-system`: `8b763d2 feat(terrain): add VegetationGrowthSystem`, `e1f99bb feat(terrain): add CharacterTerrainEffects, FootstepAudioResolver, FootstepAudioProfile`, `b4b7751 feat(terrain): integrate MacroSimulator terrain catch-up and MapController grid sync`, `437f5d1 feat(terrain): add TerrainTypeRegistry initialization to GameLauncher boot sequence`, `51277db docs: add SKILL.md files for terrain-weather and character-terrain systems`.
- (After merge) `.agent/skills/terrain-weather/SKILL.md`, `.agent/skills/character-terrain/SKILL.md`.
