---
type: system
title: "Farming / Plot System"
tags: [farming, crops, harvestable, plot, perennial, tier-1]
created: 2026-04-28
updated: 2026-04-30
sources: []
related:
  - "[[terrain-and-weather]]"
  - "[[world]]"
  - "[[world-macro-simulation]]"
  - "[[world-map-hibernation]]"
  - "[[character]]"
  - "[[character-equipment]]"
  - "[[items]]"
  - "[[save-load]]"
  - "[[job-farmer]]"
  - "[[kevin]]"
status: wip
confidence: medium
primary_agent: harvestable-resource-node-specialist
secondary_agents:
  - world-system-specialist
  - character-system-specialist
owner_code_path: "Assets/Scripts/Farming/"
depends_on:
  - "[[terrain-and-weather]]"
  - "[[world]]"
  - "[[items]]"
  - "[[character]]"
  - "[[save-load]]"
depended_on_by:
  - "[[dev-mode]]"
---

# Farming / Plot System

## Summary
Stardew-style farming on top of the existing `TerrainCellGrid`. A plot **is** a cell — there is no separate Plot GameObject. A character holds a `SeedSO` in hand, presses E to start placement, clicks a tilled cell, and the crop grows once per in-game day if conditions are met (currently moisture only; seasons deferred). At maturity the crop **becomes** a `CropHarvestable` GameObject which is one-shot (despawns on harvest, e.g. wheat) or perennial (stays standing and refills via the same daily condition pipeline, e.g. apple tree). All persistent state is encoded in `TerrainCell` fields that already serialise — the system adds **zero new save state**.

## Purpose
Anchor the living world's economy in a player-controlled production loop. Players can plant, water, harvest, and (with the right tool) destroy crops; NPCs can do the same via the same `CharacterAction` plumbing (rule #22 player↔NPC parity). The system reuses the pre-wired farming fields on [[terrain-and-weather|TerrainCell]] (`IsPlowed`, `PlantedCropId`, `GrowthTimer`, `TimeSinceLastWatered`) so save/load and hibernation come along for free, and integrates with the moisture pipeline (rain → moisture → growth) without new bridges.

## Responsibilities

**Layer 1 — Content & lookup (`Assets/Scripts/Farming/Pure/`)**
- Define crops as `CropSO` ScriptableObjects: growth duration, moisture threshold, perennial flag + regrow days, destruction (tool + outputs), produce item + count, growing-stage sprites.
- O(1) lookup `CropRegistry.Get(string id)` mirrors `TerrainTypeRegistry`.
- Item leaves: `SeedSO : MiscSO` (placement-active) and `WateringCanSO : MiscSO`.

