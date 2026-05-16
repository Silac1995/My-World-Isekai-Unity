# Building SO + Commercial Treasury Seed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the inline `WorldSettingsData.BuildingRegistry` (a `List<BuildingRegistryEntry>` struct) with a per-building `BuildingSO` ScriptableObject hierarchy, add a `BuildingCommercialSO` subclass carrying a `BaseTreasury` seed amount, add a `NativeCurrency` field on `CommunityData`, and credit each commercial building's Treasury safe once at construction-complete with `BaseTreasury` in the local map's `NativeCurrency` (or `CurrencyId.Default` when there is no enclosing map).

**Architecture:**
- Lift building blueprint data (PrefabId, BuildingName, Icon, BuildingPrefab, InteriorPrefab, CommunityPriority, BuildingType, ConstructionRequirements, DefaultFurnitureLayout) from inline `BuildingRegistryEntry` rows + duplicated `Building.cs` prefab fields into a single `BuildingSO` asset per building.
- `Building.cs` holds `[SerializeField] BuildingSO _blueprint` and exposes blueprint fields via getters (deleting the duplicated prefab fields in the same commit to keep `DeliveredMaterials.RequirementIndex` positional contract valid).
- `WorldSettingsData.BuildingRegistry` becomes `List<BuildingSO>`. PrefabId strings preserved verbatim → zero save migration. Lookup methods scan the SO list.
- `CommunityData.NativeCurrency` (new `CurrencyId` field) drives the seed currency at construction-complete time.
- `CommercialBuilding.OnDefaultFurnitureSpawned()` override calls `CreditTreasury` once, gated by a server-only `_treasurySeeded` flag persisted via `BuildingSaveData.TreasurySeeded` to survive save/reload re-credit.

**Tech Stack:** Unity 6.0 / NGO 2.x, C# 9, NUnit EditMode tests via `tests-run` MCP tool, ScriptableObject assets in `Assets/Resources/Data/Buildings/`, JSON save format.

**Rules enforced throughout:** CLAUDE.md rules #1, #2, #6 (correctness over speed), #15 (`_underscorePrefix`), #18/#19/#19b (server-authoritative + late-joiner audit), #28/#29/#29b (SKILL + agent + wiki updates), #31 (try/catch around the credit), #34 (no per-frame allocations, gate logs).

---

## File Structure

**New files:**
- `Assets/Scripts/World/Data/BuildingSO.cs` — base ScriptableObject.
- `Assets/Scripts/World/Data/BuildingCommercialSO.cs` — subclass with `BaseTreasury`.
- `Assets/Editor/BuildingRegistryToBuildingSOMigration.cs` — one-shot migration tool.
- `Assets/Resources/Data/Buildings/*.asset` — one SO per existing registry entry (created by migration).
- `Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs` — EditMode test.
- `Assets/Editor/Tests/Buildings/TreasurySeedIdempotencyTests.cs` — EditMode test.

