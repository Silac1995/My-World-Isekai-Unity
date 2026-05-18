---
type: audit
title: "City Founding — Multiplayer Late-Joiner Audit (rule #19b)"
tags: [audit, multiplayer, late-joiner, city-founding, plan-4b, plan-4c, rule-19b]
created: 2026-05-18
updated: 2026-05-18
audited_features:
  - "Plan 4b (JobBuilder + GOAP supply chain)"
  - "Plan 4c (AdministrativeBuilding RPCs + JoinRequest + DrifterMigration + UI)"
  - "Plan 4c polish (UI prefabs, AB.prefab, Civic flips, RTS placement cursor)"
  - "Post-handoff: BTAction_PursueAmbition + NeedAmbitionFinishConstruction + tier-as-SO"
auditor: claude
status: audit-complete
verdict_summary: "11 SAFE / 4 SUSPICIOUS / 0 HIGH — no blockers. SUSPICIOUS items all collapse to a single root cause: Community state uses save-round-trip stopgap instead of NetVar (documented Plan 4c known limit, backlog item E)."
---

# City Founding — Multiplayer Late-Joiner Audit (rule #19b)

## Audit method

Rule #19b's mandatory six-question audit, applied to every networked surface
added by Plans 4b + 4c + the polish/handoff iterations. For each surface:

