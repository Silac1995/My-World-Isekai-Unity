---
type: system
title: "City Management (Admin Console, Migration, Tier-Up)"
tags: [city-founding, ui, community, building, gameplay-loop, tier-1-child]
created: 2026-05-18
updated: 2026-05-18
sources:
  - "[[.agent/skills/dev-mode/SKILL.md]]"
related:
  - "[[administrative-building]]"
  - "[[world-community]]"
  - "[[citizenship]]"
  - "[[building-grid]]"
  - "[[job-builder]]"
  - "[[construction]]"
  - "[[player-hud]]"
  - "[[kevin]]"
status: wip
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - npc-ai-specialist
  - ui-hud-specialist
owner_code_path: "Assets/Scripts/UI/CityManagement/, Assets/Scripts/World/Community/, Assets/Scripts/World/Furniture/"
depends_on:
  - "[[administrative-building]]"
  - "[[world-community]]"
  - "[[job-builder]]"
  - "[[building-grid]]"
  - "[[player-hud]]"
depended_on_by: []
---

# City Management (Admin Console, Migration, Tier-Up)

## Summary

The player-facing surface of the city-founding loop. A community leader walks to the [[administrative-building|AdministrativeBuilding]]'s `CityManagementFurniture`, opens the `UI_CityManagementPanel` (a `UI_WindowBase` variant per rule #39), and uses three tabs to manage their city: **TierUp** (progress toward next tier + Promote button), **PlaceBuilding** (list of tier-unlocked Civic blueprints + RTS-style placement), **JoinRequests** (pending drifter applicants + Accept/Decline). The same server methods (`AB.RequestPromoteLevelServerRpc`, `AB.PlaceCityBlueprintServerRpc`, `AB.AcceptJoinRequestServerRpc`, etc.) drive both the player UI and NPC-leader behavior (rule #22 parity). A `DrifterMigrationSystem` attached to each `MapController` spawns one drifter NPC per chartered community per `TimeManager.OnNewDay`; the drifter walks to the AB's `JoinRequestDesk` and submits a join request. Leaders process the request via the UI; on Accept, the applicant gains citizenship + community membership.

## Purpose

Plan 4c closes the city-founding gameplay loop. Plan 4a placed the AB skeleton; Plan 4b shipped the autonomy backbone (`BuildOrder` + `JobBuilder` + supply-chain cascade + CityHarvester); Plan 4c adds the **human-facing controls**: tier-up gating, civic placement UI, applicant intake. After Plan 4c the loop is end-to-end playable: a leader walks up to their AB, places houses + farms via the admin console, drifters daily arrive to grow the population, and tier-ups unlock new Civic blueprints. The RTS-placement cursor mode + AB.prefab preplaced furniture + UI prefab variants are the remaining polish items (deferred from this plan to a Plan 4c follow-up — see "Open questions" below).

## Responsibilities

- **Tier-up gate**: `Community.TryPromoteLevel(ab)` validates Population / Treasury / RequiredBuildings against `CommunityTierRequirementsSO.Get(nextLevel)`. Server-only, called from `AB.RequestPromoteLevelServerRpc` (player UI) or directly by NPC leader BT actions.
- **Civic placement**: `AB.PlaceCityBlueprintServerRpc(prefabId, targetCell)` validates leader-authority + Civic-category + tier-unlock + `BuildingGrid.CanPlace`, delegates to `BuildingPlacementManager.PlaceCivicBuildingForLeader`, wires community ownership + grid + auto-creates `BuildOrder` (Plan 4b consumer).
- **Daily migration**: `DrifterMigrationSystem` on each `MapController` subscribes to `TimeManager.OnNewDay`, walks `CommunityManager.activeCommunities`, filters to chartered communities whose AB sits on this map, spawns drifters at random NavMesh-sampled map-edge points.
- **Join-request lifecycle**: `JoinRequestDesk : OccupiableFurniture` forwards drifter interactions to `AB.SubmitJoinRequestServerRpc`; AB maintains a replicated `NetworkList<JoinRequest>`. Leaders process via `AcceptJoinRequestServerRpc` / `DeclineJoinRequestServerRpc`. Accept fires `AddMember` + `JoinCommunity` + `SetCitizenship` + `ClearCurrentAction` (releases the OccupyFurniture seat lock).
- **Player UI**: `CityManagementFurniture` (in-world interactable) → `PlayerUI.OpenCityManagementWindow(ab)` → `UI_CityManagementPanel` with three tab MonoBehaviours (`UI_TierUpTab` / `UI_PlaceBuildingTab` / `UI_JoinRequestsTab`) + two row prefabs (`UI_JoinRequestRow` / `UI_CivicBlueprintRow`).

