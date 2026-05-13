---
type: gotcha
title: "Save-restore state flip silently drops post-Complete cascade"
tags: [building, save-load, network-lifecycle, navmesh, furniture, hibernation]
created: 2026-05-13
updated: 2026-05-13
sources:
  - "[Building.cs](../../Assets/Scripts/World/Buildings/Building.cs) — `RestoreFromSaveData` (~1804), `Start` (~473), `OnNetworkSpawn` (~320), `HandleStateChanged` (~894), `_isStarted` flag (~150)"
  - "[MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) — `SpawnSavedBuildings` (~859), `ApplyDynamicSaveDataToBuilding` (~959)"
  - "[building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) — Save/restore ordering"
  - "2026-05-13 conversation with Kevin — Lumberyard crate vanishing on save/load"
related:
  - "[[building]]"
  - "[[building-state]]"
  - "[[save-load]]"
  - "[[construction]]"
status: mitigated
confidence: high
---

# Save-restore state flip silently drops post-Complete cascade

## Summary
`MapController.SpawnSavedBuildings` → `Instantiate` → `NetworkObject.Spawn()` → `Building.OnNetworkSpawn` → `ApplyDynamicSaveDataToBuilding` → `building.RestoreFromSaveData(bSave)` runs synchronously in one frame. `OnNetworkSpawn` auto-derives `_currentState.Value = UnderConstruction` for any prefab with non-empty `_constructionRequirements` and `_spawnAsComplete = false`. `RestoreFromSaveData` then writes `_currentState.Value = bSave.State` (e.g. `Complete`). The `OnValueChanged` fires, but the subscription `_currentState.OnValueChanged += HandleStateChanged` lives in `Start()` — which Unity hasn't yet dispatched. **No subscriber → `HandleStateChanged` never runs → no `TrySpawnDefaultFurniture`, no NavMesh carve, no `OnConstructionComplete` event.** Default furniture (prefab-authored `_defaultFurnitureLayout` slots, e.g. the Lumberyard's pre-placed storage crate) silently vanishes on every load. Storage CONTENTS restore also no-ops because the storage doesn't exist when the content-restore step runs.

This is the **save-restore sibling** of the BuildInstantly race documented at [[buildinstantly-pre-start-lifecycle-race]]. Same root cause, different code path, different chosen fix.

## Symptom
On a save → exit → load cycle, the building re-spawns, **but**:
- Prefab-authored storage furniture (e.g. Lumberyard crate, Forge anvil) is missing.
- Storage contents that WERE saved are gone (because the storage object that owned them never spawned, so the per-furniture key lookup in `RestoreStorageFurnitureContents` matches nothing → silent `continue`).
- NavMesh is not carved (characters walk through the footprint until a different building triggers a rebake).
- Building visuals appear correctly as Complete (because `Start.ApplyConstructionVisuals(_currentState.Value)` reads the now-`Complete` value directly when Start runs next frame — masking the cascade failure exactly like the BuildInstantly case).
- Log smoking gun: `[Building.OnNetworkSpawn] {name} reqs=N → state=UnderConstruction` followed by `[MapController:RestoreStorage] ...silent skip...` — but **no** `[Building.HandleStateChanged] UnderConstruction → Complete` line.

ShopBuilding appeared to dodge this because its SellShelves are nested-NetworkObject children of the prefab, absorbed into `_defaultFurnitureLayout` at Awake by `ConvertNestedNetworkFurnitureToLayout`. They land in the same `_defaultFurnitureLayout` and hit the same gate — so Shop's crates are equally vulnerable; they just appeared to work because the test rarely exercised them with `_constructionRequirements` non-empty.

## Root cause
Lifecycle ordering of four events in the same frame, server side:

1. `MapController.SpawnSavedBuildings` → `Instantiate(prefab)` (Awake runs — `ConvertNestedNetworkFurnitureToLayout`, base.Awake, etc.).
2. `netObj.Spawn()` → `Building.OnNetworkSpawn` runs synchronously inside Spawn:
   - Sets `_currentState.Value = UnderConstruction` (reqCount > 0 path).
   - Skips `ConfigureNavMeshObstacles()` and `TrySpawnDefaultFurniture()` because they're gated on `state == Complete`.
3. `MapController.ApplyDynamicSaveDataToBuilding(building, bSave)` runs synchronously (same call site, same frame):
   - Eventually invokes `building.RestoreFromSaveData(bSave)` which writes `_currentState.Value = bSave.State` (= Complete).
   - The `OnValueChanged` fires now — but **no subscriber is wired**.
4. `Start()` runs on Unity's next dispatch:
   - Subscribes `_currentState.OnValueChanged += HandleStateChanged`.
   - Reads `_currentState.Value` and calls `ApplyConstructionVisuals(Complete)` — visuals come up correctly.
   - **The cascade-driving state transition has already happened and been missed.** Future state writes would fire `HandleStateChanged`, but the building is already in Complete and there are no further writes.

End state: visuals look right, but no default furniture, no NavMesh, no OnConstructionComplete event.

Same lifecycle bug as [[buildinstantly-pre-start-lifecycle-race]]. The difference is the trigger (save-restore vs. instant-mode placement) and the fix pattern (see below).

## How to avoid
Same principles as the BuildInstantly variant:

