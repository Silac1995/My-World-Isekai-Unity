---
type: system
title: "Network (NGO)"
tags: [network, multiplayer, ngo, tier-2]
created: 2026-04-19
updated: 2026-04-24
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

- `Assets/Scripts/Core/Network/GameSessionManager.cs` — session lifecycle. Applies `ApplyTransportTuning` on `StartSolo` / `JoinMultiplayer` (raises `UnityTransport.MaxPacketQueueSize` to 4096 and `NetworkConfig.SpawnTimeout` to 30 s — defaults are 128 / 10 s which overflow at connect time for content-heavy worlds). Also runs `PurgeBrokenSpawnedNetworkObjects` inside `ApprovalCheck` as a defence-in-depth against half-spawned NOs blowing up NGO sync.
- `Assets/Scripts/Core/Network/ClientNetworkTransform.cs` — owner-authoritative transform.
- `Assets/DefaultNetworkPrefabs.asset` — network prefab registry. Only prefabs with a `NetworkObject` component at their root belong here. Entries without one (or pointing to deleted assets) are silently stripped at NGO init, but pollute logs and bloat the index.
- `Character`, `CharacterActions`, `BattleManager` — all `NetworkBehaviour`.

## Transport tuning & connect-time failure modes

Two classes of client-join failure show up as NGO log spam or a stuck handshake. Both are handled at runtime in `GameSessionManager`:

### A. `Receive queue is full` + `[Deferred OnSpawn] ... NetworkObject was not received within 10s`

**What's happening:** the server blasts the entire initial snapshot (all scene-placed + spawned `NetworkObject`s + their `NetworkVariable` state + `NetworkTransform` ticks) when a client joins. `UnityTransport.MaxPacketQueueSize` is the per-connection receive-queue capacity; default **128** overflows for any world with more than a handful of networked entities. Dropped packets cost spawn messages → client receives deltas for NOs it never knew → 10 s `SpawnTimeout` → purge.

**Fix:** `ApplyTransportTuning()` runs before every `StartHost` / `StartClient`, setting:
- `UnityTransport.MaxPacketQueueSize = 4096`
- `NetworkConfig.SpawnTimeout = 30` seconds

Chosen empirically. Bump higher if you still see the warnings with a larger save. Architectural levers if queue size alone isn't enough: interest management via `NetworkVisibility`, lower `NetworkTransform` tick rate on idle objects, shard via `NetworkSceneManager`.

### B. Host NRE at `NetworkObject.Serialize` during connection approval

**What's happening:** a spawned NetworkObject on the host has a null internal `NetworkManagerOwner` field (or other half-spawned state) — usually left behind by save-restore paths that `Destroy` a GO without proper `NetworkObject.Despawn()`, or that reparent a spawned NO in a way NGO doesn't expect. The NRE kills the whole client-approval handshake, so every client silently fails to join. **Fresh worlds are unaffected** because nothing gets restored; the bug only manifests with loaded saves.

**Fix (defensive):** `PurgeBrokenSpawnedNetworkObjects` runs inside `ApprovalCheck`. It iterates `NetworkManager.SpawnManager.SpawnedObjects`, checks three conditions via reflection (null ref, destroyed GO, null `NetworkManagerOwner`), then **invokes `Serialize` as a probe** and catches any exception. Any NO that would have NRE'd is removed before NGO's sync loop touches it. A `Pre-sync scan: N spawned NetworkObjects, none broken.` info log confirms the purge ran clean; `Purging broken spawned NetworkObject id=X name='Y' reason='…'` warnings identify the offenders for source-level follow-up.

**Fix (root cause — still open):** the save-restore path should be audited so this doesn't happen in the first place. Suspected culprit: `MapController.SpawnSavedBuildings` calls `bNet.Spawn()` before `SetParent(this.transform)`; NGO prefers the parent to be set *before* spawn. Tracked in Open questions.

## Specialized agents

- **network-specialist** — implement / design networked features.
- **network-validator** — read-only auditor to verify multiplayer compatibility after implementation.

Use the validator proactively after any networked change.

## Open questions / TODO

- [ ] **Fill out a proper data-flow diagram** once `NETWORK_ARCHITECTURE.md` is authored or its content is pasted into `raw/design-docs/`.
- [ ] Enumerate the NPC state that goes over the wire vs stays server-only — a common bug class.
- [ ] Client late-join — how does it catch up state? Especially hibernated map contents.
- [ ] Confirm the exact interest-management config: pure-distance? per-map scoping?
- [ ] **Save-restore ordering audit** — `MapController.SpawnSavedBuildings` calls `NetworkObject.Spawn()` before `transform.SetParent(mapController.transform)`. NGO's preferred order is parent-before-spawn. Saves created with old building prefabs can produce half-spawned NOs that later NRE `NetworkObject.Serialize` during client-sync (only visible with loaded worlds, fresh worlds are fine). The defensive purge in `GameSessionManager.PurgeBrokenSpawnedNetworkObjects` masks the symptom; root fix is switching the order + auditing any other server-side spawn path that reparents post-spawn.

## Change log
- 2026-04-24 — Documented transport tuning (`MaxPacketQueueSize`, `SpawnTimeout`) applied at runtime in `GameSessionManager.ApplyTransportTuning`, and the defensive half-spawned-NetworkObject purge (`PurgeBrokenSpawnedNetworkObjects`) invoked from `ApprovalCheck`. Added the save-restore ordering gotcha to Open questions. — claude
- 2026-04-19 — Stub. Low confidence until NETWORK_ARCHITECTURE.md exists. — Claude / [[kevin]]

## Sources
- [.agent/skills/multiplayer/SKILL.md](../../.agent/skills/multiplayer/SKILL.md)
- [.agent/skills/network-troubleshooting/SKILL.md](../../.agent/skills/network-troubleshooting/SKILL.md)
- [.claude/agents/network-specialist.md](../../.claude/agents/network-specialist.md)
- [.claude/agents/network-validator.md](../../.claude/agents/network-validator.md)
- Root [CLAUDE.md](../../CLAUDE.md) rules #18–#19.
- ⚠ Missing: `NETWORK_ARCHITECTURE.md` — tracked in [[TODO-docs]].
