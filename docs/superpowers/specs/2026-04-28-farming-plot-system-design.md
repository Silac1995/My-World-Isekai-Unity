# Farming / Plot System — Design Spec

**Date:** 2026-04-28
**Status:** Draft
**Branch:** multiplayyer
**Author:** Claude (with Kevin)

---

## 1. Problem Statement

The project has the **terrain scaffolding for farming pre-wired** (`TerrainCell.IsPlowed`, `PlantedCropId`, `GrowthTimer`, `TimeSinceLastWatered`, `Fertility`, `Moisture`) and an existing wild-vegetation ticker (`VegetationGrowthSystem`) that explicitly skips plowed cells — but no system actually reads or writes those farming fields.

This spec defines a **Stardew-style farming loop** that uses that scaffolding:

- A character holds a seed, places it on a tilled tile, waters it, and harvests it after N in-game days.
- Growth advances **once per in-game day**, gated by a small ordered set of conditions.
- The crop→harvestable transition is **one-way**: a crop grows from stage 0 to `DaysToMature`, then it **becomes** a `CropHarvestable` GameObject. The "growing crop" phase is over — the cell never reverts to growing.
- The spawned `CropHarvestable` then has one of two flavors:
  - **One-shot** (wheat, carrot) — harvesting drops the produce, despawns the harvestable, and clears the cell. Manual re-plant.
  - **Perennial** (apple tree, berry bush) — harvesting drops the produce but the harvestable **stays standing** (the tree visual remains). It enters a "depleted" state. The same daily-condition pipeline that grew the crop now refills the harvestable: every in-game day the conditions are met, regrow progress accrues; after `RegrowDays`, the harvestable is "ready" again. Cycles indefinitely.
- **Up to two interaction paths exist on every `Harvestable`** (this is a base-class feature, not crop-specific — wild scene-placed trees and flowers benefit too):
  - **Yield path** — produce items drop. Existing `CharacterHarvestAction` flow. Optionally tool-gated via `_requiredHarvestTool` (`null` = bare hands work).
  - **Destruction path** (opt-in via `_allowDestruction`) — drops a *different* item set (e.g. wood) and despawns the harvestable. Tool-gated via `_requiredDestructionTool`. E.g. apple tree + axe in hand → chop down → wood.
  - **Yield-only harvestables** (e.g. flower): `_allowDestruction = false`. The yield path is the only option, and it despawns the harvestable on harvest (one-shot). For these, "harvest" and "destroy" are the same action — there's no menu, no choice.
- **Input pattern (player only):**
  - **Tap E** (key released within `~0.4s`): quick yield-path harvest of the closest harvestable (no menu).
  - **Hold E** (key held past threshold): opens the **interaction menu** (`UI_InteractionMenu`) listing every valid path the player's current tool unlocks. Selecting a row queues the corresponding `CharacterAction`.
