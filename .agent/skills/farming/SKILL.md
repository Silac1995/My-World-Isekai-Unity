# Farming / Plot System — Procedures

> **Architecture lives in [wiki/systems/farming.md](../../../wiki/systems/farming.md).** This file is for HOW-TO only — adding crops, debugging growth, configuring destructible Harvestables. Do not duplicate architectural notes here.

## Add a new crop

1. **Create the `CropSO` asset.** `Project → Create → Game → Farming → Crop`. Save in `Assets/Resources/Data/Farming/Crops/`. Required Inspector fields:
   - `Id` — string used as `TerrainCell.PlantedCropId`. Must be unique across all crops. Lowercase, no spaces.
   - `DaysToMature` — int, days from stage 0 to ready.
   - `MinMoistureForGrowth` — float, default `0.3`. Cell must be ≥ this each daily tick to advance.
   - `PlantDuration` — float, seconds the player stands still during the plant action.
   - `ProduceItem` — drag the `ItemSO` that drops on harvest. (Inspector picker is permissive — any `ScriptableObject` is accepted; designers must pick an `ItemSO` subclass. The Pure-asmdef boundary forces this. See gotcha in `wiki/systems/farming.md`.)
   - `ProduceCount` — int, how many `ProduceItem`s drop per harvest.
   - `RequiredHarvestTool` — optional `ItemSO`; null = bare hands work.
   - `StageSprites` — array of `Sprite`, length **must equal `DaysToMature`**. Each is one growing-day stage. Mature visual lives separately on the `CropHarvestable` prefab (`_readySprite`).
   - `HarvestablePrefab` — drag the matching `CropHarvestable_*.prefab` (built in step 2).

2. **Build the `CropHarvestable` prefab.** New scene → `GameObject → 2D Object → Sprite`. Add components in this order:
   - `NetworkObject` (required for runtime spawn/despawn syncing).
   - `Harvestable` (auto-added when you add `CropHarvestable` because of the inheritance chain — confirm via Inspector).
   - `CropHarvestable` (the leaf; brings `[RequireComponent(typeof(CropHarvestableNetSync))]`).
   - `CropHarvestableNetSync` (added automatically by the RequireComponent).
   - Wire `CropHarvestable._spriteRenderer` → the SpriteRenderer (root or child).
   - Wire `_readySprite` → the "fruited" sprite (e.g. tree with apples).
   - Wire `_depletedSprite` → the "fruitless" sprite (e.g. tree without apples). Required only for perennials.
   - The base `Harvestable` fields (`_outputItems`, `_maxHarvestCount`, `_isDepletable`, `_respawnDelayDays`) can be left default — `CropHarvestable.InitializeFromCell` overwrites them at runtime from the `CropSO`.
   - Save the prefab in `Assets/Prefabs/Farming/`.
   - Drag the prefab back onto `CropSO._harvestablePrefab`.

3. **Configure perennial settings (optional).** On the `CropSO`:
   - `IsPerennial = true`.
   - `RegrowDays` — days of conditions-met it takes to refill the harvestable. Must satisfy `1 <= RegrowDays <= DaysToMature` (enforced by `OnValidate`).
   - The `CropHarvestable` prefab MUST have `_depletedSprite` set, otherwise the harvestable will appear blank when depleted.

4. **Configure destruction (optional).** On the `CropSO`:
   - `AllowDestruction = true`.
   - `RequiredDestructionTool` — `ItemSO` reference (e.g. `Item_Axe`). Null = any held item works.
   - `DestructionOutputs` — list of `ItemSO`s to drop (e.g. `[Item_Wood]`).
   - `DestructionOutputCount` — how many of each output to spawn.
   - `DestructionDuration` — seconds for the destroy action.

5. **Create the seed item.** `Project → Create → Game → Items → Seed`. Save in `Assets/Resources/Data/Items/`. Set `_cropToPlant` to the `CropSO`. The seed item works automatically when the player holds it and presses E.

## Configure a destructible wild Harvestable

The destruction surface lives on base `Harvestable`, not just `CropHarvestable` — wild scene-placed trees/rocks work the same way.

