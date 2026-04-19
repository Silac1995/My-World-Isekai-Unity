# Systems Index

Architectural encyclopedia of every game system. Parents are listed in bold;
children are indented under them. Tier-2 and Tier-3 pages are listed separately.
Operational procedures live in `.agent/skills/<system>/SKILL.md` — every system
page's Sources section links to its matching SKILL.

_Last regenerated: 2026-04-19 (wiki bootstrap)_

---

## Gameplay (Tier 1)

### [[character]] — Character facade
Subsystem sub-pages:
- [[character-stats]], [[character-needs]], [[character-skills]], [[character-traits]], [[character-bio]], [[character-movement]], [[character-progression]], [[character-profile]], [[character-book-knowledge]], [[character-blueprints]], [[character-schedule]], [[character-job]], [[character-locations]], [[character-speech]]
- [[character-combat]] — see [[combat]]
- [[character-equipment]] — see [[items]]
- [[character-party]] — see [[party]]
- [[character-interaction]], [[character-invitation]], [[character-mentorship]], [[character-relation]] — see [[social]]
- [[character-community]] — adapter to [[world-community]]
- [[character-archetype]] **(stub — post-merge)**, [[character-terrain]] **(stub — post-merge)**

### [[combat]] — Turn-paced combat
- [[combat-battle-manager]], [[combat-engagement]], [[combat-ai-logic]], [[combat-damage]], [[combat-abilities]], [[combat-status-effect]], [[combat-circle-indicators]], [[combat-styles]]

### [[ai]] — NPC decision stack
- [[ai-behaviour-tree]], [[ai-goap]], [[ai-actions]], [[ai-conditions]], [[ai-pathing]], [[ai-navmesh]], [[ai-obstacle-avoidance]], [[ai-player-nav-switch]]

### [[party]] — Small-group travel
No child sub-pages yet (see [[character-party]]).

### [[social]] — Interactions + memory
- [[character-interaction]], [[interaction-exchanges]], [[character-invitation]], [[character-mentorship]], [[character-relation]]

### [[jobs-and-logistics]] — Economy
- [[job-employment]], [[job-roles]], [[building-logistics-manager]], [[building-task-manager]], [[order-types]], [[virtual-supply]], [[crafting-loop]]

### [[building]] — Buildings & furniture
- [[building-hierarchy]], [[building-state]], [[furniture-grid]], [[commercial-building]], [[building-interior]], [[building-placement-manager]]

### [[items]] — Item / inventory / equipment
- [[item-data]], [[item-instance]], [[world-items]], [[character-equipment]], [[inventory]], [[keys-and-locks]]

### [[dialogue]] — Scripted conversations
- [[dialogue-data]], [[dialogue-manager]], [[scripted-speech]]

### [[shops]] — Commerce
- [[shop-building]], [[shop-queue]], [[shop-vendor]], [[shop-customer-ai]]

### [[world]] — Living world + communities
- [[world-map-hibernation]], [[world-macro-simulation]], [[world-community]], [[world-biome-region]], [[world-offset-allocation]], [[world-map-transitions]]

### [[terrain-and-weather]] — **STUB (post-merge)**
Code lives on `feature/character-archetype-system`. Tracked in [[TODO-post-merge]].

---

## Rendering (Tier 1)

### [[shadows]] — 2D sprite cast shadows (URP)
No child sub-pages yet.

---

## Infrastructure (Tier 2)

- [[save-load]] — Character profile + world save pipelines.
- [[network]] — Unity NGO server-authority model. ⚠ Missing `NETWORK_ARCHITECTURE.md`.
- [[visuals]] — `ICharacterVisual` abstraction; Spine migration planned.
- [[player-ui]] — HUD, menus, notifications (49 files).

---

## Engine Plumbing (Tier 3 — aggregated)

See [[engine-plumbing]] for the single aggregated page covering:
- Time: `time-manager`, `day-night-cycle`, `game-speed-controller`.
- Notifications / UI overlays: `notification-system`, `toast-notification`, `tooltip-system`.
- Input / view: `point-click-system`, `camera-follow`, `billboard`.
- Interactions (non-social): `interactable-system`, `door-lock-system`.
- Debug: `debug-script`, `map-controller-debug-ui`, `network-troubleshooting`.
- Spawning / utilities: `spawn-manager`, `screen-fade-manager`, `color-utils`, `world-ui-manager`, `game-controller`.
- Audio (post-merge): footstep audio resolver.
- Unknown scope: `grass-system`.

---

## Backlogs (actions for Kevin)

- [[TODO-post-merge]] — pages blocked on `feature/character-archetype-system`.
- [[TODO-skills]] — systems documented in the wiki that still need `.agent/skills/<name>/SKILL.md`.
- [[TODO-docs]] — missing design docs referenced by the wiki (incl. `NETWORK_ARCHITECTURE.md`).
