# Building Default-Furniture Auto-Conversion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let level designers author Furniture as visible children of building prefabs and have the runtime auto-convert them into `_defaultFurnitureLayout` entries before NGO sees the nested NetworkObjects, while hoisting the layout system from `CommercialBuilding` up to `Building` so every building subclass benefits.

**Architecture:** Move all `_defaultFurnitureLayout`-related members (nested type, field, flag, spawn methods, OnNetworkSpawn call site) from `CommercialBuilding` to `Building`. Replace the existing `is CraftingBuilding crafting` SOLID violation with a `protected virtual OnDefaultFurnitureSpawned()` hook overridden by subclasses. Add a new `ConvertNestedNetworkFurnitureToLayout()` method that runs in `Building.Awake()` on every peer, captures each network-bearing Furniture child's pose into a fresh `DefaultFurnitureSlot`, and destroys the child so NGO never half-spawns it. The existing top-level `Spawn()` path in `TrySpawnDefaultFurniture` re-creates each entry post-spawn.

**Tech Stack:** Unity 2022 LTS, C#, Unity Netcode for GameObjects (NGO).

**Spec:** [docs/superpowers/specs/2026-05-01-building-default-furniture-auto-conversion-design.md](../specs/2026-05-01-building-default-furniture-auto-conversion-design.md)

---

## File Map

| Path | Change |
| ---- | ------ |
| [Assets/Scripts/World/Buildings/Building.cs](../../../Assets/Scripts/World/Buildings/Building.cs) | edit — receive hoisted layout system; add `ConvertNestedNetworkFurnitureToLayout`; add `OnDefaultFurnitureSpawned` virtual hook; call conversion from `Awake()`, call `TrySpawnDefaultFurniture` from `OnNetworkSpawn` server branch |
| [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) | edit — remove hoisted code; add `OnDefaultFurnitureSpawned` override; drop `TrySpawnDefaultFurniture()` call from its own `OnNetworkSpawn` |
| [Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs](../../../Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs) | edit — add `OnDefaultFurnitureSpawned` override (`base` + `InvalidateCraftableCache()`); refresh stale `_defaultFurnitureLayout` docstring reference |
| [Assets/Scripts/World/Buildings/FurnitureManager.cs](../../../Assets/Scripts/World/Buildings/FurnitureManager.cs) | edit — refresh two stale `CommercialBuilding._defaultFurnitureLayout` docstring references to `Building._defaultFurnitureLayout` |
| [.agent/skills/building_system/SKILL.md](../../../.agent/skills/building_system/SKILL.md) | edit — document the new "author Furniture as nested prefab children" pattern + the auto-conversion-in-Awake step |
| [wiki/systems/building.md](../../../wiki/systems/building.md) | edit — add a section documenting `_defaultFurnitureLayout` at the `Building` level (lifted from commercial-building.md); bump `updated:` to 2026-05-01; append change-log entry |
| [wiki/systems/commercial-building.md](../../../wiki/systems/commercial-building.md) | edit — collapse the existing `## Default furniture spawn (_defaultFurnitureLayout)` section to a one-liner that links to building.md; document the `OnDefaultFurnitureSpawned` override; bump `updated:`; append change-log entry |

---

## Task 1: Hoist layout system from `CommercialBuilding` to `Building`

This is one atomic move — intermediate states will not compile. All four chunks (nested type, field, flag, methods, call site) must move together.

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Building.cs`
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`

- [x] **Step 1.1: Add `using` directives to `Building.cs` if missing**

`Building.cs` currently has `using System.Collections.Generic; using Unity.Netcode; using UnityEngine;`. The hoisted code does **not** need any new using directive (it already references `FurnitureItemSO`, `Furniture`, `Room`, `NetworkObject`, all of which are reachable).

Verify no edit needed.

- [x] **Step 1.2: Hoist the `DefaultFurnitureSlot` nested type into `Building.cs`**

Cut the entire `DefaultFurnitureSlot` nested-class block from `CommercialBuilding.cs` (its xmldoc comment + the `[System.Serializable] public class DefaultFurnitureSlot { ... }` body). Paste into `Building.cs` immediately above the `[Header("Building Info")]` block. Keep visibility `public class DefaultFurnitureSlot` so `Building.DefaultFurnitureSlot` is the new fully-qualified name. Update the xmldoc to say `Building` everywhere it currently says `CommercialBuilding` / `BuildingPlacementManager`-flavored language; the rule is identical for any subclass now.