**Modified files:**
- `Assets/Scripts/World/Data/WorldSettingsData.cs` — registry shape + lookups.
- `Assets/Scripts/World/MapSystem/MapRegistry.cs` — `CommunityData.NativeCurrency` + `BuildingSaveData.TreasurySeeded`.
- `Assets/Scripts/World/MapSystem/MapController.cs` — `NativeCurrency` convenience getter (server side).
- `Assets/Scripts/World/Buildings/Building.cs` — `_blueprint` field, deleted prefab fields, derived properties, save round-trip wires.
- `Assets/Scripts/World/Buildings/CommercialBuildings/CommercialBuilding.cs` — `OnDefaultFurnitureSpawned` override, `_treasurySeeded` flag, save round-trip.
- `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs` — placement entry point uses `BuildingSO` instead of `PrefabId` string.
- `Assets/Scripts/World/Buildings/BuildingInteriorRegistry.cs` — `GetInteriorPrefab` callers.
- `Assets/Scripts/World/MapSystem/MacroSimulator.cs` — registry iteration + sort.
- `Assets/Scripts/Character/Components/CharacterMapTracker.cs` — interior-prefab lookup.
- `Assets/Scripts/UI/Building/UI_BuildingPlacementMenu.cs` — enumerate `List<BuildingSO>`.
- `Assets/Scripts/UI/Building/UI_BuildingEntry.cs` — bind from `BuildingSO`.
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingOverviewSubTab.cs` — blueprint read-out.
- All 7 building prefabs in `Assets/Prefabs/Building/*` (and any subfolders) — set `_blueprint` field, clear deleted fields.

**Docs updated:**
- `wiki/systems/building.md` — blueprint section + Sources.
- `wiki/systems/commercial-building.md` — BaseTreasury seed flow.
- `wiki/systems/commercial-treasury.md` — seed source documented.
- `wiki/systems/construction.md` — `OnDefaultFurnitureSpawned` hook noted as seeding entry-point.
- `.agent/skills/buildings/SKILL.md` — `BuildingSO` API surface.
- `.claude/agents/building-furniture-specialist.md` — domain refresh.

---

## Task 1: Add `NativeCurrency` field to `CommunityData`

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapRegistry.cs:577-678` (CommunityData class body)
- Test: `Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs`:

```csharp
using NUnit.Framework;
using MWI.Economy;
using MWI.WorldSystem;
using UnityEngine;

namespace MWI.Tests.Buildings
{
    public class CommunityDataNativeCurrencyTests
    {
        [Test]
        public void CommunityData_default_NativeCurrency_is_CurrencyId_Default()
        {
            var c = new CommunityData();
            Assert.AreEqual(CurrencyId.Default, c.NativeCurrency,
                "Brand-new CommunityData must default NativeCurrency to CurrencyId.Default so legacy saves load with sane behaviour.");
        }

        [Test]
        public void CommunityData_NativeCurrency_round_trips_through_JsonUtility()
        {
            var c = new CommunityData { MapId = "test-map", NativeCurrency = new CurrencyId(42) };
            var json = JsonUtility.ToJson(c);
            var back = JsonUtility.FromJson<CommunityData>(json);
            Assert.AreEqual(new CurrencyId(42), back.NativeCurrency);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Use the `tests-run` MCP tool with `testMode: EditMode`, filter `MWI.Tests.Buildings.CommunityDataNativeCurrencyTests`.
Expected: FAIL with "CommunityData does not contain a definition for NativeCurrency".

- [ ] **Step 3: Add the field**

In `Assets/Scripts/World/MapSystem/MapRegistry.cs`, inside `public class CommunityData` (around line 583, after `public Vector2Int OriginChunk;`):

```csharp
        /// <summary>
        /// The currency used by this community. Drives BuildingCommercialSO.BaseTreasury
        /// seeding at construction-complete time (see CommercialBuilding.OnDefaultFurnitureSpawned).
        /// Defaults to CurrencyId.Default so legacy saves (no field) deserialize cleanly.
        /// Designer-editable per-community on the CommunityData inspector surface.
        /// </summary>
        public MWI.Economy.CurrencyId NativeCurrency = MWI.Economy.CurrencyId.Default;
```

- [ ] **Step 4: Run test to verify it passes**

Run `MWI.Tests.Buildings.CommunityDataNativeCurrencyTests` via `tests-run`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MapRegistry.cs Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs
git commit -m "feat(world): add CommunityData.NativeCurrency for treasury seed"
```

---

## Task 2: Add `MapController.NativeCurrency` convenience getter

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapController.cs` (add property near top of class, after `MapId`)
- Test: `Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs` (append)

- [ ] **Step 1: Write the failing test**

Append to `BuildingSOSaveRoundTripTests.cs`:

```csharp
    public class MapControllerNativeCurrencyTests
    {
        [Test]
        public void MapController_NativeCurrency_falls_back_to_Default_when_no_community()
        {
            // Headless: spawn a bare MapController GameObject without a CommunityData entry.
            var go = new GameObject("TestMap");
            var map = go.AddComponent<MapController>();
            try
            {
                Assert.AreEqual(CurrencyId.Default, map.NativeCurrency,
                    "MapController without a registered CommunityData must return CurrencyId.Default.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run `MWI.Tests.Buildings.MapControllerNativeCurrencyTests`.
Expected: FAIL with "MapController does not contain a definition for NativeCurrency".

- [ ] **Step 3: Add the property**

In `Assets/Scripts/World/MapSystem/MapController.cs`, immediately after the line `public string MapId;` (line 17):

```csharp
        /// <summary>
        /// Convenience accessor for the community's NativeCurrency. Returns CurrencyId.Default
        /// when this MapController has no registered CommunityData yet (e.g., during scene
        /// boot before MapRegistry.Start runs, or for dynamic maps mid-creation).
        /// </summary>
        public MWI.Economy.CurrencyId NativeCurrency
        {
            get
            {
                if (MapRegistry.Instance == null) return MWI.Economy.CurrencyId.Default;
                if (string.IsNullOrEmpty(MapId)) return MWI.Economy.CurrencyId.Default;
                var comm = MapRegistry.Instance.GetCommunity(MapId);
                return comm != null ? comm.NativeCurrency : MWI.Economy.CurrencyId.Default;
            }
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run `MWI.Tests.Buildings.MapControllerNativeCurrencyTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MapController.cs Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs
git commit -m "feat(world): expose MapController.NativeCurrency convenience getter"
```

---

## Task 3: Create `BuildingSO` base ScriptableObject

**Files:**
- Create: `Assets/Scripts/World/Data/BuildingSO.cs`
- Test: `Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs` (append)

- [ ] **Step 1: Write the failing test**

Append to `BuildingSOSaveRoundTripTests.cs`:

```csharp
    public class BuildingSOAuthoringTests
    {
        [Test]
        public void BuildingSO_default_fields_are_safe()
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            try
            {
                Assert.IsTrue(string.IsNullOrEmpty(so.PrefabId), "PrefabId must default to empty (designer must author it explicitly).");
                Assert.AreEqual(0, so.CommunityPriority);
                Assert.IsNull(so.BuildingPrefab);
                Assert.IsNull(so.InteriorPrefab);
                Assert.IsNull(so.Icon);
                Assert.IsNotNull(so.ConstructionRequirements);
                Assert.AreEqual(0, so.ConstructionRequirements.Count);
                Assert.IsNotNull(so.DefaultFurnitureLayout);
                Assert.AreEqual(0, so.DefaultFurnitureLayout.Count);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run `MWI.Tests.Buildings.BuildingSOAuthoringTests`.
Expected: FAIL with "The type or namespace name 'BuildingSO' could not be found".

- [ ] **Step 3: Create the SO**

Create `Assets/Scripts/World/Data/BuildingSO.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Blueprint ScriptableObject for a single building type. Replaces the inline
    /// BuildingRegistryEntry struct on WorldSettingsData and the duplicated prefab
    /// fields on Building.cs (BuildingName / BuildingType / ConstructionRequirements /
    /// DefaultFurnitureLayout). One asset per building type, authored under
    /// Assets/Resources/Data/Buildings/.
    ///
    /// PrefabId is the cross-session identity key (matches the string written into
    /// BuildingSaveData.PrefabId). Must be preserved verbatim across the migration
    /// from BuildingRegistryEntry — renaming silently invalidates every existing save.
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingSO", menuName = "MWI/World/BuildingSO", order = 100)]
    public class BuildingSO : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable cross-session key (e.g. 'Shop_Armor_A'). Persisted into BuildingSaveData.PrefabId. NEVER rename — breaks every save that references this building type.")]
        [SerializeField] private string _prefabId;

        [Tooltip("Designer-facing display name. Falls back to GameObject name if blank.")]
        [SerializeField] private string _buildingName;

        [SerializeField] private Sprite _icon;

        [SerializeField] private BuildingType _buildingType = BuildingType.Residential;

        [Header("Prefabs")]
        [Tooltip("The networked Building prefab spawned when this type is placed.")]
        [SerializeField] private GameObject _buildingPrefab;

        [Tooltip("Optional interior MapController prefab. Null for non-enterable buildings.")]
        [SerializeField] private GameObject _interiorPrefab;

        [Header("Community")]
        [Tooltip("Higher = community leaders auto-build this first when offline auto-build fires (MacroSimulator.SimulateCityGrowth). Single int sorted descending.")]
        [SerializeField] private int _communityPriority;

        [Header("Construction")]
        [Tooltip("Items + amounts that must be delivered to finish construction. Order is the positional index used by BuildingSaveData.DeliveredMaterials — never reorder existing entries (would corrupt in-flight construction saves). Append only.")]
        [SerializeField] private List<CraftingIngredient> _constructionRequirements = new List<CraftingIngredient>();

        [Header("Default Furniture")]
        [Tooltip("Layout spawned by the server on first construction-complete (and not on save-restore). Mirrors the legacy Building._defaultFurnitureLayout authoring surface.")]
        [SerializeField] private List<Building.DefaultFurnitureSlot> _defaultFurnitureLayout = new List<Building.DefaultFurnitureSlot>();

        public string PrefabId => _prefabId;
        public string BuildingName => _buildingName;
        public Sprite Icon => _icon;
        public BuildingType BuildingType => _buildingType;
        public GameObject BuildingPrefab => _buildingPrefab;
        public GameObject InteriorPrefab => _interiorPrefab;
        public int CommunityPriority => _communityPriority;
        public IReadOnlyList<CraftingIngredient> ConstructionRequirements => _constructionRequirements;
        public IReadOnlyList<Building.DefaultFurnitureSlot> DefaultFurnitureLayout => _defaultFurnitureLayout;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run `MWI.Tests.Buildings.BuildingSOAuthoringTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Data/BuildingSO.cs Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs
git commit -m "feat(world): add BuildingSO blueprint ScriptableObject base"
```

---

## Task 4: Create `BuildingCommercialSO` subclass with `BaseTreasury`

**Files:**
- Create: `Assets/Scripts/World/Data/BuildingCommercialSO.cs`
- Test: `Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs` (append)

- [ ] **Step 1: Write the failing test**

Append to `BuildingSOSaveRoundTripTests.cs`:

```csharp
    public class BuildingCommercialSOTests
    {
        [Test]
        public void BuildingCommercialSO_default_BaseTreasury_is_zero()
        {
            var so = ScriptableObject.CreateInstance<BuildingCommercialSO>();
            try { Assert.AreEqual(0, so.BaseTreasury); }
            finally { Object.DestroyImmediate(so); }
        }

        [Test]
        public void BuildingCommercialSO_is_assignable_to_BuildingSO_field()
        {
            var so = ScriptableObject.CreateInstance<BuildingCommercialSO>();
            try
            {
                BuildingSO asBase = so;
                Assert.IsNotNull(asBase, "Subclass must be substitutable for base (SOLID LSP — rule #11).");
            }
            finally { Object.DestroyImmediate(so); }
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run `MWI.Tests.Buildings.BuildingCommercialSOTests`.
Expected: FAIL with "The type or namespace name 'BuildingCommercialSO' could not be found".

- [ ] **Step 3: Create the subclass**

Create `Assets/Scripts/World/Data/BuildingCommercialSO.cs`:

```csharp
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Commercial-flavoured BuildingSO blueprint. Adds the BaseTreasury seed amount
    /// that <see cref="CommercialBuilding.OnDefaultFurnitureSpawned"/> credits into
    /// the building's Treasury-role SafeFurniture at construction-complete time.
    /// Currency is resolved at credit time from the enclosing MapController's
    /// NativeCurrency (or CurrencyId.Default when there is no enclosing map).
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingCommercialSO", menuName = "MWI/World/BuildingCommercialSO", order = 101)]
    public class BuildingCommercialSO : BuildingSO
    {
        [Header("Commercial — Treasury Seed")]
        [Tooltip("Amount credited into the Treasury safe ONCE at construction-complete. Currency is resolved at that moment from CommunityData.NativeCurrency (or CurrencyId.Default when no enclosing community exists). Persisted via BuildingSaveData.TreasurySeeded so a save/reload does not re-credit.")]
        [Min(0)]
        [SerializeField] private int _baseTreasury;

        public int BaseTreasury => _baseTreasury;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run `MWI.Tests.Buildings.BuildingCommercialSOTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Data/BuildingCommercialSO.cs Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs
git commit -m "feat(world): add BuildingCommercialSO with BaseTreasury seed"
```

---

## Task 5: Add `_blueprint` field on `Building.cs` and derive properties

This task is in-place but compile-only — no test added at this step because Step 6 (Task 6) immediately deletes the legacy fields and the EditMode compile pass is the verification.

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Building.cs:48-77` (Header block + duplicated fields)
- Modify: `Assets/Scripts/World/Buildings/Building.cs:153-227` (Identity + SupportsInterior)

- [ ] **Step 1: Add `_blueprint` SerializeField above the Building Info header**

In `Assets/Scripts/World/Buildings/Building.cs`, immediately above line 48 (`[Header("Building Info")]`):

```csharp
    [Header("Blueprint")]
    [Tooltip("The BuildingSO that authored this building. Source of truth for PrefabId, BuildingName, BuildingType, ConstructionRequirements, DefaultFurnitureLayout. Set on every authored prefab; clones inherit it via Inspector serialization.")]
    [SerializeField] protected BuildingSO _blueprint;

    public BuildingSO Blueprint => _blueprint;
```

- [ ] **Step 2: Replace the duplicated identity getters with blueprint-derived properties**

In `Assets/Scripts/World/Buildings/Building.cs`, replace lines 172-178 (`public string BuildingName ...` through `public string PrefabId ...`) with:

```csharp
    public string BuildingName =>
        _blueprint != null && !string.IsNullOrEmpty(_blueprint.BuildingName)
            ? _blueprint.BuildingName
            : (string.IsNullOrEmpty(buildingName) ? name : buildingName);

    public virtual BuildingType BuildingType =>
        _blueprint != null ? _blueprint.BuildingType : _buildingType;

    public bool IsPublicLocation => _isPublicLocation;
    public Collider BuildingZone => _buildingZone;
    public Zone DeliveryZone => _deliveryZone;

    public string PrefabId =>
        _blueprint != null ? _blueprint.PrefabId : _prefabId;

    public string BuildingId => NetworkBuildingId.Value.ToString();
```

Note: we KEEP the legacy `_prefabId`, `buildingName`, `_buildingType`, `_constructionRequirements`, `_defaultFurnitureLayout` fields for one transitional commit so the migration step (Task 11) can read from them. The deletion happens in Task 6.

- [ ] **Step 3: Update `SupportsInterior` to prefer blueprint**

Replace lines 218-227 (`public bool SupportsInterior { ... }`):

```csharp
    public bool SupportsInterior
    {
        get
        {
            if (_blueprint != null) return _blueprint.InteriorPrefab != null;
            if (string.IsNullOrEmpty(_prefabId)) return false;
            var settings = GetCachedWorldSettings();
            if (settings == null) return false;
            return settings.GetInteriorPrefab(_prefabId) != null;
        }
    }
```

- [ ] **Step 4: Update `GetPendingMaterials` and `ComputeProgress` to read requirements from blueprint when present**

Find `_constructionRequirements` references at lines 1242, 1216 (`ComputeProgress`), 1568-1574 (`GetPendingMaterials`). Introduce a helper near the top of the class:

```csharp
    protected IReadOnlyList<CraftingIngredient> EffectiveConstructionRequirements =>
        _blueprint != null && _blueprint.ConstructionRequirements != null && _blueprint.ConstructionRequirements.Count > 0
            ? _blueprint.ConstructionRequirements
            : (IReadOnlyList<CraftingIngredient>)_constructionRequirements;
```

Replace each `_constructionRequirements` read site (NOT writes — _contributedMaterials writes are unchanged) with `EffectiveConstructionRequirements`. Concretely:

- `Building.cs:1242` (`foreach (var req in _constructionRequirements)` inside GetPendingMaterials) → `foreach (var req in EffectiveConstructionRequirements)`.
- `Building.cs:1216` and any other read site found by Ctrl+F on `_constructionRequirements` that is NOT an assignment.

Leave the inline `_constructionRequirements` field intact in this commit — Task 6 deletes it.

- [ ] **Step 5: Wire `_defaultFurnitureLayout` to read from blueprint when populated**

Find `_defaultFurnitureLayout` references in `TrySpawnDefaultFurniture`. Add a helper:

```csharp
    protected IReadOnlyList<DefaultFurnitureSlot> EffectiveDefaultFurnitureLayout =>
        _blueprint != null && _blueprint.DefaultFurnitureLayout != null && _blueprint.DefaultFurnitureLayout.Count > 0
            ? _blueprint.DefaultFurnitureLayout
            : (IReadOnlyList<DefaultFurnitureSlot>)_defaultFurnitureLayout;
```

Update every read of `_defaultFurnitureLayout` inside `TrySpawnDefaultFurniture` (around line 700+) and the corresponding `ConvertNestedNetworkFurnitureToLayout` block to use `EffectiveDefaultFurnitureLayout` for reads. Writes from `ConvertNestedNetworkFurnitureToLayout` continue to append to the legacy `_defaultFurnitureLayout` (runtime-only mutation, prefab asset unaffected).

- [ ] **Step 6: Compile check**

Use the `console-get-logs` MCP tool after triggering a Unity domain reload (e.g., `assets-refresh`).
Expected: no compilation errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/World/Buildings/Building.cs
git commit -m "refactor(building): introduce _blueprint SerializeField with derived properties"
```

---

## Task 6: Delete the now-duplicated legacy fields on `Building.cs`

This task lands in the SAME PR as Task 5 (already committed separately for atomic-rollback safety) and BEFORE Task 11 (migration). Task 11's migration script must copy data OUT of these fields BEFORE this task lands — see Task 11's ordering note.

**Skip this task until Task 11 (Migration) has run on every existing prefab.** If you arrive here before Task 11 has executed, jump to Task 11 first.

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Building.cs` (delete fields)

- [ ] **Step 1: Delete the duplicated fields**

In `Assets/Scripts/World/Buildings/Building.cs`:

1. Delete line 49: `[SerializeField] protected string buildingName;`
2. Delete line 53: `[SerializeField] protected BuildingType _buildingType = BuildingType.Residential;`
3. Delete line 61: `[SerializeField] protected List<CraftingIngredient> _constructionRequirements = ...`
4. Delete lines 128-135: the `_defaultFurnitureLayout` block + its `[FormerlySerializedAs]` attribute.
5. Delete line 154: `[SerializeField] protected string _prefabId;`

- [ ] **Step 2: Update the property fallback chain to no longer reference deleted fields**

Replace `BuildingName`:

```csharp
    public string BuildingName =>
        _blueprint != null && !string.IsNullOrEmpty(_blueprint.BuildingName)
            ? _blueprint.BuildingName
            : name;
```

Replace `BuildingType`:

```csharp
    public virtual BuildingType BuildingType =>
        _blueprint != null ? _blueprint.BuildingType : BuildingType.Residential;
```

Replace `PrefabId`:

```csharp
    public string PrefabId => _blueprint != null ? _blueprint.PrefabId : string.Empty;
```

Replace `SupportsInterior` (remove the legacy fallback branch):

```csharp
    public bool SupportsInterior =>
        _blueprint != null && _blueprint.InteriorPrefab != null;
```

Replace `EffectiveConstructionRequirements` (remove the legacy fallback):

```csharp
    protected IReadOnlyList<CraftingIngredient> EffectiveConstructionRequirements =>
        _blueprint != null ? _blueprint.ConstructionRequirements : System.Array.Empty<CraftingIngredient>();
```

Replace `EffectiveDefaultFurnitureLayout` (keep runtime-mutable list as the carrier — see ConvertNestedNetworkFurnitureToLayout requirement):

Restore a runtime-only field for `ConvertNestedNetworkFurnitureToLayout` to append into; it's no longer authored on the prefab, only mutated at runtime:

```csharp
    /// <summary>
    /// Runtime-only list. Initialised from <see cref="BuildingSO.DefaultFurnitureLayout"/>
    /// on first read by <see cref="EffectiveDefaultFurnitureLayout"/> if the blueprint
    /// has authored slots; otherwise stays empty. <see cref="ConvertNestedNetworkFurnitureToLayout"/>
    /// appends here for nested-prefab-converted slots. Not serialized — Inspector
    /// authoring moved to the BuildingSO asset.
    /// </summary>
    private List<DefaultFurnitureSlot> _runtimeDefaultFurnitureLayout;

    protected IReadOnlyList<DefaultFurnitureSlot> EffectiveDefaultFurnitureLayout
    {
        get
        {
            if (_runtimeDefaultFurnitureLayout == null)
            {
                _runtimeDefaultFurnitureLayout = new List<DefaultFurnitureSlot>();
                if (_blueprint != null && _blueprint.DefaultFurnitureLayout != null)
                {
                    for (int i = 0; i < _blueprint.DefaultFurnitureLayout.Count; i++)
                        _runtimeDefaultFurnitureLayout.Add(_blueprint.DefaultFurnitureLayout[i]);
                }
            }
            return _runtimeDefaultFurnitureLayout;
        }
    }
```

Update `ConvertNestedNetworkFurnitureToLayout` to append to `_runtimeDefaultFurnitureLayout` instead of the deleted `_defaultFurnitureLayout`. The first access lazily seeds from the blueprint, so appended runtime slots layer on top.

- [ ] **Step 3: Delete `GetCachedWorldSettings()` and `_cachedWorldSettings` static** if no longer referenced

These were only used by the legacy `SupportsInterior` fallback. Confirm via Ctrl+F — if zero remaining references, delete the static helper and its two backing fields (lines 233-243). If anything else (e.g., a future-proofing dev tool) still references it, leave intact.

- [ ] **Step 4: Compile check**

Trigger `assets-refresh`. Expected: no compilation errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Buildings/Building.cs
git commit -m "refactor(building): delete legacy duplicated fields; blueprint is the source of truth"
```

---

## Task 7: Introduce `List<BuildingSO> Blueprints` alongside legacy `BuildingRegistry`

**Critical migration safety note:** We cannot change `WorldSettingsData.BuildingRegistry` from `List<BuildingRegistryEntry>` to `List<BuildingSO>` in one shot — Unity treats a field-type change as a serialization break and silently empties the asset's stored YAML, losing every existing registry row. We split into three phases: (a) add a new field alongside the legacy one (this task), (b) migrate data into it (Task 11), (c) delete the legacy field and rename (Task 18).

**Files:**
- Modify: `Assets/Scripts/World/Data/WorldSettingsData.cs`
- Test: `Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs` (append)

- [ ] **Step 1: Write the failing test**

Append to `BuildingSOSaveRoundTripTests.cs`:

```csharp
    public class WorldSettingsDataRegistryTests
    {
        [Test]
        public void GetBuildingPrefab_returns_null_for_unknown_id_without_throwing()
        {
            var settings = ScriptableObject.CreateInstance<WorldSettingsData>();
            try
            {
                Assert.IsNull(settings.GetBuildingPrefab("does-not-exist"));
                Assert.IsNull(settings.GetInteriorPrefab("does-not-exist"));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void GetBuildingPrefab_resolves_by_PrefabId_string_from_SO_list()
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            var stubPrefab = new GameObject("StubPrefab");
            try
            {
                // Use reflection to seed BuildingSO._prefabId + _buildingPrefab (test-only).
                var f1 = typeof(BuildingSO).GetField("_prefabId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var f2 = typeof(BuildingSO).GetField("_buildingPrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                f1.SetValue(so, "TestPrefabId");
                f2.SetValue(so, stubPrefab);

                var settings = ScriptableObject.CreateInstance<WorldSettingsData>();
                settings.Blueprints.Add(so);

                Assert.AreSame(stubPrefab, settings.GetBuildingPrefab("TestPrefabId"));
                Object.DestroyImmediate(settings);
            }
            finally
            {
                Object.DestroyImmediate(so);
                Object.DestroyImmediate(stubPrefab);
            }
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run `MWI.Tests.Buildings.WorldSettingsDataRegistryTests`.
Expected: FAIL — `settings.Blueprints` does not exist yet.

- [ ] **Step 3: Add the new field + lookups; keep legacy field intact**

In `Assets/Scripts/World/Data/WorldSettingsData.cs`, ADD (don't remove) — after the existing `BuildingRegistry` field declaration:

```csharp
        [Tooltip("BuildingSO blueprints. After the 2026-05-16 migration this is the source of truth; the legacy BuildingRegistry list above is kept until the cleanup task lands so existing scenes/saves don't lose data during the transition.")]
        public List<BuildingSO> Blueprints = new List<BuildingSO>();
```

Replace the body of `GetBuildingPrefab(string)` and `GetInteriorPrefab(string)` and ADD a new `GetBuildingBlueprint(string)` helper:

```csharp
        public BuildingSO GetBuildingBlueprint(string prefabId)
        {
            if (string.IsNullOrEmpty(prefabId)) return null;
            for (int i = 0; i < Blueprints.Count; i++)
            {
                var entry = Blueprints[i];
                if (entry != null && entry.PrefabId == prefabId) return entry;
            }
            return null;
        }

        public GameObject GetBuildingPrefab(string prefabId)
        {
            var blueprint = GetBuildingBlueprint(prefabId);
            if (blueprint != null) return blueprint.BuildingPrefab;

            // Legacy fall-through: only fires during the migration window when
            // Blueprints is partially populated or empty. Remove with the legacy
            // field deletion (Task 18).
            foreach (var entry in BuildingRegistry)
            {
                if (entry.PrefabId == prefabId) return entry.BuildingPrefab;
            }
            return null;
        }

        public GameObject GetInteriorPrefab(string prefabId)
        {
            var blueprint = GetBuildingBlueprint(prefabId);
            if (blueprint != null) return blueprint.InteriorPrefab;
            foreach (var entry in BuildingRegistry)
            {
                if (entry.PrefabId == prefabId) return entry.InteriorPrefab;
            }
            return null;
        }
```

Leave the legacy `public List<BuildingRegistryEntry> BuildingRegistry` field and the `BuildingRegistryEntry` struct **in place**. Mark the field deprecated:

```csharp
        [System.Obsolete("Use Blueprints (List<BuildingSO>) — this field will be removed after migration. See docs/superpowers/plans/2026-05-16-building-so-and-treasury.md Task 18.")]
        [Tooltip("DEPRECATED — migrated to Blueprints. Kept until Task 18 cleanup.")]
        public List<BuildingRegistryEntry> BuildingRegistry = new List<BuildingRegistryEntry>();
```

Note: `[System.Obsolete]` on a public field generates compile warnings at every read site. Suppress with `#pragma warning disable CS0618` at the WorldSettingsData consumer sites during the transition, OR just let the warnings accumulate as a visible "TODO: finish migration" signal. Choose the latter — warnings are intended.

- [ ] **Step 4: Run test to verify it passes**

Run `MWI.Tests.Buildings.WorldSettingsDataRegistryTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Data/WorldSettingsData.cs Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs
git commit -m "refactor(world): add Blueprints List<BuildingSO> alongside legacy BuildingRegistry"
```

The codebase still compiles and the legacy registry still loads. Migration (Task 11) populates `Blueprints` next.

---

## Task 8: Update the 9 consumer sites

**Files (one Step per site):**
- Modify: `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs`
- Modify: `Assets/Scripts/World/MapSystem/MapController.cs` (registry consumers near :914, :1921, :1971)
- Modify: `Assets/Scripts/World/MapSystem/MacroSimulator.cs:447-485`
- Modify: `Assets/Scripts/World/Buildings/BuildingInteriorRegistry.cs:200`
- Modify: `Assets/Scripts/Character/Components/CharacterMapTracker.cs:242`
- Modify: `Assets/Scripts/UI/Building/UI_BuildingPlacementMenu.cs:97`
- Modify: `Assets/Scripts/UI/Building/UI_BuildingEntry.cs:21`
- Modify: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingOverviewSubTab.cs`

**Pattern to apply at every site that iterates the legacy `BuildingRegistry` list directly:** switch to `settings.Blueprints` (and add a null-skip). Sites that only call `settings.GetBuildingPrefab(prefabId)` / `settings.GetInteriorPrefab(prefabId)` do NOT need to change — the helpers already check `Blueprints` first and fall back to the legacy list (per Task 7).

- [ ] **Step 1: BuildingPlacementManager — accept `BuildingSO` instead of `PrefabId` string at the entry surface**

Locate `StartPlacement(string prefabId)` (around line 72) and the server-side `RequestPlacementServerRpc` (around line 435). Replace `entry.PrefabId == prefabId` `Find()` lookups with `settings.GetBuildingBlueprint(prefabId)` (the new API from Task 7). Where the manager previously constructed a `BuildingRegistryEntry` local, switch to the `BuildingSO` reference directly. Pull `BuildingPrefab`, `InteriorPrefab`, `BuildingName`, `Icon` from the SO.

The server-authoritative `RequestPlacementServerRpc` keeps taking a `string prefabId` over the wire (cheaper than serializing an SO ref, and PrefabId is the cross-session key anyway) — only the server's resolution call changes.

Pattern (apply at every former `Find()` site):

```csharp
// BEFORE:
var entry = settings.BuildingRegistry.Find(e => e.PrefabId == prefabId);
if (string.IsNullOrEmpty(entry.PrefabId)) { /* not found */ return; }
var prefab = entry.BuildingPrefab;