## Non-responsibilities

- **Does not** own the BuildOrder consumer chain — that's [[job-builder]].
- **Does not** own the BuildingGrid runtime state — see [[building-grid]].
- **Does not** persist `BuildOrder` across server restarts (Plan 4c follow-up).
- **Does not** model NPC-leader Accept/Decline heuristics in v1 — NPC leaders route directly through `AB.AcceptJoinRequestServerRpc` and always accept (v1 stub).
- **Does not** model population caps per tier (Darwinian — food shortage via existing `CharacterNeeds.Hunger` is the natural bound).
- **Does not** model demolition + refund + ownership transfer of city buildings (Phase Next).
- **Does not** model the RTS-placement cursor / ghost-preview mode in `PlayerController` (deferred follow-up; v1 UI logs the Place-click intent and designers can drive placement via the AB ServerRpc through a debug tool).

## Key classes / files

| File | Role |
|---|---|
| [Assets/Scripts/World/Community/CommunityTierRequirementsSO.cs](../../Assets/Scripts/World/Community/CommunityTierRequirementsSO.cs) | Per-CommunityLevel requirements SO |
| [Assets/Scripts/World/Community/CommunityTierRegistry.cs](../../Assets/Scripts/World/Community/CommunityTierRegistry.cs) | Lazy static lookup by level |
| `Assets/Resources/Data/CommunityTiers/TierRequirements_*.asset` | 7 bootstrap tier assets (SmallGroup → Empire) |
| [Assets/Scripts/World/Community/Community.cs](../../Assets/Scripts/World/Community/Community.cs) | `TryPromoteLevel` POCO method |
| [Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs) | 4 new ServerRpcs + NetworkList<JoinRequest> |
| [Assets/Scripts/World/Buildings/BuildingPlacementManager.cs](../../Assets/Scripts/World/Buildings/BuildingPlacementManager.cs) | `PlaceCivicBuildingForLeader` server helper |
| [Assets/Scripts/World/Buildings/BuildingGrid.cs](../../Assets/Scripts/World/Buildings/BuildingGrid.cs) | `GetCellCenter(cell, y)` helper |
| [Assets/Scripts/World/Community/DrifterMigrationSystem.cs](../../Assets/Scripts/World/Community/DrifterMigrationSystem.cs) | Daily migration ticker |
| [Assets/Scripts/World/Community/JoinRequest.cs](../../Assets/Scripts/World/Community/JoinRequest.cs) | INetworkSerializable struct |
| [Assets/Scripts/World/Furniture/JoinRequestDesk.cs](../../Assets/Scripts/World/Furniture/JoinRequestDesk.cs) | Drifter-side desk |
| [Assets/Scripts/World/Furniture/CityManagementFurniture.cs](../../Assets/Scripts/World/Furniture/CityManagementFurniture.cs) | Leader-side console |
| [Assets/Scripts/UI/CityManagement/UI_CityManagementPanel.cs](../../Assets/Scripts/UI/CityManagement/UI_CityManagementPanel.cs) | Multi-tab management window |
| [Assets/Scripts/UI/CityManagement/UI_TierUpTab.cs](../../Assets/Scripts/UI/CityManagement/UI_TierUpTab.cs) | Promote button + progress display |
| [Assets/Scripts/UI/CityManagement/UI_PlaceBuildingTab.cs](../../Assets/Scripts/UI/CityManagement/UI_PlaceBuildingTab.cs) | Tier-unlocked blueprint list |
| [Assets/Scripts/UI/CityManagement/UI_JoinRequestsTab.cs](../../Assets/Scripts/UI/CityManagement/UI_JoinRequestsTab.cs) | Applicant queue + Accept/Decline |
| [Assets/Scripts/UI/CityManagement/UI_JoinRequestRow.cs](../../Assets/Scripts/UI/CityManagement/UI_JoinRequestRow.cs) | Row leaf |
| [Assets/Scripts/UI/CityManagement/UI_CivicBlueprintRow.cs](../../Assets/Scripts/UI/CityManagement/UI_CivicBlueprintRow.cs) | Row leaf |

## Public API / entry points

