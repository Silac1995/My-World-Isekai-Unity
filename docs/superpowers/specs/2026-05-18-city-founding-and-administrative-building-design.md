# City Founding & Administrative Building System — Design

**Date:** 2026-05-18
**Status:** Spec — awaiting plan
**Scope:** Founder character (player or NPC, ambition-driven) creates a Community on an empty map, places an `AdministrativeBuilding` (new `CommercialBuilding` subclass) that charters the community as a "city," and then directs city construction via an RTS-style admin console. Hired `JobBuilder`s + `JobHarvester`s + `JobLogisticsManager` at the AB execute `BuildOrder`s (new order type) sourcing materials via the existing logistics chain (real shops → CraftingBuildings → VirtualResourceSupplier → fallback physical harvest). Migration of new citizens via a `JoinRequestDesk` furniture and a daily `TimeManager.OnNewDay` drifter spawn. Tier-up gated on population + buildings + treasury, manually triggered from the admin console. New `BuildingGrid` per `MapController` snaps city building placement to 8-unit cells.

**Out of scope (deferred / future):**
- Crafting-as-sourcing chain for builders (covered by existing CraftingBuilding logistics — no new code; if no crafter exists for a tier-required material, harvest fallback covers it).
- `JobBuilder` skill progression (tier system already seated via `SkillId`).
- Private builder-company buildings (BuildOrder is designed to support this — non-AB hosts — but only AB hosts in v1).
- Population caps and territorial expansion (citizens just keep arriving; food bottlenecks via existing `CharacterNeeds.Hunger`).
- Founder starter shelter / starter food kit (Darwinian survival — founder must source from harvestables).
- Furniture grid snap (the existing `FurnitureGrid` doesn't snap on the cursor today; tracked as a separate follow-up).
- City-vs-city diplomacy, raids, taxes, edicts (`Order_Edict`-style civic commands).
- Player invocation UI for founding outside of dev tools (dev button suffices for v1; production UI later).

---

## Problem

The world today is full of authored maps that come pre-loaded with established communities, NPCs, and buildings. There is no gameplay loop for **starting from nothing**: spawn a single founder on an empty map (just `TerrainCell` grid + biome `Harvestable`s + maybe a `WildernessZone` or two) and grow a city around their ambition.

The pieces are all individually shipped:
- `Ambition` system (2026-05-02) — `AmbitionSO → QuestSO → TaskBase`, `BTAction_PursueAmbitionStep` drives the BT, `OverridesSchedule` flag, `IQuest` reuse.
- `Community` data class — `members`, `leader` (singular), `level : CommunityLevel`, `ChangeLevel(...)`, `AddMember`, `RemoveMember`, hierarchy + zones + ownedBuildings.
- `CharacterCommunity` — `CreateCommunity()`, `JoinCommunity(...)`, `LeaveCurrentCommunity()`, save/load.
- `CharacterBlueprints` — `_unlockedBuildingIds`, replicated tier of unlocked blueprints.
- `Phase 1 cooperative construction loop` (2026-05-06/07) — any character in `BuildingZone` can finish via `CharacterAction_FinishConstruction`; server-authoritative; persists.
- `BuildingLogisticsManager` (facade) + `LogisticsOrderBook` + `LogisticsTransportDispatcher` + `LogisticsStockEvaluator` — supply chain with `BuyOrder`, `TransportOrder`, `CraftingOrder`, B2B shop-buy preference (2026-05-09), reputation-weighted picking (2026-05-17).
- `JobFarmer`-shape precedent — goal cascade, `_scratchValidActions`, `ExecuteIntervalSeconds=0.3f`, GOAP plan + worldState predicates.
- `Shop loop` (2026-05-07/09/14) — `CharacterAction_BuyFromShop`, `Cashier` (OccupiableFurniture), `JobVendor` with seat-via-action.
- `Furniture occupancy via CharacterAction` (2026-05-14) — `CharacterAction_OccupyFurniture` is the canonical seating pattern.
- `Safe + Treasury` (through 2026-05-17) — `SafeFurniture`, `CharacterAction_DepositToSafe`, multi-owner safe-role assignment.
- `TerrainCellGrid` — world grid, 1-unit cells, crops snap to it.
- `FurnitureGrid` — per-room interior discrete grid with `Vector2Int sizeInCells` math.

What's missing is the **coordinating spine**: the chartering act (AB placement), the BuildOrder type that lets builders consume work in the same shape as transporters consume TransportOrders, the multi-leader migration on Community, the citizenship concept, the RTS-style admin console placement flow that snaps to a coarser building grid, the migration ticker that spawns drifters seeking citizenship, and the manual tier-up promotion. Plus the new furniture types (admin console, join-request desk) that surface the city's command surfaces to players.

## Goals

- **One coherent gameplay loop**: solo founder → community creation → AB placement (charter) → AB construction → hired builders + harvesters + logistics manager execute city construction → migration → tier-up. Same loop for player-founder and NPC-founder (Rule #22 parity throughout).
- **Ambition-driven decision**: `Ambition_FoundACity` (new `AmbitionSO`) authored once, drives both player and NPC. Quest 1 creates the community; Quest 2 builds the AB; Quest 3+ promotes through tiers. Player follows via quest-log UI + dev triggers; NPC follows via `BTAction_PursueAmbitionStep`.
- **Strip community-creation gates**: `CharacterCommunity.CheckAndCreateCommunity()` retains only the "not-already-leading" guard. Trait check (`CanCreateCommunity`) and friend-count check (≥4) are removed. The ambition itself is what makes the NPC decide; the player decides via UI. Anybody can create a community.
- **`AdministrativeBuilding : CommercialBuilding`** (NOT plain Building) — inherits jobs/wages/treasury/logistics. Hosts `JobBuilder × 2`, `JobHarvester × 1`, `JobLogisticsManager × 1` by default. Owners[] = community leaders. Preplaced furniture in prefab: admin console, city treasury safe, join-request desk, storage furniture. One per community (placement gate).
- **`BuildOrder : IQuest`** — new order type parallel to `BuyOrder`/`CraftingOrder`/`TransportOrder`. Non-expiring. Background-committed (`IsPlaced = true`). Created server-side when leader places a city blueprint via admin console. Drives `JobBuilder` GOAP + cascades to `JobLogisticsManager` `BuyOrder` placement for materials. Designed to support future non-AB hosts (private builder companies).
- **Multi-leader Community**: migrate `Community.leader` (singular) → `Community.leaders : List<Character>` (multi). Index 0 = primary; remainder = secondary leaders. Only primary can promote/demote secondaries; all leaders share admin console authority. Mirrors the multi-owner pattern on `Building._ownerIds`. Follows the lesson of the 2026-05-17 `singular-owner-vs-multi-owner-isowner` gotcha.
- **Citizenship**: `CharacterCommunity.Citizenship : Community` (new field, replicated, persisted). Granted on AB construction-complete (founder) or join-request accepted (migrants). At most 1 city at a time. V1 = member-of-chartered-community; placeholder for future rights (taxes, civic jobs, voting).
- **`JoinRequestDesk` furniture (NEW)**: preplaced inside AB. Migrants interact → enqueues a `JoinRequest` on the AB. Leader (player UI or NPC BT) processes accept/decline. NPC leaders auto-accept (v1 stub) until conditions are added.
- **Daily migration ticker**: `TimeManager.OnNewDay` event hook — spawns 1 generated drifter at a random map edge per chartered community on the map. Drifter intent: "walk to AB → interact with JoinRequestDesk → wait." No population cap (Darwinian — food shortage via existing `CharacterNeeds.Hunger` is the natural bound).
- **RTS-style placement via admin console**: leader opens admin console (any leader, primary or secondary) → picks tier-unlocked blueprint from list → cursor enters ghost-place mode on the world map → click confirms → server snaps to `BuildingGrid` cell → spawns building (UnderConstruction) → creates BuildOrder. Leader does not walk to the site.
- **`BuildingGrid` per `MapController`**: new per-map grid parallel to `TerrainCellGrid`. `CellSizeUnits = 8` (eight times the 1-unit crop/furniture cell). Buildings declare `BuildingSO.GridFootprintCells : Vector2Int` (e.g. House = 1×1, Shop = 2×1, AB = 3×3). Both pathways (founder's normal placement of AB + RTS placement from admin console) snap to this grid.
- **Tier-up criteria (C)**: per-tier `CommunityTierRequirementsSO` asset declaring `MinPopulation`, `RequiredBuildings : List<BuildingSO>`, `MinTreasury : int`, `UnlockedBlueprints : List<BuildingSO>`. Tier-up trigger (D): manual click from admin console, server-method `Community.TryPromoteLevel()` gates on the SO criteria. `Community.ChangeLevel()` stays guard-less internally — the gate is at `TryPromoteLevel`. Same path for player and NPC leader.
- **Personal vs civic blueprints**: `BuildingSO.BlueprintCategory : enum { Personal, Civic }` + `BuildingSO.MinTier : CommunityLevel?` (for Civic). Personal = placeable by any character via normal pathway (the AB itself + founder's starter kit, if any). Civic = placeable ONLY through admin console of a city at the matching tier.
- **Builder material sourcing**: full cascade — AB storage → BuyOrder to shops (via existing `LogisticsStockEvaluator.RequestStock` + B2B preference scan) → CraftingBuilding production → `VirtualResourceSupplier` (biome pool depletion) → physical harvest fallback by AB `JobHarvester` (uses `CharacterAwareness` to scan for nearby `Harvestable`s yielding the wanted item). Leader can opt-in to any step (Rule #22 parity).
- **Citizens explicitly do NOT participate** in city construction. They have their own jobs / lives. Leader-only + AB-employees-only build. Enforced passively (no GOAP goal for citizens targets construction sites).
- **Server-authoritative throughout** (Rule #18). All state mutations on the server. Clients see replicated state via existing NetworkLists / NetworkVariables. Multi-leader, citizenship, BuildOrder replication, BuildingGrid occupancy — all server-authored.
- **Late-join safe + persistence**: every new field saves through existing `ICharacterSaveData` / `BuildingSaveData` / `CommunityData` pipelines. Late-joining client receives the chartered state, citizenships, and BuildOrders on connection.

## Non-Goals

- **Skill-based builder speed.** `JobBuilder` consume budget = 1 (same Phase 1 default). `BuilderSkill` integration via `Character.GetSkillLevelOrZero(SkillId.Builder)` already seated; tuning the formula is a follow-up.
- **Custom Tier 0 founder kit.** Founder starts with AB blueprint only (granted on `CreateCommunity`). No starter shelter, no starter food. They forage from biome harvestables.
- **NPC-leader decision logic for accept/decline.** V1 = always accept. Future = personality / relation / population-pressure heuristics.
- **Population cap per tier.** No cap. The food bottleneck IS the cap.
- **City-vs-city interactions.** No diplomacy, no trade treaties, no raids. Each city is independent.
- **City taxes / tribute.** Citizens earn wages; AB doesn't claim a cut. Treasury fills from leader deposits + (future) civic jobs. No automatic taxation in v1.
- **Multi-map cities.** A city is per-map. A founder on Map A can't found a city that owns territory on Map B.
- **Building-grid SnapStep customization per blueprint.** Every building uses the same `CellSizeUnits = 8` from `BuildingGrid`. If you want a "wide road" (visually larger structure with 1-cell snap), do it via `GridFootprintCells = (4, 1)` not via per-blueprint cell size.
- **Furniture grid snap.** Existing `FurnitureGrid` math supports snap via `GetPlacementPositions(cursorWorldPos, sizeInCells, ...)` but the cursor-driven snap UX isn't wired today. Tracked as separate follow-up; out of this spec.
- **Demolition + refund + ownership transfer of city buildings.** Phase Next.
- **Multi-AB cities.** Hard-gate: one AB per community.
- **Migrant origin stories.** Drifters are generated, disposable, no save-file footprint outside this map. A future "hibernated NPC from neighboring map" path can layer on without touching the core spawn loop.

---

## Architecture

### Component map

```
Founder Layer
─────────────
AmbitionSO (existing layer)
  └─ Ambition_FoundACity (NEW AmbitionSO asset, OverridesSchedule = true)
        ├─ Quest 1: Create Community           → Task_CreateCommunity
        ├─ Quest 2: Build the Capital          → Task_PlaceBuilding(AB) + Task_FinishConstruction
        ├─ Quest 3: Promote to Camp            → Task_PromoteCommunity(Camp)
        ├─ Quest 4: Promote to Village         → Task_PromoteCommunity(Village)
        ├─ Quest 5: Promote to Town            → … etc through City / Kingdom / Empire
        └─ (Each Quest is a QuestSO asset; Tasks are TaskBase subclasses)

TaskBase additions (NEW)
 ├─ Task_CreateCommunity            — calls CharacterCommunity.CreateCommunity(); completes on success
 ├─ Task_PlaceBuilding(BuildingSO)  — completes when a building of that SO is in PlacedByCharacterId = self
 ├─ Task_FinishConstruction(target) — completes when target.IsComplete
 └─ Task_PromoteCommunity(CommunityLevel) — completes when community.Level >= target

CharacterCommunity (modified)
 ├─ CheckAndCreateCommunity (gates stripped — only "not-already-leading" remains)
 ├─ CreateCommunity() — public, gate-free; instantiates Community via CommunityManager
 │       → grants AB blueprint to character.CharacterBlueprints (NEW: GrantBlueprint API)
 │       → no longer logs "Small Group of Friends" name — defaults to "{name}'s Settlement"
 ├─ Citizenship : Community (NEW field; replicated; persisted)
 │       → granted on AB completion (founder) or join-request accept (migrant)
 │       → revoked on leader-kick or character-renounce
 │       → at most 1 city at a time
 └─ Trait/Friend checks deleted (and Trait.CanCreateCommunity flag deleted)

CharacterBlueprints (additions)
 ├─ GrantBlueprint(BuildingSO so)            — NEW server-method; appends to _unlockedBuildingIds
 └─ HasBlueprint(BuildingSO so) → bool        — NEW convenience accessor

Community (multi-leader migration)
 ├─ leaders : List<Character>                 — replaces singular `leader`
 ├─ PrimaryLeader => leaders.Count > 0 ? leaders[0] : null
 ├─ SecondaryLeaders => leaders.Skip(1)
 ├─ IsLeader(Character c) => leaders.Contains(c)         — multi-aware predicate
 ├─ PromoteToSecondaryLeader(Character c)     — primary-only authority
 ├─ DemoteFromLeadership(Character c)         — primary-only authority
 ├─ TransferPrimaryLeadership(Character newPrimary) — primary-only
 ├─ AdministrativeBuilding : AdministrativeBuilding ref  (NEW; null until placed)
 ├─ IsChartered => AdministrativeBuilding != null
 ├─ Citizens => members.Where(m => m.CharacterCommunity.Citizenship == this)
 └─ All existing fields (level, members, zones, ownedBuildings, parent/sub) unchanged

AdministrativeBuilding : CommercialBuilding (NEW)
 ├─ BuildingType = BuildingType.Administrative (NEW enum value)
 ├─ Preplaced furniture in prefab (under _completedVisualRoot):
 │     CityManagementFurniture       — admin console
 │     SafeFurniture                  — city treasury (reuses existing Safe system)
 │     JoinRequestDesk                — applicant-side desk
 │     storage furniture × N          — AB material stockpile (BuildingLogisticsManager-managed)
 ├─ Jobs initialised in InitializeJobs():
 │     JobBuilder × 2
 │     JobHarvester × 1   (CityHarvester variant — see below)
 │     JobLogisticsManager × 1
 ├─ Auto-owner binding: on placement, _ownerIds = community.leaders[*].CharacterId
 ├─ One per community enforced in BuildingPlacementManager (NEW pre-check)
 └─ On placement: Community.AdministrativeBuilding = this, IsChartered = true
   On Building.Finalize() Complete: founder.CharacterCommunity.Citizenship = community

Admin Console Surface
─────────────────────
CityManagementFurniture (NEW Furniture subtype)
 ├─ Inherits ManagementFurniture pattern (owner-only interact opens panel)
 ├─ Multi-leader-aware: any character in Community.leaders is "owner" for this furniture
 ├─ Opens UI_CityManagementPanel (NEW, distinct from UI_OwnerManagementPanel)
 │     Tab list:
 │       - PlaceBuildingTab  — list of tier-unlocked Civic blueprints; cursor-place mode
 │       - BuildOrdersTab    — active BuildOrders + status (pending/in-progress)
 │       - JoinRequestsTab   — pending JoinRequests + Accept/Decline buttons
 │       - LeadersTab        — promote/demote secondary leaders (primary-only Accept)
 │       - TierUpTab         — progress toward next tier + Promote button (greyed until criteria clear)
 │       - CitizensTab       — member list, kick (leader-only)
 │       - TreasuryTab       — read-only city safe balance + recent transactions
 └─ NPC route: BT/ambition tick uses server-method equivalents (no UI), same authorization gate

JoinRequestDesk (NEW Furniture subtype)
 ├─ Inherits OccupiableFurniture (so an applicant can "wait in line")
 ├─ OnInteract(applicant): server-side check + enqueue
 │     - applicant.CharacterCommunity.Citizenship == null (not a citizen anywhere else; renounce-first?)
 │     - applicant.CharacterCommunity.CurrentCommunity == null (not in any community yet)
 │     - target city is chartered, AB is complete
 │     → adds new JoinRequest{ApplicantId, RequestedAt} to AB.PendingJoinRequests
 │     → applicant queues CharacterAction_OccupyFurniture (sits at desk to "wait")
 ├─ Server-authoritative; client-initiated paths route via ServerRpc
 └─ Toast on success/failure ("You have submitted a request to join {city}." / "{city} is not accepting requests right now.")

UI_CityManagementPanel (NEW, modeled after UI_OwnerManagementPanel)
 └─ Generic shell with tab list. Reuses the IManagementTab + IManagementTabView pattern
       from .agent/skills/management-panel/SKILL.md. Resources/UI/CityManagement/ prefabs.

BuildOrder Layer
────────────────
BuildOrder : MWI.Quests.IQuest (NEW, parallels BuyOrder/CraftingOrder/TransportOrder)
 ├─ TargetBuilding : Building                   — the UnderConstruction site
 ├─ HostBuilding   : CommercialBuilding         — the AB (or future private company)
 ├─ ClientBoss     : Character                  — the leader who placed it
 ├─ HasExpiration  => false                     — explicit non-expiring
 ├─ IsPlaced       => true                      — background-commit; no walk-to-supplier handshake
 ├─ IsCompleted    => TargetBuilding == null || !TargetBuilding.IsUnderConstruction
 ├─ GetMissingMaterials() → IEnumerable<(ItemSO, int)>
 ├─ Implements IQuest so it shows in dev inspector + quest log
 └─ Lives on HostBuilding.BuildingLogisticsManager._activeBuildOrders (NEW list,
       parallel to _activeOrders for BuyOrders)

LogisticsOrderBook (modified)
 ├─ + _activeBuildOrders : List<BuildOrder>
 ├─ + AddBuildOrder(BuildOrder) / RemoveBuildOrder(BuildOrder)
 └─ + OnBuildOrderAdded / OnBuildOrderRemoved events (mirror existing BuyOrder events)

LogisticsStockEvaluator (modified)
 └─ ProcessActiveBuildOrders() — NEW pass per evaluator tick (alongside CheckStockTargets):
       for each BuildOrder:
         for each missing material:
           if no in-flight BuyOrder covers it AND no in-flight crafting:
             RequestStock(itemSO, missingQty)  — existing supply chain entry point
                 → B2B shop scan → producer chain → VirtualResourceSupplier
       If RequestStock returns false (no supplier of any kind):
         enqueue (itemSO, missingQty) onto AB._unfulfillableMaterialHarvestQueue
         (JobHarvester's worldState reads this queue; see JobHarvester section)

JobBuilder : Job (NEW)
 ├─ Category = JobCategory.Builder (NEW enum value)
 ├─ Type     = JobType.Builder (NEW enum value)
 ├─ ExecuteIntervalSeconds = 0.3f (mirror JobFarmer's heavy-planning cadence)
 ├─ Workplace = AdministrativeBuilding
 ├─ GetWorkSchedule() = 6h-18h (mirror JobFarmer)
 ├─ Goal cascade (high → low):
 │     1. HasMaterialsInHand + ABBuildOrderExists  → DeliverAndConstructGoal
 │     2. ABBuildOrderExists + ABStorageHasMaterials → FetchFromABStorageGoal
 │     3. ABBuildOrderExists                          → IdleAtABGoal (wait for logistics fill)
 │     4. else                                        → IdleInBuildingGoal (existing)
 ├─ GOAP action library (fresh per plan):
 │     GoapAction_TakeMaterialFromABStorage    (NEW; mirrors GoapAction_GatherStorageItems)
 │     GoapAction_GoToConstructionSite         (NEW; move to BuildOrder.TargetBuilding zone entry)
 │     GoapAction_DropMaterialAtZone           (NEW; wraps CharacterAction_DropItem at zone)
 │     GoapAction_FinishBuildingConstruction   (NEW; wraps CharacterAction_FinishConstruction)
 │     GoapAction_IdleInBuilding               (existing)
 └─ worldState predicates (mirror IsValid filters per Rule "worldstate-predicate / action IsValid symmetry"):
       hasActiveBuildOrder, hasMaterialsInHand_<itemId>, hasMatchingMaterialInABStorage_<itemId>,
       insideConstructionSite_<netId>, materialDeliveredFor_<netId>_<itemId>

JobHarvester (extended for CityHarvester variant)
 ├─ Existing JobHarvester is HarvestingBuilding-bound (works on HarvestingBuilding.HarvestZone)
 ├─ NEW: building-less mode triggered when Workplace is AdministrativeBuilding
 │     - Reads AB._unfulfillableMaterialHarvestQueue for wanted items
 │     - Uses CharacterAwareness.GetVisibleInteractables<Harvestable>() to find nearby producers
 │     - Falls back to a wider scan (GoapAction_ExploreForHarvestables with ad-hoc wanted-item list)
 │     - Harvests, picks up, walks to AB storage, deposits (existing GoapAction_DepositResources path)
 └─ This extension also benefits HarvestingBuilding cases where the building wants ad-hoc items
       beyond its declared yield list — separately tracked as a follow-up

JobLogisticsManager (existing — extended to handle BuildOrders)
 └─ Already handles BuyOrder dispatch + CraftingOrder placement. Adds:
       _building.BuildingLogisticsManager.ProcessActiveBuildOrders() — invoked from existing tick
       (alongside the existing ProcessActiveBuyOrders + restock evaluation passes)

BuildingGrid Layer
──────────────────
BuildingGrid (NEW per-MapController, server-only state)
 ├─ CellSizeUnits : float = 8.0f                   — fixed for v1 (8× crop/furniture cell)
 ├─ OriginXZ : Vector2                              — world-space origin of cell (0,0)
 ├─ Width, Depth : int                              — derived from MapController bounds / CellSizeUnits
 ├─ Cells[w,d] : ulong                              — 0 = free, else Building.NetworkObjectId occupying
 ├─ SnapToGridCenter(Vector3 worldPos) → Vector3
 ├─ GetCellCoord(Vector3 worldPos)     → Vector2Int
 ├─ CanPlace(Vector2Int originCell, Vector2Int sizeInGridCells) → bool
 ├─ Register(Building b, Vector2Int originCell, Vector2Int sizeInGridCells)
 ├─ Release(Building b)
 └─ Replicated occupancy: NetworkList<BuildingGridCellOccupancy> for late-join visualisation
       (each entry: cellCoord + building netId; client-side overlay reads this for ghost validation)

BuildingSO (additions)
 ├─ GridFootprintCells : Vector2Int                 — (e.g. House=1×1, Shop=2×1, AB=3×3)
 ├─ BlueprintCategory : enum { Personal, Civic }    — Personal = pre-charter placeable; Civic = admin-only
 └─ MinTier : CommunityLevel? (nullable)            — null = tier-independent; for Civic, unlocked at MinTier

BuildingPlacementManager (modified)
 ├─ Ghost preview: snaps to BuildingGrid cell center under cursor
 ├─ Validation chain: BuildingGrid.CanPlace AND existing IsInsideRegion AND IsInsideMap
 ├─ Placement path A: founder pre-charter places Personal blueprint (e.g. AB) via normal flow
 ├─ Placement path B: leader RTS-places Civic blueprint via admin console (server-side only;
 │     no character involvement, cursor input from admin console UI)
 └─ Both paths share the same server-method RequestPlaceBuildingServerRpc — only the
       authorization gate differs (Personal → blueprint-owner gate; Civic → leader-of-community gate)

Tier System
───────────
CommunityTierRequirementsSO (NEW ScriptableObject — one per CommunityLevel value)
 ├─ Level : CommunityLevel
 ├─ MinPopulation : int
 ├─ RequiredBuildings : List<BuildingSO>            — must all be present in community.ownedBuildings (Complete)
 ├─ MinTreasury : int                                — AB safe balance ≥ this
 └─ UnlockedBlueprints : List<BuildingSO>           — blueprints exposed in admin console at this tier

Community.TryPromoteLevel() — NEW server-method
 ├─ Reads CommunityTierRequirementsSO for current+1 level
 ├─ Validates population + buildings + treasury
 ├─ On success: ChangeLevel(currentLevel + 1) and broadcasts event
 └─ Returns failure reason on miss (for UI display)

Migration Layer
───────────────
DrifterMigrationSystem (NEW MonoBehaviour, one per MapController)
 ├─ Subscribes to TimeManager.OnNewDay
 ├─ For each chartered community on the map: spawn 1 generated drifter at random map edge
 ├─ Drifter init: generated from DrifterArchetypeSO (NEW SO — random name/appearance/traits)
 ├─ Drifter intent: BT branch "head to AB → interact JoinRequestDesk → wait in occupancy queue"
 └─ Server-only; no replication of the spawn cadence — clients see new Character spawn

JoinRequest (NEW lightweight struct)
 ├─ ApplicantNetworkObjectId : ulong
 ├─ RequestedAtDay : int                            — TimeManager day stamp
 └─ Replicated via NetworkList<JoinRequest> on AdministrativeBuilding for late-join + admin UI
```

### Authority model

- **Server-authoritative throughout.** Same model as Phase 1 + shop loop. Every state mutation runs on the server: community creation, leader roster, citizenship grant/revoke, BuildOrder lifecycle, BuildingGrid occupancy, tier promotion, join-request enqueue/process, drifter spawn.
- **Multi-leader authorization gate**: all admin console actions check `Community.IsLeader(actor)` (mirroring `building.IsOwner(actor)` from the multi-owner pattern). The primary-leader-only actions (promote/demote secondaries, transfer primary) further check `community.PrimaryLeader == actor`.
- **No new RPCs for BuildOrder** — uses the existing logistics event/order pipeline.
- **NEW RPCs**:
  - `CityManagementFurniture` → `UI_CityManagementPanel` (open is local; tab actions route through server methods on AB / Community).
  - `JoinRequestDesk.RequestJoinServerRpc` — applicant-initiated join.
  - `AdministrativeBuilding.AcceptJoinRequestServerRpc(uint requestId)` / `DeclineJoinRequestServerRpc(uint requestId)` — leader-initiated.
  - `AdministrativeBuilding.PlaceCityBlueprintServerRpc(string blueprintId, Vector2Int targetCell)` — RTS placement from admin console.
  - `Community.PromoteToSecondaryLeaderServerRpc(ulong characterNetId)` / `DemoteFromLeadershipServerRpc(ulong)` / `TransferPrimaryLeadershipServerRpc(ulong)`.
  - `Community.TryPromoteLevelServerRpc()` — tier-up attempt.
- **Cooperative finalize on city buildings** — unchanged from Phase 1. Any character in `BuildingZone` can run `CharacterAction_FinishConstruction`. Leaders + AB employees fall under this naturally. Citizens *could* opt-in via Phase 1 cooperative loop but no NPC goal targets it (passive non-participation enforcement).

---

## Data Model

### `AmbitionSO` chain for "Found a City"

```
Ambition_FoundACity.asset
 ├─ DisplayName: "Found a City"
 ├─ Description: "Establish a community, charter it as a city, and grow it into a thriving settlement."
 ├─ OverridesSchedule: true
 ├─ Quests: [
 │     Quest_CreateCommunity.asset,
 │     Quest_BuildCapital.asset,
 │     Quest_PromoteCamp.asset,        // tier 1 → 2
 │     Quest_PromoteVillage.asset,
 │     Quest_PromoteTown.asset,
 │     Quest_PromoteCity.asset,
 │     Quest_PromoteKingdom.asset,
 │     Quest_PromoteEmpire.asset
 │ ]
 └─ Parameters: []                      (no parameter slots; the ambition targets "any community
                                          founded by self")

Quest_CreateCommunity.asset
 └─ Tasks: [ Task_CreateCommunity ]     (Sequential ordering implicit)

Quest_BuildCapital.asset
 └─ Tasks: [
       Task_PlaceBuilding(ABSO),
       Task_FinishConstruction(BuildingTarget = the placed AB instance, resolved via context bag)
   ]

Quest_PromoteCamp.asset (similar pattern for each subsequent tier)
 └─ Tasks: [ Task_PromoteCommunity(CommunityLevel.Camp) ]
```

### `TaskBase` subclasses (NEW)

```csharp
public class Task_CreateCommunity : TaskBase {
    public string CommunityName;  // optional override; default = "{founder.Name}'s Settlement"
    
    public override bool IsComplete(Character actor, AmbitionContext ctx) =>
        actor.CharacterCommunity.CurrentCommunity != null
        && actor.CharacterCommunity.CurrentCommunity.IsLeader(actor);
    
    public override void OnExecute(Character actor, AmbitionContext ctx) {
        // Server-side execution; called from BTAction_PursueAmbitionStep
        actor.CharacterCommunity.CreateCommunity(CommunityName);
        ctx["FoundedCommunity"] = actor.CharacterCommunity.CurrentCommunity;
    }
}

public class Task_PlaceBuilding : TaskBase {
    public BuildingSO TargetBlueprint;  // e.g. ABSO
    
    public override bool IsComplete(Character actor, AmbitionContext ctx) {
        // Completion = any building of the target SO is currently placed-by self
        // (UnderConstruction is fine; advances Quest_BuildCapital to next Task)
        return BuildingManager.Instance.allBuildings.Any(b => 
            b.BuildingSO == TargetBlueprint
            && b.PlacedByCharacterId == actor.CharacterId);
    }
    
    public override void OnExecute(Character actor, AmbitionContext ctx) {
        // For NPC: GOAP plans the placement (uses BuildingPlacementManager).
        // For Player: this Task surfaces in quest log; player UI does the work.
        // ctx["PlacedBuilding"] is set by the placement path on success for downstream Tasks.
    }
}

public class Task_FinishConstruction : TaskBase {
    public ContextBinding<Building> TargetBuildingBinding;  // reads ctx["PlacedBuilding"] or similar
    
    public override bool IsComplete(Character actor, AmbitionContext ctx) {
        var target = TargetBuildingBinding.Resolve(ctx);
        return target != null && !target.IsUnderConstruction;
    }
    
    public override void OnExecute(Character actor, AmbitionContext ctx) {
        // For NPC: GOAP plans walking to BuildingZone + finishing.
        // For Player: surfaces in quest log; player does the manual work.
    }
}

public class Task_PromoteCommunity : TaskBase {
    public CommunityLevel TargetLevel;
    
    public override bool IsComplete(Character actor, AmbitionContext ctx) =>
        actor.CharacterCommunity.CurrentCommunity != null
        && actor.CharacterCommunity.CurrentCommunity.Level >= TargetLevel;
    
    public override void OnExecute(Character actor, AmbitionContext ctx) {
        // For NPC leader: walk to AB → invoke Community.TryPromoteLevel() server-method.
        // For Player leader: surfaces in quest log; player clicks the Promote button.
    }
}
```

### `Community` schema migration

```csharp
[Serializable]
public class Community {
    public string communityName;
    public CommunityLevel level;
    
    // CHANGED: was `public Character leader;`
    [Header("Leadership")]
    public List<Character> leaders = new List<Character>();   // [0] = primary
    
    [Header("Members")]
    public List<Character> members = new List<Character>();
    
    [Header("Hierarchy")]
    [NonSerialized] public Community parentCommunity;
    [NonSerialized] public List<Community> subCommunities = new List<Community>();
    
    [Header("Territory & Assets")]
    public List<Zone> communityZones = new List<Zone>();
    public List<Building> ownedBuildings = new List<Building>();
    
    // NEW
    public AdministrativeBuilding AdministrativeBuilding;
    public bool IsChartered => AdministrativeBuilding != null;
    
    // NEW convenience accessors
    public Character PrimaryLeader => leaders.Count > 0 ? leaders[0] : null;
    public IEnumerable<Character> SecondaryLeaders => leaders.Skip(1);
    public bool IsLeader(Character c) => c != null && leaders.Contains(c);
    public IEnumerable<Character> Citizens => 
        members.Where(m => m.CharacterCommunity != null 
                        && m.CharacterCommunity.Citizenship == this);
    
    public Community(string name, Character founder) {
        communityName = name;
        leaders.Add(founder);   // founder = primary leader
        level = CommunityLevel.SmallGroup;
        members.Add(founder);
    }
    
    // NEW server-only methods
    public bool PromoteToSecondaryLeader(Character c) { ... }     // primary-only authority
    public bool DemoteFromLeadership(Character c) { ... }          // primary-only authority
    public bool TransferPrimaryLeadership(Character newPrimary) { ... }  // primary-only
    
    // NEW gated tier mutator
    public TierPromotionResult TryPromoteLevel() {
        var next = (CommunityLevel)((int)level + 1);
        var spec = CommunityTierRequirementsRegistry.GetSpec(next);
        if (spec == null) return TierPromotionResult.NoSuchTier;
        if (members.Count < spec.MinPopulation) return TierPromotionResult.PopulationShortfall;
        if (!HasAllBuildings(spec.RequiredBuildings)) return TierPromotionResult.MissingBuildings;
        if (AdministrativeBuilding == null) return TierPromotionResult.NotChartered;
        if (AdministrativeBuilding.GetTreasuryBalance() < spec.MinTreasury) 
            return TierPromotionResult.TreasuryShortfall;
        ChangeLevel(next);
        return TierPromotionResult.Success;
    }
    
    // Existing — UNCHANGED (still guard-less, the gate lives at TryPromoteLevel)
    public void ChangeLevel(CommunityLevel newLevel) { ... }
    
    // Existing — UPDATED to handle multi-leader
    public void RemoveMember(Character member) {
        // ... existing logic ...
        if (leaders.Contains(member)) {
            leaders.Remove(member);
            // If primary left and a secondary remains, promote oldest secondary to primary
            // (no-op if no secondaries — community without leadership goes "leaderless"
            //  until a new leader is appointed or the community dissolves)
        }
    }
    
    // Existing — DELETED
    // public void SetLeader(Character newLeader) — replaced by leader-roster mutators
}
```

### `AdministrativeBuilding`

```csharp
public class AdministrativeBuilding : CommercialBuilding {
    public override BuildingType BuildingType => BuildingType.Administrative;  // NEW enum value
    
    [Header("Administrative")]
    [SerializeField] private CityManagementFurniture _cityManagement;   // preplaced
    [SerializeField] private JoinRequestDesk _joinRequestDesk;          // preplaced
    [SerializeField] private SafeFurniture _cityTreasury;               // preplaced
    [SerializeField] private List<StorageFurniture> _materialStorages;  // preplaced
    
    // Replicated state
    public NetworkList<JoinRequest> PendingJoinRequests = new NetworkList<JoinRequest>();
    
    // Server-only state
    private List<(ItemSO, int)> _unfulfillableMaterialHarvestQueue = new();
    
    public Community OwnerCommunity { get; private set; }   // server-set on placement
    
    protected override void InitializeJobs() {
        _jobs.Add(new JobBuilder("Builder"));
        _jobs.Add(new JobBuilder("Builder 2"));
        _jobs.Add(new JobHarvester("City Harvester", JobType.CityHarvester));   // NEW JobType
        _jobs.Add(new JobLogisticsManager("City Logistics"));
    }
    
    public int GetTreasuryBalance() => _cityTreasury?.GetCurrencyAmount() ?? 0;
    
    // Server-only: builders + harvesters call to find work
    public BuildOrder GetActiveBuildOrder() => 
        BuildingLogisticsManager?.GetFirstActiveBuildOrder();
    
    public IEnumerable<(ItemSO, int)> GetUnfulfillableHarvestQueue() => 
        _unfulfillableMaterialHarvestQueue;
    
    public void EnqueueUnfulfillableMaterial(ItemSO item, int amount) { ... }
    public void DequeueFulfilledMaterial(ItemSO item, int amount) { ... }
    
    // Citizenship grant on Finalize completion
    protected override void OnFinalize() {
        base.OnFinalize();
        if (OwnerCommunity != null && PlacedByCharacterId != 0) {
            var founder = ResolveCharacter(PlacedByCharacterId);
            if (founder?.CharacterCommunity != null) {
                founder.CharacterCommunity.SetCitizenship(OwnerCommunity);
            }
        }
    }
}
```

### `BuildOrder`

```csharp
[Serializable]
public class BuildOrder : MWI.Quests.IQuest {
    public Building TargetBuilding { get; private set; }
    public CommercialBuilding HostBuilding { get; private set; }
    public Character ClientBoss { get; private set; }
    public int PlacedOnDay { get; private set; }   // TimeManager.CurrentDay stamp
    
    public bool HasExpiration => false;            // explicit non-expiring
    public bool IsPlaced => true;                  // background-commit
    public bool IsCompleted => 
        TargetBuilding == null || !TargetBuilding.IsUnderConstruction;
    
    public BuildOrder(Building target, CommercialBuilding host, Character clientBoss, int placedOnDay) { ... }
    
    public IEnumerable<(ItemSO Item, int MissingCount)> GetMissingMaterials() {
        if (TargetBuilding == null) yield break;
        var reqs = TargetBuilding.ConstructionRequirements;
        var delivered = TargetBuilding.ContributedMaterials;
        for (int i = 0; i < reqs.Count; i++) {
            var req = reqs[i];
            if (req.Item == null) continue;
            int delv = delivered.TryGetValue(req.Item, out int d) ? d : 0;
            int missing = req.Amount - delv;
            if (missing > 0) yield return (req.Item, missing);
        }
    }
    
    // IQuest implementation
    public string QuestId => $"BuildOrder_{TargetBuilding?.NetworkObjectId}";
    public string DisplayTitle => $"Build {TargetBuilding?.BuildingName ?? "<unknown>"}";
    public string Description => $"{ClientBoss?.CharacterName ?? "<unknown>"} commissioned construction at {HostBuilding?.BuildingName}.";
    public IQuestTarget Target => new BuildingTarget(TargetBuilding);
    public QuestState State { get; private set; } = QuestState.Active;
    public event Action OnStateChanged;
    public Character Issuer => ClientBoss;
    public string OriginMapId { get; private set; }
}
```

### `BuildingGrid`

```csharp
public class BuildingGrid {
    public const float CellSizeUnits = 8.0f;   // 8× crop/furniture cell
    
    private readonly Vector2 _originXZ;        // world-space origin of cell (0,0)
    private readonly int _width;
    private readonly int _depth;
    private readonly ulong[,] _cells;          // 0 = free, else Building.NetworkObjectId
    
    public BuildingGrid(Vector2 originXZ, int width, int depth) { ... }
    
    public Vector3 SnapToGridCenter(Vector3 worldPos) {
        var cell = GetCellCoord(worldPos);
        return new Vector3(
            _originXZ.x + (cell.x + 0.5f) * CellSizeUnits,
            worldPos.y,
            _originXZ.y + (cell.y + 0.5f) * CellSizeUnits
        );
    }
    
    public Vector2Int GetCellCoord(Vector3 worldPos) {
        int cx = Mathf.FloorToInt((worldPos.x - _originXZ.x) / CellSizeUnits);
        int cz = Mathf.FloorToInt((worldPos.z - _originXZ.y) / CellSizeUnits);
        return new Vector2Int(cx, cz);
    }
    
    public bool CanPlace(Vector2Int originCell, Vector2Int sizeInGridCells) {
        for (int dx = 0; dx < sizeInGridCells.x; dx++) {
            for (int dz = 0; dz < sizeInGridCells.y; dz++) {
                int x = originCell.x + dx;
                int z = originCell.y + dz;
                if (x < 0 || x >= _width || z < 0 || z >= _depth) return false;
                if (_cells[x, z] != 0) return false;
            }
        }
        return true;
    }
    
    public void Register(Building b, Vector2Int originCell, Vector2Int sizeInGridCells) { ... }
    public void Release(Building b) { ... }   // server-side; called from Building.OnDestroy / OnDespawned
}
```

### `BuildingSO` (additions)

```csharp
public class BuildingSO : ScriptableObject {
    // ... existing fields ...
    
    [Header("Placement (NEW)")]
    [SerializeField] private Vector2Int _gridFootprintCells = new Vector2Int(1, 1);
    [SerializeField] private BlueprintCategory _blueprintCategory = BlueprintCategory.Personal;
    [SerializeField] private CommunityLevel _minTier = CommunityLevel.SmallGroup;  // applies for Civic
    
    public Vector2Int GridFootprintCells => _gridFootprintCells;
    public BlueprintCategory BlueprintCategory => _blueprintCategory;
    public CommunityLevel MinTier => _minTier;
}

public enum BlueprintCategory { Personal, Civic }
```

### `CharacterCommunity.Citizenship` (NEW field)

```csharp
public class CharacterCommunity : CharacterSystem, ICharacterSaveData<CommunitySaveData> {
    private Community _currentCommunity;
    private Community _citizenship;       // NEW
    
    public Community CurrentCommunity => _currentCommunity;
    public Community Citizenship => _citizenship;
    
    // NEW server-only methods
    public void SetCitizenship(Community c) {
        if (_citizenship != null && _citizenship != c) {
            // implicit renounce of prior citizenship
        }
        _citizenship = c;
        // (Network sync via existing CommunitySaveData round-trip + future NetVar for live)
    }
    
    public void RenounceCitizenship() {
        _citizenship = null;
    }
}
```

### `CommunitySaveData` (additions)

```csharp
public class CommunitySaveData {
    public string communityMapId;
    public string citizenshipMapId;          // NEW — round-trips Citizenship
    // (Community itself extends through MapSaveData → CommunityData, not here)
}
```

### `CommunityData` save additions (MapRegistry side)

```csharp
public class CommunityData {
    public string CommunityName;
    public int Level;
    public List<string> leaderIds = new();   // CHANGED — was singular leaderId
    public List<string> memberIds = new();
    public ulong AdministrativeBuildingNetId;  // NEW
    // ... existing fields ...
    
    public bool IsLeader(string characterId) => leaderIds.Contains(characterId);
    public string PrimaryLeaderId => leaderIds.Count > 0 ? leaderIds[0] : null;
}
```

---

## Data Flow & Lifecycle

### 1. Founding sequence

```
Player or NPC has Ambition_FoundACity active.

NPC path:
  BTAction_PursueAmbitionStep ticks → Quest_CreateCommunity active → Task_CreateCommunity
    → NPC walks to clear spot, OnExecute() calls CharacterCommunity.CreateCommunity()
    → CreateCommunity instantiates Community via CommunityManager, founder = leaders[0]
    → CharacterBlueprints.GrantBlueprint(ABSO) — founder now has AB blueprint
    → Task_CreateCommunity.IsComplete returns true → Quest advances
  Quest_BuildCapital active → Task_PlaceBuilding(ABSO)
    → NPC GOAP plans placement via BuildingPlacementManager (uses BuildingGrid snap)
    → AB spawns at snapped cell in UnderConstruction state
    → Server sets Community.AdministrativeBuilding = AB, IsChartered = true
    → AB.OwnerCommunity = community, _ownerIds = community.leaderIds
    → BuildOrder created on AB.BuildingLogisticsManager._activeBuildOrders for the AB itself
        (NB: a self-referential BuildOrder — the AB hires builders who will construct
         the AB itself. This works because Phase 1 cooperative loop allows ANY character
         in BuildingZone to finish; if no builders exist yet — they're hired after AB
         finishes — the founder personally builds the AB. See Edge Cases.)
  Quest_BuildCapital continues → Task_FinishConstruction(AB)
    → Founder walks to AB BuildingZone, runs CharacterAction_FinishConstruction (Phase 1)
    → Construction completes → Building.Finalize → AB.OnFinalize:
        - Preplaced furniture activates
        - founder.CharacterCommunity.SetCitizenship(community)  ← citizenship granted
    → Task_FinishConstruction.IsComplete = true → Quest advances
  Quest_PromoteCamp active → Task_PromoteCommunity(Camp)
    → NPC waits for criteria. JobBuilders + JobHarvesters + JobLogisticsManager auto-hire
        once AB is complete (NPC posts hiring via existing help-wanted; migrants arrive
        via DrifterMigrationSystem; some get hired into JobBuilder).
    → Once population + buildings + treasury criteria all clear:
        NPC walks to AB → invokes Community.TryPromoteLevel()
        → ChangeLevel(Camp)
        → Task_PromoteCommunity.IsComplete = true → Quest advances
  ... etc through higher tiers.

Player path:
  Quest log shows "Found a City" ambition with current step highlighted.
  Step 1 ("Create Community"): player invokes dev/UI button → CharacterCommunity.CreateCommunity()
        (route: PlayerController dev command → server method)
  Step 2 ("Build the Capital"): player uses BuildingPlacementManager UI (ghost-snap to grid),
        places AB. Walks to BuildingZone. Hauls materials (forages from harvestables initially —
        no Tier 1 yet because no shops exist). Runs CharacterAction_FinishConstruction.
        Once complete, citizenship is granted; admin console becomes usable.
  Step 3+ ("Promote to Camp / Village / ..."): player opens admin console → TierUpTab →
        sees population/buildings/treasury readout → clicks Promote button (server gate).
```

### 2. Migration loop (TimeManager.OnNewDay)

```
DrifterMigrationSystem (one per MapController, server-only):
  Subscribes to TimeManager.OnNewDay
  
  On tick:
    For each Community on this map where IsChartered == true:
      Generate a DrifterArchetype instance (random name/appearance/traits from DrifterArchetypeSO pool)
      Pick a random map edge point (NavMesh-sampled to be valid)
      Spawn the new Character at that point
      Initial BT priority injection: GoToInteractable(community.AB.JoinRequestDesk)
      
  Drifter walks to AB via NavMesh.
  Drifter reaches JoinRequestDesk interaction zone.
  Drifter calls JoinRequestDesk.OnInteract(self):
    → Server adds JoinRequest{ApplicantId = self.netId, RequestedAtDay = currentDay}
        to AB.PendingJoinRequests
    → Server queues CharacterAction_OccupyFurniture(self, JoinRequestDesk)
        — drifter "waits in line" (visual feedback for player)
  
  Drifter remains seated at desk until:
    - Leader accepts → JoinCommunity(community) + Citizenship granted + drifter leaves desk
    - Leader declines → drifter removed from queue + leaves desk + wanders away
    - N days pass with no response → drifter gives up and leaves (timeout: 7 in-game days)
```

### 3. Tier-up flow

```
Leader walks to AB → opens admin console (CityManagementFurniture interact opens UI_CityManagementPanel).
  
Player leader:
  TierUpTab shows current level + next tier requirements with progress bars:
    Population: 12/20 ✗
    Buildings:  [Farm ✓, Shop ✓, House×3 ✓, Inn ✗]
    Treasury:   500/1000 ✗
  Promote button is disabled until all criteria pass.
  Player clicks Promote → AdministrativeBuilding.RequestPromoteLevelServerRpc()
  → Server invokes Community.TryPromoteLevel():
      validates all criteria server-side (cheat-resistant)
      on success: ChangeLevel(currentLevel + 1) + broadcast event
      on failure: returns reason → toast displayed via ClientRpc
  
NPC leader:
  Task_PromoteCommunity(TargetLevel) BT step active.
  NPC walks to AB, when in interaction zone of admin console, invokes Community.TryPromoteLevel().
  Result feeds back into Task.IsComplete.
  If criteria not yet met, NPC abandons the Task this tick (will retry once world state matches);
  meanwhile the BT falls back to lower-priority ambition steps or schedule activities.

On successful tier-up:
  - Community.Level changes (replicated via existing save flow + future NetVar)
  - New Civic blueprints unlock in admin console (UI refresh on UnlockedBlueprints diff)
  - Toast / banner / cinematic event (TBD — UI polish, deferred)
```

### 4. City building placement (RTS via admin console)

```
Leader opens admin console → PlaceBuildingTab.
  Tab displays list of tier-unlocked Civic blueprints (read from current community's tier spec).
  Leader picks "House".
  UI enters ghost-place mode:
    Cursor on world map → ghost preview snaps to BuildingGrid cell under cursor
    Visual: cell highlight (green = valid placement, red = invalid)
    Validity: BuildingGrid.CanPlace AND BuildingPlacementManager.IsInsideRegion 
              AND BuildingPlacementManager.IsInsideMap AND community territory rules
  Leader clicks confirmed cell:
    AdministrativeBuilding.PlaceCityBlueprintServerRpc(blueprintId, targetCell)
  Server:
    1. Validate: leader is in community.leaders + valid blueprint + tier unlocked + CanPlace
    2. BuildingPlacementManager.RequestPlacementServerRpc with the snapped position
    3. Building spawns at snapped center in UnderConstruction state
       (existing Phase 1 placement flow; PlacedByCharacterId = leader.CharacterId)
    4. community.ownedBuildings.Add(newBuilding)
    5. newBuilding._ownerIds = community.leaderIds (multi-owner)
    6. BuildOrder created on community.AdministrativeBuilding.BuildingLogisticsManager._activeBuildOrders
       BuildOrder.TargetBuilding = newBuilding, HostBuilding = AB, ClientBoss = leader
    7. BuildingGrid.Register(newBuilding, targetCell, blueprintSO.GridFootprintCells)
    8. Broadcast OnBuildOrderAdded → JobBuilder + JobLogisticsManager pick up

Founder's pre-AB placement (non-Civic personal pathway):
  Founder has AB blueprint in CharacterBlueprints.
  Founder uses normal BuildingPlacementManager UI (not via admin console — AB isn't placed yet!)
  Ghost snaps to BuildingGrid (same grid, both paths use it).
  Confirm → server places AB → Community.AdministrativeBuilding = AB.
  No BuildOrder created for the AB itself (the AB is the host; it can't host an order
    for itself before its logistics manager exists).
  → Founder personally builds the AB via Phase 1 cooperative loop (no employees yet).
  → AB.OnFinalize → preplaced furniture + JobBuilder/JobHarvester/JobLogisticsManager job slots
    become hireable; founder.Citizenship granted.
```

### 5. BuildOrder execution (JobBuilder side)

```
JobBuilder.Execute() (server, every 0.3s when worker is on shift):
  
  Step 1 — Worldstate snapshot:
    hasActiveBuildOrder = AB.GetActiveBuildOrder() != null
    hasMaterialsInHand_<itemId> = (hands or bag contains itemId)
    hasMatchingMaterialInABStorage_<itemId> = AB.GetStorageInventory().Contains(itemId)
    insideConstructionSite_<netId> = inside the active site's BuildingZone
  
  Step 2 — Goal selection (cascade):
    if (hasMaterialsInHand_<x> && hasActiveBuildOrder)         → DeliverAndConstructGoal
    if (hasActiveBuildOrder && hasMatchingMaterialInABStorage) → FetchFromABStorageGoal
    if (hasActiveBuildOrder)                                    → IdleAtABGoal (waiting for logistics)
    else                                                        → IdleInBuildingGoal
  
  Step 3 — GOAP planner backward-search from selected goal:
    DeliverAndConstructGoal:
      Effect: BuildingComplete_<netId> = true (provisional — multi-trip)
      ← GoapAction_FinishBuildingConstruction
         Precondition: insideConstructionSite_<netId> + materialsDeliveredFor_<netId>_<itemId>
         Wraps: CharacterAction_FinishConstruction (Phase 1)
      ← GoapAction_DropMaterialAtZone
         Precondition: insideConstructionSite_<netId> + hasMaterialsInHand_<itemId>
         Wraps: CharacterAction_DropItem at BuildingZone.bounds.center
      ← GoapAction_GoToConstructionSite
         Effect: insideConstructionSite_<netId> = true
         Wraps: CharacterMoveToAction(building.TryGetZoneEntryPoint(worker.position))
    
    FetchFromABStorageGoal:
      Effect: hasMaterialsInHand_<itemId> = true
      ← GoapAction_TakeMaterialFromABStorage
         Precondition: hasMatchingMaterialInABStorage_<itemId>
         Wraps: CharacterTakeFromFurnitureAction on AB's storage furniture
      ← (move to AB storage, existing GoapAction_MoveToTarget)
    
    IdleAtABGoal: wraps GoapAction_IdleInBuilding
  
  Step 4 — Per-tick action execution + re-validation (same pattern as JobFarmer).
  
Per-trip loop:
  - JobBuilder takes from AB storage → walks to site → drops in zone → runs FinishConstruction
  - FinishConstruction consumes dropped items, advances ConstructionProgress
  - Loops until ConstructionProgress = 1 → Building.Finalize → BuildOrder.IsCompleted = true
  - BuildOrder removed from _activeBuildOrders
  - JobBuilder re-plans for next BuildOrder (if any)
```

### 6. JobLogisticsManager BuildOrder cascade

```
JobLogisticsManager.Execute() — every tick (also OnWorkerPunchIn):
  ProcessActiveBuildOrders():
    For each BuildOrder in HostBuilding.BuildingLogisticsManager._activeBuildOrders:
      For each (itemSO, missing) in buildOrder.GetMissingMaterials():
        InFlightCount = stockEvaluator.GetInFlightBuyOrderCount(itemSO)
                       + currentStorageCount(itemSO)
        Needed = missing - InFlightCount
        if (Needed > 0):
          success = stockEvaluator.RequestStock(itemSO, Needed)
          // RequestStock cascades:
          //   1. B2B shop-buy preference scan (2026-05-09; same map shops)
          //   2. FindSupplierFor → real producer / CraftingBuilding
          //   3. VirtualResourceSupplier (biome pool depletion)
          
          if (!success):
            HostBuilding.EnqueueUnfulfillableMaterial(itemSO, Needed)
            // Picked up by JobHarvester next tick
```

### 7. JobHarvester (AB CityHarvester variant)

```
JobHarvester.Execute() — if Workplace is AdministrativeBuilding:
  
  Step 1 — Read harvest queue:
    queue = AB.GetUnfulfillableHarvestQueue()
  
  Step 2 — If queue empty: IdleInBuildingGoal
  
  Step 3 — If queue has (itemSO, qty):
    Use CharacterAwareness.GetVisibleInteractables<Harvestable>() to scan nearby
    Filter: h.Yields.Contains(itemSO) && h.HasResources
    If found nearby (within awareness radius):
      Use existing GoapAction_HarvestResources path (extended building-less mode)
      On harvest complete → pickup → walk to AB storage → AddToInventory
      AB.DequeueFulfilledMaterial(itemSO, harvestedQty)
    If NOT found nearby:
      Use GoapAction_ExploreForHarvestables (extended ad-hoc mode)
      NPC wanders looking for matching Harvestable
      Once found, switches to HarvestResources path
      If wander exhausted (no match anywhere on map): clear from queue with log, 
        leader is notified via toast on next admin console open
  
  Leader can opt-in: leader's own GOAP / manual play can perform the same harvest
    and deposit at AB storage. Same code paths, no special leader behavior.
```

### 8. Join-request flow

```
Drifter arrives at JoinRequestDesk → OnInteract(drifter):
  Server validates:
    drifter.CharacterCommunity.CurrentCommunity == null (not already member)
    drifter.CharacterCommunity.Citizenship == null (not already citizen)
    AB.IsComplete && AB.OwnerCommunity.IsChartered
  On success:
    AB.PendingJoinRequests.Add(new JoinRequest{ApplicantId = drifter.netId, 
                                               RequestedAtDay = TimeManager.CurrentDay})
    drifter.CharacterActions.ExecuteAction(new CharacterAction_OccupyFurniture(drifter, desk))
    Toast to leaders: "{drifter.name} has requested to join your city."
  On failure:
    Toast back to drifter (or log if drifter was a generated NPC).

Leader processes via admin console JoinRequestsTab:
  Player leader:
    Sees list of pending requests with [Accept] [Decline] buttons.
    Click Accept → AB.AcceptJoinRequestServerRpc(requestId).
    Click Decline → AB.DeclineJoinRequestServerRpc(requestId).
  
  NPC leader (v1 stub):
    BT/ambition tick reads AB.PendingJoinRequests.
    For each request, calls Accept (always — v1 unconditional).
    Future: filter by relation / personality / city food capacity.

On Accept:
  community.AddMember(applicant)
  applicant.CharacterCommunity.JoinCommunity(community)  — existing
  applicant.CharacterCommunity.SetCitizenship(community) — NEW
  applicant.OccupyingFurniture released (CharacterAction_OccupyFurniture cancelled)
  Toast: "Welcome to {city}!"
  
On Decline:
  AB.PendingJoinRequests.RemoveAt(requestId)
  applicant.OccupyingFurniture released
  applicant queues "wander away" BT branch
  Toast: "{city} has declined your request."

On timeout (7 in-game days):
  Same as Decline, plus log "{drifter} gave up waiting at {city}."
```

---

## Networking

### Replication layout (additive)

| State | Owner | Replication |
|---|---|---|
| `Community.leaders` (List<Character>) | Server | Existing `CommunityData.leaderIds` save round-trip + new NetVar for live sync |
| `Community.AdministrativeBuilding` | Server | NetVar of `AB.NetworkObjectId` on `CommunityData` (new) |
| `CharacterCommunity.Citizenship` | Server | New NetVar on `CharacterCommunity` (resolves to Community via map lookup) |
| `AdministrativeBuilding.PendingJoinRequests` | Server | `NetworkList<JoinRequest>` (new) |
| `BuildOrder` collection | Server | Lives in `LogisticsOrderBook._activeBuildOrders` (server-only); UI reads via existing logistics RPCs |
| `BuildingGrid` occupancy | Server | `NetworkList<BuildingGridCellOccupancy>` (new) for client-side placement validation |

### Late-join

- Client connects → receives full `Community.leaders` + `AdministrativeBuilding` + `BuildingGrid` snapshots via the standard save/load + late-join paths (existing infrastructure).
- Client connecting mid-construction: sees the BuildingGrid cells occupied by UnderConstruction sites + active BuildOrders' progress (via existing `Building.ConstructionProgress` NetVar).
- Client connecting mid-join-request: sees the pending requests in `AB.PendingJoinRequests`.
- Client late-joining as a leader: admin console UI binds to the same NetVars; tabs populate correctly.

### Host/Client/NPC matrix (Rule #19)

| Scenario | Behavior |
|---|---|
| Host founds community + AB | Direct server calls. NPC drifters arrive on host's daily tick. |
| Client founds community + AB | Client invokes `CreateCommunity` via ServerRpc → server creates. Subsequent AB placement, BuildOrder cascade all server-authoritative. |
| Host issues admin-console action | Server-direct. |
| Client (leader) issues admin-console action | UI calls server method via ServerRpc on AB or Community. Server validates `IsLeader(actor)` + further authority gates. |
| Multi-leader concurrent edit | Last-write-wins (no transactional UI lock for v1). Future: optimistic concurrency token on AB state. |
| NPC drifter spawn during a client's session | Server-authoritative spawn; client sees via NGO `NetworkObject.Spawn`. |
| Late-join while NPC builder is mid-trip | Client sees worker's `CharacterAction` proxy; reconstructs state from `ConstructionProgress` + replicated worker position. |
| AB destroyed during gameplay (vandalism, future) | All BuildOrders abandoned; citizens lose Citizenship; community returns to IsChartered=false. (Not v1 — listed as Edge Case.) |

---

## Persistence

### `CommunityData` (existing → extended)

```
CommunityData
 ├─ communityName : string
 ├─ Level : int
 ├─ leaderIds : List<string>                  [CHANGED — was singular leaderId string]
 ├─ memberIds : List<string>
 ├─ AdministrativeBuildingNetId : ulong       [NEW]
 ├─ communityZones : ...
 ├─ ownedBuildingIds : ...
 ├─ parentCommunityMapId : string
 └─ subCommunityMapIds : List<string>
```

### `CommunitySaveData` (per-character)

```
CommunitySaveData
 ├─ communityMapId : string                   (existing — character's current community)
 └─ citizenshipMapId : string                 [NEW — character's chartered city of citizenship]
```

### `BuildingSaveData` (extension — only for AB)

```
BuildingSaveData
 ├─ ...all existing fields (Phase 1 + others)...
 └─ PendingJoinRequests : List<JoinRequestDTO> [NEW — only on AB instances]
     JoinRequestDTO { string ApplicantCharacterId, int RequestedAtDay }
```

### `BuildingGridSaveData` (new, per-map)

```
BuildingGridSaveData (lives on MapSaveData)
 ├─ Width, Depth : int
 ├─ OriginXZ : Vector2
 └─ OccupiedCells : List<{Vector2Int cell, ulong buildingNetId}>
       (sparse representation — only non-zero cells)
```

### `BuildOrderSaveData` (new)

```
BuildOrderSaveData (lives in HostBuilding.BuildingLogisticsManager's serializer)
 ├─ TargetBuildingNetId : ulong
 ├─ ClientBossCharacterId : string
 └─ PlacedOnDay : int
       (BuildOrder is reconstructible from these fields + the running Building)
```

### Reload flow

1. `MapSaveData` loads → instantiates `MapController`, restores `BuildingGrid` from `BuildingGridSaveData`.
2. Each `BuildingSaveData` instance restores its Building (existing).
3. Each `CommunityData` restores leaders/members/AB-ref via map-resident character lookup.
4. AB's `BuildingSaveData` restores `PendingJoinRequests` via `NetworkList` rebroadcast post-spawn.
5. BuildOrders rebuild from `BuildOrderSaveData`; tied to live `Building` instances post-load.
6. Each `CharacterCommunity.Deserialize` restores `Citizenship` from `citizenshipMapId` (resolves at runtime when map loads).

---

## Edge Cases

### Founder edge cases

- **Founder builds AB alone, dies mid-construction.** AB stays in UnderConstruction state. Community has no leader. Future: community dissolves automatically OR a citizen can claim leadership (Phase Next).
- **Founder places AB but never finishes.** Building remains scaffolded. No employees can be hired (AB.IsComplete is false). Migration still spawns drifters (community IS chartered, just not "operational"). Drifters wait at JoinRequestDesk... but the desk hasn't spawned yet (preplaced furniture activates on Complete). The drifters mill near the AB but can't submit requests. **Fix**: gate the migration ticker on `AB.IsComplete` instead of just `IsChartered`.

### Multi-leader edge cases

- **Primary leader leaves the community.** Auto-promote oldest secondary leader to primary (index 0). If no secondaries, community becomes leaderless (defined: IsLeader returns false for everyone). Admin console is unusable until a new leader is appointed (manual route TBD; v1 = leaderless community is dormant).
- **Primary leader dies.** Same as leaving.
- **Two leaders simultaneously edit admin console tabs.** Last-write-wins; no UI lock for v1. Player-vs-NPC: NPC ticks are infrequent enough (0.3s) that human edits should always win in practice.
- **Secondary leader tries to promote a citizen to secondary leader.** Server gate: only primary can promote. Toast on failure.
- **Demoting the primary.** Not allowed; primary must transfer leadership first.

### BuildOrder edge cases

- **Building destroyed mid-construction (vandalism / despawn).** `BuildOrder.IsCompleted` returns true (TargetBuilding == null). Order auto-removed; JobBuilder re-plans.
- **All builders quit / die.** BuildOrders persist; leader can hire new builders via admin console Hiring tab. Resumes when builders are available.
- **AB destroyed during active BuildOrders.** All orders dropped; UnderConstruction buildings remain in UnderConstruction state with no one to finish them. Community loses citizenship of all citizens (IsChartered = false). (Not v1 — listed as future hazard.)
- **No supplier anywhere for a material AND no Harvestable on map yields it.** BuildOrder remains active indefinitely (non-expiring). JobHarvester logs the failure once per day. Leader is notified via admin console BuildOrdersTab. Manual leader action required (e.g., bring the material themselves, or import via TransportOrder from another map).

### Migration edge cases

- **Drifter spawns inside an impassable area (NavMesh-invalid).** Spawner re-rolls map edge up to N attempts; if all fail, skip this day's spawn.
- **Drifter starves while waiting at JoinRequestDesk.** Existing `CharacterNeeds.Hunger` ticks normally; drifter eventually dies. Their JoinRequest auto-cleaned on death.
- **City full of starving citizens.** Citizens die at the existing rate. Migration keeps adding new drifters (Darwinian intent — leader must build farms / source food).
- **Drifter arrives at AB but JoinRequestDesk is destroyed / not yet preplaced.** Treated as "city not accepting"; drifter wanders away after a timeout.
- **Drifter rejected, returns to map edge, despawned (or wanders indefinitely).** V1 simplification: rejected drifters wander for 1 day then despawn. Future: they linger and might try other cities on the map.

### Placement edge cases

- **Leader RTS-places building on a cell that becomes occupied between ghost preview and click confirm.** Server-side `CanPlace` re-check rejects with toast "That spot is no longer available."
- **Leader tries to place a Civic blueprint at a tier they haven't reached.** Server-side `MinTier` check rejects with toast.
- **Founder places AB where `BuildingGrid` doesn't yet cover** (founder ahead of map's grid initialisation). Edge case caught by `MapController.Awake` — grid is initialised on map spawn, before any building can be placed.
- **Placement at the edge of map / clipping a Region boundary.** Existing `BuildingPlacementManager.IsInsideRegion` check handles this; grid snap doesn't loosen the rule.

### Tier-up edge cases

- **Population dips below threshold after promotion.** No downgrade; tier is sticky. Future: configurable demotion threshold.
- **Required building destroyed after promotion.** No downgrade.
- **Treasury drops below `MinTreasury` after promotion.** No downgrade.
- **Leader attempts promotion while a previous promotion is in-flight.** Server-method is synchronous; no race possible.

### Performance budget (Rule #34)

- `JobBuilder` GOAP plan + worldState build: 0.3s tick rate, scratch buffers reused, per-NPC. Profiled budget < 0.5 ms/tick.
- `BuildingGrid.CanPlace` is O(width × depth of footprint) — bounded by max building footprint size (~5×5 = 25 cell checks). Negligible.
- `DrifterMigrationSystem.OnNewDay`: O(chartered communities on map), typically 1-3. Negligible.
- `LogisticsStockEvaluator.ProcessActiveBuildOrders`: O(buildOrders × missingMaterials × suppliers). Cached supplier lists per-tick.
- `CharacterAwareness.GetVisibleInteractables<Harvestable>()`: existing cached path (0.3 s TTL), no new cost.

### Defensive logging (Rule #27)

All single-branch failure points gated behind `NPCDebug.VerboseCity` (new toggle) per Rule #34:
- "No site within range" → JobBuilder idle reason
- "Material X unfulfillable everywhere" → JobLogisticsManager fallback escalation
- "Drifter spawn skipped (no valid NavMesh point)" → migration ticker
- "Tier promotion rejected: <reason>" → admin console UI feedback
- "Leader auto-promoted (primary left)" → community lifecycle

---

## File Changes Summary

### New files

```
Assets/Scripts/World/Buildings/AdministrativeBuilding.cs
Assets/Scripts/World/Buildings/AdministrativeBuilding.cs.meta
Assets/Scripts/World/Buildings/BuildingGrid.cs
Assets/Scripts/World/Buildings/BuildingGridCellOccupancy.cs   (INetworkSerializable struct)
Assets/Scripts/World/Buildings/BuildingGridSaveData.cs

Assets/Scripts/World/Furniture/CityManagementFurniture.cs
Assets/Scripts/World/Furniture/JoinRequestDesk.cs
Assets/Scripts/World/Furniture/JoinRequestDeskNetSync.cs

Assets/Scripts/World/Jobs/BuildOrder.cs
Assets/Scripts/World/Jobs/BuildOrderSaveData.cs
Assets/Scripts/World/Jobs/JobBuilder.cs

Assets/Scripts/AI/GOAP/Actions/GoapAction_TakeMaterialFromABStorage.cs
Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToConstructionSite.cs
Assets/Scripts/AI/GOAP/Actions/GoapAction_DropMaterialAtZone.cs
Assets/Scripts/AI/GOAP/Actions/GoapAction_FinishBuildingConstruction.cs

Assets/Scripts/World/Community/CommunityTierRequirementsSO.cs
Assets/Scripts/World/Community/CommunityTierRequirementsRegistry.cs

Assets/Scripts/Character/CharacterAmbition/Tasks/Task_CreateCommunity.cs
Assets/Scripts/Character/CharacterAmbition/Tasks/Task_PlaceBuilding.cs
Assets/Scripts/Character/CharacterAmbition/Tasks/Task_FinishConstruction.cs
Assets/Scripts/Character/CharacterAmbition/Tasks/Task_PromoteCommunity.cs

Assets/Scripts/World/MapSystem/DrifterMigrationSystem.cs
Assets/Scripts/World/MapSystem/DrifterArchetypeSO.cs

Assets/Scripts/UI/Management/UI_CityManagementPanel.cs
Assets/Scripts/UI/Management/CityTabs/PlaceBuildingTab.cs
Assets/Scripts/UI/Management/CityTabs/PlaceBuildingTabView.cs
Assets/Scripts/UI/Management/CityTabs/BuildOrdersTab.cs
Assets/Scripts/UI/Management/CityTabs/BuildOrdersTabView.cs
Assets/Scripts/UI/Management/CityTabs/JoinRequestsTab.cs
Assets/Scripts/UI/Management/CityTabs/JoinRequestsTabView.cs
Assets/Scripts/UI/Management/CityTabs/LeadersTab.cs
Assets/Scripts/UI/Management/CityTabs/LeadersTabView.cs
Assets/Scripts/UI/Management/CityTabs/TierUpTab.cs
Assets/Scripts/UI/Management/CityTabs/TierUpTabView.cs
Assets/Scripts/UI/Management/CityTabs/CitizensTab.cs
Assets/Scripts/UI/Management/CityTabs/CitizensTabView.cs
Assets/Scripts/UI/Management/CityTabs/TreasuryTab.cs
Assets/Scripts/UI/Management/CityTabs/TreasuryTabView.cs

Assets/Resources/UI/CityManagement/*.prefab   (one per tab + parent panel)
Assets/Resources/Data/CityTiers/*.asset       (one CommunityTierRequirementsSO per CommunityLevel)
Assets/Resources/Data/Ambitions/Ambition_FoundACity.asset
Assets/Resources/Data/Ambitions/Quests/Quest_CreateCommunity.asset
Assets/Resources/Data/Ambitions/Quests/Quest_BuildCapital.asset
Assets/Resources/Data/Ambitions/Quests/Quest_PromoteCamp.asset
... (one Quest_PromoteX per tier)
Assets/Resources/Data/Buildings/AdministrativeBuildingSO.asset
Assets/Prefabs/Buildings/AdministrativeBuilding.prefab   (with preplaced furniture children)
```

### Modified files

```
# Community + Character
Assets/Scripts/World/Community/Community.cs
  - leader (singular) → leaders : List<Character>
  + PrimaryLeader, SecondaryLeaders, IsLeader convenience
  + AdministrativeBuilding ref + IsChartered
  + PromoteToSecondaryLeader / DemoteFromLeadership / TransferPrimaryLeadership
  + TryPromoteLevel() with TierPromotionResult enum
  + Citizens accessor
  - RemoveMember updated for multi-leader

Assets/Scripts/World/Community/CommunityData.cs
  - leaderId (singular) → leaderIds : List<string>
  + AdministrativeBuildingNetId : ulong
  + IsLeader(string) predicate

Assets/Scripts/World/Community/CommunityManager.cs
  - CreateNewCommunity sets leaders[0] = founder (was leader = founder)

Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs
  - CheckAndCreateCommunity: strip Trait + GetFriendCount gates; keep "not-already-leading"
  - CreateCommunity: grant AB blueprint via CharacterBlueprints.GrantBlueprint
  + Citizenship field + SetCitizenship + RenounceCitizenship
  - default community name updated ("Settlement" not "Band of Friends")

Assets/Scripts/Character/CharacterCommunity/CommunitySaveData.cs
  + citizenshipMapId : string

Assets/Scripts/Character/CharacterBlueprints/CharacterBlueprints.cs
  + GrantBlueprint(BuildingSO so) — server-method
  + HasBlueprint(BuildingSO so) — convenience accessor

Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs (or similar)
  - delete CanCreateCommunity flag + its usages

# Buildings + grid
Assets/Scripts/World/Data/BuildingSO.cs
  + GridFootprintCells : Vector2Int
  + BlueprintCategory : enum { Personal, Civic }
  + MinTier : CommunityLevel?

Assets/Scripts/World/Buildings/BuildingType.cs
  + Administrative entry

Assets/Scripts/World/Buildings/BuildingPlacementManager.cs
  - ghost preview snaps to BuildingGrid cell center under cursor
  - placement validation: BuildingGrid.CanPlace AND existing IsInsideRegion + IsInsideMap
  + RequestPlaceCityBlueprintServerRpc — admin-console-driven RTS placement entry point
  + authorization gate: Civic blueprints require leader-of-community + tier check

Assets/Scripts/World/Buildings/Building.cs
  + AB-specific hooks if needed (citizenship grant on Finalize for AB only)
  + GridFootprintCells convenience accessor (reads from BuildingSO)

Assets/Scripts/World/MapSystem/MapController.cs
  + BuildingGrid : per-map instance
  + DrifterMigrationSystem : per-map instance
  + BuildingGrid registration hook on Building register/unregister

Assets/Scripts/World/MapSystem/MapSaveData.cs
  + BuildingGridSaveData

# Logistics + jobs
Assets/Scripts/World/Buildings/Logistics/BuildingLogisticsManager.cs
  + _activeBuildOrders : List<BuildOrder> (via LogisticsOrderBook)
  + AddBuildOrder / RemoveBuildOrder facade methods

Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs
  + _activeBuildOrders + Add/Remove + events

Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs
  + ProcessActiveBuildOrders pass (alongside CheckStockTargets)

Assets/Scripts/World/Jobs/Job.cs
  (no change — JobBuilder/JobHarvester extensions add via subclassing)

Assets/Scripts/World/Jobs/JobCategory.cs (or similar enum file)
  + Builder entry

Assets/Scripts/World/Jobs/JobType.cs
  + Builder entry
  + CityHarvester entry

Assets/Scripts/World/Jobs/JobHarvester.cs
  + building-less mode when Workplace is AdministrativeBuilding
  + reads AB._unfulfillableMaterialHarvestQueue
  + uses CharacterAwareness scan + GoapAction_ExploreForHarvestables (ad-hoc mode)

# Ambition
Assets/Scripts/Character/CharacterAmbition/CharacterAmbition.cs
  (no schema change — additions live in new Task subclasses + new AmbitionSO asset)

# Time
Assets/Scripts/World/Time/TimeManager.cs
  (no schema change — DrifterMigrationSystem subscribes to existing OnNewDay event)

# Docs (per Rules #28, #29, #29b)
.agent/skills/community-system/SKILL.md         — multi-leader migration, citizenship, IsChartered, TryPromoteLevel
.agent/skills/job_system/SKILL.md               — JobBuilder + JobHarvester CityHarvester variant
.agent/skills/logistics_cycle/SKILL.md          — BuildOrder integration
.agent/skills/order-system/SKILL.md             — (no change; BuildOrder is a logistics order, not a CharacterOrder)
.agent/skills/building_system/SKILL.md          — AdministrativeBuilding + BuildingGrid + RTS placement
.agent/skills/management-panel/SKILL.md         — UI_CityManagementPanel pattern
.agent/skills/ambition-system/SKILL.md          — Ambition_FoundACity + new Tasks
.agent/skills/goap/SKILL.md                     — JobBuilder GOAP example
.agent/skills/notification-system/SKILL.md      — new toast types (join requested, accepted, declined, tier-up)

.claude/agents/building-furniture-specialist.md — AdministrativeBuilding + CityManagementFurniture + JoinRequestDesk + BuildingGrid + RTS placement
.claude/agents/character-social-architect.md    — Community multi-leader + Citizenship + InteractionInviteCommunity update
.claude/agents/npc-ai-specialist.md             — JobBuilder + JobHarvester CityHarvester + Ambition_FoundACity Tasks
.claude/agents/world-system-specialist.md       — DrifterMigrationSystem + BuildingGrid per-MapController
.claude/agents/save-persistence-specialist.md   — CommunitySaveData.citizenshipMapId + BuildingGridSaveData + BuildOrderSaveData

wiki/systems/community-system.md                — multi-leader + IsChartered + Citizenship
wiki/systems/building.md                        — BuildingGrid + admin RTS placement
wiki/systems/construction.md                    — Phase 2 NPC autonomy (now JobBuilder-driven; closes the Phase 1 todo)
wiki/systems/farming.md                         — cross-reference BuildingGrid as the building-side counterpart to TerrainCellGrid
wiki/systems/character-blueprints.md            — GrantBlueprint API
wiki/systems/character-community.md             — Citizenship field
wiki/systems/job-builder.md                     — NEW page
wiki/systems/administrative-building.md         — NEW page
wiki/systems/city-tiers.md                      — NEW page
wiki/systems/ambition-system.md                 — Ambition_FoundACity example
wiki/systems/build-order.md                     — NEW page (parallel to buy-order.md if it exists)
wiki/concepts/citizenship.md                    — NEW concept page
wiki/concepts/charter.md                        — NEW concept page

# Wiki gotchas (per the singular-owner-vs-multi-owner-isowner pattern lesson)
wiki/gotchas/singular-leader-vs-multi-leader-isleader.md  — NEW gotcha: never compare against Community.leader[0] or PrimaryLeader for auth gates; always use IsLeader(c)
```

### Documentation

```
docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md  (this file)
docs/superpowers/plans/2026-05-18-city-founding-and-administrative-building.md         (created by writing-plans)
```

---

## Testing Approach

### Unit (EditMode)

- `Community.IsLeader / PrimaryLeader / SecondaryLeaders` correctness across single/multi/empty leader rosters.
- `Community.TryPromoteLevel` returns correct `TierPromotionResult` for each failure mode.
- `Community.RemoveMember` auto-promotes secondary on primary departure.
- `BuildingGrid.SnapToGridCenter / GetCellCoord` round-trip correctness.
- `BuildingGrid.CanPlace` correctly rejects overlapping placements + out-of-bounds.
- `BuildOrder.GetMissingMaterials` correctness for partial / complete / empty delivery.
- `BuildOrder.IsCompleted` correctness for null / under-construction / complete target.
- `CharacterBlueprints.GrantBlueprint` idempotent (no duplicate entries).
- `CommunityTierRequirementsSO` lookup by `CommunityLevel`.
- `CharacterCommunity.SetCitizenship / RenounceCitizenship` semantics.

### PlayMode — Solo (Host)

- **Founder solo build**: spawn host player with `Ambition_FoundACity`. Player path:
  - Step 1: invoke dev button → CreateCommunity → AB blueprint granted. Verify.
  - Step 2: place AB on `BuildingGrid` (snap visible). Construct. Verify citizenship granted on Complete.
  - Step 3: open admin console. Verify all tabs render. Place a House via RTS click. Verify BuildOrder created + JobBuilder picks it up (after hiring).
- **NPC founder**: spawn host-side NPC with `Ambition_FoundACity`. Verify full ambition chain executes through Tier-up steps.
- **Daily migration**: chart a city. Advance days. Verify 1 drifter spawns per day, walks to AB, sits at JoinRequestDesk.
- **Join-request flow**: process accept via admin console. Verify member added + citizenship granted.
- **Tier-up gating**: try to promote with criteria unmet. Verify rejection + toast. Meet criteria. Verify promotion + unlock of new blueprints.
- **Material fallback**: place a House in a city with no shops. Verify JobLogisticsManager tries BuyOrder, fails, JobHarvester picks up harvest task, finds nearby tree, harvests Wood, deposits at AB, JobBuilder fetches + delivers + constructs.

### PlayMode — Multiplayer (Rule #19 matrix)

- Host + Client both in scene. Host founds City A; client founds City B on the same map.
- Both cities accept drifters daily.
- Host's NPC builders work on host's BuildOrders; client sees the construction progress.
- Client opens admin console for City B — full UI render, can place buildings, can accept requests.
- Multi-leader: host promotes a client-controlled NPC to secondary leader of City A. Verify the client's NPC can now use the admin console.
- Late-join: connect a third peer mid-game. Verify all city state replicates (leaders, AB, BuildOrders, pending requests, BuildingGrid occupancy).
- Save mid-construction. Reload. Verify all state preserved.

### Persistence

- Save mid-ambition (founder has community but no AB yet). Load → ambition resumes from current step.
- Save mid-construction. Load → AB resumes UnderConstruction; if leader-citizenship was already granted, preserved.
- Save with pending join requests. Load → requests preserved + admin console UI shows them.
- Save mid-tier-up (criteria not yet met). Load → state preserved; tier-up still available when criteria clear.

### Defensive / softlock guards

- AB destroyed mid-construction → all BuildOrders cleaned, JobBuilders re-plan to IdleAtAB or wander.
- Founder dies before AB Complete → community has no primary leader; auto-promote secondary (none); community goes dormant.
- All builders quit → BuildOrders persist; leader hires new builders; resumes.
- Map's BuildingGrid full → placement attempts return clear toast.

### Dev tools

- `DevModePanel` Order tab: gain entry for `Order_PlaceBuilding` (preserved if you still want it as a dev-test order — see Open Questions).
- BuildingInspectorView shows `GridFootprintCells`, `BlueprintCategory`, `MinTier`, and (for AB) active BuildOrders count.
- CharacterInspectorView shows Citizenship + leader-of references.
- New `CommunityInspectorView` for community-scoped state (leaders, AB, IsChartered, tier readiness).

### Profiler checkpoints (Rule #34)

- 3 cities with 10 builders + 5 active BuildOrders each: total JobBuilder + JobHarvester GOAP < 5 ms/tick combined.
- BuildingGrid placement validation + ghost preview: < 1 ms per cursor move.
- Daily migration tick: < 0.5 ms for 5 chartered communities on one map.

---

## Open Questions

- **`Order_PlaceBuilding` dev test surface**: the original session ask included a `DevActionPlaceBuilding` button issuing `Order_PlaceBuilding` (an `OrderImmediate`) for testing NPC building autonomy. The new design replaces NPC autonomy with `JobBuilder`-via-`BuildOrder`. Keep the dev test order anyway (force-trigger a single NPC to construct a target site) or drop it? Recommend: **keep** as a debug shortcut; it bypasses BuildOrder + JobBuilder hire and runs the same `CharacterAction_FinishConstruction` directly. Useful for "test the construction loop without the whole city stack."
- **`Community.IsChartered` semantics**: chartering happens on AB **placement** or AB **completion**? I picked **placement** for instant feedback. Push back if completion is correct.
- **Drifter-to-citizen one-shot vs migrant pool**: drifters are generated fresh per day. Should there be a "rejected drifter" pool that retries after N days? V1 says no — they wander away and despawn.
- **Founder bootstrap inventory**: no starter shelter / starter food (Darwinian). Test if this is too brutal on day 1 — adjust by giving the founder a small inventory of `Food × 3` if needed. Out of v1; flag during PlayMode if it's a problem.
- **Player UI for `CreateCommunity` outside dev mode**: V1 uses dev-mode button only. Future: surface in the character sheet (e.g., "Found Community" button visible when no current community + appropriate ambition). Defer to UI polish phase.
- **`DrifterArchetypeSO` content**: what does the pool look like? Random name from list + random `CharacterArchetypeSO` from a drifter-flagged subset? Defer — designer-authored once the system is wired.
- **`UI_CityManagementPanel` styling**: a city-level distinct UI from per-building `UI_OwnerManagementPanel`. Different theme (gold-accented "throne room" vs neutral commercial)? Defer to UI polish.
- **`Community.PrimaryLeader` change-of-hands UI**: how does the primary transfer leadership (specific gesture in the LeadersTab — confirm dialog?). Defer to UI polish.
- **Demolition + cell release**: when a building is destroyed (vandalism, future demolish action), `BuildingGrid.Release(building)` runs and the cell goes back to free. V1 doesn't have a demolish action; tracked as Phase Next.
- **`BuildOrder` priorities**: if multiple BuildOrders exist, do builders pick the oldest? The closest? The leader-flagged priority? V1: FIFO (oldest first). Add a priority field later for "rush this one."
- **Tier-up animation / cinematic**: SimCity-style fanfare when a city promotes. Out of v1; nice-to-have.
