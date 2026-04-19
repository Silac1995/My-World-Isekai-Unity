---
type: system
title: "Network (NGO)"
tags: [network, multiplayer, ngo, tier-2]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[character]]"
  - "[[world]]"
  - "[[combat]]"
  - "[[save-load]]"
  - "[[kevin]]"
status: stable
confidence: medium
primary_agent: network-specialist
secondary_agents:
  - network-validator
  - save-persistence-specialist
owner_code_path: "Assets/Scripts/Core/Network/"
depends_on: []
depended_on_by:
  - "[[character]]"
  - "[[world]]"
  - "[[combat]]"
  - "[[save-load]]"
  - "[[items]]"
  - "[[party]]"
---

# Network (NGO)

## Summary
The project runs on **Unity NGO (Netcode for GameObjects)** with a **server-authoritative** model. Server owns game state; clients predict where safe (owner movement, combat intent) and validate server-side. Every networked feature must pass all three relationship scenarios: **Host↔Client**, **Client↔Client**, and **Host/Client↔NPC** (project rule #19). Static data on the server is invisible to clients unless mirrored via `NetworkVariable`, ClientRpc, or `OnValueChanged` callbacks.

> **⚠ Missing document** — root [CLAUDE.md](../../CLAUDE.md) rule #18 references `NETWORK_ARCHITECTURE.md`, which does not exist in the repo. Tracked in [[TODO-docs]]. When that doc lands in `raw/design-docs/`, `/ingest` it and expand this page.

## Architectural pillars

1. **Server authority** — game state mutations go through `[Rpc(SendTo.Server)]` or ServerRpc. Clients never mutate shared state directly.
2. **Owner prediction** — movement and combat intent predict locally on the owner, server validates.
3. **Interest management** — large spatial offsets between maps (see [[world]] `WorldOffsetAllocator`) let NGO filter cross-map traffic naturally.
4. **Delta compression** — `NetworkVariable`s use NGO's built-in delta compression.
5. **NetworkTransform vs ClientNetworkTransform** — NPCs use `NetworkTransform` (server authority); players use owner-authoritative `ClientNetworkTransform`.
6. **NPC parity rule** — anything a player can do, an NPC can do. All gameplay effects go through `CharacterAction` which itself is networked the same way for both.

## Validation matrix (project rule #19)

| Scenario | What to check |
|---|---|
| Host↔Client | Local host + remote client see identical state; RPCs round-trip cleanly. |
| Client↔Client | Two non-host clients observe a third-party event consistently. |
| Host/Client↔NPC | NPCs behave identically whether host or client observes them. |

## Key classes / files

- `Assets/Scripts/Core/Network/GameSessionManager.cs` — session lifecycle.
- `Assets/Scripts/Core/Network/ClientNetworkTransform.cs` — owner-authoritative transform.
- `Assets/DefaultNetworkPrefabs.asset` — network prefab registry.
- `Character`, `CharacterActions`, `BattleManager` — all `NetworkBehaviour`.

## Specialized agents

- **network-specialist** — implement / design networked features.
- **network-validator** — read-only auditor to verify multiplayer compatibility after implementation.

Use the validator proactively after any networked change.

## Open questions / TODO

- [ ] **Fill out a proper data-flow diagram** once `NETWORK_ARCHITECTURE.md` is authored or its content is pasted into `raw/design-docs/`.
- [ ] Enumerate the NPC state that goes over the wire vs stays server-only — a common bug class.
- [ ] Client late-join — how does it catch up state? Especially hibernated map contents.
- [ ] Confirm the exact interest-management config: pure-distance? per-map scoping?

## Change log
- 2026-04-19 — Stub. Low confidence until NETWORK_ARCHITECTURE.md exists. — Claude / [[kevin]]

## Sources
- [.agent/skills/multiplayer/SKILL.md](../../.agent/skills/multiplayer/SKILL.md)
- [.agent/skills/network-troubleshooting/SKILL.md](../../.agent/skills/network-troubleshooting/SKILL.md)
- [.claude/agents/network-specialist.md](../../.claude/agents/network-specialist.md)
- [.claude/agents/network-validator.md](../../.claude/agents/network-validator.md)
- Root [CLAUDE.md](../../CLAUDE.md) rules #18–#19.
- ⚠ Missing: `NETWORK_ARCHITECTURE.md` — tracked in [[TODO-docs]].