See SKILL.md sections (TBD in follow-up) for procedural details. Key surface:

- `Community.TryPromoteLevel(AdministrativeBuilding ab) → (bool ok, string reason)` — server-only POCO.
- `AdministrativeBuilding.RequestPromoteLevelServerRpc()` — UI entry; replies via `TierUpResultClientRpc`.
- `AdministrativeBuilding.PlaceCityBlueprintServerRpc(string prefabId, Vector2Int targetCell)` — UI entry; replies via `PlaceCityBlueprintResultClientRpc`.
- `AdministrativeBuilding.SubmitJoinRequestServerRpc(ulong applicantNetId)` — called by `JoinRequestDesk.OnInteract`.
- `AdministrativeBuilding.AcceptJoinRequestServerRpc(ulong applicantNetId)` / `DeclineJoinRequestServerRpc(ulong)` — leader UI buttons.
- `AdministrativeBuilding.GetJoinRequestDesk()` — lazy-cached accessor used by `DrifterMigrationSystem`.
- `CommunityTierRegistry.Get(CommunityLevel)` / `GetForNextLevelFrom(current)` — runtime tier lookup (lazy-init from Resources).
- `BuildingPlacementManager.PlaceCivicBuildingForLeader(BuildingSO, Character placer, Vector3, Quaternion)` — server-only spawn helper.
- `BuildingGrid.GetCellCenter(Vector2Int cell, float y)` — world-space cell center.
- `PlayerUI.OpenCityManagementWindow(AdministrativeBuilding ab)` / `CloseCityManagementWindow()` / `IsCityManagementWindowOpen`.

## Data flow

```
Player UI flow:
   Leader taps E on CityManagementFurniture
       → CityManagementFurniture.OnInteract validates leader + local-player
       → PlayerUI.OpenCityManagementWindow(ab)
           → UI_CityManagementPanel.Initialize(ab)
               → UI_TierUpTab.RefreshFromAB() reads CommunityTierRegistry + community state
               → UI_PlaceBuildingTab.RefreshFromAB() lists UnlockedBlueprints
               → UI_JoinRequestsTab.RefreshFromAB() reads AB.PendingJoinRequests + subscribes OnListChanged

Tier-up:
   Leader clicks Promote (TierUpTab button)
       → AdministrativeBuilding.RequestPromoteLevelServerRpc()
           Server: ResolveCharacterFromClientId → IsLeader gate
                → Community.TryPromoteLevel(this):
                       reads CommunityTierRegistry.GetForNextLevelFrom(level)
                       checks Population / Treasury / RequiredBuildings (duplicate-aware)
                       on success: ChangeLevel(nextLevel)
                → TierUpResultClientRpc(ok, message) (single-client target)
                       Plan 4c follow-up: route to a proper toast channel.

Civic placement:
   Leader clicks a blueprint Place button (PlaceBuildingTab row)
       → v1: logs intent (cursor-mode hand-off TODO).
       Future: PlayerController.BeginCityPlacementMode(blueprint) →
                cursor raycasts world → BuildingGrid snap → click confirm →
                AB.PlaceCityBlueprintServerRpc(blueprint.PrefabId, targetCell)
   Server: leader gate → Civic gate → tier-unlock gate → BuildingGrid.CanPlace gate
        → BuildingGrid.GetCellCenter(cell, ab.transform.position.y)
        → BuildingPlacementManager.PlaceCivicBuildingForLeader(blueprint, leader, worldPos, identity):
              Instantiate prefab → set PlacedByCharacterId → NetworkObject.Spawn
              → RegisterBuildingWithMap (existing flow; parents to map, adds to CommunityData)
        → community.ownedBuildings.Add(newBuilding)
        → foreach leader: newBuilding.AddOwner(leader)
        → hostMap.BuildingGrid.Register(newBuilding.NetworkObjectId, cell, footprint)
        → LogisticsManager.AddBuildOrder(new BuildOrder(target=newBuilding, host=this, client=leader, day))
           → JobLogisticsManager.ProcessActiveBuildOrders consumes on next tick (Plan 4b)
           → JobBuilder fetches material from AB storage → walks to site → drops → constructs
        → PlaceCityBlueprintResultClientRpc(ok, message)

Daily migration:
   TimeManager.OnNewDay fires (server-only)
       → DrifterMigrationSystem.HandleNewDay() on each MapController
           For each chartered community whose AB resolves to THIS map:
              For 0..MaxDriftersPerCommunityPerDay:
                 PickRandomMapEdgePoint → NavMesh.SamplePosition
                 If _drifterPrefab wired: Instantiate + NetworkObject.Spawn
                 movement.SetDestination(ab.transform.position)
       → drifter walks to AB
       → drifter interacts with AB's JoinRequestDesk

Join-request submission:
   JoinRequestDesk.OnInteract(drifter)
       → AB.SubmitJoinRequestServerRpc(applicantNetId)
           Server: validates not-already-member + not-already-citizen
                → PendingJoinRequests.Add(JoinRequest{applicantNetId, day})
                → NetworkList replicates to leader's UI subscribers
       → base.OnInteract queues CharacterAction_OccupyFurniture (drifter sits and waits)

Leader processes:
   Leader clicks Accept or Decline on a UI_JoinRequestRow
       → AB.AcceptJoinRequestServerRpc(applicantNetId)
           Server: leader gate
                → AddMember + JoinCommunity + SetCitizenship + ClearCurrentAction
                → PendingJoinRequests.Remove (NetworkList replicates removal)
       OR AB.DeclineJoinRequestServerRpc(applicantNetId)
           Server: leader gate → just removes + releases the seat lock.
```