- [x] **Step 1.3: Hoist `_defaultFurnitureLayout` and `_defaultFurnitureSpawned` fields into `Building.cs`**

Cut from `CommercialBuilding.cs` the `[SerializeField] private List<DefaultFurnitureSlot> _defaultFurnitureLayout = new List<DefaultFurnitureSlot>();` declaration with its `[Tooltip(...)]`, plus the `_defaultFurnitureSpawned` flag with its xmldoc comment.

Paste into `Building.cs` directly below the `[Header("Construction")]` group, under a new `[Header("Default Furniture")]` group. Apply `[FormerlySerializedAs("_defaultFurnitureLayout")]` as a precaution against Unity hierarchy-promotion serialization edge cases:

```csharp
[Header("Default Furniture")]
[Tooltip("Furniture spawned automatically by the server when this building first comes into existence in a fresh world.\n" +
         "Skipped on save-restore — restored buildings reuse their persisted furniture state.\n" +
         "Use this for any furniture whose prefab carries a NetworkObject; nesting a network-bearing furniture\n" +
         "PrefabInstance directly inside the building prefab half-spawns the child and NRE's NGO sync.\n" +
         "As of 2026-05-01: Furniture authored as nested children of the building prefab is auto-captured into this list at runtime by ConvertNestedNetworkFurnitureToLayout(); manual authoring of slots remains supported (and wins over auto-converted entries with the same ItemSO).")]
[UnityEngine.Serialization.FormerlySerializedAs("_defaultFurnitureLayout")]
[SerializeField] private List<DefaultFurnitureSlot> _defaultFurnitureLayout = new List<DefaultFurnitureSlot>();

/// <summary>
/// Set true after <see cref="TrySpawnDefaultFurniture"/> runs so multiple OnNetworkSpawn
/// invocations (rare, e.g. domain reload during a session) cannot duplicate the layout.
/// Not networked — clients never spawn furniture; this flag is server-only state.
/// </summary>
private bool _defaultFurnitureSpawned;
```

- [x] **Step 1.4: Hoist `TrySpawnDefaultFurniture` and `SpawnDefaultFurnitureSlot` methods into `Building.cs`**

Cut the `// =====...DEFAULT FURNITURE SPAWN (server-only)` section block from `CommercialBuilding.cs` — both methods (`TrySpawnDefaultFurniture` and `SpawnDefaultFurnitureSlot`) plus the section-header comment. Paste into `Building.cs` near the bottom of the file, after `GetPendingMaterials()`. Keep both methods `private`. (Use Grep on `TrySpawnDefaultFurniture` in `CommercialBuilding.cs` to find the current line range — line numbers will have shifted from earlier cuts in this task.)

- [x] **Step 1.5: Move the `TrySpawnDefaultFurniture()` call from `CommercialBuilding.OnNetworkSpawn` to `Building.OnNetworkSpawn`**

In `CommercialBuilding.cs`, delete the `TrySpawnDefaultFurniture();` call inside the `if (IsServer)` block of `OnNetworkSpawn` (Grep `TrySpawnDefaultFurniture` to locate; only one call site).

In `Building.cs`, modify `OnNetworkSpawn` to add the call inside its existing `if (IsServer && NetworkBuildingId.Value.IsEmpty)` neighborhood. The cleanest landing site is **after** the existing ID-derivation logic but still inside an `if (IsServer)` guard — add a new server-only block:

```csharp
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();

    if (IsServer && NetworkBuildingId.Value.IsEmpty)
    {
        // ... existing ID-derivation logic stays here unchanged ...
    }

    ConfigureNavMeshObstacles();

    // Server-only: spawn any _defaultFurnitureLayout entries (manual + auto-converted)
    // that don't already have a matching restored Furniture child. Hoisted from
    // CommercialBuilding 2026-05-01 so every Building subclass benefits.
    if (IsServer)
    {
        TrySpawnDefaultFurniture();
    }
}
```

- [x] **Step 1.6: Save files; let Unity recompile; resolve compile errors**

Save both modified files. Wait for the Unity Editor to recompile. Verify the Console is clean by calling the MCP tool `mcp__ai-game-developer__console-get-logs` (filter level=Error) — preferred over manual Console inspection because it returns a structured list.

Expected: zero compile errors. Two warning categories acceptable:
- `CS0114` if any subclass shadowed `OnNetworkSpawn` without `override` — should not happen, but check.
- `CS0649` ("field never assigned") on the ItemSO/TargetRoom slots is normal for `[SerializeField]` fields.