- Do not rely on `OnValueChanged` callbacks for state writes that happen between `Spawn()` returning and `Start()` running on the same frame.
- Subscribers in `Start()` must not be depended on by code paths invoked synchronously after `Spawn()`.
- For a server-only restore entry point that writes a state NetworkVariable, fire any side-effect cascade explicitly inside the restore method instead of trusting the change callback.

## How to fix (if already hit)
`Building.RestoreFromSaveData()` ([Building.cs:1804](../../Assets/Scripts/World/Buildings/Building.cs)) manually invokes the post-Complete server-only side effects when state ends up Complete **and** `_isStarted == false`:

```csharp
public void RestoreFromSaveData(BuildingSaveData data)
{
    if (!IsServer) return;
    if (data == null) return;

    if (_currentState.Value != data.State) _currentState.Value = data.State;
    ConstructionProgress.Value = Mathf.Clamp01(data.ConstructionProgress);

    // Save-restore subscription-timing fix: HandleStateChanged subscription happens
    // in Start (next Unity frame), so the OnValueChanged fired by the state write
    // above has no subscriber. Manually invoke the server-only side effects of the
    // post-Complete branch of HandleStateChanged. Idempotent — TrySpawnDefaultFurniture
    // is guarded by _defaultFurnitureSpawned; ConfigureNavMeshObstacles is a safe
    // rebuild. Visual swap is handled by Start.ApplyConstructionVisuals reading the
    // now-correct value when Start eventually runs.
    if (_currentState.Value == BuildingState.Complete && !_isStarted)
    {
        try { TrySpawnDefaultFurniture(); }
        catch (Exception e) { Debug.LogException(e, this); }
        try { ConfigureNavMeshObstacles(); }
        catch (Exception e) { Debug.LogException(e, this); }
    }
    // ... DeliveredMaterials replay below ...
}
```

Paired with reordering inside `MapController.ApplyDynamicSaveDataToBuilding`: call `RestoreFromSaveData` **before** `RestorePlacedFurnitureForBuilding` + `RestoreStorageFurnitureContents` + `RestoreCashierContents` + the ShopBuilding hook. That way, the default-layout furniture is live when the per-furniture content restores run:

```csharp
// Owners + employees first (existing order).
building.RestoreOwnersFromSaveData(bSave.OwnerCharacterIds);
if (building is CommercialBuilding commercial)
    commercial.RestoreEmployeesFromSaveData(bSave.Employees);

// State + manual cascade — default-layout furniture spawns here.
building.RestoreFromSaveData(bSave);

// Now content-restore steps find live furniture.
RestorePlacedFurnitureForBuilding(building, bSave);
RestoreStorageFurnitureContents(building, bSave);
RestoreCashierContents(building, bSave);
if (building is ShopBuilding shop)
{
    shop.RestoreShopFromSaveData(bSave);
    shop.OnFurnituresLoaded();
}
```

We deliberately do **not** mirror the full `HandleStateChanged` body — `EvictLeftoversToPerimeter` is a one-time construction-loop side effect (saved `WorldItem`s on the footprint come back via the separate world-item save pipeline), and firing `OnConstructionComplete` from save-restore would falsely signal quest hooks / event listeners that the building just finished construction.

## Why not the BuildInstantly coroutine-defer pattern?
[[buildinstantly-pre-start-lifecycle-race]] solves the same race by deferring the state flip via a coroutine that polls `_isStarted` and waits one frame for Start to run. That works for instant placement because nothing else in the same frame depends on the state being Complete yet — the gameplay path can tolerate a one-frame wait.

Save-restore can't use that pattern because:
- `MapController.RestoreStorageFurnitureContents` runs immediately after `RestoreFromSaveData` in the same synchronous block and walks `building.GetFurnitureOfType<StorageFurniture>()` looking for default-layout storages to bind contents to. If furniture spawn is deferred to the next frame, the content-restore step finds zero live storages and silently no-ops (the original symptom).
- The same applies to the cashier restore, the ShopBuilding sell-shelf hook, and any future per-furniture binding step.

So save-restore needs the cascade side effects to land **synchronously** inside the same call. The manual-invoke pattern guarantees that; the coroutine-defer pattern doesn't. Both patterns are valid for their respective call sites — the choice is dictated by whether the caller can tolerate a one-frame delay.

## Affected systems
- [[building]]
- [[building-state]]
- [[save-load]]
- [[construction]]
- [[navmesh]]

## Links
- [[buildinstantly-pre-start-lifecycle-race]] — same root cause, instant-mode placement variant, coroutine-defer fix.
- [[furnituremanager-replace-style-rescan]] — sibling FurnitureManager hazard around spawn-cascade ordering.
- [[host-player-uuid-timing-on-load]] — different system, same flavour of "thing observed at frame N must be checked at frame M" lifecycle bug.

## Sources
- 2026-05-13 conversation with [[kevin]] — Lumberyard crate vanishing on save/load.
- [Building.cs](../../Assets/Scripts/World/Buildings/Building.cs) — `RestoreFromSaveData`, `_isStarted`, `HandleStateChanged`.
- [MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) — `SpawnSavedBuildings`, `ApplyDynamicSaveDataToBuilding`.
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) — Save/restore ordering invariants.