## Dependencies

### Upstream
- [[administrative-building]] — workplace, OwnerCommunity, IsLeader gate, treasury accessors.
- [[world-community]] — Community POCO, ChangeLevel, members, ownedBuildings.
- [[building-grid]] — per-MapController BuildingGrid (CanPlace, GetCellCenter, Register).
- [[job-builder]] — Plan 4b BuildOrder consumer; civic placement auto-creates a BuildOrder it picks up.
- [[player-hud]] — UI_WindowBase (rule #39), PlayerUI flat-façade pattern.
- [[citizenship]] — `CharacterCommunity.SetCitizenship` + `JoinCommunity`.

### Downstream
None in v1. Future Plan 4d / 5 may add: AB.prefab preplaced furniture authoring, RTS placement cursor in PlayerController, BuildOrder persistence, dedicated `Community.Level` NetVar, NPC-leader Accept/Decline heuristics, cinematic tier-up effects.

## State & persistence

- `CommunityTierRequirementsSO` assets — designer-authored ScriptableObjects in `Resources/Data/CommunityTiers/`. Read-only at runtime.
- `Community.level` — POCO field, persisted via existing `CommunityData` save round-trip; replicated to clients on map load.
- `AdministrativeBuilding.PendingJoinRequests` — server-only `NetworkList<JoinRequest>`. Standard NGO replication handles late-joiners. NOT persisted across server restarts in v1 (requests rebuild from drifter walks if applicable; otherwise discarded).
- `BuildOrder` instances on `BuildingLogisticsManager._activeBuildOrders` — server-only list. Same Plan 4b deferral: not persisted across server restarts. PlayMode cycles are stable.

**Zero new save schema** — all surfaces reuse pre-Plan-4c persistence.

## Network rules

All mutations server-only:
- `Community.TryPromoteLevel` POCO method called from server-only RPC entry.
- `DrifterMigrationSystem.HandleNewDay` early-returns on `!IsServer`.
- All AB RPCs are `[ServerRpc(RequireOwnership = false)]` — UI / NPC code calls them on the server-side mutation path.

Replication paths:
- `AdministrativeBuilding.PendingJoinRequests` (NetworkList) — UI_JoinRequestsTab subscribes to `OnListChanged` for live refresh.
- `Building.ConstructionProgress` (existing NetVar) — drives the in-place build site progress bar after civic placement.
- `Community.level` — via save round-trip (Plan 4c follow-up may add a dedicated NetVar; v1 uses `TierUpResultClientRpc` to surface the change immediately + scheduled CommunityData snapshot for late-joiners).

Per rule #19 validated scenarios:
- Host↔Client: host runs migration + tier-up; client sees the AB.PendingJoinRequests list refresh live + Building.ConstructionProgress fill on civic placement.
- Client↔Client: not applicable (server-authoritative).
- Host/Client↔NPC: NPC leader Accept/Decline route through the same server methods; NPC drifters walk to JoinRequestDesk + interact normally.

## Common pitfalls

- **`BuildingSO.GridFootprintCells` must be non-zero** for CanPlace to succeed. Defaults to (1,1); civic blueprints can be larger (AB = 3×3).
- **`CommunityTierRequirementsSO.UnlockedBlueprints` defaults to empty in the v1 bootstrap** — designers must populate per tier in the Inspector, and flip the matching `BuildingSO._blueprintCategory` to `Civic`. Without this, the admin-console PlaceBuildingTab shows "no civic blueprints unlocked at this tier" for every level.
- **`DrifterMigrationSystem._drifterPrefab` must be wired** in the Inspector on the MapController. Until set, the system logs `"would spawn 1 drifter ..."` and skips. Pipeline doesn't break; the chartered city just doesn't gain drifters until designers wire a prefab.
- **`JoinRequestDesk` + `CityManagementFurniture` baked into AB.prefab require the rule #19b spawn-race late-binding** (`TryRegisterWithAB`). Without it, `GetComponentInParent` in Awake may race the AB's parenting and silently no-op.
- **`PlayerUI._cityManagementWindow` SerializeField must be wired** in the scene (Task 8 follow-up). Until then `OpenCityManagementWindow` logs a clear warning and returns — tap-E on `CityManagementFurniture` silently no-ops.
- **Plan 4c does NOT ship the RTS placement cursor mode** in PlayerController. Civic placement's UI Place button logs the intent; the server method works correctly when called directly (debug tool). The cursor mode is a separate follow-up.

## Open questions / TODO

- **AB.prefab authoring (manual — 5-minute Editor task for Kevin)**: Roslyn-authoring a fully-functional Building.prefab is fragile (many private SerializeFields on Building/CommercialBuilding that lose state on script swap). Use this manual recipe instead:
    1. In Unity Project view, right-click `Assets/Prefabs/Building/Commercial/Shop/Shop.prefab` → Duplicate.
    2. Rename the copy `AdministrativeBuilding.prefab` and move to `Assets/Prefabs/Building/Commercial/AdministrativeBuilding/`.
    3. Open it → Inspector: remove `ShopBuilding` component → Add Component → `AdministrativeBuilding`.
    4. Re-assign the inherited fields that didn't transfer:
       - `_blueprint` → `Assets/Resources/Data/Buildings/AdministrativeBuilding.asset`
       - `_buildingZone` → the existing `BuildingZone` child collider
       - `_completedVisualRoot` / `_constructionVisualRoot` → existing children (carried over from Shop layout)
       - `_ownerIds` → leave NetworkList default
    5. Delete the Shop-specific children (Cashier, sell shelves) — these don't belong on an AB.
    6. Add 4 child GameObjects with the new furniture scripts:
       - `CityManagement` GameObject + `CityManagementFurniture` component + child collider.
       - `JoinRequestDesk` GameObject + `JoinRequestDesk` component + child collider + child interaction point Transform.
       - `Safe` GameObject + existing `SafeFurniture` component (or drag in the existing safe furniture prefab).
       - `Storage1` + `Storage2` GameObjects each with `StorageFurniture` component (or use existing prefabs).
    7. Open `Assets/Resources/Data/Buildings/AdministrativeBuilding.asset` → set `_buildingPrefab` to the new `AdministrativeBuilding.prefab`.
    8. Save the prefab + asset. The full end-to-end city-founding loop now works.
- **Plan 4c follow-up — UI polish**: visual styling (fonts, panel theming, button colors) on UI_CityManagementPanel.prefab and the two row prefabs. The scaffold renders functionally; visual polish is a designer pass.
- **PlayerController cursor mode**: `BeginCityPlacementMode(BuildingSO)` + cursor raycast + BuildingGrid snap + green/red cell highlight + ESC cancel + click-to-confirm.
- **BuildOrder persistence** across server restarts.
- **`Community.Level` dedicated NetVar** (vs save-round-trip + per-action ClientRpc v1 stopgap).
- **NPC-leader Accept/Decline heuristics** (relation / personality / city food capacity).
- **Tier-up cinematic / banner** effect.
- **Renounce-then-rejoin citizenship UX**.
- **Multi-leader concurrent edit** — last-write-wins for v1; future: optimistic concurrency.

### Playtest hooks (2026-05-18 — Dev-Mode City Founding sub-tab)

The full city-founding loop can be driven without authoring content or waiting for the natural in-game cadence. Enter dev mode (F3 in Editor / `/devmode on` in release builds), Ctrl+Click an NPC or your own character to inspect it, then open the **Founding** sub-tab (11th tab on `CharacterInspectorView`). Lives at [`CharacterCityFoundingSubTab.cs`](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/CharacterCityFoundingSubTab.cs).

| Button / Section | Effect | Routes through |
|---|---|---|
| **Create Community** *(when target has no CurrentCommunity)* | Founds a new SmallGroup-tier community. Auto-grants the AB blueprint. | `CharacterCommunity.CreateCommunity(name)` |
| **Assign Ambition_FoundACity** | Loads the SO from `Resources/Data/Ambitions/Ambition_FoundACity` and queues it. BT picks it up next tick. | `CharacterAmbition.SetAmbition(so)` |
| **Community readout** | Read-only: name, level, IsChartered, members/leaders, AB ref, treasury (CurrencyId.Default). | — |
| **Force-Promote ±1 Tier** | Bypasses TryPromoteLevel's population / treasury / required-building gates. | `AdministrativeBuilding.DevForceChangeCommunityLevel(int delta)` |
| **Grant Treasury N** | Credits N units of the enclosing map's NativeCurrency to the AB's Treasury safes. | `CommercialBuilding.DevForceCreditTreasury(int amount)` → canonical `CreditTreasury(currency, amount, reason)` |
| **Submit Join Request** *(when target is in no community + ≥1 chartered AB exists)* | Submits a join request as the inspected character. Identical to the drifter ↔ JoinRequestDesk path. | `AdministrativeBuilding.SubmitJoinRequestServerRpc(applicantNetId)` |
| **Force NewDay N** | Pumps `OnNewDay` N times → drives `DrifterMigrationSystem` daily-roll → one drifter NPC per chartered community per tick. | `MWI.Time.TimeManager.DevForceNewDay(int count)` |

Each mutator is host-only and DevMode-gated via the inherited `CommercialBuilding.DevAssertHostAndDevMode` helper (or an equivalent inline guard on `TimeManager` which is not a `NetworkBehaviour`). Every state mutation flows through the same paths production callers use — no new replication channels are introduced. See [`.agent/skills/dev-mode/SKILL.md` §11](../../.agent/skills/dev-mode/SKILL.md) for the full Dev-Mode contract.

**Recommended end-to-end smoke sequence:**
1. Spawn an NPC via the Spawn tab (or use the host player).
2. Inspect them → Founding sub-tab → **Create Community**.
3. **Assign Ambition_FoundACity** → wait for the NPC's BT to walk through the planning step + place the AB (or place it via player click for the host).
4. After the AB is placed, finish construction (cooperative finalize or via Build Inspector's Force Finalize).
5. **Grant Treasury 1000** for sandbox spend money.
6. **Force-Promote +1 Tier** to unlock the next tier's blueprints.
7. **Force NewDay** a few times → drifters should arrive and queue join requests at the JoinRequestDesk.
8. Inspect a drifter who hasn't applied yet → **Submit Join Request** to bypass the JoinRequestDesk walk-up step manually.

## Change log

- 2026-05-18 — **Plan 4c scripts + content shipped.** Commits `9eafe529..` on `multiplayyer`: Plan 4c doc, CommunityTierRequirementsSO + 7 tier assets, Community.TryPromoteLevel + AB.RequestPromoteLevelServerRpc, AB.PlaceCityBlueprintServerRpc + BuildingPlacementManager.PlaceCivicBuildingForLeader + BuildingGrid.GetCellCenter, DrifterMigrationSystem, JoinRequest struct + JoinRequestDesk + AB submit/accept/decline RPCs, CityManagementFurniture + UI_CityManagementPanel + 3 tabs + 2 row scripts + PlayerUI surface. 214 EditMode tests pass. Prefab variants (UI + AB) + PlayerController RTS cursor mode deferred to follow-up. — claude
- 2026-05-18 — **Dev-Mode playtest hooks shipped.** New 11th `CharacterCityFoundingSubTab` on the Character inspector with 7 sections (Create Community / Assign Ambition_FoundACity / Community readout / Force-Promote / Grant Treasury / Submit Join Request / Force NewDay). Three new DevForce* methods: `AdministrativeBuilding.DevForceChangeCommunityLevel`, `CommercialBuilding.DevForceCreditTreasury`, `MWI.Time.TimeManager.DevForceNewDay`. `CharacterSubTab.Refresh` is now virtual so widget-based sub-tabs can bypass the text-only `RenderContent` contract. 214/214 EditMode tests pass. — claude
- 2026-05-18 — **Ambition-driven autonomous founding shipped.** New `BTAction_PursueAmbition` ([Assets/Scripts/AI/Actions/BTAction_PursueAmbition.cs](../../Assets/Scripts/AI/Actions/BTAction_PursueAmbition.cs)) closes Plan 4b's gap: previously the passive ambition tasks (`Task_PlaceBuilding` / `Task_FinishConstruction` / `Task_PromoteCommunity`) only watched world state and nothing drove them, so an NPC assigned `Ambition_FoundACity` just wandered. The new BTAction sits between Schedule (priority 6) and GOAP (priority 7) on `NPCBehaviourTree`; each tick it reads `CharacterAmbition.Current.CurrentStepQuest`, dispatches to a per-task driver (Place / Finalize / Promote), then pumps `AmbitionQuest.TickActiveTasks` so active tasks (`Task_CreateCommunity`) and passive watchers advance the state machine. Drivers route through canonical paths (`BuildingPlacementManager.PlaceCivicBuildingForLeader` + `BuildingGrid.Register`, `CharacterAction_FinishConstruction`, `Community.TryPromoteLevel`); no new replication channel. 214/214 EditMode tests pass. — claude
- 2026-05-18 — **Autonomous material gather + carry + drop loop shipped.** `BTAction_PursueAmbition.DriveFinishConstruction` reworked from a stub-walk-into-zone into a 6-step gather state machine: (1) carrying a relevant item → walk to BuildingZone + `CharacterDropItem`; (2) loose `WorldItem` nearby matches a missing req → walk + `CharacterPickUpItem`; (3) cached `_targetHarvestable` still yields a missing item → walk to its `InteractionZone` + `CharacterHarvestAction`; (4) scan `CharacterAwareness.GetVisibleInteractables<Harvestable>` for a yielder of any missing material; (5) inside zone with consumable items → `CharacterAction_FinishConstruction`; (6) empty awareness → `NavMesh.SamplePosition` wander within ~18u, re-rolled every 4-8s, so the NPC actually drifts looking for a tree. Persistent across ticks: `_targetHarvestable`, `_wanderDestination`, `_wanderUntilTime` — cleared on `OnEnter`/`OnExit` so a BT branch switch (combat, hunger) restarts cleanly. Rule #34 alloc discipline (`Dictionary<ItemSO,int>` + `Collider[32]` cached instance fields, no LINQ). Rule #22 parity — every world mutation flows through existing canonical CharacterActions, no new ServerRpcs. 214/214 EditMode tests pass. — claude
- 2026-05-18 — **Refactored ambition Finalize step from BTAction to GOAP** (per .agent/skills/goap/SKILL.md "GOAP as Life Manager" architectural principle). New `NeedAmbitionFinishConstruction` (POCO `CharacterNeed`, registered alongside `NeedJob` in `CharacterNeeds.Awake`, urgency 75 — above `NeedJob`'s 60) supplies `GoapGoal { ambitionBuildingFinalized=true }` + composite `GoapAction_FulfillAmbitionConstruction` to `CharacterGoapController.Replan`. The GoapAction's `Execute()` runs the same 5-step state machine (carry-and-drop / pickup / harvest-cached / scan-awareness / consume) — every world mutation still flows through the canonical `CharacterHarvestAction`, `CharacterPickUpItem`, `CharacterDropItem`, `CharacterAction_FinishConstruction` atoms. `BTAction_PursueAmbition` now returns `BTNodeStatus.Failure` for `Task_FinishConstruction` so the BT Selector falls through to GOAP. Outcome: the goal + action surface visibly in the per-character GOAP inspector (closing the "his goap goal and action is at none" gap from the prior BTAction-only path); wander emerges from BT fallback (Ambition → GOAP → Wander) when awareness is empty, no duplicate wander code. `Task_PlaceBuilding` + `Task_PromoteCommunity` stay driven inline by the BTAction since they're short atomic operations. — claude
- 2026-05-18 — **Tier identity migrated from `CommunityLevel` enum to `CommunityTierRequirementsSO` reference.** Designers can now drop a new SO into `Resources/Data/CommunityTiers/`, assign an `Order` field to slot it into the ladder (e.g. `Order=2.5` for a Province tier between Camp and Village, or `Order=7` past Empire), and gameplay paths pick it up — no enum edit, no recompile. The enum is preserved as a legacy display hint + back-compat shim; authoritative reads use `Community.CurrentTier` (SO ref) + `currentTierId` (serializable string for save data). New SO fields: `TierId` (stable string, defaults to asset name), `Order` (int — `GetNext` / `GetPrevious` walk by ascending), `DisplayName`, `MinHappyPopulationFraction` (new gate, v1 stub treats all citizens as happy until a mood / `NeedHappiness` system ships). `CommunityTierRegistry` extended with `GetById`, `GetByOrder`, `GetNext`, `GetPrevious`, `AllAscending`; legacy `Get(CommunityLevel)` + `GetForNextLevelFrom` kept as thin shims. `Community.ChangeTier(SO)` is the new authoritative mutator; `ChangeLevel(enum)` keeps working (routes through `ChangeTier` via registry lookup). `TryPromoteLevel` walks `GetNext(CurrentTier)` instead of enum +1. `Task_PromoteCommunity` got `TargetTier` (SO ref) + `TargetTierId` (string) fields — priority `TargetTier → TargetTierId → TargetLevel` — and compares by `Order` so off-enum tiers sequence correctly. `AdministrativeBuilding.DevForceChangeCommunityLevel` walks the SO ladder via `GetNext` / `GetPrevious`. UI displays render `CurrentTier.DisplayName` (with enum fallback). 7 existing tier asset YAMLs migrated; orders match the previous enum values for save-data continuity. 214/214 EditMode tests pass. — claude
- 2026-05-18 — **Polish iterations from end-to-end playtest** (single playtest session, 12 commits between `8ca17d80` and `6cc5de1c`): (a) `GoapAction_FulfillAmbitionConstruction` now also considers `Harvestable.DestructionOutputs` (two-pass: harvest-yield first, destroy-yield second), so the founder chops an apple tree for Wood via `CharacterAction_DestroyHarvestable` even though the tree's `HarvestOutputs` is Apple — the YieldMode enum + `ClassifyYield` + `CanNpcDestroy` gates `AllowDestruction` + `AllowNpcDestruction`. (b) `FindCarriedRelevantItem` also inspects `HandsController.CarriedItem` after the bag inventory walk — NPCs without a bag end up with picked-up items in hand and the prior check missed them, causing pick-drop loops near the next loose Wood. (c) `FindNearbyMatchingWorldItem` radius bumped 4u→12u + closest-match selection + `FindObjectsByType` fallback for the rare collider-lifecycle race after a fresh WorldItem spawn. (d) BuildingZone center projected to actor Y before `SetDestination` — zone BoxColliders are tall (extents.y ~7.5u), so feeding `bounds.center.y` to NavMesh.SamplePosition's 5m sample tolerance was rejecting the destination as off-NavMesh. (e) "Drop only near zone center" — added `dropDistanceFromCenter` gate (smaller of 1.5u or half-extent − 1u) so the `±0.3u` random offset from `CharacterDropItem.ExecutePhysicalDrop` can't land the item outside the collider; without this the actor dropped at the boundary, the item landed outside, Step 2 re-picked it forever. (f) Step 0.5 promoted: consume-zone-items now fires BEFORE drop/pickup so a fresh drop gets consumed immediately rather than re-picked. Step 2 also explicitly skips WorldItems inside the BuildingZone. (g) `CharacterAction_FinishConstruction.MaxStallTicks` 5→1: autonomous loops pay the entire stall window between every delivery; reducing to one stall tick saves ~4 real seconds (~5 in-game minutes) per consumed item. (h) Step 5 scene-wide scan (no distance cap) + Step 5b active exploration wander at 36–60u when the scene is empty of viable sources — replaces the BT Wander fallback (which picks within 50u of current position and often stays near the zone) with a GoapAction-internal wander that actually covers ground and brings fresh tiles into awareness on every iteration. — claude

## Sources

- [docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md](../../docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md) — full design spec.
- [docs/superpowers/plans/2026-05-18-city-admin-console-migration-tier-up.md](../../docs/superpowers/plans/2026-05-18-city-admin-console-migration-tier-up.md) — Plan 4c implementation plan.
- [[administrative-building]] — workplace, ServerRpcs entry surface.
- [[job-builder]] — Plan 4b BuildOrder consumer; Plan 4c's auto-BuildOrder cascades into it.
- [[building-grid]] — per-MapController grid + Plan 4c's GetCellCenter addition.
- [[player-hud]] — UI_WindowBase rule #39 conformance.
- 2026-05-18 conversation with [[kevin]].