If compile errors mention `CommercialBuilding.DefaultFurnitureSlot` or `CommercialBuilding._defaultFurnitureLayout`, those are residual references — fix by replacing with `Building.DefaultFurnitureSlot` / inherited `_defaultFurnitureLayout` access.

- [x] **Step 1.7: Stage and commit**

```bash
git add Assets/Scripts/World/Buildings/Building.cs Assets/Scripts/World/Buildings/CommercialBuilding.cs
git status   # confirm only those two files changed
git commit -m "refactor(building): hoist _defaultFurnitureLayout system from CommercialBuilding to Building"
```

---

## Task 2: Replace `is CraftingBuilding` cast with virtual `OnDefaultFurnitureSpawned` hook

Removes the SOLID rule #11 / #14 violation in `TrySpawnDefaultFurniture`.

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Building.cs`
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs`

- [x] **Step 2.1: Add the virtual hook in `Building.cs`**

Inside `Building.cs`, add this virtual method directly above `TrySpawnDefaultFurniture`:

```csharp
/// <summary>
/// Subclass extension point fired after <see cref="TrySpawnDefaultFurniture"/> finishes a
/// fresh-world spawn pass. Default no-op. Override to invalidate any subclass-owned cache
/// that depends on the just-spawned furniture (storage furniture cache on
/// <see cref="CommercialBuilding"/>, craftable cache on <c>CraftingBuilding</c>, etc.).
///
/// Always chain via <c>base.OnDefaultFurnitureSpawned()</c> in overrides so the parent
/// class's invalidations still run.
/// </summary>
protected virtual void OnDefaultFurnitureSpawned() { }
```

- [x] **Step 2.2: Replace the cast in `TrySpawnDefaultFurniture` with the hook call**

In `Building.cs`, locate the tail of `TrySpawnDefaultFurniture` (the part hoisted from CommercialBuilding):

```csharp
// Tier 2 cache invalidation: ...
InvalidateStorageFurnitureCache();
if (this is CraftingBuilding crafting)
{
    crafting.InvalidateCraftableCache();
}
```

Replace with:

```csharp
// Subclass cache invalidation hook. Default no-op; overridden by CommercialBuilding
// (storage furniture cache) and CraftingBuilding (+ craftable cache).
OnDefaultFurnitureSpawned();
```

This removes the dependency on `InvalidateStorageFurnitureCache()` / `CraftingBuilding` from `Building`, which is essential because `Building` lives in the base namespace and shouldn't know about its descendants.

- [x] **Step 2.3: Add the override in `CommercialBuilding.cs`**

Add this method to `CommercialBuilding` (anywhere in the class — near other commercial-building lifecycle methods is fine):

```csharp
/// <summary>
/// Invalidate the StorageFurniture cache after the default furniture layout spawns,
/// so logistics/supplier queries pick up freshly-spawned chests within the 2 s TTL window.
/// See wiki/projects/optimisation-backlog.md entry #2 for the cache rationale.
/// </summary>
protected override void OnDefaultFurnitureSpawned()
{
    base.OnDefaultFurnitureSpawned();
    InvalidateStorageFurnitureCache();
}
```

- [x] **Step 2.4: Add the override in `CraftingBuilding.cs`**

Add this method to `CraftingBuilding`:

```csharp
/// <summary>
/// Chains <see cref="CommercialBuilding.OnDefaultFurnitureSpawned"/> (storage cache)
/// and adds the craftable cache invalidation so newly-spawned CraftingStations are
/// visible to FindSupplierFor / GetCraftableItems on the next access.
/// </summary>
protected override void OnDefaultFurnitureSpawned()
{
    base.OnDefaultFurnitureSpawned();
    InvalidateCraftableCache();
}
```

- [x] **Step 2.5: Save; let Unity recompile; verify no errors**

Expected: zero compile errors. Behavior identical to before for any building that already used `_defaultFurnitureLayout` — both `InvalidateStorageFurnitureCache` and `InvalidateCraftableCache` still run for `CraftingBuilding`, and only `InvalidateStorageFurnitureCache` runs for non-crafting `CommercialBuilding`.

- [x] **Step 2.6: Commit**

```bash
git add Assets/Scripts/World/Buildings/Building.cs \
        Assets/Scripts/World/Buildings/CommercialBuilding.cs \
        Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs
git commit -m "refactor(building): replace 'is CraftingBuilding' cast with OnDefaultFurnitureSpawned virtual hook"
```