- **Tool-as-data principle.** The required-tool fields are plain `ItemSO` references. The fact that an item plays *other* roles (e.g. an axe is also a `WeaponSO` usable in combat) is irrelevant to the harvestable — only the `ItemSO` identity match matters. This keeps `Harvestable` ignorant of `WeaponSO` / equipment / combat. A pickaxe (plain `ItemSO`, no weapon role) and an axe (`WeaponSO`) are equally valid as destruction tool references.
- Everything routes through `CharacterAction` so NPCs can plant/water/harvest with the same code path as players (rule #22).
- All persistent state is already covered by `TerrainCellSaveData` — this spec adds **no new persistence layer**.

**Explicitly out of scope** (deferred — see §10):
- Seasons / season gating on crops.
- Building placement snapping to the terrain grid (separate sub-project).
- Wither/death from drought.
- Separate plowing action (V1 plows automatically on plant).
- A visible world-grid overlay for the player.

---

## 2. Architecture Overview

**Approach:** Cell-grid native — the existing `TerrainCellGrid` is the source of truth. A plot **is** a cell. New code is split into five small concerns layered on top of the existing terrain stack:

```
Layer 5 — UI input          PlayerController E-key dispatch + UI_InteractionMenu (player only)
Layer 4 — Visual            CropVisualSpawner (client, growing-stage sprite per cell)
                            CropHarvestable (NetworkObject, ready/depleted swap)
Layer 3 — Mutation          CharacterAction_PlaceCrop / _WaterCrop / _DestroyHarvestable
                            (CharacterHarvestAction reused for the yield path)
Layer 2 — Daily tick        FarmGrowthSystem (server, OnNewDay → 3-branch pipeline:
                            growing crop / live-and-ready / live-and-refilling)
Layer 1 — Content & lookup  CropSO / CropRegistry / SeedSO / WateringCanSO
```

```
TimeManager.OnNewDay (server)
        │
        ▼
FarmGrowthSystem.HandleNewDay()
   ├─ for each cell with IsPlowed && PlantedCropId != null:
   │      ├─ resolve CropSO via CropRegistry (skip if orphaned)
   │      ├─ A. growing  (GrowthTimer < DaysToMature):
   │      │     ├─ if watered → GrowthTimer++
   │      │     └─ if just crossed DaysToMature → spawn CropHarvestable, register in _activeHarvestables
   │      ├─ B. live & ready (GrowthTimer ≥ DaysToMature, TimeSinceLastWatered < 0): nothing
   │      └─ C. live & depleted (perennial only, TimeSinceLastWatered ≥ 0):
   │            ├─ if watered → TimeSinceLastWatered++
   │            └─ if ≥ RegrowDays → harvestable.Refill(), TimeSinceLastWatered = -1
   └─ MapController.SendDirtyCellsClientRpc(...)
                                    │
                                    ▼
              ┌─────────────────────┴─────────────────────┐
              ▼                                           ▼
   CropVisualSpawner (client)                  CropHarvestable.IsDepleted
   shows cell stage sprite                     NetworkVariable<bool>
   only while GrowthTimer < DaysToMature       drives ready/depleted sprite swap

Player input (PlayerController.Update, IsOwner)
   ├─ Seed/WateringCan held + E   → CropPlacement.StartPlacement / StartWatering
   ├─ Tap E (no placement item)   → nearest Interactable.Interact() = yield path
   └─ Hold E ≥ 0.4s               → UI_InteractionMenu (yield + destruction options)
                                       └─ selected option → CharacterAction (queued via Character)
```

**Network model.** Server is sole authority over `TerrainCell` mutations. Clients receive cell deltas via the existing `MapController` ClientRpc and rebuild sprite visuals locally — **no `NetworkObject` per crop visual** (per the existing rule against `NetworkObject` in visual clones). The only `NetworkObject` introduced is `CropHarvestable`, which inherits from `Harvestable` and reuses its existing networking exactly.

---

## 3. Data Model

### 3.1 `CropSO : ScriptableObject` — content definition

```csharp
[CreateAssetMenu(menuName = "Game/Farming/Crop")]
public class CropSO : ScriptableObject
{
    [SerializeField] private string _id;                  // matches TerrainCell.PlantedCropId
    [SerializeField] private string _displayName;
    [SerializeField] private int _daysToMature = 4;
    [SerializeField] private float _minMoistureForGrowth = 0.3f;
    [SerializeField] private float _plantDuration = 1f;   // CharacterAction duration
    [SerializeField] private ItemSO _produceItem;
    [SerializeField] private int _produceCount = 1;
    [SerializeField] private ItemSO _requiredHarvestTool;   // null = bare hands (or any item) is fine for the yield path
    [SerializeField] private Sprite[] _stageSprites;      // length == _daysToMature  (one sprite per growing day; mature visual lives on CropHarvestable._readySprite)
    [SerializeField] private GameObject _harvestablePrefab; // CropHarvestable prefab; falls back to a default if null

    [Header("Perennial (apple tree, berry bush)")]
    [SerializeField] private bool _isPerennial = false;
    [SerializeField] private int _regrowDays = 3;          // days of conditions-met to refill the harvestable; only used if _isPerennial

    [Header("Destruction (e.g. chopping the tree down)")]
    [SerializeField] private bool _allowDestruction = false;
    [SerializeField] private ItemSO _requiredDestructionTool;     // null = any item (or no item) works
    [SerializeField] private List<ItemSO> _destructionOutputs = new List<ItemSO>();
    [SerializeField] private int _destructionOutputCount = 1;
    [SerializeField] private float _destructionDuration = 3f;

    public string Id => _id;
    public int DaysToMature => _daysToMature;
    public float MinMoistureForGrowth => _minMoistureForGrowth;
    public float PlantDuration => _plantDuration;
    public ItemSO ProduceItem => _produceItem;
    public int ProduceCount => _produceCount;     // items dropped per harvest interaction
    public ItemSO RequiredHarvestTool => _requiredHarvestTool;
    public bool IsPerennial => _isPerennial;
    public int RegrowDays => _regrowDays;
    public bool AllowDestruction => _allowDestruction;
    public ItemSO RequiredDestructionTool => _requiredDestructionTool;
    public IReadOnlyList<ItemSO> DestructionOutputs => _destructionOutputs;
    public int DestructionOutputCount => _destructionOutputCount;
    public float DestructionDuration => _destructionDuration;

    /// Growing-stage sprite. Caller must guard against `growthTimer >= DaysToMature`
    /// (mature visual lives on CropHarvestable). Clamp is defensive only.
    public Sprite GetStageSprite(int growthTimer)
        => _stageSprites[Mathf.Clamp(growthTimer, 0, _stageSprites.Length - 1)];
    public GameObject HarvestablePrefab => _harvestablePrefab;
}
```

Validation in `OnValidate()`: `_stageSprites.Length == _daysToMature` (one sprite per growing day; the mature/ready visual lives on `CropHarvestable._readySprite`), non-empty `_id`, non-null `_produceItem`, non-null `_harvestablePrefab` with a `CropHarvestable` component on the root, and if `_isPerennial` then `_regrowDays >= 1 && _regrowDays <= _daysToMature` and `CropHarvestable._depletedSprite != null`. Editor warning otherwise.

> **Visual ownership:** `_stageSprites[0..DaysToMature-1]` are the growing-stage looks owned by `CropVisualSpawner` (cell-side, no `NetworkObject`). The "ready" and "depleted/fruitless" looks live on the `CropHarvestable` prefab as `_readySprite` and `_depletedSprite` respectively (harvestable-side, networked). The boundary is at `GrowthTimer == DaysToMature` — see §6 visual handoff.

### 3.2 `CropRegistry` — runtime O(1) lookup

Mirrors `TerrainTypeRegistry` exactly. Static `Initialize()` called from `GameLauncher.LaunchSequence` after scene load (same ordering rule — must run before any `MapController.WakeUp()`). Loads all `CropSO` via `Resources.LoadAll<CropSO>("Data/Farming/Crops")`. `Get(string id)` returns the `CropSO` or null. `Clear()` is called by `SaveManager.ResetForNewSession()`.

### 3.3 `SeedSO : ItemSO`

```csharp
[CreateAssetMenu(menuName = "Game/Items/Seed")]
public class SeedSO : ItemSO
{
    [SerializeField] private CropSO _cropToPlant;
    public CropSO CropToPlant => _cropToPlant;
}
```

Held in the active hand like any other `ItemSO`. The runtime instance is a regular `ItemInstance` — no new instance subclass required.

### 3.4 `WateringCanSO : ItemSO`

```csharp
[CreateAssetMenu(menuName = "Game/Items/WateringCan")]
public class WateringCanSO : ItemSO
{
    [SerializeField] private float _moistureSetTo = 1f;
    public float MoistureSetTo => _moistureSetTo;
}
```

V1: infinite uses, no charge/refill. A `_charges` field can be added later without breaking the action signature.

### 3.5 `TerrainCell` — no schema change

All new state lives in fields that already exist (no `TerrainCell` schema change):
- `IsPlowed: bool` — true once a seed is placed (auto-tilled in V1).
- `PlantedCropId: string` — `CropSO.Id` of what's on this cell (growing crop OR live harvestable). `null` = empty cell.
- `GrowthTimer: float` — incremented by `1f`/day during the **growing-crop phase** (0 → `DaysToMature`). At `>= DaysToMature` it freezes; the cell is now "harvestable spawned" and `GrowthTimer` is no longer touched.
- `TimeSinceLastWatered: float` — repurposed for the **perennial harvestable refill** phase:
  - `< 0f` (sentinel `-1f`) → harvestable is **ready** (full yield).
  - `>= 0f` → harvestable is **depleted**; value is days of conditions-met progress toward refill. When it reaches `cropDef.RegrowDays`, the harvestable refills and the value resets to `-1f`.
  - Ignored on cells without a perennial mature harvestable.
- `Moisture: float` — read by the growth/refill condition; written by rain (`TerrainWeatherProcessor`) and `CharacterAction_WaterCrop`.

This encoding lets the cell be the single source of truth across save/load and hibernation — the spawned `CropHarvestable` GameObject is reconstructible purely from `(PlantedCropId, GrowthTimer >= DaysToMature, TimeSinceLastWatered)`.

---

## 4. Daily Growth Tick — `FarmGrowthSystem`

```csharp
public class FarmGrowthSystem : MonoBehaviour
{
    private TerrainCellGrid _grid;
    private MapController _map;

    public void Initialize(TerrainCellGrid grid, MapController map)
    {
        _grid = grid; _map = map;
        if (NetworkManager.Singleton.IsServer)
            TimeManager.Instance.OnNewDay += HandleNewDay;
    }
    private void OnDestroy()
    {
        if (TimeManager.Instance != null)
            TimeManager.Instance.OnNewDay -= HandleNewDay;
    }
    private void HandleNewDay() { /* iterate grid, run pipeline */ }
}
```

**Lifecycle:** instantiated on `MapController` next to `VegetationGrowthSystem` and `TerrainWeatherProcessor`. Subscribes server-only. `OnNewDay` fires from `TimeManager` and is server-authoritative because hibernated maps don't tick — they catch up via `MacroSimulator` instead (§7).

**Pipeline** (per cell with `IsPlowed && !string.IsNullOrEmpty(PlantedCropId)`):

1. `var crop = CropRegistry.Get(cell.PlantedCropId);` — if null, log once and skip (orphan ID; data was loaded without the crop's SO).
2. **Branch on phase:**
   - **A. Growing crop** (`cell.GrowthTimer < crop.DaysToMature`):
     - If `cell.Moisture >= crop.MinMoistureForGrowth` → `cell.GrowthTimer += 1f`. Else stall.
     - If we just crossed `DaysToMature`: spawn the `CropHarvestable` (§6) and register it in `_activeHarvestables[cellIndex]`. Initialise `cell.TimeSinceLastWatered = -1f` (perennial: ready). Growing phase is over for this cell.
   - **B. Live harvestable, ready** (`cell.GrowthTimer >= crop.DaysToMature && cell.TimeSinceLastWatered < 0f`):
     - Nothing to do. Wait for the player/NPC to harvest.
   - **C. Live harvestable, depleted (perennial only)** (`cell.GrowthTimer >= crop.DaysToMature && cell.TimeSinceLastWatered >= 0f`):
     - If `cell.Moisture >= crop.MinMoistureForGrowth` → `cell.TimeSinceLastWatered += 1f`. Else stall.
     - If `cell.TimeSinceLastWatered >= crop.RegrowDays`: tell the harvestable to refill (`harvestable.Refill()`) and reset `cell.TimeSinceLastWatered = -1f`.

**The system holds `Dictionary<int, CropHarvestable> _activeHarvestables`** — keyed by linear cell index, **per-`FarmGrowthSystem`-instance** (one system lives on each active `MapController`, so the registry is map-scoped). Used to dispatch refill calls without iterating GameObjects. Populated on spawn (branch A end), pruned when a one-shot harvestable's `OnDepleted` fires or a destruction completes.

**Hibernation behaviour:** the dictionary is server-side runtime state, lost when the map hibernates. On wake, the post-wake sweep (§9) reconstructs both the harvestables and the registry from cell state — the cell encoding `(PlantedCropId, GrowthTimer ≥ DaysToMature, TimeSinceLastWatered)` is sufficient to know what to spawn and in which visual state.

**Cross-instance call shape:** `CropHarvestable` reaches its owning `FarmGrowthSystem` via `_map.GetComponent<FarmGrowthSystem>().UnregisterHarvestable(cellX, cellZ)` — no statics, no singletons. The harvestable already holds `_map` from `InitializeFromCell`.

**Why one combined system?** Crop growth and harvestable refill share the exact same condition (`Moisture >= MinMoistureForGrowth`) and the same daily cadence. Putting them in one pipeline avoids two parallel iterators and one shared subscription to `TimeManager.OnNewDay`.

After the loop, dirty cell indices are pushed via `MapController.SendDirtyCellsClientRpc(int[] indices, TerrainCellSaveData[] payload)` (a thin variant of the existing full-grid RPC — listed as a deferred TODO on the terrain wiki, this spec implements it).

**Performance.** ~35k cells on a 750×750 map. The pipeline is O(N) once per in-game day. With early-exit on `!IsPlowed`, the typical cost is dozens of cells — well within budget. No per-frame work; no allocations (no LINQ, no string interpolation in hot path).

**Giga-speed.** Because the tick is event-driven on `OnNewDay` and `TimeManager.AdvanceOneHour` already fires `OnNewDay` on midnight rollover, time-skip works for free. No catch-up loop needed inside `FarmGrowthSystem` itself.

---

## 5. Planting Flow

### 5.1 Input gate (`PlayerController`)

Per rule #33, all E-key handling lives in a single `PlayerController.Update()` block. This block is the **only** place in the codebase that reads `KeyCode.E`. Logical priority (first match wins):

```csharp
if (!IsOwner || character.IsBuilding) return;
var held = character.CharacterEquipment?.GetActiveHandItem();
bool eDown = Input.GetKeyDown(KeyCode.E);
bool eUp   = Input.GetKeyUp(KeyCode.E);
bool eHeld = Input.GetKey(KeyCode.E);

// Priority 1: a placement-active item is held → E starts placement (no tap/hold distinction)
if (eDown && held?.ItemSO is SeedSO seed)         { character.CropPlacement.StartPlacement(held); return; }
if (eDown && held?.ItemSO is WateringCanSO)       { character.CropPlacement.StartWatering(held); return; }

// Priority 2: harvestable interaction (tap-vs-hold — full pseudocode in §6.2)
HandleHarvestableEKey(eDown, eHeld, eUp, held);
```

`CropPlacement` is the new `CharacterSystem` (§5.2). The harvestable tap/hold handling is detailed in §6.2.

### 5.2 `CropPlacementManager : CharacterSystem`

Mirrors `BuildingPlacementManager` in shape (per-character `CharacterSystem` with `_character`, ghost lifecycle, ServerRpc to commit). One instance per Character GameObject.

**Public API:**
```csharp
void StartPlacement(ItemInstance seedInstance);   // ghost on, mouse follows, click to plant
void StartWatering(ItemInstance canInstance);     // hover indicator on, click to water
void CancelPlacement();                           // RMB / ESC
bool IsPlacementActive { get; }
```

**Per-frame loop** (Player only — `IsPlacementActive` gates):
1. Raycast mouse → ground plane → `worldPos`.
2. `var (x,z) = grid.WorldToCellIndex(worldPos);` snap ghost to cell centre.
3. Validate:
   - Cell exists.
   - `cell.GetCurrentType().CanGrowVegetation == true`.
   - `string.IsNullOrEmpty(cell.PlantedCropId)`.
   - Distance from character to cell centre ≤ `_settings.MaxPlantRange`.
4. Set ghost sprite tint (valid/invalid material on ghost SpriteRenderer).
5. On `Input.GetMouseButtonDown(0)`: `RequestPlaceCropServerRpc(x, z, seedSO.CropToPlant.Id)`.

**Ghost prefab:** a single SpriteRenderer GameObject, NO `NetworkObject`, NO collider. Created from `CropSO.GetStageSprite(0)`. Destroyed on cancel/place.

**`RequestPlaceCropServerRpc`** (server-side):
1. Re-validate everything above (anti-cheat — never trust client).
2. Verify the caller still holds a `SeedSO` whose `CropToPlant.Id == cropId`.
3. Verify no other action is in-flight for that cell (use a `HashSet<int> _cellsBeingMutated` on `MapController` keyed by cell index, checked + cleared on action complete/cancel — prevents double-plant race between two players clicking the same cell).
4. Queue `CharacterAction_PlaceCrop` on the caller via `character.CharacterActions.ExecuteAction(...)`.

### 5.3 `CharacterAction_PlaceCrop : CharacterAction`

```csharp
public class CharacterAction_PlaceCrop : CharacterAction
{
    private readonly int _cellX, _cellZ;
    private readonly CropSO _crop;
    private readonly ItemInstance _seed;
    private readonly MapController _map;
    private readonly TerrainCellGrid _grid;

    public CharacterAction_PlaceCrop(Character actor, MapController map, int cellX, int cellZ, CropSO crop, ItemInstance seed)
        : base(actor, crop.PlantDuration)
    { _map = map; _grid = map.TerrainGrid; _cellX = cellX; _cellZ = cellZ; _crop = crop; _seed = seed; }

    public override bool CanExecute() { /* re-validate cell + seed in inventory */ }
    public override void OnStart() { /* face the cell, play "plant" anim */ }
    public override void OnApplyEffect()
    {
        // Server-only. Mutate cell, consume seed, push delta.
        ref var cell = ref _grid.GetCellRef(_cellX, _cellZ);
        cell.IsPlowed = true;
        cell.PlantedCropId = _crop.Id;
        cell.GrowthTimer = 0f;
        cell.TimeSinceLastWatered = -1f;   // sentinel "not depleted"; ignored during growing-crop phase
        character.CharacterEquipment.ConsumeFromActiveHand(1);
        _map.SendDirtyCellsClientRpc(/* this cell */);
    }
    public override void OnCancel() { /* release reservation in _cellsBeingMutated */ }
}
```

The `MapController` is passed to the action by `RequestPlaceCropServerRpc` (it has the caller's current map already; the RPC is server-side and chooses the right map from the caller's position).

**NPC parity:** an NPC GOAP/BT node constructs the same action with a chosen cell — no UI involved. Same effect on the cell. (Wiring NPC farming AI is **not** in this spec; the action exists for them when ready.)

---

## 6. Harvesting

When `FarmGrowthSystem` detects a cell whose `GrowthTimer` has just crossed `DaysToMature`, it spawns a **`CropHarvestable`** GameObject at the cell centre. **The crop phase is over** — the cell is now "occupied by a live harvestable" until the player removes it (one-shot harvest clears the cell; perennial cycles indefinitely).

```csharp
public class CropHarvestable : Harvestable
{
    [SerializeField] private Sprite _readySprite;     // shown when full yield (e.g. tree WITH apples)
    [SerializeField] private Sprite _depletedSprite;  // shown post-harvest (perennial: tree WITHOUT apples)

    public int CellX { get; private set; }
    public int CellZ { get; private set; }
    public TerrainCellGrid Grid { get; private set; }

    private CropSO _crop;
    private MapController _map;
    public NetworkVariable<bool> IsDepleted = new NetworkVariable<bool>(false);

    /// Server-only. Called once when FarmGrowthSystem spawns this harvestable.
    /// `startDepleted` reflects the cell's encoded refill state (TimeSinceLastWatered >= 0f).
    /// On a fresh maturity (cell.TimeSinceLastWatered == -1f) → false. On post-wake of a depleted
    /// perennial (cell.TimeSinceLastWatered in [0, RegrowDays)) → true. This is the load-bearing
    /// line for save/load and hibernation correctness — see §9.
    public void InitializeFromCell(TerrainCellGrid grid, MapController map, int x, int z, CropSO crop, bool startDepleted)
    {
        Grid = grid; _map = map; CellX = x; CellZ = z; _crop = crop;
        // Configure base Harvestable from CropSO content (server-side):
        //   _outputItems      = [ crop.ProduceItem ]
        //   _maxHarvestCount  = 1     (one Interact() = drop the whole yield in a burst)
        //   _isDepletable     = true
        //   _respawnDelayDays = 0     (we manage post-deplete state via the cell, not the base timer)
        if (startDepleted) SetDepleted();
        else                SetReady();
    }

    /// Restores full yield + ready visual. Called on fresh spawn (non-depleted cell) AND on each
    /// perennial Refill() after RegrowDays.
    public void SetReady()
    {
        ResetHarvestState();        // protected helper on Harvestable.cs (zeroes _currentHarvestCount, clears _isDepleted)
        IsDepleted.Value = false;   // fans out to clients via OnValueChanged → swap sprite
    }

    /// Puts the harvestable in the "no fruit, regrowing" state without running the deplete pipeline.
    /// Called only from InitializeFromCell on post-load / post-wake of a depleted perennial.
    public void SetDepleted()
    {
        MarkDepletedNoCallback();   // protected helper on Harvestable.cs (sets _isDepleted = true, blocks CanHarvest())
        IsDepleted.Value = true;
    }

    /// Called by FarmGrowthSystem after RegrowDays of conditions met. Perennial only.
    public void Refill() => SetReady();

    private void OnIsDepletedChanged(bool _, bool isNow)
    {
        ApplyVisualSwap(isNow ? _depletedSprite : _readySprite);
    }
    // Wire OnIsDepletedChanged in OnNetworkSpawn() (both server + client). Late-joiners get the
    // current value automatically via NGO's NetworkVariable initial-sync, then the OnValueChanged
    // callback fires once on first read, swapping to the correct sprite without server intervention.

    /// Override the post-deplete path so harvest also updates cell state correctly.
    protected override void OnDepleted()
    {
        ref var cell = ref Grid.GetCellRef(CellX, CellZ);

        if (_crop.IsPerennial)
        {
            // Perennial: harvestable STAYS standing. Mark cell "depleted, refilling".
            cell.TimeSinceLastWatered = 0f;       // 0 = depleted, refill in progress. -1 = ready.
            IsDepleted.Value = true;              // visual swap fans out via OnValueChanged on all clients
            // Do NOT despawn. FarmGrowthSystem will call Refill() after RegrowDays of conditions met.
        }
        else
        {
            // One-shot: harvestable destroyed, cell cleared (IsPlowed stays true).
            cell.PlantedCropId = null;
            cell.GrowthTimer = 0f;
            cell.TimeSinceLastWatered = -1f;      // restore sentinel
            _map.GetComponent<FarmGrowthSystem>().UnregisterHarvestable(CellX, CellZ);
            NetworkObject.Despawn();
        }

        _map.SendDirtyCellsClientRpc(/* just this cell */);
    }

    private void ApplyVisualSwap(Sprite s)
    {
        // Local-only sprite swap — invoked from OnIsDepletedChanged on every peer.
        // Both server and clients flip their own SpriteRenderer; no networking here.
    }
}
```

This requires extracting a `protected virtual void OnDepleted()` hook from the existing `Harvestable.Deplete()` body so we don't fork the class. Existing `Harvestable` behavior is unchanged for non-crop usage.

**Why reuse `Harvestable`?** The harvest interaction (`CharacterHarvestAction`, animation, item spawn into world, `Interact()` plumbing on `InteractableObject`) is already battle-tested. Crops configure the base from `CropSO` at spawn time:

| `CropSO` field | `Harvestable` field set at spawn |
|---|---|
| `ProduceItem` | `_outputItems = [ProduceItem]` |
| `ProduceCount` | overridden `Harvest()` spawns `ProduceCount` `WorldItem`s in one burst (single `Interact()` = full yield) |
| `IsPerennial` | branched in `OnDepleted()` above |
| (always) | `_isDepletable = true`, `_maxHarvestCount = 1`, `_respawnDelayDays = 0` (cell owns post-deplete state) |

**One-shot vs perennial summary:**

| Crop type | After harvest | Refill mechanism | Visual after harvest |
|---|---|---|---|
| **One-shot** (wheat) | Harvestable despawns. Cell cleared (`PlantedCropId = null`, `GrowthTimer = 0`, `IsPlowed` stays true). | None. Manual re-plant from a new seed. | Empty cell (`CropVisualSpawner` removes its sprite). |
| **Perennial** (apple tree) | Harvestable **stays standing**. Cell sets `TimeSinceLastWatered = 0` (depleted, refill in progress). | `FarmGrowthSystem` daily tick advances `TimeSinceLastWatered` by 1 when `Moisture` ≥ threshold. After `RegrowDays`, the system calls `harvestable.Refill()` → ready again. | `_depletedSprite` on the same harvestable (e.g. tree without apples). |

**What `Harvestable._respawnDelayDays` is NOT.** That field is a whole-object respawn timer for **wild scene-placed harvestables** (rocks, wild forest trees) — it deletes the visual entirely for N days then restores. Perennial crops use a different mechanic: they never disappear, they just lose their fruit. Both coexist; this spec doesn't unify them (see §10).

**Re-planting (one-shot crops only):** the cell stays `IsPlowed = true` after harvest. Player walks back, presses E with a seed → places again. No re-plowing required. Perennial crops never need re-planting — the same plant cycles indefinitely.

### 6.1 Two interaction paths on `Harvestable`

Every `Harvestable` (not just `CropHarvestable`) gains a tool-gated yield path and an opt-in destruction path. Both fields are mirrored onto `CropSO`. Note that flowers / wheat-like one-shots simply leave `_allowDestruction = false` — they have only the yield path, and harvesting it despawns them (consistent with the existing one-shot flow).

```csharp
[Header("Yield (the default 'pick' interaction)")]
[SerializeField] private ItemSO _requiredHarvestTool;        // null = bare hands (or any item) work

[Header("Destruction (axe / pickaxe etc., optional)")]
[SerializeField] private bool _allowDestruction = false;
[SerializeField] private ItemSO _requiredDestructionTool;    // ignored when _allowDestruction = false
[SerializeField] private List<ItemSO> _destructionOutputs = new List<ItemSO>();
[SerializeField] private int _destructionOutputCount = 1;
[SerializeField] private float _destructionDuration = 3f;

public bool CanHarvestWith(ItemSO heldItem)
{
    if (!CanHarvest()) return false;   // existing: !_isDepleted && _outputItems.Count > 0
    return _requiredHarvestTool == null || heldItem == _requiredHarvestTool;
}

public bool CanDestroyWith(ItemSO heldItem)
{
    if (!_allowDestruction) return false;
    return _requiredDestructionTool == null || heldItem == _requiredDestructionTool;
}
```

`Interact(Character)` becomes the **tap-E quick path** — it tries the yield path first and falls back to nothing. The destruction path is only triggered through the menu (§6.2).

```csharp
public override void Interact(Character interactor)
{
    if (interactor == null || interactor.CharacterActions == null) return;
    var held = interactor.CharacterEquipment?.GetActiveHandItem()?.ItemSO;

    // Default tap action = yield path. Destruction is a menu-only choice (§6.2).
    if (CanHarvestWith(held))
    {
        interactor.CharacterActions.ExecuteAction(new CharacterHarvestAction(interactor, this));
        return;
    }
    // No-op if the player can't yield-harvest with their current tool. They can hold E to see the menu.
}
```

**`CharacterAction_DestroyHarvestable`** (new):

```csharp
public class CharacterAction_DestroyHarvestable : CharacterAction
{
    private readonly Harvestable _target;
    public CharacterAction_DestroyHarvestable(Character actor, Harvestable target)
        : base(actor, target.DestructionDuration) { _target = target; }

    public override bool CanExecute()
        => _target != null && _target.CanDestroyWith(character.CharacterEquipment?.GetActiveHandItem()?.ItemSO);

    public override void OnApplyEffect()
    {
        // Server-only. Spawn destruction outputs as WorldItems, then despawn (with crop hook).
        _target.DestroyForOutputs();
    }
}
```

**`Harvestable.DestroyForOutputs()`** (new, server-only):

```csharp
public void DestroyForOutputs()
{
    for (int i = 0; i < _destructionOutputCount; i++)
        foreach (var item in _destructionOutputs)
            SpawnWorldItem(item);   // existing helper

    OnDestroyed();           // virtual hook for subclasses
    NetworkObject.Despawn();
}

protected virtual void OnDestroyed() { /* no-op base */ }
```

**`CropHarvestable.OnDestroyed`** (override) — same cleanup as the one-shot `OnDepleted` branch (clear cell, unregister from `FarmGrowthSystem`, push delta). Refactor: extract that body into a private `ClearCellAndUnregister()` helper used by both.

**Tap-vs-hold dispatch (no path conflict, ever).** The tap-E flow always runs the yield path (it is the "default"). The destruction path is reachable only via the menu (§6.2). This eliminates the previous design's tool-priority guesswork: an axe in hand never accidentally chops down an apple tree just because the player tapped E next to it. To chop, the player must hold E and explicitly pick the destruction option.

**Wild scene-placed harvestables benefit too.** A scene-authored `Harvestable` for a wild forest tree can set `_allowDestruction = true`, `_requiredDestructionTool = Item_Axe`, `_destructionOutputs = [Item_Wood]`, `_destructionOutputCount = 5` and instantly support chopping with no farming code involved. This is why the destruction fields live on the base class.

### 6.2 Tap E vs Hold E — input + interaction menu

**Input pattern (player only — lives in `PlayerController.Update()` per rule #33).**

```csharp
// Pseudocode — runs only on the local owner.
if (Input.GetKeyDown(KeyCode.E)) _eHeldStartTime = Time.unscaledTime;

bool eIsHeld = Input.GetKey(KeyCode.E);
bool justReleased = Input.GetKeyUp(KeyCode.E);
const float HoldThreshold = 0.4f;

if (eIsHeld && Time.unscaledTime - _eHeldStartTime >= HoldThreshold && !_menuOpen)
{
    var target = character.GetClosestInteractable() as Harvestable;
    if (target != null) UI_InteractionMenu.Open(character, target);
    _menuOpen = true;
}
else if (justReleased && !_menuOpen)
{
    // Tap path
    var target = character.GetClosestInteractable();
    target?.Interact(character);   // for Harvestables this routes to the yield path
}
else if (justReleased)
{
    _menuOpen = false;   // menu closes by selection or by separate ESC handler
}
```

**Conflict resolution with placement-active items.** When the active-hand item is a `SeedSO` or `WateringCanSO`, the **placement flow wins** — tap E starts placement (§5/§7). To harvest, the player swaps to bare hands or to a non-placement item. This matches Stardew's hotbar swap and avoids E-key overload.

**`UI_InteractionMenu`** — small screen-anchored panel.

- Driven by `Harvestable.GetInteractionOptions(Character actor): List<HarvestInteractionOption>`. Each option is a struct/record:
  ```csharp
  public readonly struct HarvestInteractionOption
  {
      public string Label;           // "Pick apples", "Chop down"
      public Sprite Icon;
      public string OutputPreview;   // "4× Apple", "4× Wood"
      public bool IsAvailable;       // false → render greyed-out with reason
      public string UnavailableReason;  // "Requires Axe", "No fruit yet"
      public Func<Character, CharacterAction> ActionFactory;
  }
  ```
- Base implementation returns one `HarvestInteractionOption` for yield (always present, may be unavailable) and one for destruction (only if `_allowDestruction`). Subclasses can override to add more.
- The menu shows ALL options regardless of tool match — unavailable ones are greyed-out with the reason. This is critical UX: the player learns what tool unlocks what.
- Selecting an available option → `option.ActionFactory(character)` → `character.CharacterActions.ExecuteAction(action)`. Menu closes.
- ESC, click outside, or releasing E away from the menu closes it without action.

**NPCs do not use the menu.** An NPC GOAP/BT node directly constructs the desired `CharacterAction` (e.g., `CharacterAction_DestroyHarvestable` for a tree-chopping job), bypassing the menu entirely. The menu is purely a player-input surface; NPCs see the underlying action API.

**Yield-only harvestables (flowers).** `_allowDestruction = false` → `GetInteractionOptions` returns just the one yield option. Tap E and hold E both produce the same action (the menu has only one row, so the player can also just tap to skip the menu).

---

## 7. Watering

Same shape as planting, simpler.

- `WateringCanSO` held → `PlayerController` calls `CropPlacement.StartWatering(canInstance)`.
- `CropPlacementManager` shows a hover indicator (a small water-droplet sprite snapped to cell). Click → `RequestWaterCellServerRpc(x, z)`.
- Server validates (cell exists, in range), queues `CharacterAction_WaterCrop`.
- `CharacterAction_WaterCrop.OnApplyEffect` sets `cell.Moisture = canSO.MoistureSetTo` (default `1f`), `cell.TimeSinceLastWatered = 0f`. Pushes delta.
- Watering an empty/unplanted cell is allowed (raises moisture, drives the existing terrain transitions Dirt → Mud as a side effect — consistent with the rest of the terrain layer).

**Rain integration:** zero new code. `TerrainWeatherProcessor` already adds moisture per tick wherever a `WeatherFront` of type `Rain` overlaps. Moisture decay (the ambient revert in the same processor) handles dry-out. The natural daily dry-out rate will be tuned during implementation; if too fast/slow we adjust `BiomeClimateProfile.AmbientMoisture` rather than introduce a new system.

---

## 8. Visual Layer — `CropVisualSpawner`

Client-only `MonoBehaviour` on `MapController`.

```csharp
private Dictionary<int, GameObject> _activeVisuals;  // key = cell linear index
private GameObject _genericVisualRoot;               // pool parent

public void OnDirtyCellsApplied(int[] indices, TerrainCellSaveData[] payload)
{
    for (int i = 0; i < indices.Length; i++)
    {
        var saved = payload[i];
        var crop = CropRegistry.Get(saved.PlantedCropId);
        if (crop == null) { RemoveVisual(indices[i]); continue; }
        EnsureVisual(indices[i], saved, crop);
    }
}
```

- `EnsureVisual`: **early-exit** with `RemoveVisual(idx)` when `saved.GrowthTimer >= crop.DaysToMature` (the `CropHarvestable` owns the visual past maturity — see §6 handoff). Otherwise, spawn a single sprite GameObject at cell centre, parent under `_genericVisualRoot`, and set its `SpriteRenderer.sprite` from `crop.GetStageSprite((int)saved.GrowthTimer)`.
- `RemoveVisual`: `Destroy(_activeVisuals[idx])` (V1 — pool later if profiling shows churn).
- **No `NetworkObject`** on the visual. **No collider.** Pure cosmetic.
- Map wakeup: server sends a full-grid sync via the existing `SendTerrainGridClientRpc`, and `CropVisualSpawner` does an initial pass to spawn visuals for every planted cell.

**Visual handoff with `CropHarvestable`:**

| Cell state | Who owns the visual |
|---|---|
| `GrowthTimer < DaysToMature` (growing crop) | `CropVisualSpawner` — shows `crop.GetStageSprite((int)GrowthTimer)`. |
| `GrowthTimer >= DaysToMature` (live harvestable, ready or depleted) | `CropHarvestable` — shows `_readySprite` or `_depletedSprite`. `CropVisualSpawner` removes its sprite the moment a harvestable is registered for the cell. |

There is **no perennial-regrow visual on the cell** — the harvestable never despawns during a perennial cycle, so the swap is internal to its own renderer (driven by a `NetworkVariable<bool> IsDepleted` for client sync).

**Spawn-order race (client-side).** Server actions that mature a cell perform two networked operations: (a) `SendDirtyCellsClientRpc(...)` for the cell delta, and (b) `NetworkObject.Spawn` of the `CropHarvestable`. The client may receive these in either order. The handoff is robust to both:

- If cell delta arrives first → `CropVisualSpawner` sees `GrowthTimer >= DaysToMature` and **removes** its sprite (does NOT try to render the mature stage). When the harvestable spawns moments later, the world has no sprite duplication.
- If harvestable spawns first → both visuals are briefly visible (≤ 1 frame, typically). When the cell delta arrives, `CropVisualSpawner` removes its sprite.

The rule that makes this work: **`CropVisualSpawner.EnsureVisual` early-exits and calls `RemoveVisual` when `saved.GrowthTimer >= crop.DaysToMature`**. That single check is the load-bearing line for the handoff.

---

## 9. Save / Network / Hibernation

### 9.1 What persists, where

| State | Authority | Persistence layer | Network sync | Lost on |
|---|---|---|---|---|
| Cell crop fields (`PlantedCropId`, `GrowthTimer`, `TimeSinceLastWatered`, `IsPlowed`, `Moisture`) | Server | `TerrainCellSaveData` already serializes every field, packed into `MapSaveData.TerrainCells` | `MapController.SendTerrainGridClientRpc` (full) + new `SendDirtyCellsClientRpc` (delta) | nothing — survives save→load AND hibernate→wake |
| `CropHarvestable` GameObject identity | Server | **Not persisted directly.** Reconstructed from cell state on every wake/load by the `FarmGrowthSystem` post-wake sweep. | `NetworkObject` (NGO) — fresh on each spawn | save/load, hibernate (recreated post-wake) |
| `CropHarvestable.IsDepleted` (NetworkVariable<bool>) | Server | **Derived** — set in `InitializeFromCell(... startDepleted)` from `cell.TimeSinceLastWatered >= 0f`. | NGO NetworkVariable; late-joiners get current value automatically | not stored separately; recomputed on every spawn |
| `_activeHarvestables` registry on `FarmGrowthSystem` | Server | Not persisted. Rebuilt during the post-wake sweep (one entry per spawned harvestable). | n/a (server runtime) | save/load, hibernate (rebuilt post-wake) |
| Crop visual sprites (`CropVisualSpawner._activeVisuals`) | Client | Not persisted. Rebuilt from cell sync on every map ready. | n/a (cosmetic, local) | every load/wake (rebuilt) |
| `_cellsBeingMutated` reservation set on `MapController` | Server | Not persisted. Empty after load (no actions in flight). | n/a | every load/wake |
| In-flight `CharacterAction_PlaceCrop` / `WaterCrop` / `DestroyHarvestable` | Server | Not persisted (transient, follows existing `CharacterActions` semantics — actions don't survive load). | n/a | save/load |
| `CropPlacementManager` ghost / placement mode | Local | Not persisted. Ghost is local-only UI; player resumes idle. | n/a | save/load |
| Active-hand item (the `SeedSO` / `WateringCanSO` the player holds) | Server (per character) | Existing `CharacterEquipment` save layer | per existing `CharacterEquipment` flow | save/load only if existing layer drops it |
| `CropRegistry` (string Id → CropSO) | Static | **Asset data** — initialized once at game launch from `Resources.LoadAll<CropSO>("Data/Farming/Crops")` | n/a (clients load same assets) | session boundary; explicit `Clear()` in `SaveManager.ResetForNewSession` |

> **Dependency on the terrain save layer.** This spec relies on `MapSaveData.TerrainCells` actually being persisted to disk by `SaveManager` across full game close-and-reopen, not just held in memory for hibernation. If the existing terrain pipeline only hibernates and doesn't disk-save (the [[terrain-and-weather]] wiki documents `MapSaveData` and `ISaveable` registration but doesn't explicitly confirm full-restart persistence), criterion §12.10 will surface that gap during acceptance — fixing it then belongs to the terrain layer, not to the farming spec. The farming code itself adds **zero new save state**.

### 9.2 Save → load is the same code path as hibernate → wake

**The cell is the single source of truth.** Both flows produce the same input state (a `MapController` with restored `TerrainCellSaveData[]`) and run the same post-wake sweep:

```
1. CropRegistry.Initialize()                              ← GameLauncher.LaunchSequence (must run first)
2. MapController.WakeUp() restores TerrainCellSaveData[]  ← existing
3. FarmGrowthSystem.PostWakeSweep() (NEW):
     for each cell with IsPlowed && PlantedCropId != null:
         crop = CropRegistry.Get(cell.PlantedCropId)
         if crop == null: continue   // orphan; logged once
         if cell.GrowthTimer >= crop.DaysToMature:
             startDepleted = (crop.IsPerennial && cell.TimeSinceLastWatered >= 0f)
             harvestable = SpawnCropHarvestable(crop, x, z)
             harvestable.InitializeFromCell(grid, map, x, z, crop, startDepleted)
             _activeHarvestables[cellIndex] = harvestable
```

This single sweep covers:

- **Live save → reload** (player saves world, returns later): cells restore from `MapSaveData`, harvestables respawn at the right state.
- **Hibernation wake** (player approaches a previously-hibernated map): same sweep runs after `MacroSimulator.SimulateCropCatchUp` advanced the cells offline.
- **Client late-join**: server already has harvestables alive (it never hibernated), and NGO syncs `NetworkObject` + `NetworkVariable<bool> IsDepleted` to the joining client automatically — no farming-side code needed for late-join.

### 9.3 Ordering requirement

`CropRegistry.Initialize()` MUST run before any `MapController.WakeUp()` or save-restore that touches cells with `PlantedCropId != null`. Otherwise `CropRegistry.Get(...)` returns null for every planted cell and the post-wake sweep silently skips them, leaving live harvestables un-spawned. Same rule the terrain layer already imposes for `TerrainTypeRegistry.Initialize()` — wired in `GameLauncher.LaunchSequence`.

### 9.4 Offline catch-up algorithm (hibernation only)

Live save → reload does **not** run catch-up — the world stops simulating at save time and resumes from exactly the same state. Only hibernation needs catch-up to advance crops/refills during the elapsed days the map was inactive. Server-only, called from `MacroSimulator` after `SimulateVegetationCatchUp`:

```
daysPassed = floor(hoursPassed / 24)
estimatedAvgMoisture = climate.AmbientMoisture + climate.RainProbability * 0.5

for each cell with IsPlowed && !string.IsNullOrEmpty(PlantedCropId):
    crop = CropRegistry.Get(cell.PlantedCropId)
    if crop == null: continue
    if estimatedAvgMoisture < crop.MinMoistureForGrowth: continue  // dry: nothing advances

    // PHASE A — still growing
    if cell.GrowthTimer < crop.DaysToMature:
        cell.GrowthTimer = min(cell.GrowthTimer + daysPassed, crop.DaysToMature)
        continue
        // Note: any "leftover" days past maturity are intentionally dropped here — the
        // post-wake FarmGrowthSystem sweep (below) handles harvestable spawning.
        // For perennials we don't carry leftover into the refill phase; the player
        // hasn't harvested yet, so refill state isn't meaningful.

    // PHASE B — live harvestable, depleted (perennial only). One-shots have no offline state.
    if crop.IsPerennial && cell.TimeSinceLastWatered >= 0f:
        cell.TimeSinceLastWatered += daysPassed
        if cell.TimeSinceLastWatered >= crop.RegrowDays:
            cell.TimeSinceLastWatered = -1f   // one full refill cycle completed (multi-cycle deferred — §10)
```

After catch-up, the post-wake sweep from §9.2 runs. Net effect: a player who leaves for a week comes back to fully-grown crops, and to apple trees that have refilled at least once if conditions allowed.

---

## 10. Out of Scope / Open Questions

Tracked here for the wiki "Open questions" section once the system page is created.

- [ ] **Seasons on `TimeManager`.** Deferred per Kevin. Adding `Season` enum + `DaysPerSeason` + `OnSeasonChanged` event later. `CropSO` will gain a `_seasons: SeasonFlags` field with a default of "all seasons" so existing assets keep working.
- [ ] **Building placement snap to `TerrainCellGrid`.** Separate sub-project: refactor `BuildingPlacementManager` ghost positioning, building footprints expressed in cells, rotation in 90° increments. Do **not** touch in this spec.
- [ ] **Wither/death from drought.** Option B chosen. Adding later via `CropSO._minMoistureForSurvival` + `IsWithered` flag on cell.
- [ ] **Separate plowing action / hoe.** V1 plows automatically when planting. Adding a `CharacterAction_PlowCell` later is purely additive.
- [ ] **Visible world-grid overlay** for the player. Needs its own design pass — toggle key, render layer, only-when-planting-or-building gate.
- [ ] **Watering can charges / refill at well.** V1 is infinite-use. Add `_charges` + a refill action when needed.
- [ ] **Crop quality / yield modifiers from Fertility.** `Fertility` exists on the cell but V1 ignores it. Hook later via `CropSO.GetYieldFor(fertility)`.
- [ ] **NPC farming AI.** The action exists; the GOAP/BT integration is a separate spec.
- [ ] **`SendDirtyCellsClientRpc` on `MapController`.** Already listed in `terrain-and-weather.md` as a deferred terrain TODO; this spec is the first consumer and will implement it.
- [ ] **Removing / uprooting a live perennial.** ~~Out of scope~~ — covered by the destruction path in §6.1 once `Item_Axe` is in the player's hand. (Resolved.)
- [ ] **Tool category system.** V1 destruction tool gating is by exact `ItemSO` reference. A future `ToolCategory` enum (Axe/Pickaxe/Hoe/Sickle) would let multiple `ItemSO` variants (Iron Axe, Steel Axe) all match `RequiredDestructionTool = Axe`.
- [ ] **Tool wear / durability.** Destruction is "free" in V1 — no axe durability loss. Add a `_useCost` later if tools should wear down.
- [ ] **Skill XP from destruction.** Currently no skill bump on chop. Hook into the eventual `[[character-skills]]` system later.
- [ ] **Unifying perennial refill with wild `Harvestable._respawnDelayDays`.** Both are "depleted resource node refills over time", but the wild flavor disappears for N days then pops back in full while the perennial stays standing and conditions-gated. Worth merging into one strategy SO if a "wild apple tree in a forest" use case appears.
- [ ] **Multi-pick across hibernation for perennials.** §9 catch-up wraps to "ready" after one full `RegrowDays`. It does **not** model "the tree refilled 4 times during a 30-day absence". Deferred until NPC harvesting AI exists (without NPCs the player has to physically tap the tree to harvest, which they couldn't do while the map was hibernated, so the multi-cycle case is degenerate today).

---

## 11. New / Modified Files

**New (Assets/Scripts/Farming/ — new folder):**
- `CropSO.cs`
- `CropRegistry.cs`
- `SeedSO.cs`
- `WateringCanSO.cs`
- `FarmGrowthSystem.cs`
- `CropPlacementManager.cs`
- `CharacterAction_PlaceCrop.cs`
- `CharacterAction_WaterCrop.cs`
- `CropHarvestable.cs`
- `CropVisualSpawner.cs`

**New (Assets/Scripts/Character/CharacterActions/):**
- `CharacterAction_DestroyHarvestable.cs` — generic; benefits all Harvestables, not just crops.

**New (Assets/Scripts/UI/Interaction/):**
- `UI_InteractionMenu.cs` — screen-anchored panel listing harvest options on Hold-E. One instance, lazy-spawned.
- `UI_InteractionOptionRow.cs` — row prefab template (label + icon + output preview + greyed-out reason).
- `HarvestInteractionOption.cs` — the option struct/record (lives next to `Harvestable.cs` in `Assets/Scripts/Interactable/`).

**Modified:**
- `Assets/Scripts/Interactable/Harvestable.cs` — four additions, no behavioural change to existing wild harvestables when their new fields default to off:
  1. Extract `protected virtual void OnDepleted()` hook from `Deplete()` (used by `CropHarvestable` for the one-shot/perennial branch).
  2. Add two protected helpers — used by `CropHarvestable` to drive perennial refill + post-load reconstruction without re-running the full `Deplete()` pipeline:
     - `protected void ResetHarvestState()` — zeroes `_currentHarvestCount`, clears `_isDepleted`, restores `_visualRoot`. Used by `SetReady()`.
     - `protected void MarkDepletedNoCallback()` — sets `_isDepleted = true` and blocks `CanHarvest()` without firing `OnDepleted()`, calling `SendWorldItem`, or scheduling the base respawn timer. Used by `SetDepleted()` on post-load / post-wake reconstruction of an already-depleted perennial.
  3. Add the yield-tool field `_requiredHarvestTool: ItemSO` + `CanHarvestWith(ItemSO)`. Update `Interact(Character)` to call `CanHarvestWith` instead of `CanHarvest` (yield-only path; no destruction here — destruction goes through the menu).
  4. Add the destruction fields (`_allowDestruction`, `_requiredDestructionTool`, `_destructionOutputs`, `_destructionOutputCount`, `_destructionDuration`) + `CanDestroyWith(ItemSO)` + `DestroyForOutputs()` + `protected virtual void OnDestroyed()` hook + `GetInteractionOptions(Character)` returning the option list.
- `Assets/Scripts/World/MapSystem/MapController.cs` — add `FarmGrowthSystem` + `CropVisualSpawner` siblings (initialised in `WakeUp()` after cell restore, then `FarmGrowthSystem.PostWakeSweep()` is called on the same frame to reconstruct harvestables), expose `TerrainCellGrid TerrainGrid { get; }` (the existing grid field as a public getter — used by `CharacterAction_PlaceCrop`), add `SendDirtyCellsClientRpc(int[] indices, TerrainCellSaveData[] payload)`, add `_cellsBeingMutated: HashSet<int>` reservation set (used by `CharacterAction_PlaceCrop` and `CharacterAction_WaterCrop` — destruction does NOT need it because the harvestable's own `_isDepleted` flag and `NetworkObject.Despawn` already serialize concurrent destroy attempts; harvest is similarly serialized by `Harvestable._isDepleted`).
- `Assets/Scripts/World/MapSystem/MacroSimulator.cs` — add `SimulateCropCatchUp` after `SimulateVegetationCatchUp`.
- `Assets/Scripts/Core/GameLauncher.cs` — call `CropRegistry.Initialize()` after `TerrainTypeRegistry.Initialize()`.
- `Assets/Scripts/Character/Character.cs` — expose `CropPlacement: CropPlacementManager` and auto-assign in `Awake` (per the Character facade pattern).
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` — wire all E-key handling: (a) placement when active-hand item is `SeedSO`/`WateringCanSO` (§5.1, §7), (b) tap-E quick yield-path harvest of nearest interactable, (c) hold-E (>= 0.4s) opens `UI_InteractionMenu` for nearest `Harvestable` (§6.2). Single owner-gated input gate; placement wins when a placement-active item is held.
- `Assets/Scripts/SaveLoad/SaveManager.cs` — call `CropRegistry.Clear()` in `ResetForNewSession()`.

**Asset / scene:**
- New folder `Assets/Resources/Data/Farming/Crops/` for `CropSO` instances. Seed with **three** samples covering the design surface:
  - `Crop_Wheat.asset` — one-shot, no tool (`IsPerennial=false`, `DaysToMature=4`, 4 stage sprites, `ProduceCount=1`, `RequiredHarvestTool=null`, `AllowDestruction=false`). Tap-E with bare hands picks wheat and clears the cell.
  - `Crop_Flower.asset` — yield-only, despawns on harvest, no tool (`IsPerennial=false`, `DaysToMature=2`, 2 stage sprites, `ProduceCount=1`, `RequiredHarvestTool=null`, `AllowDestruction=false`). Demonstrates the simplest interaction (one menu row, tap-E and hold-E equivalent).
  - `Crop_AppleTree.asset` — perennial + destructible (`IsPerennial=true`, `DaysToMature=4`, 4 stage sprites, `RegrowDays=2`, `ProduceCount=4`, `RequiredHarvestTool=null`, `AllowDestruction=true`, `RequiredDestructionTool=Item_Axe`, `DestructionOutputs=[Item_Wood]`, `DestructionOutputCount=4`). Two menu rows. Short cycle keeps the test fast (4 days to grow + 2 days to refill).
- Sample items under `Assets/Resources/Data/Items/`: `Item_Seed_Wheat.asset`, `Item_Seed_Flower.asset`, `Item_Seed_AppleSapling.asset` (`SeedSO`), `Item_WateringCan.asset` (`WateringCanSO`), `Item_Wood.asset` (plain `ItemSO`), `Item_Apple.asset` (plain `ItemSO`), `Item_Axe.asset` (`WeaponSO` — axes are weapons too, demonstrates the orthogonality of `RequiredDestructionTool` to combat roles).
- Sample crop harvestable prefabs: `CropHarvestable_Wheat.prefab`, `CropHarvestable_Flower.prefab`, `CropHarvestable_AppleTree.prefab` under `Assets/Prefabs/Farming/`.
- One sample crop visual prefab (or procedurally-spawned — TBD in implementation).

**Documentation (per rule #28, #29, #29b):**
- New `.agent/skills/farming/SKILL.md` — procedural how-to for adding a new crop.
- New `wiki/systems/farming.md` — architecture page.
- Update `wiki/systems/terrain-and-weather.md` — note that `IsPlowed` / `PlantedCropId` / `GrowthTimer` are now consumed by `[[farming]]`; remove these from its "Open questions".
- Evaluate whether a `farming-specialist` agent is warranted (likely not until NPC AI farming is added).

---

## 12. Acceptance Criteria

V1 ships when **all** of the following work in a multiplayer host+client session:

1. **Plant.** A player holding `Item_Seed_Wheat` presses E on plowable terrain, ghost follows mouse, click commits. Cell mutates on host AND client; sprite appears at stage 0; seed count decrements.
2. **Water.** A player holding `Item_WateringCan` presses E, click on a planted cell. Cell `Moisture` jumps to 1.0; sprite tint reflects moist soil (existing terrain-cell visual layer).
3. **Grow.** With watered cells, advancing the in-game day (live or via `TimeSkipController`) bumps `GrowthTimer` by 1; sprite advances one stage.
4. **Stall.** Letting moisture decay below `MinMoistureForGrowth` before the day rollover → `GrowthTimer` does NOT increment; cell visual stays at current stage.
5. **One-shot mature → harvest.** With `Crop_Wheat`: when `GrowthTimer == DaysToMature`, a `CropHarvestable` spawns. Player harvests → wheat items drop; cell clears `PlantedCropId`/`GrowthTimer` but stays `IsPlowed`; the harvestable despawns and the cell visual goes blank.
6. **Perennial mature → harvest → refill (no respawn).** With `Crop_AppleTree` (`DaysToMature=4`, `RegrowDays=2`): when `GrowthTimer == 4`, a `CropHarvestable` spawns showing `_readySprite`. Player harvests → 4 apples drop in one burst, harvestable swaps to `_depletedSprite` and **stays standing**, cell sets `TimeSinceLastWatered = 0`. With watered conditions, advancing 2 in-game days → `FarmGrowthSystem` calls `harvestable.Refill()`, sprite swaps back to `_readySprite`, `TimeSinceLastWatered = -1`. Cycle repeats indefinitely. The harvestable GameObject is the same instance throughout.
7. **Re-plant (one-shot only).** Pressing E with a wheat seed on the just-harvested wheat cell plants again with no plowing step. Perennial cells reject re-plant attempts (the harvestable is in the way).
8. **Late joiner.** A second client joining a session mid-growth sees all planted cells at the correct stage immediately on map sync — including a perennial in its "fruitless" stage.
9. **Hibernation.** Leave the map for 5 in-game days (force hibernate via debug). On return: growing crops have advanced by up to 5 days (clamped at maturity); mature ones have a harvestable spawned post-wake; a perennial harvested-then-hibernated for 5 days with `RegrowDays = 2` shows its harvestable in the **ready** state again on wake.
10. **Save/load (full close-and-reopen).** Set up a cell with growing wheat at stage 2, a cell with a ready apple-tree harvestable, and a cell with a depleted apple-tree harvestable mid-refill (`TimeSinceLastWatered = 1`). Save the world, **fully close the game** (process exit), reopen, load the world. Verify on the host AND on a client that:
    - Wheat cell shows the stage-2 sprite (not stage 0, not mature).
    - Ready apple tree shows `_readySprite`, harvestable is interactable, picking yields apples.
    - Depleted apple tree shows `_depletedSprite` (NOT the ready sprite — this is the bug class the spawn-time `startDepleted` flag prevents), tap-E does nothing (yield path empty), hold-E menu shows "Pick apples" greyed-out, advancing one in-game day refills it.
11. **No allocs in tick.** Profile a 35k-cell grid with 500 planted cells; `FarmGrowthSystem.HandleNewDay()` pass shows zero `GC.Alloc` (per rule #34).
12. **Destroy a perennial via the menu.** With a fully-grown apple tree (ready or depleted) and `Item_Axe` in hand, **holding E** opens `UI_InteractionMenu` showing two rows: "Pick apples" (greyed if depleted) and "Chop down — 4× Wood — requires Axe" (available). Selecting "Chop down" plays `CharacterAction_DestroyHarvestable` for `DestructionDuration` seconds, then 4 `Item_Wood` drop, the tree despawns, and the cell clears (`PlantedCropId = null`, `IsPlowed` stays true so re-planting works). Same flow works on a wild scene-placed `Harvestable` configured with `_allowDestruction = true`.
13. **Tap-E never destroys.** With `Item_Axe` in hand and a ready apple tree, **tapping E** picks apples (yield path) — the tree is NOT destroyed. The destruction path is reachable only via hold-E + menu. (This is the key UX guarantee.)
14. **Yield-only flower.** Plant `Crop_Flower`, wait 2 days, **tap E** with bare hands → flower picked, cell cleared. **Hold E** opens the menu with one row only ("Pick flower"), no destruction option present.
15. **Greyed-out option visibility.** With bare hands and a ready apple tree, hold E → menu shows "Pick apples" (available) AND "Chop down — requires Axe" (greyed-out with reason "Requires Axe"). Player learns what the destruction option is even without the right tool.
16. **Placement-active item suppresses harvest input.** With `Item_Seed_Wheat` in active hand and standing next to a ready apple tree, tap E → seed placement mode starts (existing behavior, §5). Holding E does NOT open the harvestable menu — placement wins. To harvest, the player swaps the active hand off the seed first.

---

## 13. Build Sequence (Outline — full plan in /writing-plans next)

1. **Data layer** — `CropSO` + `CropRegistry` + `SeedSO` + `WateringCanSO`. No behaviour. Compile-only.
2. **`Harvestable` refactor** — extract `OnDepleted()` hook, add `ResetHarvestState()` helper, add yield-tool field + `CanHarvestWith` (and switch `Interact()` to use it), add destruction fields + `CanDestroyWith` + `DestroyForOutputs` + `OnDestroyed` virtual + `GetInteractionOptions(Character)`. Verify existing wild harvestables still work with destruction defaults off.
3. **`CharacterAction_DestroyHarvestable`**. Since `Interact()` no longer dispatches destruction (menu-only, ships at step 10), test this step via either: (a) a `/devmode` chat command that queues the action against the selected harvestable, or (b) a temporary `[ContextMenu]` button on `Harvestable` that does the same in-editor. Either approach is throwaway scaffolding for steps 3–9; step 10 replaces it with the real UX.
4. **`CropHarvestable`** — subclass + ready/depleted sprite swap (`NetworkVariable<bool> IsDepleted`) + `OnDepleted` branch (one-shot despawn-and-clear vs perennial stay-and-mark-cell) + `OnDestroyed` override + `Refill()`. Manual editor test for all three modes (one-shot harvest, perennial harvest+refill, destruction).
5. **`FarmGrowthSystem`** — server tick + three-branch pipeline (growing / ready-do-nothing / depleted-refilling), `_activeHarvestables` registry, mature-spawn + perennial-refill dispatch, **and the `PostWakeSweep()` method** (§9.2) that reconstructs harvestables from cell state. Wire `PostWakeSweep()` to fire after `MapController.WakeUp()` AND after world-load cell-restore. Manual seed-via-script test that survives close-and-reopen.
6. **`SendDirtyCellsClientRpc`** + `CropVisualSpawner` — visuals appear & update on clients.
7. **`CropPlacementManager` + `CharacterAction_PlaceCrop`** — seed-in-hand → ghost → click → plant. Player path.
8. **`CharacterAction_WaterCrop`** — watering can flow.
9. **`PlayerController` E-key dispatch** — single owner-gated handler that branches: placement (held seed/can) > tap-E quick yield-harvest > hold-E (≥0.4s) opens menu.
10. **`UI_InteractionMenu`** + `UI_InteractionOptionRow` + `HarvestInteractionOption` struct + `Harvestable.GetInteractionOptions(Character)`. Menu shows greyed-out unavailable rows. Manual playtest of all three sample crops (wheat / flower / apple tree) and a wild destructible scene Harvestable.
11. **`GameLauncher` registry init** + **`SaveManager.ResetForNewSession`** clear.
12. **`MacroSimulator.SimulateCropCatchUp`** — offline progress.
13. **Acceptance pass** — all 16 criteria from §12.
14. **Docs** — `wiki/systems/farming.md` + `.agent/skills/farming/SKILL.md` + terrain-weather page update.

---

## Sources

- [Assets/Scripts/Terrain/TerrainCell.cs](../../Assets/Scripts/Terrain/TerrainCell.cs) — pre-wired farming fields.
- [Assets/Scripts/Terrain/TerrainCellGrid.cs](../../Assets/Scripts/Terrain/TerrainCellGrid.cs) — grid the system layers on top of.
- [Assets/Scripts/Terrain/VegetationGrowthSystem.cs](../../Assets/Scripts/Terrain/VegetationGrowthSystem.cs) — sibling pattern for the daily ticker.
- [Assets/Scripts/Terrain/TerrainWeatherProcessor.cs](../../Assets/Scripts/Terrain/TerrainWeatherProcessor.cs) — already drives moisture from rain.
- [Assets/Scripts/Interactable/Harvestable.cs](../../Assets/Scripts/Interactable/Harvestable.cs) — base class for `CropHarvestable`.
- [Assets/Scripts/World/Buildings/BuildingPlacementManager.cs](../../Assets/Scripts/World/Buildings/BuildingPlacementManager.cs) — shape mirrored by `CropPlacementManager`.
- [Assets/Scripts/Character/CharacterActions/CharacterAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterAction.cs) — base class for `CharacterAction_PlaceCrop` / `_WaterCrop`.
- [Assets/Scripts/DayNightCycle/TimeManager.cs](../../Assets/Scripts/DayNightCycle/TimeManager.cs) — `OnNewDay` event drives the tick.
- [Assets/Scripts/World/MapSystem/MacroSimulator.cs](../../Assets/Scripts/World/MapSystem/MacroSimulator.cs) — offline catch-up insertion point.
- [wiki/systems/terrain-and-weather.md](../../wiki/systems/terrain-and-weather.md) — sibling system architecture.
- 2026-04-28 conversation with Kevin — design decisions (Option A cell-grid, Option B no-wither, deferred seasons, deferred building grid-snap).
