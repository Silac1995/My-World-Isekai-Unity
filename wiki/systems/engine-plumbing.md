---
type: system
title: "Engine Plumbing"
tags: [engine, utilities, tier-3]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[world]]"
  - "[[player-ui]]"
  - "[[social]]"
  - "[[items]]"
  - "[[kevin]]"
status: stable
confidence: medium
primary_agent: null
secondary_agents:
  - debug-tools-architect
  - world-system-specialist
owner_code_path: "Assets/Scripts/"
depends_on: []
depended_on_by: []
---

# Engine Plumbing

## Summary
Aggregated page for small cross-cutting systems that don't warrant their own full page. Each entry is 2–3 lines: what it does, where it lives, and what it depends on. If any entry grows in complexity, promote it to its own page and remove the row here.

## Index

### Time

- **[[time-manager]]** — `Assets/Scripts/DayNightCycle/TimeManager.cs`. Global authoritative time: `CurrentDay`, `CurrentTime01`. Single source of truth for all offline/online time math (project rule #30). No SKILL.md yet — **tracked in [[TODO-skills]] (high)**. Referenced by every tick-based system and the macro simulator ([[world-macro-simulation]]). **Potential refactor:** extract `TimeManager.cs` from the `DayNightCycle/` folder (Kevin's Q10 open question).
- **[[day-night-cycle]]** — `Assets/Scripts/DayNightCycle/`. Visual day/night transition driven by `TimeManager`. Does **not** own time itself. No SKILL.md.
- **[[game-speed-controller]]** — `Assets/Scripts/DayNightCycle/GameSpeedController.cs`. Scales simulation time (1x → 8x Giga). UI-class consumers must use `Time.unscaledDeltaTime` (project rule #26). For Giga speed, tick-based simulators run catch-up loops (`while` / `ticksToProcess`). Has SKILL.md.

### Notifications / UI overlays

- **[[notification-system]]** — persistent + toast notifications. Has SKILL.md. Rendered by `Assets/Scripts/UI/Notifications/`.
- **[[toast-notification]]** — transient popups. Distinct from persistent notifications. Has SKILL.md.
- **[[tooltip-system]]** — hover tooltips for items, abilities, UI. Has SKILL.md.

### Input / view

- **[[point-click-system]]** — click-to-move + interaction dispatch. Has SKILL.md.
- **[[camera-follow]]** — `Assets/Scripts/CameraFollow.cs`. Tracks the local player. No SKILL.md — **tracked in [[TODO-skills]] (low)**.
- **[[billboard]]** — `Assets/Scripts/Billboard.cs`. Makes 2D sprites face the camera in 3D space (project rule #17). No SKILL.md.

### Interactions (non-social)

- **[[interactable-system]]** — `Assets/Scripts/Interactable/` (9 files). Hit-target abstraction for the world (items, doors, characters, buildings). Has SKILL.md.
- **[[door-lock-system]]** — lock ID + key tier + breakable door via `DoorHealth : IDamageable`. Has SKILL.md. See also [[keys-and-locks]].

### Debug / diagnostic

- **[[dev-mode]]** — Promoted to its own Tier-2 page. Host-only F3/`/devmode` developer panel with Spawn + Select modules and the `IDevAction` plug-in contract. Primary owner: `debug-tools-architect` agent. Has SKILL.md.
- **[[debug-script]]** — `Assets/Scripts/DebugScript.cs`. In-editor cheat and spawn UI. Primary owner: `debug-tools-architect` agent. No SKILL.md.
- **[[map-controller-debug-ui]]** — per-map hibernation diagnostics overlay. Referenced in [[world]] §8.
- **[[network-troubleshooting]]** — desync + RPC logging helpers. Has SKILL.md.

### Spawning / utilities

- **[[spawn-manager]]** — `Assets/Scripts/SpawnManager.cs`. Character + item spawn orchestration. No SKILL.md — **tracked in [[TODO-skills]] (medium)**.
- **[[screen-fade-manager]]** — `Assets/Scripts/UI/ScreenFadeManager.cs`. Real-time (unscaled) fades for transitions. Consumed by [[world-map-transitions]].
- **[[color-utils]]** — `Assets/Scripts/ColorUtils.cs`. Static helpers (HSV conversion, palette lookup, etc.).
- **[[world-ui-manager]]** — `Assets/Scripts/WorldUIManager.cs`. World-space UI overlays (floating names, health bars).
- **[[game-controller]]** — `Assets/Scripts/GameController.cs`. Root orchestrator (scene boot). Potentially overlaps with [[game-launcher]] / session manager.

### Audio

- **footstep-audio** — on feature branch only; see [[character-terrain]] + `FootstepAudioResolver` + `FootstepAudioProfile`. Tracked in [[TODO-post-merge]].

### Vegetation / terrain (pre-feature-branch)

- **[[grass-system]]** — `Assets/Scripts/Grass/` (3 files). **Scope unknown** — could be vegetation shader, farming crops, or cosmetic sway. Flagged per Kevin's Q10. **Do not classify further until confirmed.**

## Promotion criteria

Promote an entry to its own page (and remove from this aggregate) when it:
- Has a SKILL.md and ≥ 3 collaborating classes, **or**
- Is referenced as a dependency by 3+ Tier-1 systems, **or**
- Kevin explicitly tags it as "worth a full page" during `/lint`.

## Open questions / TODO

- [ ] `grass-system` scope (Kevin's Q10) — awaiting answer.
- [ ] Confirm every entry here has at least a `TODO-skills` or `TODO-docs` row if no SKILL exists.
- [ ] `time-manager` vs `day-night-cycle` folder reshuffle (Kevin's Q10 suggested refactor).

## Change log
- 2026-04-19 — Initial aggregated page. 21 small subsystems rolled up. — Claude / [[kevin]]

## Sources
- Various SKILL.md files linked inline.
- 2026-04-18 conversation with [[kevin]] (Q10 — grass / DayNightCycle reshuffle / CombatStyles).
