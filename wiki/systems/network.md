---
type: system
title: "Network (NGO)"
tags: [network, multiplayer, ngo, tier-2]
created: 2026-04-19
updated: 2026-04-25
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

**What's happening:** a spawned NetworkObject on the host has a null internal `NetworkManagerOwner` field (or other half-spawned state). The NRE kills the whole client-approval handshake, so every client silently fails to join.

**Confirmed root cause (2026-04-25):** baking a furniture instance whose prefab carries its own `NetworkObject` directly inside a runtime-spawned building prefab. When the building is `Spawn()`'d via `BuildingPlacementManager` (or restored via `MapController.SpawnSavedBuildings`), NGO walks the hierarchy and tries to register every nested `NetworkObject` — but the nested child never goes through a clean independent spawn path, ending up half-registered. The bug fires equally on **fresh** placement and **save-restore** — earlier suspicion that save-restore was the only trigger was wrong. Reproduced concretely: a Forge prefab (which nested `CraftingStation.prefab`) → place 1 Forge → next client-join attempt NREs every `ProcessPendingApprovals` tick. Co-symptoms on the same trigger: 2D-Animation `TransformAccessJob` `ArgumentException: Key: -X is not present` (the half-spawned child's SpriteSkin transform never unregistered from the deformation manager) and `BattleManager.*ClientRpc could not resolve NetworkObjects` (the broken NO never reached the client, so `NetworkObjectReference.TryGet` returns false on the client).

**Fix (root cause):** never nest a furniture-with-NetworkObject directly into a building prefab. Instead, author the building's default furniture in `CommercialBuilding._defaultFurnitureLayout` (a `List<DefaultFurnitureSlot>` of `(FurnitureItemSO, LocalPosition, LocalEulerAngles)`); the server-side branch of `OnNetworkSpawn` calls `TrySpawnDefaultFurniture()` which `Instantiate`s each entry, `SetParent`s to the building (worldPositionStays:true) **before** `Spawn()`, and is gated by `_defaultFurnitureSpawned` (idempotent) plus an "any Furniture child already present?" guard for the save-restore path. Furniture without its own `NetworkObject` (e.g. TimeClock, which strips its NO via `m_RemovedComponents`) is still safe to bake. See `.agent/skills/building_system/SKILL.md` § Default furniture spawn for buildings.

**Fix (defense-in-depth):** `PurgeBrokenSpawnedNetworkObjects` runs inside `ApprovalCheck`. It scans **both** `NetworkManager.SpawnManager.SpawnedObjects` (the dict) and `SpawnedObjectsList` (the HashSet — see §10 of `.agent/skills/multiplayer/SKILL.md` for the dict-vs-HashSet trap), checks each entry via reflection (null ref, destroyed GO, null `NetworkManagerOwner`), then **invokes `Serialize` as a probe** and catches any exception. Any NO that would have NRE'd is removed from both collections before NGO's sync loop touches it. A `Pre-sync scan: N spawned NetworkObjects (HashSet count: M), none broken.` info log confirms the purge ran clean; `Purging broken spawned NetworkObject id=X name='Y' reason='…'` warnings identify the offenders for source-level follow-up.

## Connection loading UI

A remote client joining a host can take several seconds while NGO walks the scene-sync handshake. The [[loading-overlay]] system surfaces this latency as a stage-based progress bar with descriptive text and a delayed cancel button. `NetworkConnectionLoadingDriver` (in `Assets/Scripts/UI/Loading/`) is the NGO-aware producer — instantiated by `GameSessionManager.JoinMultiplayer()` immediately before `StartClient()`, it subscribes to `OnClientStarted` / `OnSceneEvent` (`Load` / `Synchronize` / `SynchronizeComplete`) / `OnClientConnectedCallback` / `OnClientDisconnectCallback` and pushes stage updates into the overlay's push API. The driver self-destructs on connect/disconnect/cancel.

See [[loading-overlay]] for the full system architecture (stage map, bar fill formula, cancel-button behaviour, edge cases). The overlay itself is generic: future loading scenarios (save-load restore, scene transitions, solo session boot) drive the same overlay through their own drivers without overlay changes.