---

## Task 3: Implement `ConvertNestedNetworkFurnitureToLayout` on `Building`

The new method per spec section "Conversion logic".

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Building.cs`

- [x] **Step 3.1: Add the conversion method**

In `Building.cs`, add this method directly above `TrySpawnDefaultFurniture`:

```csharp
/// <summary>
/// Edit-time, the level designer authors furniture as nested children of this prefab
/// (visible, easy to position). At runtime — BEFORE NGO half-spawns the nested
/// NetworkObjects and corrupts <c>SpawnManager.SpawnedObjectsList</c> — this method:
///
///   1. Captures each network-bearing Furniture child's <see cref="FurnitureItemSO"/>
///      + local pose + nearest <c>Room</c> ancestor into a runtime-only
///      <see cref="DefaultFurnitureSlot"/> appended to <c>_defaultFurnitureLayout</c>.
///   2. <see cref="UnityEngine.Object.Destroy(UnityEngine.Object)"/>'s the child
///      GameObject so NGO never sees it as a nested NetworkObject.
///
/// <see cref="TrySpawnDefaultFurniture"/> (server-only, in <see cref="OnNetworkSpawn"/>)
/// then re-spawns each appended entry as a top-level NetworkObject parented under the
/// building. End result: same visual layout as authored, no half-spawned NOs, clients
/// stay in sync.
///
/// Runs on every peer (server + clients) because every peer's <c>Instantiate</c> of
/// this prefab brings the nested children along; without local destruction, clients
/// would keep broken half-registered NetworkObject children in their scene.
///
/// Furniture children WITHOUT a NetworkObject (e.g. a plain-MonoBehaviour TimeClock
/// stripped of its NO) are LEFT IN PLACE — they're legal as nested non-network children,
/// and <see cref="TrySpawnDefaultFurniture"/>'s per-slot ItemSO dedup already handles them.
///
/// All <c>_defaultFurnitureLayout</c> mutations are in-memory only on the live instance.
/// The <c>!Application.isPlaying</c> guard prevents any edit-mode invocation, so Unity's
/// serialization system never sees the runtime mutation — the prefab asset stays clean.
/// </summary>
private void ConvertNestedNetworkFurnitureToLayout()
{
    if (!Application.isPlaying) return;

    Furniture[] children = GetComponentsInChildren<Furniture>(includeInactive: true);
    if (children == null || children.Length == 0) return;

    int converted = 0;
    int skipped = 0;
    foreach (var furniture in children)
    {
        if (furniture == null) continue;
        if (furniture.gameObject == gameObject) continue; // defensive: not on building root

        var netObj = furniture.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj == null)
        {
            // Plain-MonoBehaviour furniture is legal as a nested child. Leave it.
            skipped++;
            continue;
        }

        if (furniture.FurnitureItemSO == null)
        {
            Debug.LogWarning(
                $"<color=orange>[Building]</color> {buildingName}: nested furniture '{furniture.name}' has a NetworkObject but no FurnitureItemSO — destroying without conversion (would have half-spawned anyway).",
                this);
            Destroy(furniture.gameObject);
            continue;
        }

        Vector3 localPos = transform.InverseTransformPoint(furniture.transform.position);
        Vector3 localEuler = (Quaternion.Inverse(transform.rotation) * furniture.transform.rotation).eulerAngles;

        // Walk parent chain for the first Room ancestor. Stops at the building root
        // (this transform) — anything above the building doesn't count as a target room.
        Room targetRoom = null;
        for (Transform t = furniture.transform.parent; t != null && t != transform.parent; t = t.parent)
        {
            var room = t.GetComponent<Room>();
            if (room != null) { targetRoom = room; break; }
        }

        var slot = new DefaultFurnitureSlot
        {
            ItemSO = furniture.FurnitureItemSO,
            LocalPosition = localPos,
            LocalEulerAngles = localEuler,
            TargetRoom = targetRoom,
        };

        // Dedup against existing serialized slots. Converted child wins.
        int existingIndex = _defaultFurnitureLayout.FindIndex(s => s != null && s.ItemSO == slot.ItemSO);
        if (existingIndex >= 0)
        {
            Debug.Log(
                $"<color=cyan>[Building]</color> {buildingName}: nested child '{furniture.name}' overrides existing manual _defaultFurnitureLayout entry [{existingIndex}] for ItemSO '{slot.ItemSO.name}'. Remove the manual slot to silence this log.",
                this);
            _defaultFurnitureLayout[existingIndex] = slot;
        }
        else
        {
            _defaultFurnitureLayout.Add(slot);
        }

        Destroy(furniture.gameObject);
        converted++;
    }

    if (converted > 0)
    {
        Debug.Log(
            $"<color=cyan>[Building]</color> {buildingName}: converted {converted} nested NetworkObject furniture child(ren) to _defaultFurnitureLayout (skipped {skipped} non-network furniture).",
            this);
    }
}
```

- [x] **Step 3.2: Save; let Unity recompile; verify no errors**

Expected: zero compile errors. Method is unused at this point — Task 4 wires it up.

- [x] **Step 3.3: Commit**

```bash
git add Assets/Scripts/World/Buildings/Building.cs
git commit -m "feat(building): add ConvertNestedNetworkFurnitureToLayout (Awake-time strip of nested NO furniture)"
```

---

## Task 4: Wire conversion into `Building.Awake()`

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Building.cs`

