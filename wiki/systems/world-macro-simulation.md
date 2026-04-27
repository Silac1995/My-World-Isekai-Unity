---
type: system
title: "World Macro Simulation"
tags: [world, hibernation, macro-sim, tier-2]
created: 2026-04-19
updated: 2026-04-27
sources: []
related:
  - "[[world]]"
  - "[[character-needs]]"
  - "[[jobs-and-logistics]]"
  - "[[save-load]]"
  - "[[world-time-skip]]"
  - "[[adr-0001-living-world-hierarchy-refactor]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: world-system-specialist
secondary_agents:
  - save-persistence-specialist
owner_code_path: "Assets/Scripts/World/MapSystem/"
depends_on:
  - "[[world]]"
  - "[[save-load]]"
depended_on_by:
  - "[[world]]"
  - "[[world-time-skip]]"
---

# World Macro Simulation

> **Phase 1 extension complete — see [[adr-0001-living-world-hierarchy-refactor]].**
> The catch-up loop now has a 6th step — **Zone Motion** — in `MacroSimulator.TickZoneMotion(daysSinceLastTick)`. It iterates all `WildernessZone` instances, sums their `IZoneMotionStrategy` daily deltas, clamps the proposed position against `WorldSettingsData.MapMinSeparation` (zone-vs-zone overlap prevention), and applies. Called from `SimulateCatchUp` after step 5 (City Growth) so map wake-up catches up accumulated offline drift in one pass. All zones default to `StaticMotionStrategy` in Phase 1, so this step is a no-op until later phases introduce reactive strategies. Active-play daily ticking is deferred — the wake-up catch-up covers observable behavior because static zones never drift. Sections below describe the **post-refactor** state.

## Summary
When a hibernating map wakes up, the `MacroSimulator` runs a catch-up pass over the elapsed delta (`CurrentTime - HibernationTime`) in strict order: (1) resource pool regen, (2) biome-driven inventory yields via `JobYieldRegistry`, (3) needs decay per character, (4) schedule snap (skip to end of current scheduled task, snap position). It's pure math over serialized data — no Unity Update loops, no NavMesh, no NetworkObject. Everything consumes `TimeManager.CurrentDay` + `CurrentTime01`, never `Time.time`.

## Purpose
Give the player a living world that evolves while they're away, without paying the cost of keeping it simulated. The catch-up is **cheap** (arithmetic over frozen snapshots) and **deterministic** (same inputs → same outputs, always).

## Responsibilities
- Computing elapsed delta in in-game days.
- Regenerating `CommunityData.ResourcePoolEntries` per biome rules.
- Adding offline inventory yields for biome-driven jobs (hunter returns with meat, harvester with logs).
- Applying needs decay per character (`CharacterNeeds.ComputeOfflineDecay`).
- Offline city growth — the community leader's `CharacterBlueprints.UnlockedBuildingIds` drives what scaffolds spawn.
- Snapping each NPC's position to the end of their current scheduled task (no path replay).
- Preparing `HibernatedNPCData` for re-instantiation.

**Non-responsibilities**:
- Does **not** run physics, NavMesh, or any Unity live systems during hibernation.
- Does **not** re-evaluate GOAP frame by frame — just skips to task end.
- Does **not** write to Inventory directly — yields become virtual stock via [[jobs-and-logistics]] `VirtualResourceSupplier` on wake-up.

## Catch-up order (strict)

```
MacroSimulator.CatchUp(MapSaveData data, TimeManager time)
       │
       ▼
deltaDays = (time.Current - data.LastHibernationTime)
       │
1.  ► Resource Pool Regeneration
           └── for each ResourcePoolEntry in CommunityData:
                  current += regenRate × deltaDays (capped at pool max)
2.  ► Inventory Yields via JobYieldRegistry
           └── for each NPC with biome-driven job (IsBiomeDriven = true):
                  yields = recipe.GetYield(BiomeDefinition, deltaDays)
                  deposit into NPC inventory (logical only, no prefab)
3.  ► Needs Decay
           └── for each NPC: CharacterNeeds.ComputeOfflineDecay(deltaDays)
4.  ► Schedule Snap
           └── for each NPC: find scheduled activity at time.Current,
                  set position to activity target (Blacksmith Forge, home, etc.)
       │
       ▼
Map is ready — spawn real prefabs from updated state, NetworkSpawn
```

