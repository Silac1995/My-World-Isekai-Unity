# Farming / Plot System — Procedures

> **Architecture lives in [wiki/systems/farming.md](../../../wiki/systems/farming.md).** This file is for HOW-TO only — adding crops, debugging growth, configuring destructible Harvestables. Do not duplicate architectural notes here.

## Add a new crop

1. **Create the `CropSO` asset.** `Project → Create → Game → Farming → Crop`. Save in `Assets/Resources/Data/Farming/Crops/`. Required Inspector fields:
   - `Id` — string used as `TerrainCell.PlantedCropId`. Must be unique across all crops. Lowercase, no spaces.
   - `DaysToMature` — int, days from stage 0 to ready.
   - `MinMoistureForGrowth` — float, default `0.3`. Cell must be ≥ this each daily tick to advance.
   - `PlantDuration` — float, seconds the player stands still during the plant action.
   - `HarvestOutputs` — list of `(Item, Count)` entries that drop on each harvest. Designers can list multiple item types (e.g. apple tree dropping 3 apples + 1 seed). Inspector picker for `Item` is permissive — any `ScriptableObject` is accepted; pick an `ItemSO` subclass. The Pure-asmdef boundary forces this. See gotcha in `wiki/systems/farming.md`. (Pre-rework `ProduceItem` + `ProduceCount` are auto-migrated by `OnValidate` into a single entry on the first edit.)
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
   - The base `Harvestable` fields (`_harvestOutputs`, `_maxHarvestCount`, `_isDepletable`, `_respawnDelayDays`) can be left default — `CropHarvestable.InitializeFromCell` overwrites them at runtime from the `CropSO`.
   - Save the prefab in `Assets/Prefabs/Farming/`.
   - Drag the prefab back onto `CropSO._harvestablePrefab`.

3. **Configure perennial settings (optional).** On the `CropSO`:
   - `IsPerennial = true`.
   - `RegrowDays` — days of conditions-met it takes to refill the harvestable. Must satisfy `1 <= RegrowDays <= DaysToMature` (enforced by `OnValidate`).
   - The `CropHarvestable` prefab MUST have `_depletedSprite` set, otherwise the harvestable will appear blank when depleted.

4. **Configure destruction (optional).** On the `CropSO`:
   - `AllowDestruction = true`.
   - `RequiredDestructionTool` — `ItemSO` reference (e.g. `Item_Axe`). Null = any held item works.
   - `DestructionOutputs` — list of `(Item, Count)` entries to drop (e.g. `[(Item_Wood, 5), (Item_Stick, 2)]`). Each entry has its own count; pre-rework `(items list, single int count)` is auto-migrated into per-entry counts by `OnValidate` on first edit.
   - `DestructionDuration` — seconds for the destroy action.

5. **Create the seed item.** `Project → Create → Game → Items → Seed`. Save in `Assets/Resources/Data/Items/`. Set `_cropToPlant` to the `CropSO`. The seed item works automatically when the player holds it and presses E.

## Configure a destructible wild Harvestable

The destruction surface lives on base `Harvestable`, not just `CropHarvestable` — wild scene-placed trees/rocks work the same way.

