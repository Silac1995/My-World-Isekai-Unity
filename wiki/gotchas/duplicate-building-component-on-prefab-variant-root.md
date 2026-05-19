---
type: gotcha
title: "Duplicate Building component on prefab-variant root crashes the Editor on placement"
tags: [building, prefab-variant, networking, ngo, requirecomponent, native-crash, editor-crash]
created: 2026-05-19
updated: 2026-05-19
sources: []
related:
  - "[[building]]"
  - "[[building-hierarchy]]"
  - "[[network]]"
  - "[[no-nested-networkobject-in-runtime-spawned-prefab]]"
  - "[[kevin]]"
status: mitigated
confidence: high
---

# Duplicate Building component on prefab-variant root crashes the Editor on placement

## Summary
Adding any of `Building`, `FurnitureGrid`, `FurnitureManager`, `BuildingInteractable`, or `ConstructionSiteScanner` to the **root** `Building_prefab.prefab` or to a **Tier-1 type base** (`CommercialBuilding_prefab.prefab`, `House prefab.prefab`) — even with the intent to "lift shared scripts up" per CLAUDE.md rule #40 — creates **two `Building`-chain `NetworkBehaviour` components** on every Tier-2 variant. When the building is spawned (placement, save-load, dev-spawn), `Building.Awake` → `ConvertNestedNetworkFurnitureToLayout` runs twice on the same hierarchy; the second invocation `DestroyImmediate`s already-destroyed nested NetworkObject children → null deref inside Unity's native scene-graph code → **whole Editor crashes** (no managed exception, recovery scenes get auto-saved to `Assets/_Recovery/`). The user-visible symptom is "the game crashes the moment I place a building."

The lift is **structurally impossible** without source code changes because of a transitive `[RequireComponent]` chain — see "Root cause" below.