1. Open the wild Harvestable prefab (e.g. `Assets/Prefabs/Harvestable/Tree.prefab`).
2. In the `Harvestable` component:
   - `_allowDestruction = true`.
   - `_requiredDestructionTool` → `Item_Axe` (or whatever the project's axe `WeaponSO` is).
   - `_destructionOutputs` → list with `Item_Wood`.
   - `_destructionOutputCount = 5`.
   - `_destructionDuration = 4` (seconds).
3. Save. No farming code needed.

In Play Mode, the player holding an axe can hold E next to the tree → menu → "Destroy" → 5 wood drops + tree despawns.

## Debug a crop that won't grow

Walk the chain in this order:

1. **`CropRegistry` initialised?** Check console for `[CropRegistry] Initialised with N crop(s).` at startup. If missing or N is 0:
   - The CropSO assets are not under `Assets/Resources/Data/Farming/Crops/` (path matters — `Resources.LoadAll` is path-sensitive).
   - `GameLauncher.LaunchSequence` didn't run (rare — check the launch path).

2. **Is the cell actually plowed and planted?** In Play Mode, run a debug script:
   ```csharp
   ref var cell = ref mapController.GetComponent<TerrainCellGrid>().GetCellRef(x, z);
   Debug.Log($"IsPlowed={cell.IsPlowed}, PlantedCropId={cell.PlantedCropId}, GrowthTimer={cell.GrowthTimer}, Moisture={cell.Moisture}, TimeSinceLastWatered={cell.TimeSinceLastWatered}");
   ```

3. **Is moisture above the threshold?** Each day, the pipeline checks `cell.Moisture >= crop.MinMoistureForGrowth` (default `0.3`). If lower, growth stalls. Sources of moisture: `TerrainWeatherProcessor` rain, `CharacterAction_WaterCrop`. Sources of decay: `TerrainWeatherProcessor` ambient revert.

4. **Has `OnNewDay` actually fired?** Trigger it manually:
   ```csharp
   for (int i = 0; i < 24; i++) MWI.Time.TimeManager.Instance.AdvanceOneHour();
   ```
   On day rollover, `FarmGrowthSystem.HandleNewDay` runs server-only.

5. **`FarmGrowthSystem` attached to MapController?** `GameObject.GetComponent<FarmGrowthSystem>()` should not be null on the active MapController. Add the component if missing.

6. **`CropSO.HarvestablePrefab` set?** When the cell crosses `DaysToMature`, `FarmGrowthSystem.SpawnCropHarvestable` logs an error and returns if the prefab is null.

## Debug a perennial that doesn't refill

1. **Has the harvestable been harvested at least once?** Refill is gated by `cell.TimeSinceLastWatered >= 0f` AND `crop.IsPerennial`. Both must be true. After a perennial harvest, `OnDepleted` sets `cell.TimeSinceLastWatered = 0f`.

2. **Conditions met?** Each day, refill needs `cell.Moisture >= crop.MinMoistureForGrowth`. Same as growth — water it or wait for rain.

3. **Tap E with depleted apple tree picks nothing?** Yes, that's correct. `Harvestable.CanHarvest()` returns false when `_isDepleted`, so the yield path is unavailable. Hold E to see the menu — "Pick apples" should be greyed out with reason "Already harvested", and "Destroy" should be available if the right tool is held.

## Debug the Hold-E menu not opening

1. **Prefab at the right path?** `Resources/UI/UI_InteractionMenu.prefab` MUST exist. Without it, console shows `[UI_InteractionMenu] Prefab not found at Resources/UI/UI_InteractionMenu.` and hold-E silently no-ops. Tap-E (yield path) still works.

2. **`_rowPrefab` / `_rowParent` wired on the menu prefab?** Both serialised fields must point to valid components. If `_rowParent` is null, `Rebuild` returns early without building any rows.

3. **Hold threshold tuning.** `PlayerController.E_HOLD_THRESHOLD` is `0.4f` (unscaled time). Adjust if the menu opens too eagerly or too slowly.

## Manual smoke test the destruction action without the menu

The destruction action is normally menu-gated, but during dev there's a `[ContextMenu]` shortcut on `Harvestable`:

1. Place any Harvestable in the scene (e.g. duplicate `Gatherable.prefab`).
2. Set `_allowDestruction = true`, `_requiredDestructionTool = null` (any tool works).
3. Enter Play Mode. Right-click the Harvestable in the Hierarchy → `Harvestable / DEV: Destroy via local player`.
4. After `_destructionDuration` seconds, destruction outputs spawn and the harvestable despawns.

Remove the `[ContextMenu]` (and the inner `Dev_DestroyViaLocalPlayer` method) once acceptance criteria pass — see Task 14 in `docs/superpowers/plans/2026-04-28-farming-plot-system.md`.

## Test the offline catch-up

Hibernation catch-up runs `MacroSimulator.SimulateCropCatchUp` automatically when a map wakes after time has passed. To test manually:

```csharp
// 1. Set up a planted cell:
ref var cell = ref grid.GetCellRef(5, 5);
cell.IsPlowed = true; cell.PlantedCropId = "wheat"; cell.GrowthTimer = 1f; cell.Moisture = 1f; cell.TimeSinceLastWatered = -1f;

// 2. Force-hibernate the map (debug command via DevModePanel or scripted):
mapController.HibernateForSkip();

// 3. Advance the clock:
for (int i = 0; i < 24 * 5; i++) MWI.Time.TimeManager.Instance.AdvanceOneHour();   // 5 in-game days

// 4. Wake up the map (player walks back into range, or scripted):
mapController.WakeUp();

// 5. Verify:
Debug.Log($"GrowthTimer={cell.GrowthTimer}");   // Should be DaysToMature (4) — clamped after 5 days passed
// CropHarvestable should have been spawned by PostWakeSweep.
```