1. Open the wild Harvestable prefab (e.g. `Assets/Prefabs/Harvestable/Tree.prefab`).
2. In the `Harvestable` component:
   - `_allowDestruction = true`.
   - `_requiredDestructionTool` → `Item_Axe` (or whatever the project's axe `WeaponSO` is).
   - `_destructionOutputs` → list with one entry: `Item = Item_Wood, Count = 5`.
   - `_destructionDuration = 4` (seconds).
3. Save. No farming code needed.

In Play Mode, the player holding an axe can hold E next to the tree → menu → "Destroy" → 5 wood drops + tree despawns.

## Debug a crop that won't grow

**Fastest path — Dev Mode `HarvestableInspectorView`.** Press F3 to open Dev Mode → Ctrl+Click the crop in the world → switch to the Inspect tab. The view dumps the full `TerrainCell` (moisture / temperature / fertility / plowed / growth timer / time-since-watered) plus the resolved `CropSO` (`DaysToMature`, `MinMoistureForGrowth`, perennial, regrow days) and the live `CropHarvestableNetSync` NetVars (`CurrentStage`, `IsDepleted`, `CropIdNet`). Selectability requires the crop prefab to be on the **`Harvestable`** layer (index 15) — `CropHarvestable_Default.prefab` already is. Use the manual chain below if the inspector cannot reach the crop (e.g. the prefab was authored on the wrong layer).

Walk the chain in this order:

1. **`CropRegistry` initialised?** Check console for `[CropRegistry] Initialised with N crop(s).` at startup. If missing or N is 0:
   - The CropSO assets are not under `Assets/Resources/Data/Farming/Crops/` (path matters — `Resources.LoadAll` is path-sensitive).
   - `GameLauncher.LaunchSequence` didn't run (rare — check the launch path).

2. **Is the cell actually plowed and planted?** Either Ctrl+Click the crop in Dev Mode (the Inspect tab dumps the cell automatically), or in Play Mode run a debug script:
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

## Debug a client that can't see / interact with crops

This section captures the bugs surfaced during the 2026-04-29 multiplayer integration session. Read [wiki/gotchas/static-registry-late-joiner-race.md](../../wiki/gotchas/static-registry-late-joiner-race.md) first — it covers the dominant root cause (joining clients skip `GameLauncher.LaunchSequence` and any static registry that isn't lazy-init will be empty for several frames).

**Fastest triage on a misbehaving client:**

1. **Console flooded with `[TerrainTypeRegistry] Not initialized`?** — Lazy auto-init was reverted or broken. Confirm `TerrainTypeRegistry.Get` and `CropRegistry.Get` both call `Initialize()` if their backing dict is null. See `Assets/Scripts/Terrain/TerrainTypeRegistry.cs` and `Assets/Scripts/Farming/Pure/CropRegistry.cs` for the pattern.

2. **Hold-E menu is empty on the client (host shows rows fine)?** — On the client, `CropHarvestable.GetInteractionOptions` short-circuits if `ResolveCropFromNet()` returns null. Check:
   - `CropRegistry.Get("apple_tree")` returns non-null on the client (lazy-init must have fired). Add a temporary `Debug.LogWarning($"[CropRegistry.Get] _initialised={_initialised}, count={_byId.Count}")` if in doubt.
   - `_netSync.CropIdNet.Value` is non-empty on the client. If it's empty, the server's spawn payload didn't carry the crop id — verify `FarmGrowthSystem.SpawnCropHarvestableAt` calls `InitializeFromCell` BEFORE `Spawn` (NetVar values must be set on the local instance before NGO serialises the spawn message).
   - `crop.HarvestOutputs.Count > 0` on the client. If 0, the CropSO asset may be unmigrated (legacy `_produceItem` field still populated, new `_harvestOutputs` empty). In the editor, OnValidate auto-migrates. For built clients, the `HarvestOutputs` getter does a runtime lazy migration (`_produceItem != null → push to _harvestOutputs`).

3. **Client can't harvest a mature crop?** — Same chain as the menu issue above. Tap-E goes through `Harvestable.Interact → CanHarvestWith(held) → CanHarvest()`. `CropHarvestable.CanHarvest` requires `IsMature() && !_netSync.IsDepleted.Value && crop.HarvestOutputs.Count > 0` — all read from net-replicated state. If any returns false, the harvest action is never queued.

4. **Client can plant but the seed doesn't disappear from their hand?** — `HandsController.CarriedItem` is **not** networked. The server's view of a dedicated client's hand is always empty, so the seed-consume code in `CharacterAction_PlaceCrop.OnApplyEffect` is a no-op for dedicated clients. The owning client must consume locally before the RPC: see `CropPlacementManager.ConsumeHeldSeedLocally`. Same pattern applies to **any** held-item-consumption flow that needs to work for non-host players.

5. **Growth visual stuck on client (initial scale correct, no day-by-day update)?** — `CropHarvestable.Update` polls the three NetVars every frame and re-applies visual on change as a safety net for any NGO `OnValueChanged` callback that misfires on remote clients. If the visual is still stuck, verify `_netSync.CurrentStage.Value` is actually changing on the client (use Dev Mode HarvestableInspectorView to read the live NetVar value). If the value isn't changing, the issue is upstream — NGO replication of `CurrentStage` is broken.

6. **Late-joining client's connection fails with `NetworkObject.Serialize` NRE loop on the host?** — Most likely cause: a runtime-spawned NetworkObject parented under another NetworkObject (e.g. via `TrySetParent`) triggers an NGO bug during initial-sync to the late-joiner. `CropHarvestable` was previously parented under `MapController` and is **no longer** — they live at scene root. If you're adding a new system that does runtime `TrySetParent` of a spawned `NetworkObject` under another spawned `NetworkObject`, expect this bug class. Workarounds: don't parent (preferred — usually no functional dependency), or implement scene-root spawn + a manual transform-follow pattern.

7. **All of the above check out and it still doesn't work?** — Force-recompile the client editor (Unity 6 MPPM clones share `Assets/` via symlink but have their own `Library/VP/<cloneId>/`). The cleanest reset is to close the clone, delete `Library/VP/<cloneId>/`, and reopen it. Confirm the build by looking for the `[CropRegistry] Initialised with N crop(s).` log on the client at game-launch time.

## Add a new static registry without re-introducing the late-joiner race

If you create a `static class XRegistry { Initialize(); Get(string id); }`:

```csharp
public static class XRegistry
{
    private static Dictionary<string, X> _byId;

    public static void Initialize()
    {
        if (_byId != null) return;                                    // ← idempotent
        _byId = Resources.LoadAll<X>("Data/X").ToDictionary(x => x.Id);
    }

    public static X Get(string id)
    {
        if (_byId == null) Initialize();                              // ← lazy auto-init
        if (string.IsNullOrEmpty(id)) return null;
        return _byId.TryGetValue(id, out var x) ? x : null;
    }
}
```

Then add `XRegistry.Initialize()` to both `GameLauncher.LaunchSequence` (for host/solo eager init + telemetry log) AND `GameSessionManager.HandleClientConnected` on the local-client branch (for joining clients — also for telemetry; the lazy-init in `Get` already covers correctness). See `[wiki/gotchas/static-registry-late-joiner-race.md](../../wiki/gotchas/static-registry-late-joiner-race.md)` for why both eager + lazy.