## Symptom
- Pressing the place-building button (or any dev-spawn that triggers `NetworkObject.Spawn` of a Building) **closes the Unity Editor** with no managed exception.
- `Assets/_Recovery/0 (N).unity` files start accumulating — one per crash. Five recovery files in 20 minutes is the canonical signature.
- Editor log (`%LOCALAPPDATA%\Unity\Editor\Editor-prev.log`) shows `Building:Awake` firing **twice per placement attempt** (search the tail for repeated `Building:Awake () (at Assets/Scripts/World/Buildings/Building.cs:334)` entries), followed by `Cleanup mono` + `abort_threads: Failed aborting id` (Unity's post-crash teardown).
- Just before the crash, `Room.Awake` may log `<color=red>[Room]</color> <X> requires a BoxCollider to define its area and initialize the FurnitureGrid.` — that's the empty inherited `FurnitureGrid` failing to bind because the root has no `BoxCollider` of its own.
- Late-joiner / NGO stability: in extreme cases the host log shows `Unity.Netcode.NetworkSpawnManager:OnDespawnObject` / `DespawnAndDestroyNetworkObjects` firing in a tight loop as NGO desperately tears down the broken NetworkObject.

## Root cause
The Building component hierarchy is **polymorphic via `NetworkBehaviour`**:

- `Building` extends `ComplexRoom` extends `Room` extends `Zone` extends `NetworkBehaviour`.
- `Zone` declares `[RequireComponent(typeof(BoxCollider))]` and `[RequireComponent(typeof(NavMeshModifierVolume))]`.
- `Room` declares `[RequireComponent(typeof(FurnitureGrid))]` and `[RequireComponent(typeof(FurnitureManager))]`.
- `ConstructionSiteScanner` and `BuildingInteractable` both declare `[RequireComponent(typeof(Building))]`.
- `CommercialBuilding : Building` declares `[RequireComponent(typeof(BuildingTaskManager))]` and `[RequireComponent(typeof(BuildingLogisticsManager))]`.
- Concrete subclasses on Tier-2 variants — `ShopBuilding`, `ForgeBuilding`, `FarmingBuilding`, `HarvestingBuilding`, `TransporterBuilding`, `AdministrativeBuilding` — each `: CommercialBuilding`.

The intended Prefab Variant chain is `Building_prefab.prefab` (root) → `CommercialBuilding_prefab.prefab` (Tier-1 type base) → `Shop.prefab` / `Forge.prefab` / etc. (Tier-2). Per CLAUDE.md rule #40, scripts shared across all Tier-2 variants "should" live on the Tier-1 base.

But the `Building`-subclass identity (`ShopBuilding`, etc.) lives on the **Tier-2 variants** — it has to, because each shop type needs its own subclass. If you add `BuildingInteractable` or `ConstructionSiteScanner` to the Tier-1 base, Unity's editor satisfies `[RequireComponent(typeof(Building))]` by **auto-adding a separate base `Building` component to the Tier-1 base prefab**. Now every Tier-2 variant inherits that base `Building` from Tier-1 **and** carries its own `ShopBuilding` subclass. Both are `Building` in the assignability sense; both are separate `NetworkBehaviour` instances on the same GameObject.

At spawn time:
1. `Building.Awake` (on the inherited base `Building` component) calls `base.Awake()` (Room/Zone init) then `ConvertNestedNetworkFurnitureToLayout()` — which walks the building's nested NetworkObject children and `DestroyImmediate`s each.
2. `ShopBuilding.Awake` (on the subclass) does the same — `base.Awake()` → `ConvertNestedNetworkFurnitureToLayout()`. The nested children have already been destroyed by step 1.
3. The second pass iterates over a list referencing destroyed-but-not-yet-cleared GameObjects. The cascade is into native Unity code (Transform/SceneGraph), not managed, so the crash leaves no managed stack trace — just `Crash!!!` in the Player log (or silent Editor termination).

The same pattern fires if you put `Building` (not a subclass) directly on the root `Building_prefab.prefab`: every Tier-1 type base (`CommercialBuilding_prefab`, `House prefab`) and every Tier-2 variant inherits that base `Building` in addition to its own Building/CommercialBuilding/ShopBuilding/etc. — guaranteed duplicate.

The 2026-05-19 incident triggered this two ways simultaneously:
- User added `Building` (plus `FurnitureGrid` / `FurnitureManager` / `NavMeshModifierVolume` / `BuildingInteractable` / `ConstructionSiteScanner`) directly to root `Building_prefab.prefab` — with `_blueprint: {fileID: 0}` (null), `_buildingZone: {fileID: 0}`, `_deliveryZone: {fileID: 0}`. Null `_blueprint` then made the duplicate `ConvertNestedNetworkFurnitureToLayout` pass dereference null inside Building's instance state, accelerating the native crash.
- User had also stripped `FurnitureGrid` / `FurnitureManager` / `BuildingInteractable` / `ConstructionSiteScanner` / `BuildingTaskManager` from every Tier-2 commercial variant, planning to inherit them from the lifted-up parent. Tier-2 variants ended up **simultaneously broken two ways**: duplicate `Building` from the root + missing required scripts on themselves.

## How to avoid
**Never put any of these scripts on `Building_prefab.prefab` (root) or on a Tier-1 type base (`CommercialBuilding_prefab.prefab`, `House prefab.prefab`):**

- `Building` (and any subclass like `CommercialBuilding`, `ShopBuilding`, `FarmingBuilding`, …)
- `FurnitureGrid`
- `FurnitureManager`
- `BuildingInteractable`
- `ConstructionSiteScanner`
- `BuildingTaskManager`
- `BuildingLogisticsManager`
- `BoxCollider` (Zone requirement)
- `NavMeshModifierVolume` (Zone requirement)

These scripts belong on the **Tier-2 specific variant** (the actual `Shop.prefab` / `Forge.prefab` / `Small house.prefab` / etc.). Each variant carries its own copy. Yes, this means "shared" scripts are duplicated across variants. **That is correct.** The duplication is *content* duplication (each variant has its own configured instance), not a *single behaviour shared across all variants* — Unity's `[RequireComponent]` chain plus polymorphism plus NGO's `NetworkBehaviour`-index assignment forces this shape.

**What `CommercialBuilding_prefab.prefab` (Tier-1) should contain at HEAD:** an empty `m_AddedComponents: []` for every nested transform. It exists purely as an inheritance anchor that gives Tier-2 commercial variants a common naming/folder root, not to carry behaviour.

**What `Building_prefab.prefab` (root) should contain:** only the most-baseline shared things that are truly the same across every building variant — currently `NetworkObject` (inherited by every variant), `BuildingConstructionOutline` (the scaffold visual). No `Building` script, no FurnitureGrid/FurnitureManager, no Interactable/Scanner.

**Litmus test before adding a component to root or Tier-1:** grep the script for `[RequireComponent]`. If the chain transitively requires `Building` OR if the script itself extends `Building`/`Zone`/`Room`/`ComplexRoom`, **do not add it to a parent prefab** — every Tier-2 variant has its own Building-chain instance and adding it higher creates duplicates.

**If you genuinely want to "lift" these scripts up someday:** the only safe path is to first **remove the `[RequireComponent(typeof(Building))]` attributes** from `BuildingInteractable.cs:20` and `ConstructionSiteScanner.cs:16` (and refactor those scripts to resolve `Building` via runtime `GetComponentInParent`). Then the lift becomes mechanically possible, but it's still a multi-prefab coordinated refactor (must also strip the duplicate copies from every Tier-2 variant in the same change), and saves break because NetworkBehaviour indices shift. **Not worth the effort** — the current per-variant pattern works and is consistent with the polymorphic shape.

## How to fix (if you already triggered the crash)
The 2026-05-19 incident recovery is the canonical recipe:

1. **Diagnose** — confirm the failure shape via:
   ```bash
   git diff --stat -- Assets/Prefabs/Building/
   git diff -- Assets/Prefabs/Building/Building_prefab.prefab | head -100
   ```
   Look for added `m_EditorClassIdentifier: Assembly-CSharp::Building` (or `FurnitureGrid` / `FurnitureManager` / `BuildingInteractable` / `ConstructionSiteScanner`) on the root or on a Tier-1 type base. Also check `Assets/_Recovery/` mtimes — clusters within minutes confirm repeated crashes.

2. **Save a snapshot patch** of the user's in-progress restructure so it isn't lost forever:
   ```bash
   mkdir -p .claude/snapshots
   git diff HEAD -- "Assets/Prefabs/Building/Building_prefab.prefab" "Assets/Prefabs/Building/House/House prefab.prefab" "Assets/Prefabs/Building/Commercial/CommercialBuilding_prefab.prefab" > ".claude/snapshots/$(date +%Y-%m-%d)-building-root-restructure.patch"
   ```

3. **Revert the root + the Tier-1 type bases** to HEAD — these are the prefabs that cascade duplicates to every variant:
   ```bash
   git checkout HEAD -- "Assets/Prefabs/Building/Building_prefab.prefab"
   git checkout HEAD -- "Assets/Prefabs/Building/House/House prefab.prefab"
   git checkout HEAD -- "Assets/Prefabs/Building/Commercial/CommercialBuilding_prefab.prefab"
   ```

4. **Restore the per-variant scripts** the user stripped from each Tier-2 commercial variant. Counts vary per variant — diff each against HEAD to know which scripts went missing. Restore via Roslyn (`script-execute` → `PrefabUtility.LoadPrefabContents` → idempotent `AddComponent` for each missing script → `PrefabUtility.SaveAsPrefabAsset` → `UnloadPrefabContents`). For the 2026-05-19 incident the restoration list was:

   | Tier-2 variant | Scripts to re-add on root |
   |---|---|
   | `Shop/Shop.prefab` | `BuildingTaskManager`, `FurnitureGrid`, `FurnitureManager`, `ConstructionSiteScanner`, `BuildingInteractable` |
   | `Crafting/Forge.prefab` | same 5 |
   | `Farm/Farming Building.prefab` | same 5 |
   | `Harvesting/Lumberyard/Lumberyard.prefab` | same 5 |
   | `Transporter/Transporter Building.prefab` | same 5 |
   | `Administrative/AdministrativeBuilding.prefab` | `BuildingTaskManager`, `FurnitureGrid`, `FurnitureManager` only (this variant historically has no `BuildingInteractable` / `ConstructionSiteScanner`) |
   | `Shop/Clothing Shop.prefab`, `Administrative/City hall.prefab`, `Farm/Farming Building Variant.prefab` | **skip** — these are Tier-3 variants of the Tier-2 above; they inherit, never define their own |

5. **Verify by counting `m_EditorClassIdentifier` definitions per file** against HEAD (`grep -c "::<sym>$"`); the goal is `NOW == HEAD` per script per file. Differences mean either the restore missed a slot or you accidentally added one (e.g. via a `[RequireComponent]` chain that pulled in `Building` to a parent prefab — back to square one).

6. **Do NOT use `AddComponent<Building>` in the Roslyn restoration script** — verify each Tier-2 variant ends up with exactly 1 component in `GetComponents<Building>()` (the subclass). 2 means you triggered the duplicate; abandon that prefab's save and investigate which `[RequireComponent]` chain pulled `Building` in. If you wrote a "skip if duplicate" guard, remember that Tier-2 variants have always had exactly 1 Building-chain component at HEAD (the subclass) — `> 1` is the bug, not the norm.

7. **`Assets → Refresh`** in the Editor (Ctrl+R) after writing prefabs via Roslyn / MCP so Unity's `AssetDatabase` picks up the file changes. The `AssetDatabase` can otherwise hold stale references and cause Round-1-skipped-Round-2-modified spurious behaviour.

8. **Test placement** of one of each type (House, Shop, Forge, Farming, Lumberyard, Transporter, Administrative). All should place cleanly. If any still crashes, re-grep its file for duplicate component definitions and re-check the [RequireComponent] chain.

## Affected systems
- [[building]] — the Building/ComplexRoom/Room/Zone hierarchy and its `[RequireComponent]` chain are the structural cause.
- [[building-hierarchy]] — Tier-2 variants carry the concrete Building subclass; lifting it to Tier-1 is the anti-pattern.
- Every commercial Tier-2 variant — `Shop`, `Clothing Shop`, `Forge`, `Farming Building`, `Lumberyard`, `Transporter Building`, `AdministrativeBuilding`, `City hall`, `Farming Building Variant`.
- Every house Tier-2 variant — `Small house`, `Medium house`.
- Any future Tier-2 building variant: same constraint.

## Links
- [[building]] — system overview; the `## Prefab Variant Hierarchy` section there now points back to this gotcha.
- [[building-hierarchy]] — `Zone → Room → ComplexRoom → Building` chain.
- [[no-nested-networkobject-in-runtime-spawned-prefab]] — sibling constraint about furniture children.
- [[host-progressive-freeze-debug-log-spam.md]] — adjacent native-crash pattern (different root cause).
- [CLAUDE.md rule #40](../../CLAUDE.md) — the Prefab Variant hierarchy rule itself, now annotated with this constraint.

## Sources
- 2026-05-19 conversation with Kevin — placement crashed the entire Editor after he added `Building` + 5 sibling scripts to the root `Building_prefab.prefab` and stripped the corresponding scripts from every Tier-2 commercial variant; full recovery procedure executed and validated.
- [Assets/Scripts/World/Buildings/Building.cs:332-356](../../Assets/Scripts/World/Buildings/Building.cs) — `Building.Awake` → `base.Awake()` → `ConvertNestedNetworkFurnitureToLayout()`; the second invocation on duplicate components is the crash trigger.
- [Assets/Scripts/World/Buildings/Construction/BuildingInteractable.cs:20](../../Assets/Scripts/World/Buildings/Construction/BuildingInteractable.cs) — `[RequireComponent(typeof(Building))]`.
- [Assets/Scripts/World/Buildings/Construction/ConstructionSiteScanner.cs:16](../../Assets/Scripts/World/Buildings/Construction/ConstructionSiteScanner.cs) — `[RequireComponent(typeof(Building))]`.
- [Assets/Scripts/World/Buildings/Rooms/Room.cs:9-10](../../Assets/Scripts/World/Buildings/Rooms/Room.cs) — `[RequireComponent(typeof(FurnitureGrid))]` + `[RequireComponent(typeof(FurnitureManager))]`.
- [Assets/Scripts/World/Zones/Zone.cs:7-8](../../Assets/Scripts/World/Zones/Zone.cs) — `[RequireComponent(typeof(BoxCollider))]` + `[RequireComponent(typeof(NavMeshModifierVolume))]`.
- [Assets/Scripts/World/Buildings/CommercialBuilding.cs:13-14](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) — `[RequireComponent(typeof(BuildingTaskManager))]` + `[RequireComponent(typeof(BuildingLogisticsManager))]`.
- `.claude/snapshots/2026-05-19-building-root-restructure.patch` (in the worktree) — the 366-line patch of the original restructure attempt, kept as a reference if a future cleaner version is ever attempted (after the [RequireComponent] attributes are removed first).
