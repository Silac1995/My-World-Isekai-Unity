---
type: system
title: "Farming / Plot System"
tags: [farming, crops, harvestable, plot, perennial, tier-1]
created: 2026-04-28
updated: 2026-04-29
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
  - "[[kevin]]"
status: wip
confidence: medium
primary_agent: null
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
depended_on_by: []
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
- `CropVisualSpawner` (client-only on `MapController`) — sprite-per-cell during the **growing** phase. Early-exits and removes its sprite the moment a cell crosses `DaysToMature`; `CropHarvestable` owns the visual past maturity.
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
- `CropVisualSpawner.cs` — client-side stage-sprite renderer.

**Generic action (not crop-specific):** [Assets/Scripts/Character/CharacterActions/CharacterAction_DestroyHarvestable.cs](../../Assets/Scripts/Character/CharacterActions/CharacterAction_DestroyHarvestable.cs).

**UI:** [Assets/Scripts/UI/Interaction/](../../Assets/Scripts/UI/Interaction/) — `UI_InteractionMenu.cs`, `UI_InteractionOptionRow.cs`, plus `Resources/UI/UI_InteractionMenu.prefab`.

**Modified existing:**
- `Assets/Scripts/Interactable/Harvestable.cs` — gained the destruction surface, the yield-tool gate, `OnDepleted`/`OnDestroyed` virtuals, `GetInteractionOptions`.
- `Assets/Scripts/Interactable/HarvestInteractionOption.cs` — option struct used by the menu.
- `Assets/Scripts/Character/CharacterActions/CharacterActions.cs` — `RequestDestroyHarvestableServerRpc`.
- `Assets/Scripts/Character/Character.cs` — `CropPlacement` accessor.
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` — E-key tap/hold dispatcher.
- `Assets/Scripts/World/MapSystem/MapController.cs` — `NotifyDirtyCells` + `SendDirtyCellsClientRpc`, `WakeUp` initialises `FarmGrowthSystem` and `CropVisualSpawner`.
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
- [[world]] — `MapController` hosts the per-map `FarmGrowthSystem` + `CropVisualSpawner` and provides `GetMapAtPosition` for ServerRpcs.
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
| `CropVisualSpawner._activeVisuals` | Client | Not persisted; rebuilt on every map ready | n/a (cosmetic local) |
| `CropPlacementManager` ghost / mode | Local | Not persisted (player resumes idle) | n/a |
| `CropRegistry` (string Id → CropSO) | Static | Asset data — `Resources.LoadAll<CropSO>("Data/Farming/Crops")` at game launch | Clients load same assets |

**The cell encoding `(PlantedCropId, GrowthTimer >= DaysToMature, TimeSinceLastWatered)` is sufficient to reconstruct any harvestable in any state.** Save/load and hibernation/wake share the same `PostWakeSweep` code path.

## Known gotchas / edge cases

- **`MWI.Farming.Pure` asmdef cannot reference `Assembly-CSharp`.** `CropSO` types its item fields (`_produceItem`, `_requiredHarvestTool`, `_requiredDestructionTool`, `_destructionOutputs`) as `ScriptableObject` rather than `ItemSO`. Runtime callers cast back to `ItemSO` at use sites (`CropHarvestable.InitializeFromCell` uses a private `CastItemList` helper). The Inspector picker accepts any ScriptableObject, so an `OnValidate` runtime check would tighten this — deferred.
- **Test asmdef placement.** Pure-logic tests live in `Assets/Tests/EditMode/Farming/` referencing `MWI.Farming.Pure`. Tests that need Assembly-CSharp types (`Harvestable`, `TerrainCell`, `CropSO` via the casts) live in `Assets/Editor/Tests/Farming/` and route to the auto-generated `Assembly-CSharp-Editor-testable` assembly.
- **Spawn-order race (client).** When a cell crosses `DaysToMature`, two networked operations fire near-simultaneously: the cell delta and the `CropHarvestable` `NetworkObject.Spawn`. Order isn't deterministic but the handoff is robust because `CropVisualSpawner.Refresh` early-exits + removes its sprite when `cell.GrowthTimer >= crop.DaysToMature`. That single line is the load-bearing handoff.
- **`CropHarvestable` is not a `NetworkBehaviour` itself.** It inherits from `Harvestable : InteractableObject : MonoBehaviour`. The `IsDepleted` NetworkVariable lives on a sibling `CropHarvestableNetSync : NetworkBehaviour` on the same prefab. Both must be present — `[RequireComponent]` enforces this. The prefab also needs a `NetworkObject` for spawn/despawn syncing.
- **Wild scene-placed `Harvestable`s aren't networked.** Only `CropHarvestable` (runtime-spawned, has `NetworkObject` on its prefab) syncs across clients. Wild rocks/trees (scene-authored, no `NetworkObject`) mutate state server-only and clients don't see updates — acceptable for static scene content, would need a refactor if a wild equivalent of perennial refill is needed.
- **`_respawnDelayDays` on base `Harvestable` ≠ perennial refill.** The base field deletes the visual entirely for N days then restores; perennial harvestables stay standing and only swap `IsDepleted`. Different mechanism, both coexist. Designers must NOT set both for the same prefab.
- **Plant-time and one-shot post-harvest both set `cell.TimeSinceLastWatered = -1f`.** That sentinel means "not depleted / ready". `0f` (depleted, refill cycle 0) is set only by perennial post-harvest. Anything ≥ 0 is meaningful only for perennial mature cells.
- **Hold-E menu requires `Resources/UI/UI_InteractionMenu.prefab`.** Without it the menu logs an error and the hold-E path silently no-ops. Tap-E (yield path via `Interact`) still works in that degraded state.
- **Multi-cycle perennial refill across hibernation is not modeled.** `MacroSimulatorCropMath.AdvanceCellOffline` wraps to "ready" after one full `RegrowDays` cycle; longer absences don't accumulate multiple harvests' worth. Becomes meaningful only when NPC farming AI lands.

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
- [ ] **Collapse `CropVisualSpawner` into `CropHarvestable` (single-GameObject-per-crop model).** Current design has two visual layers: `CropVisualSpawner` (local-only stage cube/sprite during growth) and `CropHarvestable` (`NetworkObject`, spawned at maturity). The handoff is the §6 "spawn-order race" — robust but conceptually heavier than needed. Cleaner alternative: spawn one `CropHarvestable` per cell at **plant-time** instead of maturity-time; drive its visual from a `NetworkVariable<int> CurrentStage` (or by reading `cell.GrowthTimer` on tick). Existing `_readySprite`/`_depletedSprite` extend to a `_stageSprites[]` array on the prefab. `CanHarvest()` returns false while growing. Removes `CropVisualSpawner.cs`, the `MapController.NotifyDirtyCells` → spawner fan-out, and the visual handoff logic in `Refresh`. Cost: one `NetworkObject` per crop (vs. one per mature crop today) — a perf concern for huge farm scenes; defer until profiling shows it. **Reason for current design:** speculative network-footprint optimization; in practice the simpler model is more likely to be the right pick for the "crops are individual" mental model and tractable scenes. Kevin flagged this 2026-04-29 as the architecturally cleaner direction.
- [ ] **`VegetationGrowthSystem` per-tree GameObject rework.** Same critique as above but for wild vegetation. Currently the terrain layer tracks growth purely as cell state and would spawn separate prefabs at each stage — a designer pain point. Per-tree GameObject (or just per-mature-tree) would mirror the per-crop approach, sharing whatever final pattern wins for farming. Out of scope for the farming spec; flagged here for continuity.

## Change log

- 2026-04-28 — System designed and implemented (10 commits). Pure-logic + math fully tested (15 EditMode tests; 58/58 green). Pending: sample assets (`Crop_Wheat`/`Crop_Flower`/`Crop_AppleTree`), `UI_InteractionMenu` prefab, manual playmode acceptance pass. — claude
- 2026-04-29 — Playmode integration session. Sample assets committed (3 crops + 4 prefabs + 6 items). UI prefab created and renamed to `UI_HarvestInteractionMenu` to avoid name collision with the pre-existing global `UI_InteractionMenu`. Several runtime bugs surfaced + fixed: prefab variant fileID/guid breakage on `_harvestablePrefab`, missing `TerrainCellGrid.Initialize` call (pre-existing terrain gap; bootstrap added from `BoxCollider`), `TerrainTypeRegistry.Get` null-key crash, `MapController.WakeUp` farming init gated to hibernation-only path (moved out, plus self-init in `Start()` for scene-authored maps that never WakeUp), `SendDirtyCellsClientRpc` host-bug (`if (IsServer) return` skipped local visual notification on the host). Crops now plant + grow + display on host. Two architectural deferrals captured: collapse `CropVisualSpawner` into `CropHarvestable` (single-GameObject-per-crop) and same critique for `VegetationGrowthSystem`. — claude / [[kevin]]

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
- 2026-04-28 conversation with Kevin — design decisions: cell-grid native (option A), no-wither (option B), one-way crop→harvestable transition, perennial-stays-standing, two-path interaction with Hold-E menu, axe-as-destruction-tool.
