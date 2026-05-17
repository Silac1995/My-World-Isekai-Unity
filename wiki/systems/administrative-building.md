---
type: system
title: "Administrative Building"
tags: [building, community, city-founding, commercial]
created: 2026-05-17
updated: 2026-05-18
sources:
  - "[AdministrativeBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs)"
  - "[Building.cs](../../Assets/Scripts/World/Buildings/Building.cs)"
  - "[Community.cs](../../Assets/Scripts/World/Community/Community.cs)"
  - "[BuildingPlacementManager.cs](../../Assets/Scripts/World/Buildings/BuildingPlacementManager.cs)"
related:
  - "[[world-community]]"
  - "[[character-community]]"
  - "[[citizenship]]"
  - "[[building-grid]]"
  - "[[found-a-city-ambition]]"
status: wip
confidence: high
primary_agent: building-furniture-specialist
secondary_agents: []
owner_code_path: Assets/Scripts/World/Buildings/CommercialBuildings/
depends_on:
  - "[[building]]"
  - "[[world-community]]"
  - "[[building-grid]]"
depended_on_by:
  - "[[found-a-city-ambition]]"
---

# Administrative Building

## Summary
The **AdministrativeBuilding** (AB) is the city-charter building. One per community by
design. On completion (`Building.Finalize` → `OnFinalize`), it grants the founder
citizenship of the host community — the founding gesture that flips a community from
"informal group" to "chartered city". Plan 4a ships the skeleton; Plan 4b adds the
job pipeline (`JobBuilder` × 2 + `JobHarvester(CityHarvester)` + `JobLogisticsManager`);
Plan 4c adds the preplaced furniture (CityManagement console, JoinRequestDesk,
city-treasury safe, material storages), the drifter-migration ticker, and the
admin-console UI.