- [x] **Step 4.1: Locate `Building.Awake` and add the call**

`Building.Awake` currently looks like:

```csharp
protected override void Awake()
{
    base.Awake();

    if (_subRooms.Count == 0)
    {
        Room[] childRooms = GetComponentsInChildren<Room>();
        foreach (Room r in childRooms)
        {
            if (r != this) AddSubRoom(r);
        }
    }

    if (IsServer) { /* state init */ }
}
```

Add the conversion call **after** the `_subRooms` auto-populate and **before** the `IsServer` state-init block. It must run on every peer, so it sits outside any server-only guard:

```csharp
protected override void Awake()
{
    base.Awake();

    if (_subRooms.Count == 0)
    {
        Room[] childRooms = GetComponentsInChildren<Room>();
        foreach (Room r in childRooms)
        {
            if (r != this) AddSubRoom(r);
        }
    }

    // Strip nested-NetworkObject Furniture children → _defaultFurnitureLayout entries.
    // Runs on every peer (server + clients); each peer destroys its own copy of the
    // children so NGO never half-spawns them. See spec
    // docs/superpowers/specs/2026-05-01-building-default-furniture-auto-conversion-design.md
    ConvertNestedNetworkFurnitureToLayout();

    if (IsServer) { /* state init unchanged */ }
}
```

- [x] **Step 4.2: Save; let Unity recompile; verify no errors**

Expected: zero compile errors. Behavior is now live — any building prefab Instantiated at runtime with nested NetworkObject Furniture children will strip them in Awake.

- [x] **Step 4.3: Commit**

```bash
git add Assets/Scripts/World/Buildings/Building.cs
git commit -m "feat(building): call ConvertNestedNetworkFurnitureToLayout from Awake on every peer"
```

---

## Task 5: Refresh stale docstring references in `FurnitureManager` and `CraftingBuilding`

Two existing docstrings reference `CommercialBuilding._defaultFurnitureLayout` / `CommercialBuilding.SpawnDefaultFurnitureSlot`. After hoisting these live on `Building`. Drift hurts future readers; trivial fix.

**Files:**
- Modify: `Assets/Scripts/World/Buildings/FurnitureManager.cs`
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs`

- [x] **Step 5.1: Update `FurnitureManager.cs`**

Two occurrences inside xmldoc comments. Grep `CommercialBuilding._defaultFurnitureLayout` in `FurnitureManager.cs` to locate; replace each with `Building._defaultFurnitureLayout`.

- [x] **Step 5.2: Update `CraftingBuilding.cs`**

One occurrence inside an xmldoc comment. Grep `CommercialBuilding.SpawnDefaultFurnitureSlot` in `CraftingBuilding.cs` to locate; replace with `Building.SpawnDefaultFurnitureSlot`. The unqualified `_defaultFurnitureLayout` reference in the same paragraph stays as-is.

- [x] **Step 5.3: Save; verify no errors; commit**

```bash
git add Assets/Scripts/World/Buildings/FurnitureManager.cs \
        Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs
git commit -m "docs(building): refresh stale CommercialBuilding._defaultFurnitureLayout xmldoc refs"
```

---

## Task 6: Migration validation — verify Unity preserved serialized data

Hierarchy promotion of `[SerializeField]` private fields *usually* preserves data, but it's not guaranteed. The `[FormerlySerializedAs]` attribute added in Step 1.3 is the safety net; this task confirms it worked.

**Files:** none modified (verification only)

- [ ] **Step 6.1: Open each affected prefab in Unity Editor and inspect `_defaultFurnitureLayout`**

For each prefab below, double-click to open in Prefab Mode and look at the `Default Furniture` section on the root building component. Confirm the slot list is **non-empty and shows the same `ItemSO` / `LocalPosition` / `LocalEulerAngles` / `TargetRoom` values** they had before the change.

- [ ] `Assets/Prefabs/Building/Commercial/CommercialBuilding_prefab.prefab`
- [ ] `Assets/Prefabs/Building/Commercial/Crafting/Forge.prefab`
- [ ] `Assets/Prefabs/Building/Commercial/Harvesting/Lumberyard/Lumberyard.prefab`
- [ ] `Assets/Prefabs/Building/Commercial/Shop/Shop.prefab`
- [ ] `Assets/Prefabs/Building/Commercial/Shop/Clothing Shop.prefab`
- [ ] `Assets/Prefabs/Building/Commercial/Transporter/Transporter Building.prefab`

If a slot list comes back **empty** on any prefab that previously had entries, the `[FormerlySerializedAs]` did its job — but Unity may need an extra prod: right-click the asset → Reimport.

- [ ] **Step 6.2: Inspect any scene that drops a Building directly (not via `BuildingPlacementManager`)**

Same check, on scene-instances rather than prefab assets. Open whichever overworld / training scene currently contains scene-authored buildings and confirm their `_defaultFurnitureLayout` entries survived.

- [ ] **Step 6.3: If anything dropped, escalate**

If any prefab/scene shows an empty list where there used to be data, surface it back to the user with the prefab name and the reproduction steps. Possible recovery paths:
- Right-click → Reimport on the prefab
- Add `[UnityEngine.Serialization.FormerlySerializedAs("CommercialBuilding._defaultFurnitureLayout")]` (full-qualified form) as a second attribute
- Worst case: re-author the slot list manually from the layout we know (commit-history snapshot of each prefab's serialized YAML)

- [ ] **Step 6.4: No commit needed if data preserved**

If everything looks good, no commit. If a prefab needed reimport, the prefab `.meta` file may have changed — commit only those:

```bash
git status   # only if changes
git add <changed prefabs/.meta>
git commit -m "chore(building): re-imported prefab after _defaultFurnitureLayout hoist"
```

---

## Task 7: Manual playtest scenarios

These are the scenarios from the spec's Validation plan. Each one is a real Play-Mode session — there are no automated tests for this surface area.

**Files:** none modified

- [ ] **Step 7.1: Existing manual-layout building — solo session**

Boot the project, start a single-player session.
- Use the Dev-Mode tool (Space+LMB) to place a Forge.
- Verify all expected furniture spawns in correct positions (CraftingStation, etc.).
- Walk a worker NPC through a craft cycle — confirm crafting still works.

Expected: identical behavior to before this change.

- [ ] **Step 7.2: Existing manual-layout building — Host + Client**

Start a Host session. Have a Client join. Repeat the placement above.

- Confirm the Host sees the furniture.
- Confirm the Client sees identical furniture in identical positions.
- Confirm no NGO warnings about "nested NetworkObjects" in either Console.

- [ ] **Step 7.3: New visual-authored building (test prefab)**

Create a temporary test prefab variant of e.g. `Forge.prefab`:
- Drop a `CraftingStation` prefab as a child of `Room_Main` inside the building prefab.
- Confirm in the Project window that the child has a `NetworkObject` component (it does — that's what makes this test meaningful).
- Clear the parent's `_defaultFurnitureLayout` so the manual slot list is empty.
- Save the variant.

In Play Mode, place this variant via Dev-Mode.

Expected:
- Awake-time log: `[Building] <name>: converted 1 nested NetworkObject furniture child(ren) to _defaultFurnitureLayout (skipped 0 non-network furniture).`
- The CraftingStation re-spawns at the same world position the prefab child was authored at.
- No NGO half-spawn warning in Console.
- Host + Client both see the station; client crafting works through it.

After the test passes, **delete the temporary variant** (it was for verification only).

- [ ] **Step 7.4: Save / load round-trip**

Place a building (any with a chest), drop a few items into the chest, then save (bed checkpoint or portal gate, whichever is wired).

Reload the save.

Expected: the chest still contains the same items. Confirms the `FurnitureKey = "{ItemId}@{x:F2},{y:F2},{z:F2}"` path still matches because `LocalPosition` values are stable across the auto-conversion vs manual-slot path.

- [ ] **Step 7.5: Idempotence under domain reload**

With a building in the active world, force a script recompile (e.g. add then remove a comment in any `.cs` file, save, wait for Unity recompile while Play Mode is running). Unity will trigger a domain reload.

Expected: building's furniture remains intact, no duplicates appear, no `_defaultFurnitureLayout` log fires the second time (`_defaultFurnitureSpawned` flag still set; no new conversion log because the children were destroyed first time).

- [ ] **Step 7.6: Mixed authoring with overlap**

In a temporary prefab variant, set up:
- Manual `_defaultFurnitureLayout` slot for `CraftingStation` ItemSO at position A.
- Nested `CraftingStation` prefab child at position B (with NetworkObject).

Place the building.

Expected:
- Console log: `nested child '...' overrides existing manual _defaultFurnitureLayout entry [0] for ItemSO 'CraftingStation'. Remove the manual slot to silence this log.`
- Exactly **one** CraftingStation spawns, at position B (the nested child's pose wins).

Delete the temporary variant after the test passes.

- [ ] **Step 7.7: Furniture without NetworkObject preserved**

In a temporary prefab variant, drop a plain-MonoBehaviour furniture (e.g. a TimeClock prefab variant whose `NetworkObject` is stripped) as a nested child of the building.

Place the building.

Expected:
- Awake log shows `skipped 1 non-network furniture` (or `converted 0` + `skipped 1` if no other furniture exists).
- The TimeClock child remains at its authored position, untouched.
- Building works, punch-in works.

- [ ] **Step 7.8: Note any failures, then commit a no-op marker if all pass**

If any scenario fails, stop and surface to the user with the failure mode. If all pass, no commit needed for this task — but it's worth a quick `git status` to confirm no stray edits leaked into the working tree from the test prefabs.

---

## Task 8: Update `.agent/skills/building_system/SKILL.md`

Per project rule #28.

**Files:**
- Modify: `.agent/skills/building_system/SKILL.md`

- [x] **Step 8.1: Read the current SKILL.md**

Locate the section that documents `_defaultFurnitureLayout` (currently anchored on `CommercialBuilding`) — likely under the building/furniture pattern docs. If no such section exists, append the new section at the end of the public-API documentation block (before any "Recent changes" / log block).

- [x] **Step 8.2: Update / add a `Default furniture authoring` section**

Two-mode authoring pattern, written so a future agent can pick the right mode without re-deriving the rules:

```markdown
## Default furniture authoring (Building-level system)

