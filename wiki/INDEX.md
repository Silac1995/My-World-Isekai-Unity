# Wiki Index

_Last regenerated: 2026-04-19 (wiki bootstrap)_

All pages grouped by entity type. Run `/map` to regenerate this file.

## Systems (78)

See also: [[systems/README]] — grouped by category.

**Tier 1 parents (12)**
- [[combat]] — status: stable · confidence: high
- [[character]] — status: stable · confidence: high
- [[ai]] — status: stable · confidence: high
- [[items]] — status: stable · confidence: high
- [[world]] — status: stable · confidence: high
- [[party]] — status: stable · confidence: high
- [[social]] — status: stable · confidence: medium
- [[building]] — status: stable · confidence: high
- [[jobs-and-logistics]] — status: stable · confidence: high
- [[shops]] — status: stable · confidence: high
- [[dialogue]] — status: stable · confidence: high
- [[terrain-and-weather]] — status: wip · confidence: low (post-merge stub)

**Tier 1 children (60)**
- character: [[character-stats]], [[character-needs]], [[character-movement]], [[character-relation]], [[character-interaction]], [[character-equipment]], [[character-mentorship]], [[character-skills]], [[character-traits]], [[character-bio]], [[character-progression]], [[character-profile]], [[character-book-knowledge]], [[character-blueprints]], [[character-archetype]] (stub), [[character-terrain]] (stub), [[character-schedule]], [[character-job]], [[character-party]], [[character-invitation]], [[character-community]], [[character-locations]], [[character-speech]], [[character-combat]], [[character-animal]]
- combat: [[combat-battle-manager]], [[combat-engagement]], [[combat-ai-logic]], [[combat-damage]], [[combat-abilities]], [[combat-status-effect]], [[combat-circle-indicators]], [[combat-styles]]
- ai: [[ai-behaviour-tree]], [[ai-goap]], [[ai-actions]], [[ai-conditions]], [[ai-pathing]], [[ai-navmesh]], [[ai-obstacle-avoidance]], [[ai-player-nav-switch]]
- items: [[item-data]], [[item-instance]], [[world-items]], [[inventory]], [[keys-and-locks]]
- world: [[world-map-hibernation]], [[world-macro-simulation]], [[world-community]], [[world-biome-region]], [[world-offset-allocation]], [[world-map-transitions]]
- building: [[building-hierarchy]], [[building-state]], [[furniture-grid]], [[commercial-building]], [[building-interior]], [[building-placement-manager]]
- jobs: [[job-employment]], [[job-roles]], [[building-logistics-manager]], [[building-task-manager]], [[order-types]], [[virtual-supply]], [[crafting-loop]]
- shops: [[shop-building]], [[shop-queue]], [[shop-vendor]], [[shop-customer-ai]]
- dialogue: [[dialogue-data]], [[dialogue-manager]], [[scripted-speech]]
- social: [[interaction-exchanges]]

**Tier 2 stubs (4)**
- [[save-load]] — status: stable · confidence: high
- [[network]] — status: stable · confidence: medium
- [[visuals]] — status: wip · confidence: medium
- [[player-ui]] — status: stable · confidence: medium

**Tier 3 aggregated (1)**
- [[engine-plumbing]] — status: stable · confidence: medium · covers ~21 subsystems

## People (1)
- [[kevin]] — solo developer, project owner.

## Concepts (0)
_(empty — populate via `/ingest` as design discussions surface)_

## Projects (0)
_(empty — Spine 2D migration should get its own project page; see memory `project_spine2d_migration`)_

## Decisions / ADRs (1)
- [[adr-0001-living-world-hierarchy-refactor]] — Region → { MapController, WildernessZone, WeatherFront } (accepted 2026-04-21).

## Gotchas (4)
- [[dont-clone-prefabs-with-networkobject-for-visuals]] — Cloning a prefab with `NetworkObject` for visual-only purposes silently breaks on clients.
- [[furnituremanager-replace-style-rescan]] — FurnitureManager rescan flow caveat.
- [[host-progressive-freeze-debug-log-spam]] — Ungated `Debug.Log` calls in hot paths cause progressive host freeze on Windows.
- [[static-registry-late-joiner-race]] — Static registries (`TerrainTypeRegistry`, `CropRegistry`, …) are uninitialised on joining clients because `LaunchSequence` is host-only — fix is lazy auto-init in `Get()`.

## Meetings (0)
_(empty)_

## References (0)
_(empty)_

## Mechanics (0)
_(empty — promote from wiki/systems/ when individual mechanics grow beyond architecture)_

## Content (0)
_(empty — concrete characters, items, levels, enemies)_

## Pipelines (0)
_(empty — build pipeline, CI, asset pipeline — create as needed)_

## Backlogs
- [[TODO-post-merge]] — 3 pages blocked on feature branch.
- [[TODO-skills]] — 23 systems missing SKILL.md.
- [[TODO-docs]] — 3 missing design docs (NETWORK_ARCHITECTURE.md, pricing model, dialogue MP semantics).

## Orphans

Pages not listed in a section README — none so far (every page is indexed in `systems/README.md` or this index).

## Malformed

None — every page has the required frontmatter (`type`, `title`, `tags`, `created`, `updated`, `sources`, `related`, `status`, `confidence`).