**Layer 2 — Daily tick (`Assets/Scripts/Farming/`)**
- `FarmGrowthPipeline` (pure C#, server-only) — three-branch state machine per cell: growing crop / live-and-ready / live-and-refilling. Returns an `Outcome` enum (`Grew`, `JustMatured`, `Refilling`, `JustRefilled`, `NoOp`, `Stalled`, `OrphanCrop`, `NotPlanted`).
- `FarmGrowthSystem : MonoBehaviour` — server-only, one per active `MapController`. Subscribes to `TimeManager.OnNewDay`. Iterates cells, dispatches `JustMatured` to `SpawnCropHarvestable`, `JustRefilled` to `harvestable.Refill()`. Batches dirty indices into a single `MapController.NotifyDirtyCells` call.
- `FarmGrowthSystem.PostWakeSweep()` — reconstructs `CropHarvestable`s from cell state on map wake. **Single code path serves both hibernation-wake and full save-load.**

**Layer 3 — Mutation actions**
- `CharacterAction_PlaceCrop` — server-only cell mutation + seed consumption.
- `CharacterAction_WaterCrop` — server-only `cell.Moisture = canSO.MoistureSetTo`.
- `CharacterAction_DestroyHarvestable` — generic; works for any [[character|Harvestable]], not just crops.
- `CropPlacementManager : CharacterSystem` — per-Character ghost-on-mouse, raycast snap to grid, ServerRpc to commit.

**Layer 4 — Visual & network**
- (removed 2026-04-29 — was `CropVisualSpawner`. The single-GameObject-per-crop rework folded its responsibility into `CropHarvestable.ApplyVisual`, which reads `CurrentStage` + `IsDepleted` from the sibling `CropHarvestableNetSync` and applies scale + sprite swap on every peer.)
- `CropHarvestable : Harvestable` — extends the existing harvest interaction. Holds `_readySprite` / `_depletedSprite`. Owns the cell coupling (CellX/CellZ/Grid).
- `CropHarvestableNetSync : NetworkBehaviour` — sibling component on the same prefab. Owns the `NetworkVariable<bool> IsDepleted` that drives the ready ↔ depleted sprite swap on every peer (host + clients + late-joiners). Exists because `Harvestable : InteractableObject : MonoBehaviour` cannot host NetworkVariables directly.

**Layer 5 — UI input**
- `PlayerController` E-key dispatcher (rule #33): Seed/WateringCan held → placement; placement active → no-op; consumable held → consume; otherwise tap-E → `Interact()` on nearest visible interactable, hold-E (≥0.4s) → open `UI_InteractionMenu`.
- `UI_InteractionMenu` — singleton-on-demand menu listing every option from `Harvestable.GetInteractionOptions(actor)`. Greyed-out unavailable rows show the missing-tool reason ("Requires Axe").

**Non-responsibilities**
- Does **not** own plowing as a separate action (V1 plows automatically on plant — see Open questions).
- Does **not** own seasons; the spec is season-agnostic in V1.
- Does **not** own NPC farming AI (action API exists; GOAP/BT integration is a follow-up spec).
- Does **not** own the moisture pipeline — rain feeds `cell.Moisture` via [[terrain-and-weather|TerrainWeatherProcessor]].
- Does **not** unify perennial refill with wild `Harvestable._respawnDelayDays` (different semantics; see Open questions).

## Key classes / files

**Farming gameplay:** [Assets/Scripts/Farming/](../../Assets/Scripts/Farming/)
- `Pure/CropSO.cs` — content SO (growth, perennial, destruction).
- `Pure/CropRegistry.cs` — static O(1) lookup.
- `SeedSO.cs`, `WateringCanSO.cs` — placement-active item leaves.
- `FarmGrowthPipeline.cs` — pure 3-branch tick logic.
- `FarmGrowthSystem.cs` — MonoBehaviour wrapper + `PostWakeSweep`.
- `CropPlacementManager.cs` — per-Character `CharacterSystem`.
- `CharacterAction_PlaceCrop.cs`, `CharacterAction_WaterCrop.cs` — mutation actions.
- `CropHarvestable.cs` — subclass of [[character|Harvestable]] with cell coupling + 1-shot/perennial branch.
- `CropHarvestableNetSync.cs` — sibling NetworkBehaviour with the `IsDepleted` NetworkVariable.
- (deleted 2026-04-29) ~~`CropVisualSpawner.cs`~~ — folded into `CropHarvestable.ApplyVisual`.

**Generic action (not crop-specific):** [Assets/Scripts/Character/CharacterActions/CharacterAction_DestroyHarvestable.cs](../../Assets/Scripts/Character/CharacterActions/CharacterAction_DestroyHarvestable.cs).

**UI:** [Assets/Scripts/UI/Interaction/](../../Assets/Scripts/UI/Interaction/) — `UI_InteractionMenu.cs`, `UI_InteractionOptionRow.cs`, plus `Resources/UI/UI_InteractionMenu.prefab`.

**Modified existing:**
- `Assets/Scripts/Interactable/Harvestable.cs` — gained the destruction surface, the yield-tool gate, `OnDepleted`/`OnDestroyed` virtuals, `GetInteractionOptions`.
- `Assets/Scripts/Interactable/HarvestInteractionOption.cs` — option struct used by the menu.
- `Assets/Scripts/Character/CharacterActions/CharacterActions.cs` — `RequestDestroyHarvestableServerRpc`.
- `Assets/Scripts/Character/Character.cs` — `CropPlacement` accessor.
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` — E-key tap/hold dispatcher.
- `Assets/Scripts/World/MapSystem/MapController.cs` — `NotifyDirtyCells` + `SendDirtyCellsClientRpc`, `WakeUp` initialises `FarmGrowthSystem` (which then calls `PostWakeSweep` to reconstruct `CropHarvestable` instances from cell state).
- `Assets/Scripts/World/MapSystem/MacroSimulator.cs` — `SimulateCropCatchUp` + `MacroSimulatorCropMath` helper.
- `Assets/Scripts/Core/GameLauncher.cs` + `Assets/Scripts/Core/SaveLoad/SaveManager.cs` — `CropRegistry.Initialize/Clear` lifecycle.

## Public API / entry points

### Plant / water (player + NPC)
```csharp
character.CropPlacement.StartPlacement(seedInstance);   // ghost on, click commits
character.CropPlacement.StartWatering();                // hover indicator, click commits
character.CharacterActions.ExecuteAction(new CharacterAction_PlaceCrop(actor, map, cellX, cellZ, crop));
character.CharacterActions.ExecuteAction(new CharacterAction_WaterCrop(actor, map, cellX, cellZ, moistureSetTo));
```

### Harvest / destroy (player path via Interact, NPC path via direct queue)
```csharp
harvestable.Interact(character);                                            // tap-E yield path
character.CharacterActions.ExecuteAction(new CharacterAction_DestroyHarvestable(actor, harvestable));
```

### Server-only daily tick
```csharp
FarmGrowthSystem.PostWakeSweep();           // reconstruct harvestables from cell state
FarmGrowthSystem.RegisterHarvestable(x, z, h);
FarmGrowthSystem.UnregisterHarvestable(x, z);
```

### Client cell delta (called by server)
```csharp
mapController.NotifyDirtyCells(int[] indices);   // fires SendDirtyCellsClientRpc with payload
```

### Static lookup
```csharp
MWI.Farming.CropRegistry.Get("apple");      // returns CropSO or null
MWI.Farming.CropRegistry.Initialize();      // GameLauncher.LaunchSequence
MWI.Farming.CropRegistry.Clear();           // SaveManager.ResetForNewSession
```

## Data flow

```
TimeManager.OnNewDay (server, day rollover)
        │
        ▼
FarmGrowthSystem.HandleNewDay()
   ├─ for each plowed+planted cell:
   │      ├─ resolve CropSO via CropRegistry
   │      └─ FarmGrowthPipeline.AdvanceOneDay(ref cell) → outcome enum
   ├─ JustMatured  → SpawnCropHarvestable (NetworkObject.Spawn THEN InitializeFromCell)
   ├─ JustRefilled → registered harvestable.Refill() → IsDepleted = false
   └─ batch dirty cell indices
        │
        ▼
MapController.NotifyDirtyCells(int[] indices)
   ├─ build TerrainCellSaveData[] payload from current grid
   └─ SendDirtyCellsClientRpc(indices, payload)
        │
        ▼
   ┌─────────┴──────────┐
   ▼                    ▼
client local grid       CropVisualSpawner.OnDirtyCells
mirror updated          ├─ for each idx: Refresh(idx)
                        ├─ growing → spawn/update stage sprite
                        └─ mature  → RemoveVisual (CropHarvestable owns it now)

CropHarvestable.IsDepleted (NetworkVariable<bool>)
   └─ OnValueChanged on every peer → ApplyDepletedVisual (ready ↔ depleted sprite)

Player input (PlayerController.Update, IsOwner)
   ├─ Seed held + E       → CropPlacement.StartPlacement
   ├─ WateringCan held + E → CropPlacement.StartWatering
   ├─ Tap E (no item)     → nearest InteractableObject.Interact() = yield path
   └─ Hold E ≥ 0.4s       → UI_InteractionMenu (yield + destruction options)
                              └─ selected → option.ActionFactory(character) → ExecuteAction

MacroSimulator (server, hibernation only)
   ├─ SimulateVegetationCatchUp (existing — skips IsPlowed)
   └─ SimulateCropCatchUp (new) → MacroSimulatorCropMath.AdvanceCellOffline per cell
        ├─ growing crop: GrowthTimer += daysPassed (clamped)
        └─ depleted perennial: TimeSinceLastWatered += daysPassed; flip to -1 after RegrowDays
```

## Dependencies

### Upstream
- [[terrain-and-weather]] — owns `TerrainCellGrid`, `TerrainCell` schema, the moisture pipeline, and the auto-skip for `IsPlowed` cells in `VegetationGrowthSystem`. Farming reuses its persistence + network sync wholesale.
- [[world]] — `MapController` hosts the per-map `FarmGrowthSystem` and provides `GetMapAtPosition` for ServerRpcs.
- [[world-map-hibernation]] — `MapController.WakeUp()` triggers `FarmGrowthSystem.PostWakeSweep` after cell restore.
- [[world-macro-simulation]] — `MacroSimulator.RunCatchUp` includes `SimulateCropCatchUp` between vegetation and yields.
- [[items]] — `ItemSO` references for produce, seed→crop links, destruction outputs, tool gating.
- [[character]] — `Character` exposes `CropPlacement` (CharacterSystem); harvest/destroy goes through `CharacterActions`.
- [[character-equipment]] — `HandsController.CarriedItem` is the source of truth for what tool the player is holding.
- [[save-load]] — `SaveManager.ResetForNewSession` calls `CropRegistry.Clear`.

### Downstream
- Future NPC farming AI (GOAP/BT) will queue the same `CharacterAction_PlaceCrop` / `_WaterCrop` / `_DestroyHarvestable` actions players use.
- Future bounty / quest content can reference crops by `CropSO.Id` for harvest-N-of-X tasks.

## State & persistence

| Data | Authority | Persistence | Sync |
|------|-----------|-------------|------|
| Cell crop fields (`IsPlowed`, `PlantedCropId`, `GrowthTimer`, `TimeSinceLastWatered`, `Moisture`) | Server | `TerrainCellSaveData` (already in `MapSaveData.TerrainCells`) | `SendTerrainGridClientRpc` (full) + `SendDirtyCellsClientRpc` (delta) |
| `CropHarvestable` GameObject identity | Server | **Not persisted directly** — reconstructed from cell state on every `WakeUp` via `PostWakeSweep` | NGO `NetworkObject.Spawn` |
| `CropHarvestable.IsDepleted` (NetworkVariable<bool>) | Server | **Derived** — set in `InitializeFromCell(... startDepleted)` from `cell.TimeSinceLastWatered >= 0f` | NGO NetworkVariable, late-joiners get current value automatically |
| `_activeHarvestables` registry on `FarmGrowthSystem` | Server | Not persisted; rebuilt during `PostWakeSweep` | n/a (server runtime) |
| (no separate visual cache) | — | Visual lives on the `CropHarvestable` GameObject itself; networked via NGO. | n/a |
| `CropPlacementManager` ghost / mode | Local | Not persisted (player resumes idle) | n/a |
| `CropRegistry` (string Id → CropSO) | Static | Asset data — `Resources.LoadAll<CropSO>("Data/Farming/Crops")` at game launch | Clients load same assets |

**The cell encoding `(PlantedCropId, GrowthTimer >= DaysToMature, TimeSinceLastWatered)` is sufficient to reconstruct any harvestable in any state.** Save/load and hibernation/wake share the same `PostWakeSweep` code path.

## Known gotchas / edge cases

- **`MWI.Farming.Pure` asmdef cannot reference `Assembly-CSharp`.** `CropSO` types its item fields (`_produceItem`, `_requiredHarvestTool`, `_requiredDestructionTool`, `_destructionOutputs`) as `ScriptableObject` rather than `ItemSO`. Runtime callers cast back to `ItemSO` at use sites (`CropHarvestable.InitializeFromCell` uses a private `CastItemList` helper). The Inspector picker accepts any ScriptableObject, so an `OnValidate` runtime check would tighten this — deferred.
- **Test asmdef placement.** Pure-logic tests live in `Assets/Tests/EditMode/Farming/` referencing `MWI.Farming.Pure`. Tests that need Assembly-CSharp types (`Harvestable`, `TerrainCell`, `CropSO` via the casts) live in `Assets/Editor/Tests/Farming/` and route to the auto-generated `Assembly-CSharp-Editor-testable` assembly.
- **(Resolved 2026-04-29 by the single-GameObject-per-crop rework.)** ~~Spawn-order race when a cell crosses `DaysToMature`...~~ The visual handoff between a cell-side spawner and the `CropHarvestable` is gone — there's only one visual now (the harvestable itself), driven by `CurrentStage` / `IsDepleted` NetworkVariables. No race possible.
- **`CropHarvestable` is not a `NetworkBehaviour` itself.** It inherits from `Harvestable : InteractableObject : MonoBehaviour`. The `IsDepleted` NetworkVariable lives on a sibling `CropHarvestableNetSync : NetworkBehaviour` on the same prefab. Both must be present — `[RequireComponent]` enforces this. The prefab also needs a `NetworkObject` for spawn/despawn syncing.
- **Wild scene-placed `Harvestable`s aren't networked.** Only `CropHarvestable` (runtime-spawned, has `NetworkObject` on its prefab) syncs across clients. Wild rocks/trees (scene-authored, no `NetworkObject`) mutate state server-only and clients don't see updates — acceptable for static scene content, would need a refactor if a wild equivalent of perennial refill is needed.
- **`_respawnDelayDays` on base `Harvestable` ≠ perennial refill.** The base field deletes the visual entirely for N days then restores; perennial harvestables stay standing and only swap `IsDepleted`. Different mechanism, both coexist. Designers must NOT set both for the same prefab.
- **Plant-time and one-shot post-harvest both set `cell.TimeSinceLastWatered = -1f`.** That sentinel means "not depleted / ready". `0f` (depleted, refill cycle 0) is set only by perennial post-harvest. Anything ≥ 0 is meaningful only for perennial mature cells.
- **Hold-E menu requires `Resources/UI/UI_InteractionMenu.prefab`.** Without it the menu logs an error and the hold-E path silently no-ops. Tap-E (yield path via `Interact`) still works in that degraded state.
- **Multi-cycle perennial refill across hibernation is not modeled.** `MacroSimulatorCropMath.AdvanceCellOffline` wraps to "ready" after one full `RegrowDays` cycle; longer absences don't accumulate multiple harvests' worth. Becomes meaningful only when NPC farming AI lands.
- **`CropHarvestable` is intentionally NOT parented under `MapController`.** See [[static-registry-late-joiner-race]] for the related registry-init race. Specifically for parenting: NGO's `SceneEventData.SortParentedNetworkObjects` walks every root NetworkObject's hierarchy via `GetComponentsInChildren<NetworkObject>()` during initial-sync to a late-joiner, which surfaces an NGO NRE bug at `NetworkObject.Serialize` line 3182 (`HasOwnershipFlags = NetworkManagerOwner.DistributedAuthorityMode`) only on the real sync path — the `PurgeBrokenSpawnedNetworkObjects` direct-Serialize probe doesn't reproduce. Removing the `TrySetParent` call sidesteps the issue. Server-side back-reference to the map lives in `CropHarvestable._map` for all logic; nothing in the codebase did `GetComponentsInChildren<CropHarvestable>` on `MapController`. Cosmetic-only effect: scene Hierarchy panel shows crops at scene root instead of nested under the map.
- **`HandsController.CarriedItem` is a non-networked plain MonoBehaviour field.** The server's view of a dedicated-client player's hand is always empty. Server-side gates that read held-item state will fail for client-issued ServerRpcs. Pattern: validate + consume held items locally on the owning client BEFORE issuing the ServerRpc, then trust the request server-side. See `CropPlacementManager.ConsumeHeldSeedLocally` for the canonical example. Same caveat applies to any future `RequestXServerRpc` that wants to know "what is the player holding?"
- **Static registry init race on joining clients.** Cross-cutting issue beyond farming. See [[static-registry-late-joiner-race]] — both `TerrainTypeRegistry` and `CropRegistry` were affected and now lazy-init in `Get()`. Any new static registry should follow the same pattern.

## Open questions / TODO

- [ ] **Seasons on `TimeManager`.** Deferred per Kevin. `CropSO` will gain a `_seasons: SeasonFlags` field defaulting to "all seasons" so existing assets keep working.
- [ ] **Building placement snap to `TerrainCellGrid`.** Separate sub-project — refactor `BuildingPlacementManager` ghost positioning, footprints in cells, 90° rotations.
- [ ] **Wither/death from drought.** V1 stalls; later add `CropSO._minMoistureForSurvival` + `IsWithered` flag.
- [ ] **Separate plowing action / hoe.** V1 plows automatically when planting; adding a `CharacterAction_PlowCell` is purely additive.
- [ ] **Visible world-grid overlay** for the player. Needs its own design pass.
- [ ] **Watering can charges / refill at well.** V1 is infinite-use.
- [ ] **Crop quality / yield modifiers from `Fertility`.** `Fertility` exists on the cell but V1 ignores it.
- [ ] **NPC farming AI.** GOAP/BT integration is a separate spec.
- [ ] **Tool category enum.** V1 destruction tool gating is by exact `ItemSO` reference. A `ToolCategory` enum (Axe/Pickaxe/Hoe/Sickle) would let multiple tool variants match a single requirement.
- [ ] **Tool wear / durability.** Destruction is "free" in V1.
- [ ] **Skill XP from destruction / harvest.** No skill bumps yet.
- [ ] **Unifying perennial refill with wild `_respawnDelayDays`.** Worth merging into one strategy SO if a "wild apple tree in a forest" use case appears.
- [ ] **Multi-pick across hibernation for perennials.** Deferred until NPC harvesting AI exists.
- [ ] **Tighten `CropSO` Inspector typing.** Item-typed fields are `ScriptableObject` due to the Pure asmdef boundary; an `OnValidate` runtime check could reject non-`ItemSO` assignments.
- [ ] **Specialist agent.** No `farming-specialist` agent yet. Likely worth one when NPC AI farming lands.
- [x] ~~**Collapse `CropVisualSpawner` into `CropHarvestable` (single-GameObject-per-crop model).**~~ **Shipped 2026-04-29** (commit `ff62d2d1`). `CropVisualSpawner.cs` deleted; one `CropHarvestable` per cell from plant-time, visual driven by `CurrentStage` / `IsDepleted` / `CropIdNet` NetworkVariables on the sibling `CropHarvestableNetSync`. `Harvestable.CanHarvest` is now `virtual` so `CropHarvestable` can add the maturity gate. Scale lerps 0.25→1.0 across growth so the procedural cube prefab is visibly progressing without art assets.
- [ ] **`VegetationGrowthSystem` per-tree GameObject rework.** Same critique as the above (now-resolved) farming entry, applied to wild vegetation. Currently the terrain layer tracks growth purely as cell state and would spawn separate prefabs at each stage — a designer pain point. Per-tree GameObject (or just per-mature-tree) would mirror the post-rework farming approach. Out of scope for the farming spec; flagged here for continuity. See [[terrain-and-weather]].

## Change log

- 2026-04-30 — JobFarmer + FarmingBuilding integration shipped (Plan 3 of farmer rollout). FarmingBuilding extends HarvestingBuilding with multi-zone field designation (`List<Zone> _farmingAreaZones`), daily PlantScan + WaterScan, auto-derived IStockProvider seed/can targets, per-task WateringCan pickup via Plan 1 ToolStorage primitive. JobFarmer GOAP-driven Plant/Water/Harvest cycle. CropSO `_harvestOutputs` extended on the 3 existing crops (Wheat, Flower, AppleTree) with matching SeedSO entries — crops now self-seed via the existing RegisterHarvestedItem deposit cycle (zero new code). See [[job-farmer]]. — claude
- 2026-04-28 — System designed and implemented (10 commits). Pure-logic + math fully tested (15 EditMode tests; 58/58 green). Pending: sample assets (`Crop_Wheat`/`Crop_Flower`/`Crop_AppleTree`), `UI_InteractionMenu` prefab, manual playmode acceptance pass. — claude
- 2026-04-29 — Playmode integration session. Sample assets committed (3 crops + 4 prefabs + 6 items). UI prefab created and renamed to `UI_HarvestInteractionMenu` to avoid name collision with the pre-existing global `UI_InteractionMenu`. Several runtime bugs surfaced + fixed: prefab variant fileID/guid breakage on `_harvestablePrefab`, missing `TerrainCellGrid.Initialize` call (pre-existing terrain gap; bootstrap added from `BoxCollider`), `TerrainTypeRegistry.Get` null-key crash, `MapController.WakeUp` farming init gated to hibernation-only path (moved out, plus self-init in `Start()` for scene-authored maps that never WakeUp), `SendDirtyCellsClientRpc` host-bug (`if (IsServer) return` skipped local visual notification on the host). Crops now plant + grow + display on host. Two architectural deferrals captured: collapse `CropVisualSpawner` into `CropHarvestable` (single-GameObject-per-crop) and same critique for `VegetationGrowthSystem`. — claude / [[kevin]]
- 2026-04-29 — Tooling — `HarvestableInspectorView` ([[dev-mode]]) added so any `Harvestable` (crop or wilderness) can be Ctrl+Click-selected and inspected at runtime. The Crop section dumps `CropHarvestableNetSync` NetVars (`CurrentStage` / `IsDepleted` / `CropIdNet`), the resolved `CropSO`, and the full `TerrainCell` state — replacing the manual `Debug.Log(cell)` recipe in `.agent/skills/farming/SKILL.md`. Selectability piggybacks on the layer rename in this same change-log entry. Layer index 15 was renamed `Crop → Harvestable` in `ProjectSettings/TagManager.asset` (prefabs reference the layer by index, no prefab edits needed). No farming code changes. — claude
- 2026-04-29 — **Single-GameObject-per-crop rework shipped same-day** (commit `ff62d2d1`). `CropVisualSpawner.cs` deleted. `CropHarvestable` now spawned at plant-time via `FarmGrowthSystem.SpawnCropHarvestableAt`. Three NetworkVariables on `CropHarvestableNetSync` drive the visible state on every peer: `CurrentStage` (gates `CanHarvest`), `IsDepleted` (perennial post-harvest), `CropIdNet` (CropSO resolution on clients). `Harvestable.CanHarvest` made virtual so `CropHarvestable` adds the maturity check. Scale lerps 0.25→1.0 across growth so the procedural cube visibly progresses without art assets. `MapController.WakeUp` and `SendDirtyCellsClientRpc` lose all spawner branches. 58/58 EditMode tests still green. The `VegetationGrowthSystem` per-tree-GameObject critique stays open in the optimisation backlog. — claude / [[kevin]]
- 2026-04-29 — **Multi-output Harvest schema.** Replaced the single `_produceItem` + flat `_produceCount` int on `CropSO` (and the single `_outputItems : List<ItemSO>` + `_destructionOutputCount` on base `Harvestable`) with a `List<(Item, Count)>` entry struct (`HarvestOutputEntry` gameplay-side / `CropHarvestOutput` Pure-side). `Harvestable.Harvest` now returns the entry list and `CharacterActions.ApplyHarvestOnServer` spawns each entry × count as separate `WorldItem`s with XZ jitter, registering one `PickupLooseItemTask` per spawn. `CropSO.OnValidate` migrates legacy fields → new entries on edit; `HarvestOutputs` / `DestructionOutputs` getters lazy-migrate at runtime as a safety net for built clients whose asset bundle predates the migration. All three sample crop assets (AppleTree / Wheat / Flower) migrated via Roslyn. — claude / [[kevin]]
- 2026-04-29 — **`Harvestable` perennial regrow bug fix.** Base `Harvestable.Deplete` was scheduling an auto-respawn via `OnNewDay` after `_respawnDelayDays`; `CropHarvestable.InitializeFromCell` sets that delay to 0 so the very next midnight rollover would un-deplete the perennial regardless of `RegrowDays` and the moisture gate in `FarmGrowthPipeline` PHASE C. Extracted the respawn scheduling into a new `protected virtual ScheduleRespawnAfterDeplete()` hook; `CropHarvestable` overrides it as a no-op so `FarmGrowthSystem` retains exclusive ownership of the perennial refill cycle. Symptom before fix: "I can harvest indefinitely / RegrowDays = 2 ignored, regrows every day." — claude / [[kevin]]
- 2026-04-29 — **Save/load completeness.** Three bugs in `MapController` / `SaveManager`: (a) `SnapshotActiveNPCs` did not serialise `TerrainCellGrid.SerializeCells()` into `MapSaveData.TerrainCells` (only `Hibernate()` did), so cells with planted crops on an *active* map were dropped on save; (b) `SaveManager.SaveWorldAsync` filter `if (HibernatedNPCs.Count == 0 && WorldItems.Count == 0) continue;` discarded snapshots that contained only `TerrainCells`; (c) `SpawnNPCsFromSnapshot` early-returned when zero NPCs, skipping the `SpawnWorldItemsFromSnapshot` call. Snapshot path now also runs `TerrainCellGrid.RestoreFromSaveData` + `FarmGrowthSystem.PostWakeSweep` (mirroring `WakeUp`), so freshly-loaded worlds reconstruct planted crops + dropped items. — claude / [[kevin]]
- 2026-04-29 — **Multiplayer client farming fixes.** Five distinct bugs surfaced when the user tested host↔client: (1) `SpawnCropHarvestableAt` reordered so `InitializeFromCell` runs **before** `Spawn` — NetVars (`CropIdNet`/`CurrentStage`/`IsDepleted`) now land in NGO's spawn payload, so the client's first `OnNetSyncChanged` sees the right values and `ApplyVisual` doesn't render the prefab at full scale. (2) `CropHarvestable` overrode `CanHarvest`, `IsMature`, `AllowDestruction`, `RequiredDestructionTool`, `GetInteractionOptions` to read from the registry-resolved `CropSO` + `_netSync` NetVars instead of server-only runtime fields (`_harvestOutputs` / `_crop`) — non-host clients now see correct hold-E menu rows and tap-E harvest succeeds. (3) `CropHarvestable.Update` polls the three NetVars and triggers `ApplyVisual` on change as a safety net for any NGO `OnValueChanged` that fails to fire on remote clients (cheap: 3 reads + 3 compares per crop per frame, idempotent on no-change). (4) `CropPlacementManager` consumes the held seed locally on the owning peer **before** issuing `RequestPlaceCropServerRpc` — `HandsController.CarriedItem` is a non-networked field so the server's view of a dedicated client's hand is always empty; the client owns its own inventory. (5) Removed `netObj.TrySetParent(mapNetObj, worldPositionStays: true)` for fresh-spawned `CropHarvestable`s — surfaced a dedicated NGO bug where parented runtime-spawned NetworkObjects NRE inside `NetworkObject.Serialize` during initial-sync to a late-joining client (the `PurgeBrokenSpawnedNetworkObjects` Serialize probe doesn't reproduce because `SortParentedNetworkObjects` pulls children into the sync list via `GetComponentsInChildren<NetworkObject>` differently from a direct probe call). Symptom before fix: host plants → client tries to join → connection fails with `NetworkObject.Serialize` NRE loop on the host. Crops now live at scene root; nothing in the codebase did `GetComponentsInChildren<CropHarvestable>` on `MapController`. — claude / [[kevin]]
- 2026-04-29 — **Static registry late-joiner race resolved.** `TerrainTypeRegistry.Get` and `CropRegistry.Get` now lazy-init on first access (idempotent `Initialize()` bails if already populated). Joining clients skip `GameLauncher.LaunchSequence` entirely (host-only path) — explicit init in `GameSessionManager.HandleClientConnected` was added but races against NGO replicating spawned NetworkObjects (`CharacterTerrainEffects.Update` / `CropHarvestable.ResolveCropFromNet`) into the joiner's scene before `OnClientConnectedCallback` fires. Documented as a recurring class of bug in [[static-registry-late-joiner-race]]. Symptoms before fix: per-frame `[TerrainTypeRegistry] Not initialized` error spam on every joining client; second client's hold-E menu was empty + couldn't harvest because `CropRegistry.Get` returned null. — claude / [[kevin]]
- 2026-04-29 — **Destroy chain pickup-task parity + GOAP↔auto-claim handoff fix.** Two coupled fixes that together unblocked the harvester's destroy → pickup → deposit loop. (a) `Harvestable.DestroyForOutputs` now returns `List<WorldItem>` of the spawned destruction drops (was `void`), and `SpawnDestructionItem` returns the spawned item. New `CharacterActions.ApplyDestroyOnServer(Harvestable)` mirrors `ApplyHarvestOnServer`: calls `DestroyForOutputs`, resolves the destroyer's `JobHarvester` workplace, registers a `PickupLooseItemTask` on the workplace's TaskManager for each spawned drop. Both `CharacterAction_DestroyHarvestable.OnApplyEffect` (host/NPC) and `CharacterActions.RequestDestroyHarvestableServerRpc` (client) now route through it. Without this pass the planner's `looseItemExists` flag never flipped after a chop and the wood sat orphaned. (b) GOAP↔auto-claim asymmetry resolved via `BuildingTaskManager.FindClaimedTaskByWorker<T>` — see [[building-task-manager]] change log. Both `GoapAction_DestroyHarvestable` and `GoapAction_HarvestResources` Phase 1 now check for a pre-existing claim before calling `ClaimBestTask`. (c) `GoapAction_ExploreForHarvestables` now uses union semantics (yield-OR-destroy match) for discovery, fixing apple-tree-as-wood-source detection. (d) `GoapAction_*` arrival tolerance bumped 1.5f → 2.5f to match `CharacterAction_*` range checks. (e) `JobHarvester` now pre-filters `_availableActions` through `IsValid` before calling `GoapPlanner.Plan` (mirrors `JobLogisticsManager`). Symptoms before fix: NPC harvester planted apple tree → loop between Idle and DestroyHarvestable, no chop, no wood, no deposit. — claude / [[kevin]]
- 2026-04-29 — **Harvestable / CropHarvestable unification (8 phases).** Folded the previous `CropHarvestable : Harvestable` two-class hierarchy into a single `Harvestable` primitive that handles wild scenery (trees, rocks), planted crops (CropSO + cell-coupled + networked), and dynamic ore nodes (HarvestableSO + free-positioned + optional networking) under one mental model. **Phase 1**: new `MWI.Interactable.Pure` asmdef; `HarvestableSO` is the universal data root (Id, HarvestOutputs, RequiredHarvestTool, depletion, destruction, sprites, prefab); `CropSO : HarvestableSO` retains only farming extensions (DaysToMature, MinMoistureForGrowth, IsPerennial, RegrowDays, PlantDuration, StageSprites). Pure-side struct `MWI.Farming.CropHarvestOutput` → `MWI.Interactables.HarvestableOutputEntry`. **Phase 2**: cell coupling fields (CellX, CellZ, Grid, _map) promoted from CropHarvestable to Harvestable as `protected`; new unified `Harvestable.InitializeAtStage(so, startStage, startDepleted, map, cellX, cellZ, grid)` entry point exposes the "place at any stage" API designers wanted. **Phase 3**: `CropHarvestableNetSync` renamed to `HarvestableNetSync` and moved to `Assets/Scripts/Interactable/`, RequireComponent dropped → opt-in. **Phase 4**: `CropHarvestable.cs` deleted. All its logic (perennial-vs-one-shot OnDepleted, ApplyVisual scaling, NetVar polling fallback, CropSO resolution, GetInteractionOptions client-readable path) now lives on Harvestable, gated on `_so is CropSO` / `IsCellCoupled` / `_netSync != null`. CropHarvestable_Default.prefab `m_Script` GUID swapped (variants inherit through prefab variant chain). **Phase 5**: new `Harvestable.OnStateChanged` event fires on Deplete / Respawn / SetReady / SetDepleted; `HarvestingBuilding` subscribes to it instead of the legacy `OnRespawned`-only path so wild trees AND CropSO-driven crops/ore are tracked identically. **Phase 6 deferred** — InitializeAtStage already exposes the API; designer UX (a stage-dropdown placement menu) can come later if needed. **Phase 7**: sample `HarvestableSO_OreNode.asset` validates the unified system supports a non-crop resource node end-to-end (5 swings → 2 wood each, requires axe, regrows after 3 days, no farming-specific fields). **Phase 8**: this change-log entry, SKILL update, and new `harvestable-resource-node-specialist` agent. — claude / [[kevin]]

## Sources

- [docs/superpowers/specs/2026-04-28-farming-plot-system-design.md](../../docs/superpowers/specs/2026-04-28-farming-plot-system-design.md) — design spec.
- [docs/superpowers/plans/2026-04-28-farming-plot-system.md](../../docs/superpowers/plans/2026-04-28-farming-plot-system.md) — implementation plan.
- [.agent/skills/farming/SKILL.md](../../.agent/skills/farming/SKILL.md) — procedural how-to (add a crop, debug growth).
- [Assets/Scripts/Farming/](../../Assets/Scripts/Farming/) — primary implementation folder.
- [Assets/Scripts/Interactable/Harvestable.cs](../../Assets/Scripts/Interactable/Harvestable.cs) — base interaction class refactored for two-path interaction.
- [Assets/Scripts/UI/Interaction/](../../Assets/Scripts/UI/Interaction/) — Hold-E menu UI.
- [Assets/Scripts/World/MapSystem/MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) — `NotifyDirtyCells` ClientRpc + WakeUp integration.
- [Assets/Scripts/World/MapSystem/MacroSimulator.cs](../../Assets/Scripts/World/MapSystem/MacroSimulator.cs) — `SimulateCropCatchUp` orchestration.
- [Assets/Scripts/World/MapSystem/MacroSimulatorCropMath.cs](../../Assets/Scripts/World/MapSystem/MacroSimulatorCropMath.cs) — pure offline catch-up math.
- [Assets/Tests/EditMode/Farming/](../../Assets/Tests/EditMode/Farming/) — Pure-asmdef tests (registry + SO validation).
- [Assets/Editor/Tests/Farming/](../../Assets/Editor/Tests/Farming/) — Assembly-CSharp tests (predicates, pipeline, catch-up).
- [Assets/Scripts/Debug/DevMode/Inspect/HarvestableInspectorView.cs](../../Assets/Scripts/Debug/DevMode/Inspect/HarvestableInspectorView.cs) — runtime debug surface for `Harvestable` + `CropHarvestable`. Reads `CropHarvestableNetSync` NetVars + `TerrainCellGrid.GetCellRef`. See [[dev-mode]].
- 2026-04-28 conversation with Kevin — design decisions: cell-grid native (option A), no-wither (option B), one-way crop→harvestable transition, perennial-stays-standing, two-path interaction with Hold-E menu, axe-as-destruction-tool.