Every `Building` (any subclass — Commercial, Residential, Harvesting, Transporter)
has a `_defaultFurnitureLayout : List<DefaultFurnitureSlot>` SerializeField.
Slots become live Furniture instances on first OnNetworkSpawn via
`TrySpawnDefaultFurniture` (server-only).

### Mode A — Visual authoring (recommended)

Drop the Furniture prefab as a nested child of the building prefab, in the room
hierarchy you want it associated with (e.g. `Room_Main/CraftingStation`).
At runtime, `Building.Awake()` calls `ConvertNestedNetworkFurnitureToLayout()`
on every peer:
  - Each network-bearing Furniture child → captured into a fresh
    `DefaultFurnitureSlot` (ItemSO + local pose + nearest Room ancestor) and
    appended to `_defaultFurnitureLayout`.
  - The child GameObject is `Destroy()`d, so NGO never half-spawns it.
  - Server-only `TrySpawnDefaultFurniture` then re-spawns each entry as a
    top-level NetworkObject parented under the building.
Plain-MonoBehaviour Furniture (no NetworkObject — e.g. TimeClock variant with NO
stripped) is LEFT IN PLACE and dedup'd by ItemSO.

### Mode B — Manual layout (legacy / opt-in)

Author each slot directly in the Inspector list. Same runtime behavior
post-spawn. Valid for cases where the slot has no canonical scene location yet,
or for scripted spawns. If both Mode A and Mode B target the same `ItemSO`,
the Mode A entry wins (logged).

### Save schema gotcha

`DefaultFurnitureSlot.LocalPosition` feeds `FurnitureKey =
"{ItemId}@{x:F2},{y:F2},{z:F2}"` for `StorageFurniture` save/restore. Moving a
slot's local position between save and load silently drops storage contents.
With Mode A, this means **moving a Furniture child in the prefab** has the
same effect — treat slot poses as part of the on-disk schema once a build ships
with stocked storages.