// AFTER:
var blueprint = settings.GetBuildingBlueprint(prefabId);
if (blueprint == null) { /* not found */ return; }
var prefab = blueprint.BuildingPrefab;
```

- [ ] **Step 2: MapController.SpawnSavedBuildings + WakeUp**

In `Assets/Scripts/World/MapSystem/MapController.cs` near lines 914, 1921, 1961, 1966, 1971: every existing call to `settings.GetBuildingPrefab(bSave.PrefabId)` continues to work (the API on `WorldSettingsData` is preserved). No code change required, but verify by Ctrl+F on `GetBuildingPrefab(` — they should still compile.

If a site walks `settings.BuildingRegistry` directly with `.Find(e => e.PrefabId == ...)`, replace with `settings.GetBuildingBlueprint(...)` per the pattern above.

- [ ] **Step 3: MacroSimulator.SimulateCityGrowth (lines 447-485)**

The current loop reads `entry.PrefabId` and sorts by `entry.CommunityPriority`. Replace `settings.BuildingRegistry` → `settings.Blueprints`:

```csharp
// BEFORE:
var candidates = settings.BuildingRegistry
    .Where(entry => knownBuildings.Contains(entry.PrefabId) &&
                    !community.ConstructedBuildings.Any(cb => cb.PrefabId == entry.PrefabId))
    .OrderByDescending(entry => entry.CommunityPriority)
    .ToList();

// AFTER:
var candidates = settings.Blueprints
    .Where(entry => entry != null &&
                    knownBuildings.Contains(entry.PrefabId) &&
                    !community.ConstructedBuildings.Any(cb => cb.PrefabId == entry.PrefabId))
    .OrderByDescending(entry => entry.CommunityPriority)
    .ToList();
```

LINQ shape is identical — only the source list and element type change (`BuildingRegistryEntry` → `BuildingSO`), and we add a null guard (`entry != null`) because asset references can deserialize as null on broken SOs (rule #38). The downstream `community.ConstructedBuildings.Add(new BuildingSaveData { PrefabId = bestEntry.PrefabId, ... })` block is unchanged.

**Note (rule #34):** `MacroSimulator.SimulateCityGrowth` runs offline / on map-wake, not per-frame. LINQ + `.ToList()` allocation is acceptable here. Do NOT replicate this LINQ shape into BT-tick or Update paths.

- [ ] **Step 4: BuildingInteriorRegistry.cs:200**

Existing call: `settings.GetInteriorPrefab(prefabId)`. No change required — API preserved.

- [ ] **Step 5: CharacterMapTracker.cs:242**

Same — `settings.GetInteriorPrefab(prefabId)`. No change required.

- [ ] **Step 6: UI_BuildingPlacementMenu.cs:97**

This enumerates `settings.BuildingRegistry`. Pattern:

```csharp
// BEFORE:
foreach (var entry in settings.BuildingRegistry)
{
    var ui = Instantiate(_entryPrefab, _content);
    ui.Bind(entry.PrefabId, entry.BuildingName, entry.Icon);
}

// AFTER:
foreach (var blueprint in settings.Blueprints)
{
    if (blueprint == null) continue;
    var ui = Instantiate(_entryPrefab, _content);
    ui.Bind(blueprint);
}
```

- [ ] **Step 7: UI_BuildingEntry.cs:21**

Replace the `Bind(string prefabId, string name, Sprite icon)` signature with `Bind(BuildingSO blueprint)`. Internally cache the blueprint:

```csharp
private BuildingSO _blueprint;

public void Bind(BuildingSO blueprint)
{
    _blueprint = blueprint;
    _iconImage.sprite = blueprint.Icon;
    _nameText.text = !string.IsNullOrEmpty(blueprint.BuildingName) ? blueprint.BuildingName : blueprint.PrefabId;
}

public void OnClick()
{
    if (_blueprint == null) return;
    BuildingPlacementManager.Instance.StartPlacement(_blueprint.PrefabId);
}
```

- [ ] **Step 8: BuildingOverviewSubTab.cs**

Locate the read-out lines that previously enumerated registry rows. Update to read from `building.Blueprint` directly:

```csharp
// Existing read-out additions:
sb.AppendLine($"Blueprint: {(b.Blueprint != null ? b.Blueprint.name : "<missing>")}");
sb.AppendLine($"PrefabId: {b.PrefabId}");
sb.AppendLine($"BuildingType: {b.BuildingType}");
```

- [ ] **Step 9: Compile check**

Trigger `assets-refresh`. Expected: no compilation errors.

- [ ] **Step 10: Commit**

```bash
git add Assets/Scripts/World/Buildings/BuildingPlacementManager.cs Assets/Scripts/World/MapSystem/MapController.cs Assets/Scripts/World/MapSystem/MacroSimulator.cs Assets/Scripts/World/Buildings/BuildingInteriorRegistry.cs Assets/Scripts/Character/Components/CharacterMapTracker.cs Assets/Scripts/UI/Building/UI_BuildingPlacementMenu.cs Assets/Scripts/UI/Building/UI_BuildingEntry.cs Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingOverviewSubTab.cs
git commit -m "refactor(building): retarget 9 registry-consumer sites onto BuildingSO"
```

---

## Task 9: Override `CommercialBuilding.OnDefaultFurnitureSpawned` to credit treasury

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/CommercialBuilding.cs` (add override + `_treasurySeeded` field)
- Test: `Assets/Editor/Tests/Buildings/TreasurySeedIdempotencyTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `Assets/Editor/Tests/Buildings/TreasurySeedIdempotencyTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MWI.Tests.Buildings
{
    public class TreasurySeedIdempotencyTests
    {
        [Test]
        public void CommercialBuilding_exposes_TreasurySeeded_default_false()
        {
            var go = new GameObject("TestShop");
            try
            {
                // CommercialBuilding requires several siblings — exercise only the flag accessor.
                var b = go.AddComponent<TestShopProbe>();
                Assert.IsFalse(b.TreasurySeededProbe,
                    "TreasurySeeded must default false on a fresh build so the construction-complete hook fires once.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // Lightweight probe to expose the protected flag without spinning up the full ShopBuilding.
        private class TestShopProbe : CommercialBuilding
        {
            public bool TreasurySeededProbe => GetTreasurySeededForTests();
        }
    }
}
```

Note: the probe relies on `CommercialBuilding.GetTreasurySeededForTests()` which we add inside an `#if UNITY_EDITOR` block.

- [ ] **Step 2: Run test to verify it fails**

Run `MWI.Tests.Buildings.TreasurySeedIdempotencyTests`.
Expected: FAIL — `GetTreasurySeededForTests` does not exist.

- [ ] **Step 3: Add `_treasurySeeded` field and the override**

In `Assets/Scripts/World/Buildings/CommercialBuildings/CommercialBuilding.cs`, near the top of the class (after the existing private fields, before the methods):

```csharp
    /// <summary>
    /// Server-only flag. True once <see cref="OnDefaultFurnitureSpawned"/> has
    /// credited <see cref="BuildingCommercialSO.BaseTreasury"/> into a Treasury
    /// safe. Persisted via <see cref="BuildingSaveData.TreasurySeeded"/> so
    /// save/reload does not double-stock the safe. Reset to false ONLY by a
    /// deliberate Dev-Mode "reseed" action (out of scope here).
    /// </summary>
    private bool _treasurySeeded;

#if UNITY_EDITOR
    /// <summary>Test-only accessor; do NOT call from production code.</summary>
    internal bool GetTreasurySeededForTests() => _treasurySeeded;
    internal void SetTreasurySeededForTests(bool v) => _treasurySeeded = v;
#endif
```

Then add the override (place it near the end of the class to match existing override-grouping convention):

```csharp
    /// <inheritdoc/>
    protected override void OnDefaultFurnitureSpawned()
    {
        base.OnDefaultFurnitureSpawned();
        if (!IsServer) return;
        if (_treasurySeeded) return;

        if (!(_blueprint is BuildingCommercialSO commercialBlueprint)) return;
        int amount = commercialBlueprint.BaseTreasury;
        if (amount <= 0) { _treasurySeeded = true; return; } // mark seeded even at zero to avoid retry

        MWI.Economy.CurrencyId currency = MWI.Economy.CurrencyId.Default;
        try
        {
            var owningMap = MapController.GetMapAtPosition(transform.position);
            if (owningMap != null) currency = owningMap.NativeCurrency;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e); // rule #31 — don't crash construction if map lookup fails
        }

        try
        {
            CreditTreasury(currency, amount, "BaseTreasury seed");
            _treasurySeeded = true;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e); // rule #31
            // Leave _treasurySeeded = false so a future reload's RestoreFromSaveData
            // re-attempts the credit (unless the save also carried TreasurySeeded=true).
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run `MWI.Tests.Buildings.TreasurySeedIdempotencyTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/CommercialBuilding.cs Assets/Editor/Tests/Buildings/TreasurySeedIdempotencyTests.cs
git commit -m "feat(commercial): seed Treasury from BuildingCommercialSO.BaseTreasury on first build"
```

---

## Task 10: Persist `_treasurySeeded` via `BuildingSaveData.TreasurySeeded`

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapRegistry.cs` (BuildingSaveData class around line 143)
- Modify: `Assets/Scripts/World/Buildings/Building.cs` (FromBuilding around line 234 + RestoreFromSaveData around line 1847)
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/CommercialBuilding.cs` (override RestoreFromSaveData if needed)
- Test: `Assets/Editor/Tests/Buildings/TreasurySeedIdempotencyTests.cs` (append)

- [ ] **Step 1: Add `TreasurySeeded` field to `BuildingSaveData`**

In `Assets/Scripts/World/MapSystem/MapRegistry.cs`, inside `public class BuildingSaveData` (around line 220, after `ShopCatalog`):

```csharp
        /// <summary>
        /// True once CommercialBuilding.OnDefaultFurnitureSpawned has credited the
        /// BaseTreasury seed. Default-false so old saves (no field) re-seed exactly
        /// once on the next load and then flip to true. Non-commercial buildings
        /// ignore this field.
        /// </summary>
        public bool TreasurySeeded;
```

- [ ] **Step 2: Write the failing round-trip test**

Append to `TreasurySeedIdempotencyTests.cs`:

```csharp
        [Test]
        public void BuildingSaveData_round_trips_TreasurySeeded_through_JsonUtility()
        {
            var data = new BuildingSaveData { BuildingId = "x", PrefabId = "y", TreasurySeeded = true };
            var json = JsonUtility.ToJson(data);
            var back = JsonUtility.FromJson<BuildingSaveData>(json);
            Assert.IsTrue(back.TreasurySeeded);
        }

        [Test]
        public void BuildingSaveData_TreasurySeeded_defaults_false_when_field_missing_from_json()
        {
            // Old save JSON shape — no TreasurySeeded key.
            var oldJson = "{\"BuildingId\":\"x\",\"PrefabId\":\"y\"}";
            var data = JsonUtility.FromJson<BuildingSaveData>(oldJson);
            Assert.IsFalse(data.TreasurySeeded);
        }
```

- [ ] **Step 3: Run test — expect first to PASS (field already added), second to PASS (default-false)**

Run `MWI.Tests.Buildings.TreasurySeedIdempotencyTests`.
Expected: PASS.

- [ ] **Step 4: Wire write side — `BuildingSaveData.FromBuilding`**

In `Assets/Scripts/World/Buildings/Building.cs` near line 247 (inside `FromBuilding`, in the object initialiser):

Find:
```csharp
            var data = new BuildingSaveData
            {
                BuildingId = building.BuildingId,
                PrefabId = building.PrefabId,
                Position = building.transform.position - mapCenter,
                Rotation = building.transform.rotation,
                State = building.CurrentState,
                ConstructionProgress = building.ConstructionProgress.Value,
                PlacedByCharacterId = building.PlacedByCharacterId.Value.ToString()
            };
```

After that block, add:

```csharp
            // Persist commercial seed-flag so the next load doesn't re-credit.
            if (building is CommercialBuilding cb)
            {
                data.TreasurySeeded = cb.GetTreasurySeededForSave();
            }
```

In `CommercialBuilding.cs`, add a non-#if'd accessor that wraps the same field:

```csharp
    /// <summary>Non-test accessor for save-write only. Server-side only.</summary>
    public bool GetTreasurySeededForSave() => _treasurySeeded;
    /// <summary>Non-test accessor for save-load only. Server-side only.</summary>
    public void SetTreasurySeededForLoad(bool v) => _treasurySeeded = v;
```

- [ ] **Step 5: Wire read side — `RestoreFromSaveData`**

In `Assets/Scripts/World/Buildings/Building.cs` near line 1847 (the Complete-state restore branch), find the existing `data.State == Complete` block. After the line that flips `_currentState.Value = Complete` and BEFORE `TrySpawnDefaultFurniture()` is invoked, add:

```csharp
            // Restore commercial seed-flag BEFORE TrySpawnDefaultFurniture → OnDefaultFurnitureSpawned
            // runs, so a previously-seeded building doesn't re-credit. Non-commercial buildings
            // ignore this field; the cast guards.
            if (this is CommercialBuilding restoredCb)
            {
                restoredCb.SetTreasurySeededForLoad(data.TreasurySeeded);
            }
```

- [ ] **Step 6: Compile check + run all Buildings tests**

Trigger `assets-refresh`, then run `MWI.Tests.Buildings.*` via `tests-run`.
Expected: all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MapRegistry.cs Assets/Scripts/World/Buildings/Building.cs Assets/Scripts/World/Buildings/CommercialBuildings/CommercialBuilding.cs Assets/Editor/Tests/Buildings/TreasurySeedIdempotencyTests.cs
git commit -m "feat(save): round-trip BuildingSaveData.TreasurySeeded to prevent re-credit on reload"
```

---

## Task 11: Migrate existing registry rows into `BuildingSO` assets

**Files:**
- Create: `Assets/Editor/BuildingRegistryToBuildingSOMigration.cs`

**Important ordering note:** This task MUST run between Task 7 (legacy `BuildingRegistry` field still exists, new `Blueprints` field is empty) and Task 18 (legacy field deletion). It can run before or after Task 6 (Building.cs legacy field deletion) — independent surfaces. Concretely: complete Tasks 1–5 and 7–10 first. Then run this migration ONCE in the Editor. Then apply Task 6 (Building.cs cleanup) and Task 18 (WorldSettingsData cleanup).

- [ ] **Step 1: Create the migration script**

Create `Assets/Editor/BuildingRegistryToBuildingSOMigration.cs`:

```csharp
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MWI.WorldSystem;

public static class BuildingRegistryToBuildingSOMigration
{
    private const string OutputFolder = "Assets/Resources/Data/Buildings";
    private const string SettingsAssetPath = "Assets/Resources/Data/World/WorldSettingsData.asset";

    [MenuItem("MWI/Migration/Convert BuildingRegistry → BuildingSO assets")]
    public static void Run()
    {
        var settings = AssetDatabase.LoadAssetAtPath<WorldSettingsData>(SettingsAssetPath);
        if (settings == null)
        {
            Debug.LogError($"[Migration] No WorldSettingsData at {SettingsAssetPath}.");
            return;
        }

        // Read both fields via SerializedObject. The legacy field is still
        // `List<BuildingRegistryEntry>` at this point (Task 7 added Blueprints
        // alongside, did NOT delete the legacy). Idempotency: if Blueprints is
        // already populated AND legacy is empty, the migration already ran.
        var so = new SerializedObject(settings);
        var legacyProp = so.FindProperty("BuildingRegistry");
        var blueprintsProp = so.FindProperty("Blueprints");
        if (legacyProp == null || !legacyProp.isArray || blueprintsProp == null || !blueprintsProp.isArray)
        {
            Debug.LogError("[Migration] Expected both BuildingRegistry (legacy) and Blueprints (new) properties on WorldSettingsData. Has Task 7 landed?");
            return;
        }

        if (blueprintsProp.arraySize > 0)
        {
            Debug.Log("[Migration] Blueprints is already populated. Nothing to do (delete Blueprints entries in Inspector to force re-migration).");
            return;
        }

        if (legacyProp.arraySize == 0)
        {
            Debug.LogWarning("[Migration] Legacy BuildingRegistry is empty — nothing to migrate.");
            return;
        }

        if (!Directory.Exists(OutputFolder)) Directory.CreateDirectory(OutputFolder);

        var createdSOs = new List<BuildingSO>();
        for (int i = 0; i < legacyProp.arraySize; i++)
        {
            var entryProp = legacyProp.GetArrayElementAtIndex(i);
            string prefabId = entryProp.FindPropertyRelative("PrefabId").stringValue;
            string buildingName = entryProp.FindPropertyRelative("BuildingName").stringValue;
            var iconObj = entryProp.FindPropertyRelative("Icon").objectReferenceValue as Sprite;
            var buildingPrefabObj = entryProp.FindPropertyRelative("BuildingPrefab").objectReferenceValue as GameObject;
            var interiorPrefabObj = entryProp.FindPropertyRelative("InteriorPrefab").objectReferenceValue as GameObject;
            int communityPriority = entryProp.FindPropertyRelative("CommunityPriority").intValue;

            if (string.IsNullOrEmpty(prefabId))
            {
                Debug.LogWarning($"[Migration] Registry row {i} has empty PrefabId — skipped.");
                continue;
            }

            // Pull the prefab's existing _buildingType + _constructionRequirements + _defaultFurnitureLayout.
            BuildingType bType = BuildingType.Residential;
            var constructionReqs = new List<CraftingIngredient>();
            var defaultLayout = new List<Building.DefaultFurnitureSlot>();
            bool isCommercial = false;
            if (buildingPrefabObj != null)
            {
                var building = buildingPrefabObj.GetComponent<Building>();
                if (building != null)
                {
                    var bSo = new SerializedObject(building);
                    bType = (BuildingType)bSo.FindProperty("_buildingType").enumValueIndex;

                    var crProp = bSo.FindProperty("_constructionRequirements");
                    if (crProp != null)
                    {
                        for (int j = 0; j < crProp.arraySize; j++)
                        {
                            var item = crProp.GetArrayElementAtIndex(j).FindPropertyRelative("Item").objectReferenceValue as ItemSO;
                            int amount = crProp.GetArrayElementAtIndex(j).FindPropertyRelative("Amount").intValue;
                            if (item != null) constructionReqs.Add(new CraftingIngredient { Item = item, Amount = amount });
                        }
                    }

                    var dflProp = bSo.FindProperty("_defaultFurnitureLayout");
                    if (dflProp != null)
                    {
                        for (int j = 0; j < dflProp.arraySize; j++)
                        {
                            var slotProp = dflProp.GetArrayElementAtIndex(j);
                            defaultLayout.Add(new Building.DefaultFurnitureSlot
                            {
                                ItemSO = slotProp.FindPropertyRelative("ItemSO").objectReferenceValue as FurnitureItemSO,
                                LocalPosition = slotProp.FindPropertyRelative("LocalPosition").vector3Value,
                                LocalEulerAngles = slotProp.FindPropertyRelative("LocalEulerAngles").vector3Value,
                                TargetRoom = slotProp.FindPropertyRelative("TargetRoom").objectReferenceValue as Room
                            });
                        }
                    }
                }

                isCommercial = buildingPrefabObj.GetComponent<CommercialBuilding>() != null;
            }

            BuildingSO blueprint = isCommercial
                ? ScriptableObject.CreateInstance<BuildingCommercialSO>()
                : ScriptableObject.CreateInstance<BuildingSO>();
            blueprint.name = prefabId;

            // Reflection write — fields are private SerializeFields.
            var t = blueprint.GetType();
            void WriteField(string fieldName, object value)
            {
                var f = t.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                     ?? t.BaseType?.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) f.SetValue(blueprint, value);
            }
            WriteField("_prefabId", prefabId);
            WriteField("_buildingName", buildingName);
            WriteField("_icon", iconObj);
            WriteField("_buildingPrefab", buildingPrefabObj);
            WriteField("_interiorPrefab", interiorPrefabObj);
            WriteField("_communityPriority", communityPriority);
            WriteField("_buildingType", bType);
            WriteField("_constructionRequirements", constructionReqs);
            WriteField("_defaultFurnitureLayout", defaultLayout);

            string assetPath = $"{OutputFolder}/{prefabId}.asset";
            AssetDatabase.CreateAsset(blueprint, assetPath);
            createdSOs.Add(blueprint);
            Debug.Log($"[Migration] Created {assetPath} (commercial={isCommercial}).");

            // Re-target the prefab's _blueprint field to the new SO.
            if (buildingPrefabObj != null)
            {
                var building = buildingPrefabObj.GetComponent<Building>();
                if (building != null)
                {
                    var bSo = new SerializedObject(building);
                    var bp = bSo.FindProperty("_blueprint");
                    if (bp != null)
                    {
                        bp.objectReferenceValue = blueprint;
                        bSo.ApplyModifiedProperties();
                        PrefabUtility.SavePrefabAsset(buildingPrefabObj);
                        Debug.Log($"[Migration] Set _blueprint on prefab '{buildingPrefabObj.name}'.");
                    }
                }
            }
        }

        // Populate the new Blueprints list. The legacy BuildingRegistry stays
        // intact (Task 18 deletes it after verification).
        blueprintsProp.ClearArray();
        for (int i = 0; i < createdSOs.Count; i++)
        {
            blueprintsProp.InsertArrayElementAtIndex(i);
            blueprintsProp.GetArrayElementAtIndex(i).objectReferenceValue = createdSOs[i];
        }
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Migration] Done. {createdSOs.Count} BuildingSO assets created and wired. Blueprints list populated; legacy BuildingRegistry preserved until Task 18 cleanup.");
    }
}
#endif
```

- [ ] **Step 2: Run the migration**

In the Unity Editor: Menu → MWI → Migration → Convert BuildingRegistry → BuildingSO assets.
Expected console: one `[Migration] Created Assets/Resources/Data/Buildings/<PrefabId>.asset` line per registry row, plus one `[Migration] Set _blueprint on prefab '<Name>'` per prefab, plus a final summary line.

If the migration logs `[Migration] Registry already holds BuildingSO references`, the migration was already applied — skip and proceed.

- [ ] **Step 3: Manual verification**

Inspect 2–3 of the newly created `.asset` files in `Assets/Resources/Data/Buildings/`. Confirm:
- `PrefabId` matches the legacy row.
- `BuildingPrefab` and `InteriorPrefab` references are non-null where they were non-null before.
- `ConstructionRequirements` list matches the prefab's old `_constructionRequirements` order (positional).
- For shops/commercial: the asset class is `BuildingCommercialSO`, not `BuildingSO`.

Inspect 2–3 building prefabs in `Assets/Prefabs/Building/...` (the actual building-type prefabs, not the infrastructure ones). Confirm `_blueprint` is set on each.

Inspect `Assets/Resources/Data/World/WorldSettingsData.asset` in the Inspector. Confirm:
- The new `Blueprints` list now has one BuildingSO reference per migrated row.
- The legacy `BuildingRegistry` list is **unchanged** (still has all the original struct rows). Task 18 deletes it after the smoke tests pass.

- [ ] **Step 4: Commit (asset changes)**

```bash
git add Assets/Resources/Data/Buildings/ Assets/Resources/Data/World/WorldSettingsData.asset "Assets/Prefabs/Building"
git add Assets/Editor/BuildingRegistryToBuildingSOMigration.cs
git commit -m "feat(migration): convert BuildingRegistryEntry rows to BuildingSO assets + wire prefabs"
```

Now is the moment to apply **Task 6** (delete legacy duplicated fields on `Building.cs`).

---

## Task 12: EditMode test — `BuildingSaveData` round-trips PrefabId byte-identically

**Files:**
- Test: `Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs` (append)

- [ ] **Step 1: Write the test**

Append to `BuildingSOSaveRoundTripTests.cs`:

```csharp
    public class BuildingSaveCompatTests
    {
        [Test]
        public void Old_save_JSON_with_PrefabId_string_loads_into_new_BuildingSaveData()
        {
            // Synthetic "old save" JSON — no TreasurySeeded, no extra fields.
            const string oldJson =
                "{\"BuildingId\":\"abc-123\",\"PrefabId\":\"Shop_Armor_A\",\"Position\":{\"x\":0,\"y\":0,\"z\":0},\"Rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"State\":1,\"ConstructionProgress\":1}";
            var data = JsonUtility.FromJson<BuildingSaveData>(oldJson);
            Assert.AreEqual("abc-123", data.BuildingId);
            Assert.AreEqual("Shop_Armor_A", data.PrefabId);
            Assert.IsFalse(data.TreasurySeeded, "Missing TreasurySeeded must default false (re-seed once on first load).");
        }
    }
```

- [ ] **Step 2: Run test**

Run `MWI.Tests.Buildings.BuildingSaveCompatTests`.
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/Tests/Buildings/BuildingSOSaveRoundTripTests.cs
git commit -m "test(save): pin BuildingSaveData backward-compat for old saves"
```

---

## Task 13: Manual playmode test — cooperative-build credits treasury once

**Files:** none modified

- [ ] **Step 1: Open GameScene**

In Editor, open `Assets/Scenes/GameScene.unity`.

- [ ] **Step 2: Enter playmode as host**

Run as Host (single-player). Use the placement HUD to place a `ShopBuilding` (any commercial type with `BaseTreasury > 0` authored on its `BuildingCommercialSO`).

- [ ] **Step 3: Deliver materials cooperatively + finish construction**

Drive an NPC builder (or use the Dev-Mode `BuildInstantly` debug action) to finish the construction.

- [ ] **Step 4: Open Dev-Mode → Inspect → Building → Console Management**

Verify the building's Treasury balance shows the `BaseTreasury` value in the map's `NativeCurrency` (or `CurrencyId.Default` if the map's currency is unset).

- [ ] **Step 5: Trigger a second `OnDefaultFurnitureSpawned`**

There is no in-game UI to do this; use the Dev-Mode `DevForce*` route or restart the building (out-of-scope here). If you cannot retrigger, this is covered by Task 14's save/reload.

- [ ] **Step 6: Verify**

Treasury balance is exactly `BaseTreasury` (not double). The CommercialBuilding's `_treasurySeeded` flag is true (visible via Dev-Mode inspector).

**Expected outcome:** ✅ Treasury credited once. If credited twice or not at all, regress to Task 9.

---

## Task 14: Manual playmode test — save/reload does NOT re-credit

**Files:** none modified

- [ ] **Step 1: With the playmode session from Task 13 still running, save the world**

Use the Bed save trigger or the Pause Menu → Save.

- [ ] **Step 2: Exit playmode, re-enter, load the save**

Load the world from the title menu. Once the map wakes, locate the same `ShopBuilding`.

- [ ] **Step 3: Verify**

Treasury balance equals what it was at save time (not doubled). Dev-Mode inspector shows `_treasurySeeded` = true on the restored building.

**Expected outcome:** ✅ No re-credit on reload. If treasury doubles, regress to Task 10.

---

## Task 15: Late-joiner audit (rule #19b)

**Files:** none modified. This is the six-question audit from `wiki/gotchas/host-only-state-blindspot.md` applied to the new feature.

- [ ] **Step 1: Walk through the six questions**

Record answers in the commit body for Task 9 or as a follow-up doc commit:

1. **Who writes / who reads?**
   - `_treasurySeeded` (server-only field): server writes inside `OnDefaultFurnitureSpawned`; server-only reads from `FromBuilding`/`RestoreFromSaveData`. Clients never read.
   - `BuildingSaveData.TreasurySeeded` (save field): server writes, server reads.
   - Treasury balance per safe: server writes via `safe.Credit`; replicated to clients via the existing `SafeFurnitureNetworkSync._networkBalances` NetworkList.

2. **Replication channel for each client-readable field?**
   - Treasury balance: `NetworkList<BuildingTreasuryEntry>` on `SafeFurnitureNetworkSync` — already wired pre-feature.
   - Building blueprint reference (`_blueprint`): SerializeField on the prefab — every peer's `Instantiate` brings the same SO ref. No NetworkVariable needed.
   - `_treasurySeeded`: server-only. Clients never need it.

3. **What does the late-joiner see on connect?**
   - SafeFurniture spawns as a child NetworkObject of the building. NGO replicates the NetworkObject + `_networkBalances` NetworkList state to late-joiners on initial sync. The late-joiner reads the post-seed balance immediately.

4. **Client-side pre-gate?**
   - There is no client pre-gate for "should this building seed?" — seeding is server-only.

5. **`GetComponentInParent` calls in `Awake` that might race?**
   - `OnDefaultFurnitureSpawned` runs AFTER `TrySpawnDefaultFurniture` parents and Spawns the safe NetworkObject, so the safe's `Cashier.TryRegisterWithShop`-style late-binder is irrelevant: the safe exists, its sync component has run `OnNetworkSpawn`, and `TreasurySafes` returns it. No race surface introduced.

6. **`InteractableObject.IsCharacterInInteractionZone` gating?**
   - No new interactable surface added. The existing safe inspect UI uses its existing zone gate.

- [ ] **Step 2: Late-joiner repro**

Required by rule #19b. Steps:
1. Start a Host session.
2. Place + cooperatively-finish a commercial building (Task 13).
3. From a second machine (or a second client process), connect as a Client.
4. Walk to the same building. Open the Treasury via the Console Management Dev-Mode subtab.
5. Confirm the balance matches the host.

**Expected outcome:** ✅ Client sees the seeded treasury balance. If the client sees 0, the SafeFurnitureNetworkSync replication broke (out-of-scope here — file an issue against that subsystem).

---

## Task 16: Wiki updates (rule #29b)

**Files:**
- Modify: `wiki/systems/building.md`
- Modify: `wiki/systems/commercial-building.md`
- Modify: `wiki/systems/commercial-treasury.md`
- Modify: `wiki/systems/construction.md`

- [ ] **Step 1: Read `wiki/CLAUDE.md` first** (required by rule #29b)

Read `wiki/CLAUDE.md` start-to-finish to confirm the frontmatter rules, wikilink format, sources block, and >5-file diff-preview rule before any wiki edit.

- [ ] **Step 2: Update `wiki/systems/building.md`**

Add to the frontmatter `updated:` line: `2026-05-16`.

Append to `## Change log`:
```
- 2026-05-16 — BuildingSO blueprint introduced. Replaces inline BuildingRegistryEntry + duplicated prefab fields. PrefabId strings preserved verbatim → zero save migration. — claude
```

Update the `Key classes / files` section to list `BuildingSO.cs` and `BuildingCommercialSO.cs`.

In the `Public API` section, add a paragraph:
> Each Building prefab carries a `_blueprint` reference to a `BuildingSO` asset under `Assets/Resources/Data/Buildings/`. The blueprint is the single source of truth for PrefabId, BuildingName, BuildingType, ConstructionRequirements, and DefaultFurnitureLayout. `Building.cs` properties derive from `_blueprint` first; for legacy / scene-static buildings without a blueprint, the previous SerializeField defaults are no longer present (deleted by the May 2026 refactor).

- [ ] **Step 3: Update `wiki/systems/commercial-building.md`**

Bump `updated:`, append change-log line:
```
- 2026-05-16 — Treasury seed flow: BuildingCommercialSO.BaseTreasury → OnDefaultFurnitureSpawned override → CreditTreasury. Currency resolved from MapController.NativeCurrency at credit time. Persisted via BuildingSaveData.TreasurySeeded. — claude
```

Add a new section `## Treasury seed flow`:
```
The `BaseTreasury` integer on `BuildingCommercialSO` seeds the building's Treasury-role
SafeFurniture once, on construction-complete, via `CommercialBuilding.OnDefaultFurnitureSpawned`.
Currency is resolved at that moment from `MapController.NativeCurrency` (which reads
`CommunityData.NativeCurrency`); buildings placed outside any MapController fall back to
`CurrencyId.Default`. The seed runs in all four spawn paths: cooperative finalize,
`_spawnAsComplete` designer flag, debug `BuildInstantly`, and `RestoreFromSaveData`
Complete-branch. Re-credit on reload is prevented by `BuildingSaveData.TreasurySeeded`,
persisted server-side only.
```

- [ ] **Step 4: Update `wiki/systems/commercial-treasury.md`**

Bump `updated:`, append change-log line, document the seed entry-point. Add a "Seed source" subsection identical in intent to Step 3.

- [ ] **Step 5: Update `wiki/systems/construction.md`**

Append change-log line:
```
- 2026-05-16 — `OnDefaultFurnitureSpawned` is now also the entry-point for `CommercialBuilding`'s BaseTreasury seed (see commercial-building.md). — claude
```

Add a Gotchas entry pointing to the multi-safe ordering decision (we currently delegate to `CreditTreasury`'s safe-selection; if multi-treasury-safe buildings emerge, revisit).

- [ ] **Step 6: Commit**

```bash
git add wiki/systems/building.md wiki/systems/commercial-building.md wiki/systems/commercial-treasury.md wiki/systems/construction.md
git commit -m "docs(wiki): document BuildingSO blueprint + BaseTreasury seed flow"
```

---

## Task 17: SKILL + agent updates (rules #28, #29)

**Files:**
- Modify: `.agent/skills/buildings/SKILL.md`
- Modify: `.claude/agents/building-furniture-specialist.md`

- [ ] **Step 1: Append to `.agent/skills/buildings/SKILL.md`**

Add a "BuildingSO Blueprint API" section:

```
## BuildingSO Blueprint API (2026-05-16)

- `BuildingSO` — base SO with PrefabId / BuildingName / Icon / BuildingPrefab / InteriorPrefab / CommunityPriority / BuildingType / ConstructionRequirements / DefaultFurnitureLayout.
- `BuildingCommercialSO : BuildingSO` — adds `BaseTreasury` (int).
- `Building._blueprint` — SerializeField on every prefab.
- `WorldSettingsData.BuildingRegistry` — `List<BuildingSO>`. `GetBuildingBlueprint(prefabId)` / `GetBuildingPrefab` / `GetInteriorPrefab` resolve by PrefabId string.
- PrefabId strings are the cross-session save key — NEVER rename them after authoring.
- Construction requirements lifted from prefab to SO; positional index contract preserved.
- `CommercialBuilding.OnDefaultFurnitureSpawned` is the canonical seeding hook (try/catch, idempotent via `_treasurySeeded`).
```

- [ ] **Step 2: Update `.claude/agents/building-furniture-specialist.md`**

Append the new knowledge to the agent's description (one-line in the front-matter description block) and to the body if relevant.

- [ ] **Step 3: Commit**

```bash
git add .agent/skills/buildings/SKILL.md .claude/agents/building-furniture-specialist.md
git commit -m "docs(skill,agent): refresh for BuildingSO + BaseTreasury seed feature"
```

---

## Task 18: Delete legacy `BuildingRegistry` field and `BuildingRegistryEntry` struct

**Run this only after Tasks 13, 14, 15 (smoke tests + late-joiner audit) have all passed.** Until then, the legacy `BuildingRegistry` list is the data-loss safety net.

**Files:**
- Modify: `Assets/Scripts/World/Data/WorldSettingsData.cs` (delete legacy field + struct + fall-through code)
- Optionally rename `Blueprints` → `BuildingRegistry` for naming continuity (see Step 3)

- [ ] **Step 1: Delete the legacy field, the struct, and the fall-through branches**

In `Assets/Scripts/World/Data/WorldSettingsData.cs`:

1. Delete the `[System.Obsolete(...)] public List<BuildingRegistryEntry> BuildingRegistry = ...` field.
2. Delete the `BuildingRegistryEntry` struct definition (lines 7-19 in the original file).
3. Inside `GetBuildingPrefab(string)` and `GetInteriorPrefab(string)`, delete the `foreach (var entry in BuildingRegistry)` legacy fall-through block. The methods become two-liners:

```csharp
        public GameObject GetBuildingPrefab(string prefabId)
            => GetBuildingBlueprint(prefabId)?.BuildingPrefab;

        public GameObject GetInteriorPrefab(string prefabId)
            => GetBuildingBlueprint(prefabId)?.InteriorPrefab;
```

- [ ] **Step 2: Rename `Blueprints` → `BuildingRegistry` (optional but recommended for naming continuity)**

```csharp
        [UnityEngine.Serialization.FormerlySerializedAs("Blueprints")]
        [Tooltip("Authored BuildingSO blueprints. One asset per building type, all lookups scan by PrefabId.")]
        public List<BuildingSO> BuildingRegistry = new List<BuildingSO>();
```

`[FormerlySerializedAs]` makes the asset YAML's existing `Blueprints` key deserialize into the renamed `BuildingRegistry` field — no data loss.

Update the 3 consumer sites touched in Task 8 that read `settings.Blueprints`:
- `MacroSimulator.SimulateCityGrowth` → swap `settings.Blueprints` back to `settings.BuildingRegistry`.
- `UI_BuildingPlacementMenu.cs:97` → same swap.
- Any other site Task 8 changed from `BuildingRegistry` → `Blueprints` → swap back.

(If you prefer to keep the field named `Blueprints`, skip the rename and leave Task 8's consumer changes as-is. The name `Blueprints` is more descriptive of the new shape anyway. Decide based on team preference.)

- [ ] **Step 3: Compile check + run all Buildings tests**

Trigger `assets-refresh`, then run `MWI.Tests.Buildings.*` via `tests-run`.
Expected: all tests PASS. No `BuildingRegistryEntry` references remain (check via `Grep` for `BuildingRegistryEntry` — should return zero matches).

- [ ] **Step 4: Verify WorldSettingsData asset YAML**

Open `Assets/Resources/Data/World/WorldSettingsData.asset` (text-mode). Confirm:
- No `BuildingRegistry:` block with struct rows (the legacy data is gone).
- A `BuildingRegistry:` (or `Blueprints:` if you skipped the rename) block with object references (Unity YAML `fileID:` lines) pointing to the BuildingSO assets.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Data/WorldSettingsData.cs Assets/Resources/Data/World/WorldSettingsData.asset
git add Assets/Scripts/World/MapSystem/MacroSimulator.cs Assets/Scripts/UI/Building/UI_BuildingPlacementMenu.cs
# (and any other consumer touched by the optional rename)
git commit -m "refactor(world): delete legacy BuildingRegistryEntry; BuildingSO is the single source of truth"
```

---

## Open questions / follow-ups (NOT in scope here)

- **Multi-treasury-safe seed distribution.** Today `CreditTreasury` picks its safe; if designer authors >1 Treasury-role safe and wants a particular split, expose `CreditTreasury(currency, amount, reason, SafeFurniture target)`. Defer until a designer asks for it.
- **Dev-Mode "Reseed Treasury" action.** Useful for testing. Not required for this feature.
- **`BuildingTreasuryWagePayer`.** Stubbed comment at `Assets/Scripts/World/Jobs/Wages/MintedWagePayer.cs:6`. The seed feature unblocks this — but the wage-payer itself is its own task.
- **Native currency on a Kingdom rather than CommunityData.** The `CurrencyId.cs` comment hints at Kingdom-minted currencies. When the Kingdom system arrives, `CommunityData.NativeCurrency` becomes a derived value (kingdom membership → kingdom currency). Single-line refactor when the time comes.

---

## Execution order quick-reference

1. Task 1 (CommunityData.NativeCurrency)
2. Task 2 (MapController.NativeCurrency)
3. Task 3 (BuildingSO)
4. Task 4 (BuildingCommercialSO)
5. Task 5 (Building._blueprint added; legacy `_prefabId`/`_buildingName`/etc. STILL PRESENT)
6. Task 7 (Add `Blueprints` List<BuildingSO> on WorldSettingsData alongside legacy `BuildingRegistry`)
7. Task 8 (Update 9 consumer sites — only ones that iterate the list directly need to switch to `Blueprints`)
8. Task 9 (CommercialBuilding.OnDefaultFurnitureSpawned override)
9. Task 10 (BuildingSaveData.TreasurySeeded round-trip)
10. **Task 11 (Migration script — run in Editor, populates `Blueprints` from legacy `BuildingRegistry` + wires `_blueprint` on prefabs)**
11. **Task 6 (Delete legacy duplicated fields on Building.cs) — only after Task 11 has wired every prefab's `_blueprint`.**
12. Task 12 (save round-trip test)
13. Task 13 (manual playmode test — single build, treasury credited)
14. Task 14 (manual playmode test — save/reload, no re-credit)
15. Task 15 (late-joiner audit + repro)
16. **Task 18 (Delete legacy `BuildingRegistry` field + `BuildingRegistryEntry` struct) — only after Tasks 13–15 pass.**
17. Task 16 (wiki updates)
18. Task 17 (SKILL + agent updates)