## Purpose
The AB is the *physical embodiment* of a community's charter. A community is chartered
iff its `Community.AdministrativeBuilding != null`. The AB:
- **Carries the charter**: while the AB exists, the community has civic status (Plan 4c
  drifter migration ticks for chartered communities; Plan 5's admin console opens
  against the AB's CityManagementFurniture).
- **Holds the city treasury**: a SafeFurniture with the Treasury role lives inside the
  AB (Plan 4c authors the preplaced layout). `AdministrativeBuilding.GetTreasuryBalance(currency)`
  is the canonical "city wealth" read.
- **Hosts the city jobs**: `JobBuilder × 2`, `JobHarvester(CityHarvester)`, and
  `JobLogisticsManager` are initialised on the AB (Plan 4b). Citizens hired via the
  admin-console Hiring tab fill these slots.
- **Grants citizenship**: on construction-complete, the founder gets `SetCitizenship`
  pointing at the AB's `OwnerCommunity`. Future join-request accepts (Plan 4c) reuse
  the same `SetCitizenship` setter.

## Responsibilities
1. Identify itself as `BuildingType.Administrative`.
2. Track its `OwnerCommunity` (server-side ref, set on placement).
3. Auto-bind every community leader as an owner of the AB so any leader can interact
   with leader-gated AB features.
4. On `OnFinalize`, grant the founder citizenship of `OwnerCommunity`.
5. Pass through to `CommercialBuilding.GetTreasuryBalance(currency)` for the city
   treasury read.
6. (Plan 4b) Initialise the four city-job slots.
7. (Plan 4c) Host the preplaced furniture surface + replicate `PendingJoinRequests`.

## Key classes / files
- `Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs` — the
  subclass.
- `Assets/Scripts/World/Buildings/BuildingType.cs` — the `Administrative` enum entry.
- `Assets/Scripts/World/Buildings/Building.cs` — the `OnFinalize` virtual hook the AB
  overrides.
- `Assets/Scripts/World/Community/Community.cs` — `AdministrativeBuilding` ref +
  `IsChartered` getter.
- `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs` — 1-per-community gate +
  auto-SetOwnerCommunity wiring.
- `Assets/Resources/Data/Buildings/AdministrativeBuilding.asset` — the BuildingSO
  (3×3 footprint, BlueprintCategory.Personal so the founder places via the normal
  ghost flow pre-charter). Prefab reference NULL until Plan 4c authors the .prefab.

## Public API
| Member | Purpose |
| --- | --- |
| `BuildingType` (override) | Returns `BuildingType.Administrative`. |
| `OwnerCommunity` | Server-only `Community` ref; set on placement. |
| `SetOwnerCommunity(Community)` | Server-only mutator. Idempotent. Back-points `community.AdministrativeBuilding = this`, auto-binds leaders as owners. |
| `GetTreasuryBalance(CurrencyId)` | Reads from the inherited CommercialBuilding treasury (sum of every Treasury-role SafeFurniture under this AB). |
| `OnFinalize()` override | Grants founder citizenship via `CharacterCommunity.SetCitizenship`. |

## Data flow
```
Placement (founder clicks ghost):
  BuildingPlacementManager.ValidatePlacement(position)
    ├─ Gate 1-5: existing (range / obstacle / region / community-permission / grid)
    └─ Gate 6 (NEW): if blueprint is Administrative AND founder community.IsChartered → reject
  BuildingPlacementManager.RequestPlacementServerRpc(...) — existing path
  Server spawns the AB GameObject; OnNetworkSpawn fires.
  RegisterBuildingWithMap(...) (server post-spawn):
    ├─ Parent to MapController
    ├─ Add to CommunityData.ConstructedBuildings (save data)
    └─ if (building is AdministrativeBuilding ab):
         resolve founder via PlacedByCharacterId → Character.FindByUUID
         ab.SetOwnerCommunity(founder.CharacterCommunity.CurrentCommunity)
           ├─ OwnerCommunity = community
           ├─ community.AdministrativeBuilding = this  (→ IsChartered = true)
           └─ TryBindLeadersAsOwners()  (every leader → Room.AddOwner(leader))

Construction (cooperative — founder works the site):
  CharacterAction_FinishConstruction (Phase 1 loop)
  BuildingProgress reaches 1f → Building.Finalize() runs server-side
    ├─ _currentState.Value = Complete (replicated to clients)
    ├─ ConstructionProgress.Value = 1f
    └─ OnFinalize() — subclass hook (try/catch wrapped per rule #31)
         AdministrativeBuilding.OnFinalize:
           founder = Character.FindByUUID(PlacedByCharacterId)
           founder.CharacterCommunity.SetCitizenship(OwnerCommunity)
```

## Dependencies
- **`Building`** (base) — provides `Finalize()`, `OnFinalize` hook, `PlacedByCharacterId`.
- **`CommercialBuilding`** (parent) — `GetTreasuryBalance(currency)`, jobs roster.
- **`Room`** (grandparent) — `AddOwner(Character)` multi-owner mutator.
- **`Community`** — the chartered entity; AB calls `community.AdministrativeBuilding = this`.
- **`CharacterCommunity`** — `SetCitizenship` setter (Plan 1).
- **`Character.FindByUUID`** — used to resolve the founder during `OnFinalize`.
- **`BuildingPlacementManager`** — the placement entry point + the 1-per-community gate.

## State & persistence
- `OwnerCommunity` is **server-only** state, not persisted directly. Reconstructed on
  load: when the AB respawns, `RegisterBuildingWithMap`'s AB-binding block re-resolves
  the founder (via `PlacedByCharacterId`) and re-calls `SetOwnerCommunity`. Plan 4a
  documents a small gap: if the founder character is hibernated on load, the rebinding
  defers until the founder spawns (no automatic retry yet — Plan 4c adds a retry hook).
- `Community.AdministrativeBuilding` is `[NonSerialized]` — a plain C# ref on a plain
  C# class. Rebuilt on load via the same `SetOwnerCommunity` path.
- Founder citizenship persists via `CharacterCommunity.Serialize` (Plan 1's
  `CommunitySaveData.citizenshipMapId`).

## Known gotchas / edge cases
- **1-per-community gate is server-authoritative**; client-side is optimistic-green
  (the client cannot see the founder's `Community.AdministrativeBuilding` ref). Server
  toast handles the real rejection. Same compromise as `BuildingGrid.CanPlace` (Plan 2).
- **The AB.prefab does not yet exist** (Plan 4a ships only the BuildingSO scaffold).
  Runtime placement attempts will warn until Plan 4c authors the prefab and links it
  on the BuildingSO.
- **`OnFinalize` is wrapped in try/catch** so a buggy override never blocks the
  Complete transition. Don't rely on exceptions from `OnFinalize` propagating up to
  callers — they are logged + swallowed (rule #31).
- **Re-binding to a different community is forbidden** — `SetOwnerCommunity` rejects
  a swap with a warning log. To re-charter, the AB must be destroyed and re-placed.
- See [[singular-leader-vs-multi-leader-isleader]] for the `IsLeader(c)` gotcha that
  applies to every leader-gated AB feature (Plan 5).

## Open questions / TODO
- *Plan 4b*: `InitializeJobs` — add JobBuilder × 2 + JobHarvester(CityHarvester) +
  JobLogisticsManager to the AB.
- *Plan 4c*: AB.prefab — preplaced CityManagementFurniture + JoinRequestDesk +
  SafeFurniture (Treasury) + N StorageFurniture (material stockpile).
- *Plan 4c*: `NetworkList<JoinRequest> PendingJoinRequests` — replicated state for
  the admin console.
- *Plan 4c*: `_unfulfillableMaterialHarvestQueue` — server-side queue read by
  JobHarvester's CityHarvester variant.
- *Plan 4c*: hibernation-aware founder citizenship grant — if the founder is
  hibernated when the AB completes, the citizenship grant currently no-ops with a
  warning. A `Character.OnCharacterSpawned` retry hook would fix it.
- *Plan 5*: server replication of `OwnerCommunity` (e.g. a NetworkVariable<ulong> for
  the community-net-id) so client UIs can display "which community owns this AB?"
  without round-tripping through `_ownerIds`.

## Change log
- 2026-05-17 — Plan 4a skeleton shipped: BuildingType.Administrative enum, AdministrativeBuilding subclass with OwnerCommunity + SetOwnerCommunity + GetTreasuryBalance + OnFinalize override (citizenship grant), Building.OnFinalize virtual hook, Community.AdministrativeBuilding + IsChartered, BuildingPlacementManager 1-per-community gate + auto-SetOwnerCommunity wiring, AdministrativeBuilding.asset scaffold, Ambition_FoundACity.asset + 8 quest assets. — claude
- 2026-05-18 — Plan 4b: InitializeJobs now staffs the AB with JobBuilder × 2 + JobHarvester + JobLogisticsManager. New unfulfillable-material harvest queue (`_unfulfillableMaterialHarvestQueue`, `EnqueueUnfulfillableMaterial`, `GetUnfulfillableHarvestQueue`, `DecrementUnfulfillableMaterial`) backs the JobLogisticsManager → JobHarvester handoff for materials the logistics chain couldn't source. JobLogisticsManager.ProcessActiveBuildOrders cascade ships (B2B → producer → virtual → unfulfillable-queue) along with four NEW JobBuilder GoapActions and the JobBuilder class itself. JobHarvester's CityHarvester runtime branch is stubbed (logs queue contents; physical harvest cascade deferred to a follow-up). — claude

## Sources
- [AdministrativeBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs)
- [Building.cs](../../Assets/Scripts/World/Buildings/Building.cs) — `OnFinalize` virtual hook + the `Finalize()` invocation.
- [Community.cs](../../Assets/Scripts/World/Community/Community.cs) — `AdministrativeBuilding` ref + `IsChartered`.
- [BuildingPlacementManager.cs](../../Assets/Scripts/World/Buildings/BuildingPlacementManager.cs) — gate #6 + AB binding in `RegisterBuildingWithMap`.
- [docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md](../../docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md) §`AdministrativeBuilding` (line 492) — design source.
- [docs/superpowers/plans/2026-05-17-administrative-building-skeleton.md](../../docs/superpowers/plans/2026-05-17-administrative-building-skeleton.md) — Plan 4a implementation.