### Subclass cache hook

`OnDefaultFurnitureSpawned()` is the virtual hook fired at the end of
`TrySpawnDefaultFurniture`. Override to invalidate subclass-owned caches that
depend on the just-spawned furniture (storage cache, craftable cache).
Always chain `base.OnDefaultFurnitureSpawned()`.
```

- [x] **Step 8.3: Save and commit**

```bash
git add .agent/skills/building_system/SKILL.md
git commit -m "docs(skills): document Building-level _defaultFurnitureLayout + visual authoring mode"
```

---

## Task 9: Update wiki pages

Per project rule #29b.

**Files:**
- Modify: `wiki/systems/building.md`
- Modify: `wiki/systems/commercial-building.md`

- [x] **Step 9.1: Read `wiki/CLAUDE.md` first**

Per the always-on rule for wiki edits. Confirm frontmatter rules, wikilinks convention, sources requirements. Also confirm `wiki/systems/building.md` exists (verified 2026-05-01 — currently titled "Building & Furniture", `updated: 2026-04-24`); if for any reason it's missing, follow `wiki/_templates/` per project rule #29b to scaffold it from the template.

- [x] **Step 9.2: Update `wiki/systems/building.md`**

- Bump `updated:` to `2026-05-01`.
- Add a `## Default furniture layout` section (lift content from the parallel section currently in `commercial-building.md`, generalize wording to apply to any subclass).
- Append to `## Change log`:
  ```markdown
  - 2026-05-01 — Hoisted `_defaultFurnitureLayout` system from `CommercialBuilding` to `Building` (every subclass now benefits). Added `ConvertNestedNetworkFurnitureToLayout()` Awake-time stripper so designers can author Furniture as nested prefab children. Replaced `is CraftingBuilding` cast with `OnDefaultFurnitureSpawned()` virtual hook. — claude
  ```
- Cross-link the SKILL.md updated in Task 8 in the `Sources` section.

- [x] **Step 9.3: Update `wiki/systems/commercial-building.md`**

- Bump `updated:` to `2026-05-01`.
- Replace the existing `## Default furniture spawn (_defaultFurnitureLayout)` section body with a one-liner pointer:
  ```markdown
  ## Default furniture spawn

  See [[building#Default furniture layout]] for the system (it now lives at the `Building` level). `CommercialBuilding` overrides `OnDefaultFurnitureSpawned()` to invalidate the storage furniture cache after the layout spawns.
  ```
- Append to `## Change log`:
  ```markdown
  - 2026-05-01 — `_defaultFurnitureLayout` system hoisted up to `Building`. CommercialBuilding now only carries the `OnDefaultFurnitureSpawned` override (storage cache invalidation). See [[building#Default furniture layout]]. — claude
  ```

- [x] **Step 9.4: Save and commit**

```bash
git add wiki/systems/building.md wiki/systems/commercial-building.md
git commit -m "docs(wiki): document Building-level _defaultFurnitureLayout system + visual authoring"
```

---

## Final verification

- [ ] **Step F.1: Verify all tasks complete**

```bash
git log --oneline -10
git status   # clean
```

Expected: **7 or 8** commits since the start of this plan (7 unconditional from Tasks 1, 2, 3, 4, 5, 8, 9; +1 conditional from Task 6.4 only if any prefab needed reimport), working tree clean. Expected commit messages contain the strings: `hoist _defaultFurnitureLayout system`, `OnDefaultFurnitureSpawned virtual hook`, `add ConvertNestedNetworkFurnitureToLayout`, `call ConvertNestedNetworkFurnitureToLayout from Awake`, `refresh stale CommercialBuilding._defaultFurnitureLayout xmldoc`, `document Building-level _defaultFurnitureLayout` (skill), `document Building-level _defaultFurnitureLayout system` (wiki). Verify final `mcp__ai-game-developer__console-get-logs` (level=Error) is empty before declaring done.

- [ ] **Step F.2: Final hand-off summary**

Report back to user with:
- Commit list (`git log --oneline <range>`)
- The visual-authoring usage example: "Drop your Furniture prefab as a child of `Room_Main` inside the building prefab. At runtime it'll be auto-captured into `_defaultFurnitureLayout` and spawned as a top-level NetworkObject — no nested NO half-spawn risk."
- Any gotcha hit during validation (Task 6 reimport, Task 7 unexpected behavior)
- Reminder of the save-schema rule: don't move Furniture children in shipped prefabs without a migration story.