## Specialized agents

- **network-specialist** — implement / design networked features.
- **network-validator** — read-only auditor to verify multiplayer compatibility after implementation.

Use the validator proactively after any networked change.

## Open questions / TODO

- [ ] **Fill out a proper data-flow diagram** once `NETWORK_ARCHITECTURE.md` is authored or its content is pasted into `raw/design-docs/`.
- [ ] Enumerate the NPC state that goes over the wire vs stays server-only — a common bug class.
- [ ] Client late-join — how does it catch up state? Especially hibernated map contents.
- [ ] Confirm the exact interest-management config: pure-distance? per-map scoping?
- [ ] **Audit remaining building prefabs for nested furniture-with-NetworkObject.** As of 2026-04-25 only `Forge.prefab` was found nesting one (`CraftingStation`); fix moved that entry to `_defaultFurnitureLayout`. Lumberyard / Shop / Clothing Shop / Transporter Building / `CommercialBuilding_prefab` were clean. Any new commercial-building Variant must follow the rule documented in `.agent/skills/multiplayer/SKILL.md` §10 + `.agent/skills/building_system/SKILL.md` § Default furniture spawn — only NetworkObject-FREE furniture may be baked.
- [ ] `MapController.SpawnSavedBuildings` parent-before-spawn ordering — still worth auditing as a defensive-coding measure even though the actual NRE we hit was the nested-furniture case. Purge remains in place as defense-in-depth either way.

## Change log
- 2026-04-25 — Promoted the inline "Connection loading UI" section into its own dedicated [[loading-overlay]] system page; this page now keeps a one-paragraph summary + wikilink. — claude
- 2026-04-25 — Added connection loading UI: `LoadingOverlay` singleton + `NetworkConnectionLoadingDriver` observer. Generic overlay (`Resources/UI/UI_LoadingOverlay.prefab`) reusable for any future loading scenario; driver translates NGO lifecycle events into stage-based progress (Connecting → Awaiting approval → Loading scene → Synchronizing → Finalizing) with a 10-s-delayed cancel button and a failure state. Spec: `docs/superpowers/specs/2026-04-25-loading-overlay-design.md`. Plan: `docs/superpowers/plans/2026-04-25-loading-overlay.md`. — claude
- 2026-04-25 — Confirmed root cause of the half-spawned-NetworkObject NRE: nested furniture-with-NO baked into building prefabs (Forge nesting CraftingStation). Documented the runtime-spawn fix via `CommercialBuilding._defaultFurnitureLayout` + `TrySpawnDefaultFurniture()`. Hardened `PurgeBrokenSpawnedNetworkObjects` to also remove from `SpawnManager.SpawnedObjectsList` (NGO's sync iterates the HashSet, not the dict — removing only from the dict was a no-op). Updated Open questions. — claude
- 2026-04-24 — Documented transport tuning (`MaxPacketQueueSize`, `SpawnTimeout`) applied at runtime in `GameSessionManager.ApplyTransportTuning`, and the defensive half-spawned-NetworkObject purge (`PurgeBrokenSpawnedNetworkObjects`) invoked from `ApprovalCheck`. Added the save-restore ordering gotcha to Open questions. — claude
- 2026-04-19 — Stub. Low confidence until NETWORK_ARCHITECTURE.md exists. — Claude / [[kevin]]

## Sources
- [.agent/skills/multiplayer/SKILL.md](../../.agent/skills/multiplayer/SKILL.md)
- [.agent/skills/network-troubleshooting/SKILL.md](../../.agent/skills/network-troubleshooting/SKILL.md)
- [.claude/agents/network-specialist.md](../../.claude/agents/network-specialist.md)
- [.claude/agents/network-validator.md](../../.claude/agents/network-validator.md)
- Root [CLAUDE.md](../../CLAUDE.md) rules #18–#19.
- ⚠ Missing: `NETWORK_ARCHITECTURE.md` — tracked in [[TODO-docs]].
