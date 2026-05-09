---
type: person
title: "Kevin"
tags: [owner, solo-dev]
created: 2026-04-18
updated: 2026-04-18
sources: []
related: []
status: active
confidence: high
role: "Solo developer / project owner of My World Isekai"
---

# Kevin

## Summary
Solo developer and sole decision-maker on My World Isekai. Authors all the
design, code, and architectural decisions. Collaborates with Claude as the
primary implementation and documentation partner.

## Responsibilities
- Product direction and design.
- All Unity / C# implementation.
- Merging feature branches into `multiplayyer`.
- Authoring operational `.agent/skills/*/SKILL.md` files (project rule #28).
- Authoring ADRs — Claude drafts, Kevin signs off via `/save decision`.
- Ground truth for ambiguity: when Claude is uncertain, Kevin is the source.

## Current focus
- Character archetype system (on `feature/character-archetype-system`).
- Pre-alpha / heavy architecture phase — not a shipping build yet.
- Terrain + weather + vegetation just landed on the feature branch.
- Planned Spine 2D migration queued behind the archetype work.

## Architectural preferences
- **SOLID** — single responsibility, open/closed, interface-segregated.
- **Modular, loosely-coupled subsystems** — each character subsystem lives on
  its own child GameObject; cross-system calls go through the `Character`
  facade.
- **Shared gameplay action layer** — players and NPCs both route effects
  through `CharacterAction`. Player HUDs only queue actions; they never
  implement effects directly.
- **Unity + NGO multiplayer** — server authority, validate every networked
  feature across Host↔Client, Client↔Client, Host/Client↔NPC (project rule #19).
- **Decoupled character from world** — character save data is a portable local
  profile (`ICharacterSaveData<T>`), loadable into any session.
- **Living world** — macro-simulation for hibernated maps, micro-simulation
  when active. Never mix the two without a catch-up loop.
- **Shader-first rendering** — prefer GPU work over CPU for dynamic visuals.
- **Defensive coding** — try/catch at system boundaries, log exceptions, never
  swallow silently.

## Working rhythm with Claude
- Prefers precise numbered clarifying questions over Claude guessing.
- Wants proactive improvement suggestions, not just the literal ask.
- Requires testing/integration guide at the end of any task.
- Expects every system change to update both its SKILL.md and any relevant
  specialist agent in `.claude/agents/`.

## Recent contributions
- 2026-04-18 — Terrain + weather + vegetation systems landed on feature branch.
- 2026-04-18 — Bootstrapped LLM wiki scaffolding.
- Earlier — Character core, Combat, Party, Invitation, AI stack (BT + GOAP),
  Building/Furniture, Items, Inventory, Save/Load, Network layer.

## Links
- Repository: `My-World-Isekai-Unity`
- Primary branch: `multiplayyer`
- See root [CLAUDE.md](../../CLAUDE.md) for the 32 project rules.

## Sources
- 2026-04-18 conversation with Kevin — wiki bootstrap, tiering, confidence rules.
- Memory: `user_dev_environment.md`, `project_visual_migration_order.md`,
  `project_spine2d_migration.md`, `project_party_system_rules.md`.