1. **Who writes / who reads the state?**
2. **What replication channel?**
3. **Late-joiner sees?**
4. **Client-side pre-gate?**
5. **`GetComponentInParent` spawn-race?**
6. **`InteractableObject.IsCharacterInInteractionZone` (rule #36)?**

A late-joiner is defined per rule #19b: host the session, mutate state
(charter community → place AB → finalize → tier-up → place civic building →
queue join request), connect a fresh client mid-session, verify replicates.

Verdicts:

| Symbol | Meaning |
| --- | --- |
| ✅ SAFE | Six-question audit passes. Late-joiner observes the same state the host has. |
| ⚠️ SUSPICIOUS | One or more answers are non-default (e.g. save-round-trip stopgap). Documented limit, not a bug. Late-joiner gets a degraded but coherent view; manual PlayMode-MP smoketest recommended to confirm. |
| 🔴 HIGH | Documented bug. Late-joiner observes broken state. Must fix before claiming feature done. |

---

## Surface 1 — `AdministrativeBuilding.PendingJoinRequests` (NetworkList&lt;JoinRequest&gt;)

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | **Writer**: server-only (Submit/Accept/Decline RPC bodies). **Reader**: every peer — `UI_JoinRequestsTab` subscribes to `OnListChanged`. |
| 2 | Replication channel | NGO `NetworkList<JoinRequest>`. `JoinRequest` is `INetworkSerializable, IEquatable<JoinRequest>` (eq keyed on `ApplicantNetId`). |
| 3 | Late-joiner sees | ✅ NetworkList replays full list on connect. Existing applicants visible immediately. |
| 4 | Client-side pre-gate | `UI_JoinRequestsTab.OnAcceptClicked` / `OnDeclineClicked` fire ServerRpc unconditionally — server re-validates leader-authority + applicant existence. No optimistic client mutation. |
| 5 | `GetComponentInParent` spawn-race | N/A — the NetworkList lives on the AB itself, not a child component. |
| 6 | Rule #36 zone gate | N/A — UI interaction, no spatial gate. |

**Verdict: ✅ SAFE.**

---

## Surface 2 — `AdministrativeBuilding.SubmitJoinRequestServerRpc`

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | **Server-only mutation**. Called from `JoinRequestDesk.OnInteract` (both server and client routes). |
| 2 | Replication channel | `[ServerRpc(RequireOwnership=false)]` — any peer (drifter NPC owned by server OR human player) can submit. Server-side mutation lands on the NetworkList → replicates. |
| 3 | Late-joiner sees | ✅ Indirect — sees the NetworkList state, which already contains the request. |
| 4 | Client-side pre-gate | Server re-validates: applicant resolved via `SpawnManager.SpawnedObjects.TryGetValue`, `applicant.CharacterCommunity.CurrentCommunity == null`, `Citizenship == null`, dedupe by ApplicantNetId. No client-side pre-gate. |
| 5 | `GetComponentInParent` spawn-race | `JoinRequestDesk._ab` is lazy-resolved in `Awake` via `GetComponentInParent<AdministrativeBuilding>()`. Late-bind fallback `TryRegisterWithAB()` re-runs the lookup on every `OnInteract` call so spawn-order races between desk + AB resolve on first interact. ✅ |
| 6 | Rule #36 zone gate | `JoinRequestDesk` inherits `Furniture.OnInteract` — proximity gated by the standard `InteractableObject.IsCharacterInInteractionZone` upstream. ✅ |

**Verdict: ✅ SAFE.**

---

## Surface 3 — `AdministrativeBuilding.AcceptJoinRequestServerRpc` / `DeclineJoinRequestServerRpc`

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Server-only mutation. Called from `UI_JoinRequestsTab` on a leader client. |
| 2 | Replication channel | `[ServerRpc(RequireOwnership=false)]`. Server-side effects fan out via NetworkList removal + `CharacterCommunity.JoinCommunity` (save-round-trip + future NetVar) + `SetCitizenship` (same). |
| 3 | Late-joiner sees | ⚠️ Sees the post-state via existing save-round-trip channels on `CharacterCommunity` + `CommunityData`. Citizens of a community are derivable from `CommunityData.memberIds` snapshot. Not a NetVar; consistent only at save / load boundaries. |
| 4 | Client-side pre-gate | Server re-validates leader authority via `OwnerCommunity.IsLeader(requester)`. Client UI does not pre-gate — UI shows Accept/Decline buttons to anyone who can open the panel; that gate is on `CityManagementFurniture.OnInteract` (leader-only). |
| 5 | `GetComponentInParent` spawn-race | N/A — RPC on the AB itself. |
| 6 | Rule #36 zone gate | N/A — UI interaction. |

**Verdict: ⚠️ SUSPICIOUS — citizenship replication uses save-round-trip stopgap.** Documented Plan 4c known limitation. Late-joiner who connects mid-session sees the citizen list at last-save granularity, not live. Manual PlayMode-MP smoketest should confirm: (a) host accepts a request, (b) drifter's `CurrentCommunity` is set server-side, (c) joining client may or may not see updated citizenship until the next save. Fix path: dedicated `CharacterCommunity.CitizenshipNetVar` (deferred backlog item E from this session).

---

## Surface 4 — `AdministrativeBuilding.PlaceCityBlueprintServerRpc`

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Server-only mutation: spawns a Building, mutates `community.ownedBuildings`, `BuildingGrid.Register`, creates `BuildOrder`. |
| 2 | Replication channel | `[ServerRpc(RequireOwnership=false)]`. Building spawn → existing NGO `NetworkObject.Spawn` (replicates). BuildingGrid occupancy → existing Plan 2 NetworkList. `community.ownedBuildings` → `[NonSerialized]` server-only ref; clients reconstruct via `CommunityData.ConstructedBuildings` save-snapshot. BuildOrder → server-only `LogisticsOrderBook._activeBuildOrders` (not replicated; UI reads via the AB's `LogisticsManager` accessor server-side). |
| 3 | Late-joiner sees | ✅ For the spawned Building: visible via standard NGO replication + `Building.ConstructionProgress` NetVar. ⚠️ For the `BuildOrder` itself: not replicated. Joining client opening the leader UI sees BuildOrder state only when `JobLogisticsManager.ProcessActiveBuildOrders` re-emits — which is server-side only. No client-readable BuildOrder list. |
| 4 | Client-side pre-gate | Server re-validates: leader authority, blueprint Civic-category, tier-unlocked, BuildingGrid.CanPlace. Client UI in `UI_PlaceBuildingTab.RefreshFromAB` reads `CurrentTier.UnlockedBlueprints` — works because tier SOs are Resources-loaded baked assets, same on every peer. |
| 5 | `GetComponentInParent` spawn-race | The RPC handler resolves the leader's `BuildingPlacementManager` via `requester.GetComponentInChildren<BuildingPlacementManager>()`. Local-player path (`UI_PlaceBuildingTab.OnPlaceClicked` → `StartCivicPlacement`) uses `NetworkManager.LocalClient.PlayerObject.GetComponentInChildren` — well-formed. ✅ |
| 6 | Rule #36 zone gate | N/A for the RPC itself; the RTS placement cursor uses raycast against ground layer. The downstream `BuildingPlacementManager.PlaceCivicBuildingForLeader` runs `IsInsideRegion` server-side. ✅ |

**Verdict: ⚠️ SUSPICIOUS — BuildOrder collection is server-only.** The construction site spawns + replicates fine; the *order* itself (what materials needed, who placed it, in-flight count) is invisible to clients. UI uses it server-side only via `_ab.LogisticsManager` accessor. Acceptable for v1 because the BuildOrder is consumed by JobBuilder server-side and the player UI doesn't surface order metadata. Future "BuildOrders tab" needs an actual NetVar / NetworkList. Documented Plan 4c known limitation.

---

## Surface 5 — `AdministrativeBuilding.RequestPromoteLevelServerRpc` + `TierUpResultClientRpc`

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Server: `Community.TryPromoteLevel` mutates `community.level`. ClientRpc broadcasts result to the requester. |
| 2 | Replication channel | `[ServerRpc(RequireOwnership=false)]` for promote + `[ClientRpc]` (single-target via `ClientRpcParams`) for result toast. `community.level` itself replicates via save-round-trip + per-action ClientRpc stopgap (no dedicated NetVar — documented). |
| 3 | Late-joiner sees | ⚠️ Joins after a tier-up: sees the new level via `CommunityData.Level` save-snapshot. Joining mid-promotion (rare): may see stale level on first UI open + corrected level on next save tick. |
| 4 | Client-side pre-gate | UI_TierUpTab shows progress + Promote button optimistically computed against replicated state. Server-side `TryPromoteLevel` is the authoritative gate — defensive against UI desync. |
| 5 | `GetComponentInParent` spawn-race | N/A. |
| 6 | Rule #36 zone gate | N/A — UI. |

**Verdict: ⚠️ SUSPICIOUS — `community.level` uses save-round-trip + per-action ClientRpc stopgap.** Plan 4c known limitation. The result ClientRpc gives the requesting client instant feedback; other clients see the tier update at next save boundary. Manual smoketest should confirm: (a) host promotes, (b) client UI receives the toast, (c) other clients' UI refreshes within one save cycle.

---

## Surface 6 — `BuildOrder` lifecycle (Plan 4b)

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Server-only. `LogisticsOrderBook._activeBuildOrders` list mutated by AB.PlaceCityBlueprint + JobBuilder workers + JobLogisticsManager. |
| 2 | Replication channel | None. Server-only data class on a server-only collection. |
| 3 | Late-joiner sees | ❌ Cannot see individual BuildOrders — but doesn't need to. Active construction sites are visible via `Building.ConstructionProgress` NetVar. |
| 4 | Client-side pre-gate | N/A — server-only. |
| 5 | `GetComponentInParent` spawn-race | N/A. |
| 6 | Rule #36 zone gate | N/A. |

**Verdict: ✅ SAFE.** The construction loop replicates correctly (`Building.ConstructionProgress` + `_currentState`); the abstract BuildOrder is server-only by design.

---

## Surface 7 — `Community.AdministrativeBuilding` (NonSerialized server ref) + `Community.level`

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | **Server-only writes** in `AdministrativeBuilding.SetOwnerCommunity` (placement) + `Community.ChangeLevel` (tier-up). **Server-side reads** by Job system, BTAction_PursueAmbition, DevForce methods. **Client-side reads** indirect via CommunityData save snapshot. |
| 2 | Replication channel | Save round-trip via `CommunityData.AdministrativeBuildingNetId` + `CommunityData.Level`. No NetVar (deferred Plan 4c stopgap). |
| 3 | Late-joiner sees | ✅ When connecting fresh: sees full CommunityData snapshot replicated by existing save-load pipeline. Joining mid-session before save: client UI may show null AB / level=SmallGroup until next save boundary. |
| 4 | Client-side pre-gate | UI panels guard against null Community / null AB defensively (e.g. `if (_ab.OwnerCommunity == null) return;` in tabs). |
| 5 | `GetComponentInParent` spawn-race | `AdministrativeBuilding.OnNetworkSpawn` doesn't yet call `SetOwnerCommunity` — it's called by `BuildingPlacementManager.RegisterBuildingWithMap` post-spawn server-side. On load, `MapController.SpawnSavedBuildings` re-runs this path. Late-joiner respawning the AB GameObject: `OwnerCommunity` re-resolves via the save snapshot. ✅ |
| 6 | Rule #36 zone gate | N/A — data state. |

**Verdict: ⚠️ SUSPICIOUS — known stopgap.** Same root cause as Surfaces 3 + 5: no dedicated NetVar for community state. Late-joiner sees coherent state via save replication. Acceptable for v1; backlog item E (dedicated `Community.Level` NetVar) addresses it.

---

## Surface 8 — `_unfulfillableMaterialHarvestQueue` on AdministrativeBuilding

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Server-only. Written by `JobLogisticsManager.ProcessActiveBuildOrders` when `RequestStock` returns false. Read by `JobHarvester.ExecuteCityHarvesterTick`. |
| 2 | Replication channel | None. Pure server-side scratch list. |
| 3 | Late-joiner sees | ❌ Cannot see — but doesn't need to. The queue is internal logistics scratch state; harvesters drain it server-side. |
| 4 | Client-side pre-gate | N/A. |
| 5 | `GetComponentInParent` spawn-race | N/A. |
| 6 | Rule #36 zone gate | N/A. |

**Verdict: ✅ SAFE.** Server-only by design.

---

## Surface 9 — `DrifterMigrationSystem`

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Server-only — subscribes to `TimeManager.Instance.OnNewDay`. Spawns Character NPCs via `Instantiate + NetworkObject.Spawn`. |
| 2 | Replication channel | Drifter Character spawn → standard NGO `NetworkObject.Spawn`. Drifter `CharacterMovement.SetDestination` → existing CharacterMovement NetVar / replication. |
| 3 | Late-joiner sees | ✅ Drifters are NetworkObjects; joining client sees them in flight via standard NGO replication. NPC controller runs server-side. |
| 4 | Client-side pre-gate | N/A — server-only spawn. |
| 5 | `GetComponentInParent` spawn-race | The component itself is attached to MapController at scene-author time (or via dev tools); no per-spawn race. |
| 6 | Rule #36 zone gate | The spawn point uses NavMesh.SamplePosition; the drifter's BT (post-spawn) uses CharacterMovement.SetDestination + interactable-zone gates downstream. ✅ |

**Verdict: ✅ SAFE.** Standard NPC spawn pattern; nothing novel.

---

## Surface 10 — `JoinRequestDesk.OnInteract` (refactored to plain Furniture, post-handoff)

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Forwards to AB.SubmitJoinRequestServerRpc (server-only mutation). Local toast raised on the interacting player's peer. |
| 2 | Replication channel | ServerRpc (above). Toast is local — client-side only. |
| 3 | Late-joiner sees | ✅ The submission writes to PendingJoinRequests (Surface 1). |
| 4 | Client-side pre-gate | Server re-validates inside `SubmitJoinRequestServerRpc`. Client-side: `interactor.IsPlayer() && interactor.IsOwner` for the toast — correct (avoids host-NPC double-toast trap per rule #19b). |
| 5 | `GetComponentInParent` spawn-race | `_ab = GetComponentInParent<AdministrativeBuilding>()` in `Awake` **plus** a `TryRegisterWithAB()` late-bind helper called from `OnInteract` (verified by inspecting the current file). Spawn-race recovery happens on first interact. ✅ |
| 6 | Rule #36 zone gate | Inherits `Furniture.OnInteract` proximity check via the upstream InteractableObject path. ✅ |

**Verdict: ✅ SAFE.**

---

## Surface 11 — `CityManagementFurniture.OnInteract`

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Reads `OwnerCommunity.leaders` server-side. Opens UI on the local player's peer only. |
| 2 | Replication channel | None — UI is local. |
| 3 | Late-joiner sees | ✅ The UI opens on tap-E on a local client; reads replicated AB state. |
| 4 | Client-side pre-gate | `OwnerCommunity.IsLeader(actor)` gate + `actor.IsLocalPlayer` gate. Server-side equivalents in the ServerRpc bodies. |
| 5 | `GetComponentInParent` spawn-race | Same `_ab = GetComponentInParent<AdministrativeBuilding>()` in Awake. `TryRegisterWithAB()` helper present (kept from the original implementation). ✅ |
| 6 | Rule #36 zone gate | Inherits Furniture's OnInteract proximity check. ✅ |

**Verdict: ✅ SAFE.**

---

## Surface 12 — `BuildingPlacementManager.StartCivicPlacement` + civic LMB path

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Client-side ghost (visual only). LMB-confirm fires `AB.PlaceCityBlueprintServerRpc` (server-only mutation, Surface 4). |
| 2 | Replication channel | Ghost is local-only visual. Confirm is ServerRpc. |
| 3 | Late-joiner sees | ✅ Cannot see another player's in-progress ghost (each peer drives its own placement). On confirm, the spawned building replicates via Surface 4. |
| 4 | Client-side pre-gate | `BuildingPlacementManager.ValidatePlacement(position)` runs locally for the green/red visual; server re-validates inside `PlaceCityBlueprintServerRpc`. |
| 5 | `GetComponentInParent` spawn-race | Local player's BPM resolved via `NetworkManager.LocalClient.PlayerObject.GetComponentInChildren<BuildingPlacementManager>()` — well-formed. |
| 6 | Rule #36 zone gate | N/A — RTS cursor uses raycast. Placement-time zone validation is `IsInsideRegion`. |

**Verdict: ✅ SAFE.**

---

## Surface 13 — BTAction_PursueAmbition / NeedAmbitionFinishConstruction / GoapAction_FulfillAmbitionConstruction

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Server-only execution (NPC BT + GOAP runs server-side per project convention). Mutates server-side ambition tracking state on the Character. |
| 2 | Replication channel | None — server-only AI. Effects (movement, action queueing, harvest, drop, construction tick) all use existing replicated paths. |
| 3 | Late-joiner sees | ✅ Sees the NPC's actions via existing CharacterMovement / CharacterActions / WorldItem / Building replication. The ambition-state internals are server-only by design (matches the rest of the AI stack). |
| 4 | Client-side pre-gate | N/A. |
| 5 | `GetComponentInParent` spawn-race | N/A — ambition system runs on `Character`, no nested component lookup. |
| 6 | Rule #36 zone gate | The GOAP action queues `CharacterHarvestAction` / `CharacterPickUpItem` / `CharacterDropItem` / `CharacterAction_FinishConstruction` which all gate proximity via the canonical pattern. ✅ |

**Verdict: ✅ SAFE.**

---

## Surface 14 — `DevForce*` methods (DevForceChangeCommunityLevel, etc.)

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Server-only mutators, gated by `DevAssertHostAndDevMode` (IsServer + DevModeManager.IsEnabled + audit log). |
| 2 | Replication channel | Calls into the same production server methods (e.g. `Community.ChangeLevel`) — replication identical to production paths. |
| 3 | Late-joiner sees | ✅ Same as production paths. |
| 4 | Client-side pre-gate | N/A — dev panel is host-only. |
| 5 | `GetComponentInParent` spawn-race | N/A. |
| 6 | Rule #36 zone gate | N/A. |

**Verdict: ✅ SAFE.**

---

## Surface 15 — `Tier-as-SO migration` (CommunityTierRequirementsSO + CommunityTierRegistry)

| # | Question | Answer |
| --- | --- | --- |
| 1 | Who writes / reads | Read-only at runtime by `Community.TryPromoteLevel`, `UI_TierUpTab`, `UI_PlaceBuildingTab`. Tier SO assets are designer-authored at edit-time. |
| 2 | Replication channel | None needed — ScriptableObject assets are baked into the build; every peer has identical data. |
| 3 | Late-joiner sees | ✅ Identical tier registry on every peer. |
| 4 | Client-side pre-gate | UI reads `community.CurrentTier` (server-side state mirrored via save) + tier SO `UnlockedBlueprints` (deterministic). |
| 5 | `GetComponentInParent` spawn-race | N/A. |
| 6 | Rule #36 zone gate | N/A. |

**Verdict: ✅ SAFE.** Tier data is content-driven; the migration retains backwards compatibility via the legacy `CommunityLevel` enum lookup.

---

## Findings summary

| # | Surface | Verdict |
| --- | --- | --- |
| 1 | `AdministrativeBuilding.PendingJoinRequests` | ✅ SAFE |
| 2 | `SubmitJoinRequestServerRpc` | ✅ SAFE |
| 3 | `Accept`/`DeclineJoinRequestServerRpc` | ⚠️ Citizenship save-round-trip stopgap |
| 4 | `PlaceCityBlueprintServerRpc` | ⚠️ BuildOrder server-only |
| 5 | `RequestPromoteLevelServerRpc` | ⚠️ `community.level` save-round-trip stopgap |
| 6 | `BuildOrder` lifecycle | ✅ SAFE (server-only by design) |
| 7 | `Community.AdministrativeBuilding` + `Community.level` | ⚠️ Save-round-trip stopgap |
| 8 | `_unfulfillableMaterialHarvestQueue` | ✅ SAFE |
| 9 | `DrifterMigrationSystem` | ✅ SAFE |
| 10 | `JoinRequestDesk.OnInteract` | ✅ SAFE (late-bind helper present) |
| 11 | `CityManagementFurniture.OnInteract` | ✅ SAFE |
| 12 | `BuildingPlacementManager.StartCivicPlacement` | ✅ SAFE |
| 13 | BTAction_PursueAmbition + GOAP | ✅ SAFE |
| 14 | DevForce* methods | ✅ SAFE |
| 15 | Tier-as-SO migration | ✅ SAFE |

**Total: 11 SAFE / 4 SUSPICIOUS / 0 HIGH.**

The 4 SUSPICIOUS findings collapse to a single root cause: **Community state (level + leaders + members + Citizenship) replicates via save-round-trip rather than dedicated NetworkVariables.** This is a documented Plan 4c stopgap (backlog item E). It manifests as:
- Joining client sees community state at last-save granularity, not live.
- A tier-up + immediate disconnect-reconnect could show stale level briefly.
- Citizenship-grant + immediate reconnect could show stale membership briefly.

The save round-trip is itself reliable (existing infrastructure used elsewhere in the project). Manual PlayMode-MP smoketest is recommended to confirm UX impact but no functional break.

---

## Manual PlayMode-MP smoketest checklist (for Kevin)

Run these on host + 1 client in a single session to validate the audit findings:

- [ ] **Host: charter a community.** Verify client sees the founder's `CharacterCommunity.CurrentCommunity` next save tick.
- [ ] **Host: place + finalize AB.** Verify client sees the AB GameObject spawn + transition through Construction → Complete via standard NetworkVariables.
- [ ] **Host: open city management UI on the AB.** Verify the UI populates correctly (tier display, pending join requests list).
- [ ] **Late-join: client connects mid-session after community + AB are chartered.** Verify the client sees the AB, can join its NavMesh, and the AB's PendingJoinRequests NetworkList replays correctly on connect.
- [ ] **Client: submit a join request via JoinRequestDesk.** Verify host UI sees the entry appear (Surface 1 OnListChanged path).
- [ ] **Host: accept the join request.** Verify the client's `CurrentCommunity` and `Citizenship` update (may take a save tick — Surface 3 stopgap).
- [ ] **Host: place a civic building via PlaceBuildingTab.** Verify the building spawns + replicates to the client + BuildOrder cascade starts.
- [ ] **Host: force-promote tier via dev tool.** Verify the result toast fires on host UI + client's UI eventually shows the new tier label (save tick).
- [ ] **Drifter migration tick.** Verify the spawned NPC is visible on both peers and walks toward the AB.

Any unchecked box → file a follow-up with the exact symptom.

---

## Out of scope

- Replacing the save-round-trip stopgap with dedicated NetVars for `Community.level` / `CharacterCommunity.Citizenship` / `Community.members` — backlog item E (next session candidate).
- Replicating `BuildOrder` to clients (no UI surface needs it in v1).
- BuildOrder save persistence — backlog item E.