### Per-hour entry point (SimulateOneHour)

`MacroSimulator.SimulateOneHour(MapSaveData, currentDay, currentTime01, JobYieldRegistry, previousHour)` is a sibling entry point used by [[world-time-skip]]. It runs the same logical pipeline as `SimulateCatchUp` but at hour-grained resolution: hour-grained steps (`ApplyNeedsDecayHours`, `SnapPositionFromSchedule` — both extracted as helpers shared with `SimulateCatchUp`) run every call; cumulative steps that integrate over `daysPassed` (resource regen, biome yields, zone motion, city growth) are wrapped in a `crossedDayBoundary` branch that fires only when the previous hour was 23 and the current hour is 0. **24× `SimulateOneHour` is intended to be byte-equivalent to 1× `SimulateCatchUp(daysPassed=1.0)` for the same fixture** — any new step added to the macro-sim must be classified as hour-grained or day-grained and placed accordingly, or the equivalence breaks silently.

## Offline city growth

Driven entirely by the community leader's `CharacterBlueprints`:
- Filter `BuildingRegistry` by leader's `UnlockedBuildingIds`.
- Check which listed buildings are missing from the community.
- Spawn scaffold data honoring `CommunityPriority`.

The actual physical scaffolds materialize when the map wakes — at wake time they're already in the right state.

## Dependencies

### Upstream
- [[world]] parent — `MapController.WakeUp` invokes this.
- [[save-load]] — reads `MapSaveData`, `HibernatedNPCData`, `HibernatedItemData`.
- [[character-needs]] — `ComputeOfflineDecay` contract.
- [[jobs-and-logistics]] — `JobYieldRegistry`, `JobYieldRecipe`, biome-driven flag.

### Downstream
- [[world]] — populates the map state before spawn.

## State & persistence

- Reads: `MapSaveData`, `HibernatedNPCData[]`, `HibernatedItemData[]`, `CommunityData.ResourcePoolEntries`, `TimeManager.CurrentDay`/`CurrentTime01`.
- Writes: updated `HibernatedNPCData` inventories/positions/needs, updated resource pools, new scaffold entries.
- All writes happen to the save data **before** respawn; the live Unity layer never sees hibernation state.

## Known gotchas

- **Order matters** — regen before yields, needs decay before schedule snap. Swapping breaks invariants.
- **Pure math, no coroutines** — must run synchronously in one frame on wake.
- **`TimeManager` is the only time source** — using `Time.time` would make solo saves non-reproducible.
- **Biome data is authoritative** — yield formulas must read from `BiomeDefinition`. Hardcoding breaks procedural biomes.
- **Missed day boundaries** — if `OnNewDay` callbacks tick in live systems but were skipped offline, macro-sim must replay them explicitly (check: does it?).
- **Large deltas** — months of offline simulation are expected. Formulas must saturate cleanly (pools cap, needs floor at 0).

## Open questions

- [ ] Does the macro-sim fire `OnNewDay` equivalents for each day in the delta, or does it roll up days into single-pass integrals? Needs code verification.
- [ ] Does macro-sim handle combat deaths that "should have happened" offline? Likely no — skipped.

## Change log
- 2026-04-19 — Initial pass. — Claude / [[kevin]]
- 2026-04-21 — Added pending-extension notice (Zone Motion catch-up step) pointing to [[adr-0001-living-world-hierarchy-refactor]]. — Claude / [[kevin]]
- 2026-04-21 — Zone Motion step 6 implemented in `MacroSimulator.TickZoneMotion`, called from `SimulateCatchUp` on map wake-up. No-op in Phase 1 with default `StaticMotionStrategy`. — Claude / [[kevin]]
- 2026-04-27 — Added SimulateOneHour entry point with day-boundary gating; extracted ApplyNeedsDecayHours and SnapPositionFromSchedule helpers shared with SimulateCatchUp. Used by [[world-time-skip]]. — Claude / [[kevin]]

## Sources
- [.agent/skills/world-system/SKILL.md](../../.agent/skills/world-system/SKILL.md) §2–§3.
- Root [CLAUDE.md](../../CLAUDE.md) — World System & Simulation section.
- [[world]] parent.
