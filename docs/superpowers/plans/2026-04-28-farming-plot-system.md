# Farming / Plot System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stardew-style farming loop on the existing `TerrainCellGrid` — plant seeds, daily growth conditions tick, mature into a `CropHarvestable` (one-shot or perennial), with an opt-in destruction path (axe → wood) and a Hold-E interaction menu. Persists across save/load and hibernation with zero new save state.

**Architecture:** Cell-grid native (the `TerrainCell` already has `IsPlowed` / `PlantedCropId` / `GrowthTimer` / `TimeSinceLastWatered` pre-wired). `FarmGrowthSystem` runs on `TimeManager.OnNewDay` server-only. `CropHarvestable : Harvestable` reuses the existing harvest interaction. Same `PostWakeSweep` reconstructs harvestables on hibernation-wake AND save-load. No new persistence layer — the cell encodes everything.

**Tech Stack:** Unity 2022 LTS, Unity Netcode for GameObjects (NGO), NUnit + Unity Test Framework (EditMode for pure C# math, manual playmode for network/MonoBehaviour).

**Spec:** [docs/superpowers/specs/2026-04-28-farming-plot-system-design.md](../specs/2026-04-28-farming-plot-system-design.md). Each task references its relevant spec sections instead of re-explaining the design.

**Testing strategy.** Unity makes pure-TDD impractical for `MonoBehaviour` / `NetworkBehaviour` paths. The plan follows the existing project pattern ([Assets/Tests/EditMode/Hunger/NeedHungerMathTests.cs](../../Assets/Tests/EditMode/Hunger/NeedHungerMathTests.cs)): extract pure C# math into a separate testable class (e.g., `FarmGrowthPipeline`, `MacroSimulatorCropMath`), unit-test it in EditMode with NUnit, and leave the thin Unity wrapper (`FarmGrowthSystem : MonoBehaviour`) for manual playmode acceptance testing against the §12 criteria.

**Conventions:**
- Every commit message starts with `feat(farming):` / `refactor(farming):` / `test(farming):` / `docs(farming):`. The spec calls these "the farming spec" — use that string in commit bodies if useful.
- All new gameplay code goes under `Assets/Scripts/Farming/` (new folder) unless a different path is explicitly named.
- Tests go under `Assets/Tests/EditMode/Farming/` with its own asmdef.

---

## File Structure (created / modified)

**New, gameplay (`Assets/Scripts/Farming/`):**
- `CropSO.cs`, `CropRegistry.cs`, `SeedSO.cs`, `WateringCanSO.cs` — content layer.
- `FarmGrowthPipeline.cs` (pure C#), `FarmGrowthSystem.cs` (MonoBehaviour wrapper).
- `CropPlacementManager.cs`.
- `CharacterAction_PlaceCrop.cs`, `CharacterAction_WaterCrop.cs`.
- `CropHarvestable.cs`.
- `CropVisualSpawner.cs`.

**New, character actions (`Assets/Scripts/Character/CharacterActions/`):**
- `CharacterAction_DestroyHarvestable.cs`.

**New, interactable (`Assets/Scripts/Interactable/`):**
- `HarvestInteractionOption.cs`.

**New, UI (`Assets/Scripts/UI/Interaction/`):**
- `UI_InteractionMenu.cs`, `UI_InteractionOptionRow.cs`.

**New, simulation (`Assets/Scripts/World/MapSystem/`):**
- `MacroSimulatorCropMath.cs` (pure C# helper for the catch-up algorithm).

**Modified:**
- `Assets/Scripts/Interactable/Harvestable.cs` (4 numbered concerns — see Task 2).
- `Assets/Scripts/World/MapSystem/MapController.cs`.
- `Assets/Scripts/World/MapSystem/MacroSimulator.cs`.
- `Assets/Scripts/Core/GameLauncher.cs`.
- `Assets/Scripts/Character/Character.cs`.
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`.
- `Assets/Scripts/SaveLoad/SaveManager.cs`.

**Tests (`Assets/Tests/EditMode/Farming/`):**
- `Farming.Tests.asmdef`.
- `CropRegistryTests.cs`, `CropSOValidationTests.cs`.
- `HarvestablePredicateTests.cs`.
- `FarmGrowthPipelineTests.cs`.
- `MacroSimulatorCropMathTests.cs`.

**Assets:**
- `Assets/Resources/Data/Farming/Crops/Crop_Wheat.asset`, `Crop_Flower.asset`, `Crop_AppleTree.asset`.
- `Assets/Resources/Data/Items/Item_Seed_Wheat.asset`, `Item_Seed_Flower.asset`, `Item_Seed_AppleSapling.asset`, `Item_WateringCan.asset`, `Item_Wood.asset`, `Item_Apple.asset`, `Item_Axe.asset`.
- `Assets/Prefabs/Farming/CropHarvestable_Wheat.prefab`, `CropHarvestable_Flower.prefab`, `CropHarvestable_AppleTree.prefab`.
- `Assets/Resources/UI/UI_InteractionMenu.prefab`.

**Docs:**
- `wiki/systems/farming.md` (new).
- `.agent/skills/farming/SKILL.md` (new).
- `wiki/systems/terrain-and-weather.md` (updated: remove farming fields from "open questions").

---

## Task 1: Content SOs + CropRegistry

References spec §3.1, §3.2, §3.3, §3.4. Pure-data layer; no behaviour. Compile-only at the end.

**Files:**
- Create: `Assets/Scripts/Farming/CropSO.cs`
- Create: `Assets/Scripts/Farming/CropRegistry.cs`
- Create: `Assets/Scripts/Farming/SeedSO.cs`
- Create: `Assets/Scripts/Farming/WateringCanSO.cs`
- Create: `Assets/Tests/EditMode/Farming/Farming.Tests.asmdef`
- Create: `Assets/Tests/EditMode/Farming/CropRegistryTests.cs`
- Create: `Assets/Tests/EditMode/Farming/CropSOValidationTests.cs`

- [ ] **Step 1: Create the test asmdef**

`Assets/Tests/EditMode/Farming/Farming.Tests.asmdef`:
```json
{
    "name": "Farming.Tests",
    "rootNamespace": "MWI.Tests.Farming",
    "references": ["GUID:27619889b8ba8c24980f49ee34dbb44a", "GUID:0acc523941302664db1f4e527237feb3", "Assembly-CSharp"],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

The two GUIDs are the standard NUnit + UnityEngine.TestRunner references (already used by [Hunger.Tests.asmdef](../../Assets/Tests/EditMode/Hunger/Hunger.Tests.asmdef) — copy them directly from there if the GUIDs differ on this branch).

- [ ] **Step 2: Write the failing CropRegistry test**

`Assets/Tests/EditMode/Farming/CropRegistryTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using MWI.Farming;

namespace MWI.Tests.Farming
{
    public class CropRegistryTests
    {
        [TearDown]
        public void TearDown() => CropRegistry.Clear();

        [Test]
        public void Get_ReturnsNull_WhenNotInitialized()
        {
            CropRegistry.Clear();
            Assert.IsNull(CropRegistry.Get("anything"));
        }

        [Test]
        public void Get_ReturnsNull_ForUnknownId()
        {
            CropRegistry.InitializeForTests(new CropSO[0]);
            Assert.IsNull(CropRegistry.Get("nope"));
        }

        [Test]
        public void Get_ReturnsCropSO_AfterInitialize()
        {
            var crop = ScriptableObject.CreateInstance<CropSO>();
            crop.SetIdForTests("wheat");
            CropRegistry.InitializeForTests(new[] { crop });

            Assert.AreSame(crop, CropRegistry.Get("wheat"));
        }

        [Test]
        public void Get_NullId_ReturnsNull_WithoutThrow()
        {
            CropRegistry.InitializeForTests(new CropSO[0]);
            Assert.IsNull(CropRegistry.Get(null));
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Open Unity → Window → General → Test Runner → EditMode → Run All. Expected: compile errors (`CropSO`, `CropRegistry` don't exist).

- [ ] **Step 4: Implement `CropSO`**

`Assets/Scripts/Farming/CropSO.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Farming
{
    /// <summary>
    /// Content definition for one crop type. See farming spec §3.1.
    /// Loaded into <see cref="CropRegistry"/> at game launch from Resources/Data/Farming/Crops.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Farming/Crop")]
    public class CropSO : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private int _daysToMature = 4;
        [SerializeField] private float _minMoistureForGrowth = 0.3f;
        [SerializeField] private float _plantDuration = 1f;
        [SerializeField] private ItemSO _produceItem;
        [SerializeField] private int _produceCount = 1;
        [SerializeField] private ItemSO _requiredHarvestTool;
        [SerializeField] private Sprite[] _stageSprites;          // length == _daysToMature
        [SerializeField] private GameObject _harvestablePrefab;

        [Header("Perennial (apple tree, berry bush)")]
        [SerializeField] private bool _isPerennial;
        [SerializeField] private int _regrowDays = 3;

        [Header("Destruction (axe / pickaxe etc.)")]
        [SerializeField] private bool _allowDestruction;
        [SerializeField] private ItemSO _requiredDestructionTool;
        [SerializeField] private List<ItemSO> _destructionOutputs = new List<ItemSO>();
        [SerializeField] private int _destructionOutputCount = 1;
        [SerializeField] private float _destructionDuration = 3f;

        public string Id => _id;
        public string DisplayName => _displayName;
        public int DaysToMature => _daysToMature;
        public float MinMoistureForGrowth => _minMoistureForGrowth;
        public float PlantDuration => _plantDuration;
        public ItemSO ProduceItem => _produceItem;
        public int ProduceCount => _produceCount;
        public ItemSO RequiredHarvestTool => _requiredHarvestTool;
        public bool IsPerennial => _isPerennial;
        public int RegrowDays => _regrowDays;
        public bool AllowDestruction => _allowDestruction;
        public ItemSO RequiredDestructionTool => _requiredDestructionTool;
        public IReadOnlyList<ItemSO> DestructionOutputs => _destructionOutputs;
        public int DestructionOutputCount => _destructionOutputCount;
        public float DestructionDuration => _destructionDuration;
        public GameObject HarvestablePrefab => _harvestablePrefab;

        /// <summary>
        /// Growing-stage sprite. Caller must guard against growthTimer >= DaysToMature
        /// (the mature visual lives on CropHarvestable._readySprite). Clamp is defensive only.
        /// </summary>
        public Sprite GetStageSprite(int growthTimer)
        {
            if (_stageSprites == null || _stageSprites.Length == 0) return null;
            return _stageSprites[Mathf.Clamp(growthTimer, 0, _stageSprites.Length - 1)];
        }

#if UNITY_EDITOR
        // Test-only seam used by EditMode tests where we cannot assign serialised fields via Inspector.
        public void SetIdForTests(string id) => _id = id;
        public void SetDaysToMatureForTests(int days) => _daysToMature = days;
        public void SetMinMoistureForTests(float m) => _minMoistureForGrowth = m;
        public void SetIsPerennialForTests(bool p) => _isPerennial = p;
        public void SetRegrowDaysForTests(int d) => _regrowDays = d;

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(_id))
                Debug.LogWarning($"[CropSO] {name}: _id is empty. The cell uses Id as the persistence key — set this field.");
            if (_produceItem == null)
                Debug.LogWarning($"[CropSO] {name}: _produceItem is null.");
            if (_stageSprites != null && _stageSprites.Length != _daysToMature)
                Debug.LogWarning($"[CropSO] {name}: _stageSprites.Length ({_stageSprites.Length}) should equal _daysToMature ({_daysToMature}). The mature visual lives on CropHarvestable._readySprite, not in _stageSprites.");
            if (_isPerennial && (_regrowDays < 1 || _regrowDays > _daysToMature))
                Debug.LogWarning($"[CropSO] {name}: perennial _regrowDays must be in [1, _daysToMature].");
        }
#endif
    }
}
```

- [ ] **Step 5: Implement `CropRegistry`**

`Assets/Scripts/Farming/CropRegistry.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Farming
{
    /// <summary>
    /// Static O(1) lookup from CropSO.Id → CropSO. Mirrors TerrainTypeRegistry. See spec §3.2.
    ///
    /// Initialise() is called once from GameLauncher.LaunchSequence after scene load.
    /// Clear() is called from SaveManager.ResetForNewSession.
    ///
    /// MUST be initialised before any MapController.WakeUp() or save-restore that reads cells
    /// with PlantedCropId set — see spec §9.3.
    /// </summary>
    public static class CropRegistry
    {
        private static readonly Dictionary<string, CropSO> _byId = new Dictionary<string, CropSO>();
        private static bool _initialised;

        public static bool IsInitialised => _initialised;

        public static void Initialize()
        {
            if (_initialised) return;
            var crops = Resources.LoadAll<CropSO>("Data/Farming/Crops");
            for (int i = 0; i < crops.Length; i++)
                Register(crops[i]);
            _initialised = true;
            Debug.Log($"[CropRegistry] Initialised with {_byId.Count} crop(s).");
        }

        public static void Clear()
        {
            _byId.Clear();
            _initialised = false;
        }

        public static CropSO Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var crop) ? crop : null;
        }

        private static void Register(CropSO crop)
        {
            if (crop == null || string.IsNullOrEmpty(crop.Id)) return;
            if (_byId.ContainsKey(crop.Id))
            {
                Debug.LogError($"[CropRegistry] Duplicate Id '{crop.Id}' on {crop.name}; overwriting.");
            }
            _byId[crop.Id] = crop;
        }

#if UNITY_EDITOR
        // Test seam — bypass Resources.LoadAll, inject crops directly.
        public static void InitializeForTests(IEnumerable<CropSO> crops)
        {
            Clear();
            foreach (var c in crops) Register(c);
            _initialised = true;
        }
#endif
    }
}
```

- [ ] **Step 6: Implement `SeedSO` and `WateringCanSO`**

`Assets/Scripts/Farming/SeedSO.cs`:
```csharp
using UnityEngine;

namespace MWI.Farming
{
    /// <summary>Seed item — when held in active hand, pressing E starts crop placement. See spec §3.3.</summary>
    [CreateAssetMenu(menuName = "Game/Items/Seed")]
    public class SeedSO : MiscItemSO   // adjust base class to whatever the project's plain-item leaf is
    {
        [SerializeField] private CropSO _cropToPlant;
        public CropSO CropToPlant => _cropToPlant;
    }
}
```

> If the project uses a different naming for "plain" items (no `MiscItemSO`), inherit from whatever concrete class `MiscInstance` belongs to or directly from `ItemSO` and override `CreateInstance()` to return a `MiscInstance`. Check [Assets/Scripts/Item/MiscInstance.cs](../../Assets/Scripts/Item/MiscInstance.cs) for the matching SO base.

`Assets/Scripts/Farming/WateringCanSO.cs`:
```csharp
using UnityEngine;

namespace MWI.Farming
{
    /// <summary>Watering can — when held, pressing E starts watering mode. See spec §3.4.</summary>
    [CreateAssetMenu(menuName = "Game/Items/WateringCan")]
    public class WateringCanSO : MiscItemSO
    {
        [SerializeField] private float _moistureSetTo = 1f;
        public float MoistureSetTo => _moistureSetTo;
    }
}
```

- [ ] **Step 7: Write the CropSO validation tests**

`Assets/Tests/EditMode/Farming/CropSOValidationTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using MWI.Farming;

namespace MWI.Tests.Farming
{
    public class CropSOValidationTests
    {
        [Test]
        public void GetStageSprite_OutOfRange_ClampsDefensively()
        {
            var crop = ScriptableObject.CreateInstance<CropSO>();
            // No sprites set → returns null safely.
            Assert.IsNull(crop.GetStageSprite(0));
            Assert.IsNull(crop.GetStageSprite(99));
        }

        [Test]
        public void Defaults_AreSensible()
        {
            var crop = ScriptableObject.CreateInstance<CropSO>();
            Assert.AreEqual(4, crop.DaysToMature);
            Assert.AreEqual(0.3f, crop.MinMoistureForGrowth);
            Assert.IsFalse(crop.IsPerennial);
            Assert.IsFalse(crop.AllowDestruction);
        }
    }
}
```

- [ ] **Step 8: Run tests**

Test Runner → EditMode → Run All. Expected: 6 tests pass (4 in CropRegistry, 2 in CropSOValidation).

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/Farming/ Assets/Tests/EditMode/Farming/
git commit -m "feat(farming): add CropSO + CropRegistry + Seed/WateringCan SOs

Pure data layer for the farming spec. CropSO holds growth + perennial +
destruction config; CropRegistry is the static O(1) lookup mirrored on
TerrainTypeRegistry; SeedSO/WateringCanSO are placement-active item types.

EditMode tests cover registry lookup edge cases and defensive clamping.

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §3."
```

---

## Task 2: Refactor `Harvestable.cs` for the four farming concerns

References spec §6.1 + §11 modified-files entry for `Harvestable.cs`. This is the largest single-file change. Existing wild Harvestables must continue to work with destruction defaults off.

**Files:**
- Modify: `Assets/Scripts/Interactable/Harvestable.cs`
- Create: `Assets/Scripts/Interactable/HarvestInteractionOption.cs`
- Create: `Assets/Tests/EditMode/Farming/HarvestablePredicateTests.cs`

- [ ] **Step 1: Create `HarvestInteractionOption`**

`Assets/Scripts/Interactable/HarvestInteractionOption.cs`:
```csharp
using System;
using UnityEngine;

/// <summary>
/// One row in UI_InteractionMenu. Returned by Harvestable.GetInteractionOptions(Character).
/// See spec §6.2.
/// </summary>
public readonly struct HarvestInteractionOption
{
    public readonly string Label;
    public readonly Sprite Icon;
    public readonly string OutputPreview;       // e.g. "4× Apple"
    public readonly bool IsAvailable;
    public readonly string UnavailableReason;   // e.g. "Requires Axe"
    public readonly Func<Character, CharacterAction> ActionFactory;

    public HarvestInteractionOption(
        string label,
        Sprite icon,
        string outputPreview,
        bool isAvailable,
        string unavailableReason,
        Func<Character, CharacterAction> actionFactory)
    {
        Label = label;
        Icon = icon;
        OutputPreview = outputPreview;
        IsAvailable = isAvailable;
        UnavailableReason = unavailableReason;
        ActionFactory = actionFactory;
    }
}
```

- [ ] **Step 2: Write predicate tests against the planned `Harvestable` API**

`Assets/Tests/EditMode/Farming/HarvestablePredicateTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;

namespace MWI.Tests.Farming
{
    /// <summary>
    /// Pure predicate tests against Harvestable.CanHarvestWith / CanDestroyWith.
    /// Avoid spawning the full GameObject — Harvestable inherits from InteractableObject
    /// which is a MonoBehaviour, so we use AddComponent on a temp GameObject and tear
    /// it down per test.
    /// </summary>
    public class HarvestablePredicateTests
    {
        private GameObject _go;
        private Harvestable _h;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestHarvestable");
            _h = _go.AddComponent<Harvestable>();
            _h.SetOutputItemsForTests(new System.Collections.Generic.List<ItemSO> {
                ScriptableObject.CreateInstance<MiscItemSO>()
            });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void CanHarvestWith_NoToolRequired_AlwaysAccepts()
        {
            _h.SetRequiredHarvestToolForTests(null);
            Assert.IsTrue(_h.CanHarvestWith(null));
            Assert.IsTrue(_h.CanHarvestWith(ScriptableObject.CreateInstance<MiscItemSO>()));
        }

        [Test]
        public void CanHarvestWith_RequiredToolMatch()
        {
            var sickle = ScriptableObject.CreateInstance<MiscItemSO>();
            _h.SetRequiredHarvestToolForTests(sickle);
            Assert.IsTrue(_h.CanHarvestWith(sickle));
            Assert.IsFalse(_h.CanHarvestWith(null));
            Assert.IsFalse(_h.CanHarvestWith(ScriptableObject.CreateInstance<MiscItemSO>()));
        }

        [Test]
        public void CanDestroyWith_DefaultsOff()
        {
            Assert.IsFalse(_h.CanDestroyWith(null));
            Assert.IsFalse(_h.CanDestroyWith(ScriptableObject.CreateInstance<MiscItemSO>()));
        }

        [Test]
        public void CanDestroyWith_AllowedAndToolMatches()
        {
            var axe = ScriptableObject.CreateInstance<MiscItemSO>();
            _h.SetAllowDestructionForTests(true);
            _h.SetRequiredDestructionToolForTests(axe);
            Assert.IsTrue(_h.CanDestroyWith(axe));
            Assert.IsFalse(_h.CanDestroyWith(null));
        }

        [Test]
        public void CanDestroyWith_AllowedAndAnyToolWorks_WhenRequiredIsNull()
        {
            _h.SetAllowDestructionForTests(true);
            _h.SetRequiredDestructionToolForTests(null);
            Assert.IsTrue(_h.CanDestroyWith(null));
            Assert.IsTrue(_h.CanDestroyWith(ScriptableObject.CreateInstance<MiscItemSO>()));
        }
    }
}
```

(`MiscItemSO` is the project's concrete plain-item leaf — adjust if the project's name differs. If the closest concrete match is something else, replace all occurrences.)

- [ ] **Step 3: Run tests to verify they fail**

Test Runner → EditMode → Run All. Expected: compile errors — `CanHarvestWith`, `CanDestroyWith`, the test seams don't exist on `Harvestable`.

- [ ] **Step 4: Refactor `Harvestable.cs` — add fields, helpers, and predicates**

Modify [Assets/Scripts/Interactable/Harvestable.cs](../../Assets/Scripts/Interactable/Harvestable.cs):

Add these fields to the existing field block:
```csharp
[Header("Yield (the default 'pick' interaction)")]
[Tooltip("If null, bare hands (or any held item) work for the yield path.")]
[SerializeField] private ItemSO _requiredHarvestTool;

[Header("Destruction (axe / pickaxe etc.)")]
[SerializeField] private bool _allowDestruction;
[SerializeField] private ItemSO _requiredDestructionTool;
[SerializeField] private List<ItemSO> _destructionOutputs = new List<ItemSO>();
[SerializeField] private int _destructionOutputCount = 1;
[SerializeField] private float _destructionDuration = 3f;
```

Add accessors near the existing public properties:
```csharp
public ItemSO RequiredHarvestTool => _requiredHarvestTool;
public bool AllowDestruction => _allowDestruction;
public ItemSO RequiredDestructionTool => _requiredDestructionTool;
public IReadOnlyList<ItemSO> DestructionOutputs => _destructionOutputs;
public int DestructionOutputCount => _destructionOutputCount;
public float DestructionDuration => _destructionDuration;

public bool CanHarvestWith(ItemSO heldItem)
{
    if (!CanHarvest()) return false;
    return _requiredHarvestTool == null || heldItem == _requiredHarvestTool;
}

public bool CanDestroyWith(ItemSO heldItem)
{
    if (!_allowDestruction) return false;
    return _requiredDestructionTool == null || heldItem == _requiredDestructionTool;
}
```

- [ ] **Step 5: Refactor `Deplete()` to expose `OnDepleted()` virtual hook + helpers**

Replace the existing private `Deplete()` body and surrounding helpers with:
```csharp
/// <summary>
/// Called when _currentHarvestCount reaches _maxHarvestCount. Overridable so
/// CropHarvestable can branch one-shot vs perennial. Existing wild-harvestable
/// behaviour is preserved by the base implementation.
/// </summary>
protected virtual void Deplete()
{
    _isDepleted = true;

    if (_visualRoot != null)
        _visualRoot.SetActive(false);

    if (MWI.Time.TimeManager.Instance != null && _respawnDelayDays > 0)
    {
        _targetRespawnDay = MWI.Time.TimeManager.Instance.CurrentDay + _respawnDelayDays;
        MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
    }

    Debug.Log($"<color=orange>[Harvest]</color> {gameObject.name} is depleted. Respawn scheduled for day {_targetRespawnDay}.");

    OnDepleted();
}

/// <summary>Hook for subclasses to react to depletion (e.g. CropHarvestable updates the cell). No-op base.</summary>
protected virtual void OnDepleted() { }

/// <summary>
/// Restore "ready" state without re-running Respawn(). Used by CropHarvestable.SetReady() on
/// perennial refill — we already own the cell state, so we don't need the base respawn pipeline.
/// </summary>
protected void ResetHarvestState()
{
    _currentHarvestCount = 0;
    _isDepleted = false;
    if (MWI.Time.TimeManager.Instance != null)
        MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
    if (_visualRoot != null)
        _visualRoot.SetActive(true);
}

/// <summary>
/// Mark depleted on a fresh spawn (post-load reconstruction) WITHOUT firing OnDepleted,
/// scheduling respawn, or running visual-root toggling. Used only by CropHarvestable.SetDepleted().
/// </summary>
protected void MarkDepletedNoCallback()
{
    _isDepleted = true;
    _currentHarvestCount = _maxHarvestCount;
    // Visual handling: CropHarvestable swaps to _depletedSprite via NetworkVariable callback.
    // We do NOT touch _visualRoot here — that is the wild-harvestable disappear-on-deplete behaviour.
}
```

The existing `Harvest()` already calls `Deplete()` directly when the count hits max, so subclasses get the hook for free.

- [ ] **Step 6: Add destruction surface — `DestroyForOutputs` + `OnDestroyed`**

Append to `Harvestable.cs`:
```csharp
/// <summary>
/// Server-only. Spawns destruction outputs as WorldItems and despawns this harvestable.
/// Called by CharacterAction_DestroyHarvestable.OnApplyEffect.
/// </summary>
public void DestroyForOutputs()
{
    if (!IsServer) return;

    for (int i = 0; i < _destructionOutputCount; i++)
        for (int j = 0; j < _destructionOutputs.Count; j++)
            SpawnDestructionItem(_destructionOutputs[j]);

    OnDestroyed();
    if (NetworkObject.IsSpawned)
        NetworkObject.Despawn();
}

/// <summary>Hook for subclasses (e.g. CropHarvestable clears the cell). No-op base.</summary>
protected virtual void OnDestroyed() { }

private void SpawnDestructionItem(ItemSO item)
{
    if (item == null || item.WorldItemPrefab == null) return;
    var pos = transform.position + Random.insideUnitSphere * 0.5f;  // small scatter
    pos.y = transform.position.y;
    var go = Instantiate(item.WorldItemPrefab, pos, Quaternion.identity);
    if (go.TryGetComponent<NetworkObject>(out var netObj) && !netObj.IsSpawned)
        netObj.Spawn(true);
    // Existing project pattern: WorldItem auto-binds its ItemSO via Initialize on spawn.
    // If your WorldItem requires explicit Initialize, call it here.
}
```

> If the project already has a server-side WorldItem spawn helper (e.g. on `MapController` or `ItemSpawner`), use it instead of the inline `Instantiate + Spawn`. Search for "WorldItemPrefab" / "SpawnWorldItem" before duplicating.

- [ ] **Step 7: Update `Interact()` to dispatch tap-E quick yield path only**

Replace the existing `Interact(Character interactor)` body with:
```csharp
public override void Interact(Character interactor)
{
    if (interactor == null || interactor.CharacterActions == null) return;

    // Tap-E always tries the yield path. Destruction is menu-only (UI_InteractionMenu).
    var held = interactor.CharacterEquipment != null
        ? interactor.CharacterEquipment.GetActiveHandItem()?.ItemSO
        : null;

    if (CanHarvestWith(held))
    {
        var gatherAction = new CharacterHarvestAction(interactor, this);
        interactor.CharacterActions.ExecuteAction(gatherAction);
    }
    // Else: no-op. The player can hold E to see the menu (which may offer destruction or list reasons).
}
```

- [ ] **Step 8: Add `GetInteractionOptions(Character)`**

Append:
```csharp
/// <summary>
/// Returns the rows shown by UI_InteractionMenu on Hold-E. See spec §6.2.
/// Includes greyed-out (unavailable) rows so the player learns what tools unlock what.
/// </summary>
public virtual IList<HarvestInteractionOption> GetInteractionOptions(Character actor)
{
    var list = new List<HarvestInteractionOption>(2);
    var held = actor != null && actor.CharacterEquipment != null
        ? actor.CharacterEquipment.GetActiveHandItem()?.ItemSO
        : null;

    // --- Yield row (always present if there are output items) ---
    if (_outputItems != null && _outputItems.Count > 0)
    {
        bool yieldOk = CanHarvestWith(held);
        string yieldReason = null;
        if (!yieldOk)
        {
            if (_isDepleted) yieldReason = "Already harvested";
            else if (_requiredHarvestTool != null) yieldReason = $"Requires {_requiredHarvestTool.ItemName}";
            else yieldReason = "Cannot harvest";
        }
        list.Add(new HarvestInteractionOption(
            label: $"Pick {_outputItems[0].ItemName}",
            icon: _outputItems[0].Icon,
            outputPreview: $"{_maxHarvestCount}× {_outputItems[0].ItemName}",
            isAvailable: yieldOk,
            unavailableReason: yieldReason,
            actionFactory: ch => new CharacterHarvestAction(ch, this)));
    }

    // --- Destruction row (only if opt-in) ---
    if (_allowDestruction && _destructionOutputs.Count > 0)
    {
        bool destroyOk = CanDestroyWith(held);
        string destroyReason = destroyOk ? null
            : (_requiredDestructionTool != null ? $"Requires {_requiredDestructionTool.ItemName}" : "Cannot destroy");
        list.Add(new HarvestInteractionOption(
            label: $"Destroy",
            icon: _destructionOutputs[0].Icon,
            outputPreview: $"{_destructionOutputCount}× {_destructionOutputs[0].ItemName}",
            isAvailable: destroyOk,
            unavailableReason: destroyReason,
            actionFactory: ch => new CharacterAction_DestroyHarvestable(ch, this)));
    }

    return list;
}
```

- [ ] **Step 9: Add the test seams (gated by `UNITY_EDITOR`)**

Append to `Harvestable.cs`:
```csharp
#if UNITY_EDITOR
    public void SetOutputItemsForTests(List<ItemSO> items) => _outputItems = items;
    public void SetRequiredHarvestToolForTests(ItemSO tool) => _requiredHarvestTool = tool;
    public void SetAllowDestructionForTests(bool b) => _allowDestruction = b;
    public void SetRequiredDestructionToolForTests(ItemSO tool) => _requiredDestructionTool = tool;
#endif
```

- [ ] **Step 10: Run tests to verify they pass**

Test Runner → EditMode → Run All. Expected: 5 new tests pass in `HarvestablePredicateTests`. Existing tests unaffected.

- [ ] **Step 11: Manual smoke test — wild Harvestable still works**

Open a scene with an existing scene-placed `Harvestable` (e.g. a tree/rock prefab in any test scene). Enter Play Mode. Walk up to it, press E → harvest action runs as before. Repeat until depleted; verify visual hides + respawn timer fires on day rollover. The defaults `_requiredHarvestTool = null` and `_allowDestruction = false` should keep behaviour identical.

- [ ] **Step 12: Commit**

```bash
git add Assets/Scripts/Interactable/Harvestable.cs Assets/Scripts/Interactable/HarvestInteractionOption.cs Assets/Tests/EditMode/Farming/HarvestablePredicateTests.cs
git commit -m "refactor(farming): extend Harvestable with yield-tool, destruction, and menu options

Four numbered concerns from the farming spec §11:
1. Extract OnDepleted() virtual + ResetHarvestState() / MarkDepletedNoCallback() helpers
2. Add _requiredHarvestTool + CanHarvestWith()
3. Add destruction fields + CanDestroyWith() + DestroyForOutputs() + OnDestroyed() virtual
4. Update Interact() to yield-only, add GetInteractionOptions() for UI_InteractionMenu

Wild Harvestables (rocks, scene trees) keep identical behaviour with the new
fields defaulted off. EditMode tests cover the predicate matrix.

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §6.1, §11."
```

---

## Task 3: `CharacterAction_DestroyHarvestable`

References spec §6.1.

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_DestroyHarvestable.cs`

- [ ] **Step 1: Implement the action**

```csharp
/// <summary>
/// Generic destruction of any Harvestable (apple tree, wild forest tree, etc.). See spec §6.1.
/// Queued by UI_InteractionMenu when the player picks the "destroy" option.
/// NPC GOAP/BT can construct it directly without UI.
/// </summary>
public class CharacterAction_DestroyHarvestable : CharacterAction
{
    private readonly Harvestable _target;

    public CharacterAction_DestroyHarvestable(Character actor, Harvestable target)
        : base(actor, target != null ? target.DestructionDuration : 3f)
    {
        _target = target;
    }

    public override string ActionName => "Destroy";

    public override bool CanExecute()
    {
        if (_target == null) return false;
        var held = character.CharacterEquipment != null
            ? character.CharacterEquipment.GetActiveHandItem()?.ItemSO
            : null;
        return _target.CanDestroyWith(held);
    }

    public override void OnStart() { /* face the target; existing CharacterActions handles animation flag */ }

    public override void OnApplyEffect()
    {
        if (_target == null) return;
        _target.DestroyForOutputs();
    }
}
```

- [ ] **Step 2: Compile-check (no test — full behaviour is exercised in Task 4 + Task 12 acceptance)**

Switch to Unity Editor, wait for compile, confirm no errors.

- [ ] **Step 3: Add a temporary `[ContextMenu]` test trigger on `Harvestable` (throwaway, removed at Task 12)**

Append to `Harvestable.cs`:
```csharp
#if UNITY_EDITOR
    [ContextMenu("DEV: Destroy via local player")]
    private void Dev_DestroyViaLocalPlayer()
    {
        var player = FindObjectOfType<PlayerController>();
        if (player == null) { Debug.LogError("No PlayerController in scene."); return; }
        if (!CanDestroyWith(player.GetComponent<Character>().CharacterEquipment?.GetActiveHandItem()?.ItemSO))
        {
            Debug.LogWarning("Player can't destroy this — wrong tool.");
            return;
        }
        player.GetComponent<Character>().CharacterActions.ExecuteAction(
            new CharacterAction_DestroyHarvestable(player.GetComponent<Character>(), this));
    }
#endif
```

This context menu is throwaway scaffolding — it gets removed in Task 12 (Step 1).

- [ ] **Step 4: Manual playmode smoke**

Place a scene `Harvestable` configured with `_allowDestruction=true`, `_requiredDestructionTool=null` (any tool works), `_destructionOutputs=[Item_Wood]`. Enter Play Mode. Right-click the Harvestable in the Hierarchy → "DEV: Destroy via local player". After `_destructionDuration` seconds: wood items appear at the harvestable's position, harvestable despawns.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_DestroyHarvestable.cs Assets/Scripts/Interactable/Harvestable.cs
git commit -m "feat(farming): add CharacterAction_DestroyHarvestable

Generic destruction action triggered via UI_InteractionMenu (player) or
GOAP/BT (NPC). Mirrors the duration/effect pattern of CharacterHarvestAction.

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §6.1."
```

---

## Task 4: `CropHarvestable` subclass

References spec §6 + §9.1 + §9.2. Uses the `startDepleted` parameter — the load-bearing line for save/load correctness.

**Files:**
- Create: `Assets/Scripts/Farming/CropHarvestable.cs`

- [ ] **Step 1: Implement the subclass**

```csharp
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Spawned by FarmGrowthSystem when a crop matures. See spec §6.
    /// One-shot crops despawn on harvest; perennials stay standing and refill via the cell.
    /// </summary>
    public class CropHarvestable : Harvestable
    {
        [Header("Crop visuals (override Harvestable._visualRoot)")]
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private Sprite _readySprite;
        [SerializeField] private Sprite _depletedSprite;

        public NetworkVariable<bool> IsDepleted = new NetworkVariable<bool>(false);

        public int CellX { get; private set; }
        public int CellZ { get; private set; }
        public TerrainCellGrid Grid { get; private set; }

        private CropSO _crop;
        private MapController _map;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            IsDepleted.OnValueChanged += OnIsDepletedChanged;
            // Apply the current value once on join (covers late-joiners and host).
            ApplyVisualSwap(IsDepleted.Value);
        }

        public override void OnNetworkDespawn()
        {
            IsDepleted.OnValueChanged -= OnIsDepletedChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Server-only. Called by FarmGrowthSystem.SpawnCropHarvestable.
        /// startDepleted reflects the cell's encoded refill state — true on post-wake/post-load
        /// of a depleted perennial, false on fresh maturity. Spec §9.2.
        /// </summary>
        public void InitializeFromCell(TerrainCellGrid grid, MapController map, int x, int z, CropSO crop, bool startDepleted)
        {
            Grid = grid; _map = map; CellX = x; CellZ = z; _crop = crop;

            // Configure base Harvestable from CropSO content.
            SetOutputItemsRuntime(new List<ItemSO> { crop.ProduceItem });
            SetMaxHarvestCountRuntime(1);   // one Interact() drops the whole yield in a burst
            SetIsDepletableRuntime(true);
            SetRespawnDelayDaysRuntime(0);  // we own post-deplete state, not the base timer

            // Mirror CropSO destruction settings onto base Harvestable so CanDestroyWith works.
            SetAllowDestructionForTests(crop.AllowDestruction);
            SetRequiredDestructionToolForTests(crop.RequiredDestructionTool);
            SetDestructionFieldsRuntime(crop.DestructionOutputs, crop.DestructionOutputCount, crop.DestructionDuration);

            if (startDepleted) SetDepleted();
            else                SetReady();
        }

        public void SetReady()
        {
            ResetHarvestState();
            IsDepleted.Value = false;
        }

        public void SetDepleted()
        {
            MarkDepletedNoCallback();
            IsDepleted.Value = true;
        }

        public void Refill() => SetReady();

        protected override void OnDepleted()
        {
            ref var cell = ref Grid.GetCellRef(CellX, CellZ);

            if (_crop.IsPerennial)
            {
                // Stay standing. Mark cell "depleted, refilling".
                cell.TimeSinceLastWatered = 0f;
                IsDepleted.Value = true;
            }
            else
            {
                ClearCellAndUnregister(ref cell);
                if (NetworkObject.IsSpawned) NetworkObject.Despawn();
            }

            _map.NotifyDirtyCells(new[] { _map.TerrainGrid.LinearIndex(CellX, CellZ) });
        }

        protected override void OnDestroyed()
        {
            ref var cell = ref Grid.GetCellRef(CellX, CellZ);
            ClearCellAndUnregister(ref cell);
            _map.NotifyDirtyCells(new[] { _map.TerrainGrid.LinearIndex(CellX, CellZ) });
            // Base class despawns the NetworkObject after OnDestroyed returns.
        }

        private void ClearCellAndUnregister(ref TerrainCell cell)
        {
            cell.PlantedCropId = null;
            cell.GrowthTimer = 0f;
            cell.TimeSinceLastWatered = -1f;
            // IsPlowed stays true so re-planting is one step.
            var sys = _map.GetComponent<FarmGrowthSystem>();
            if (sys != null) sys.UnregisterHarvestable(CellX, CellZ);
        }

        private void OnIsDepletedChanged(bool _, bool isNow) => ApplyVisualSwap(isNow);

        private void ApplyVisualSwap(bool depleted)
        {
            if (_spriteRenderer == null) return;
            _spriteRenderer.sprite = depleted ? _depletedSprite : _readySprite;
        }
    }
}
```

- [ ] **Step 2: Add the runtime-config helpers on `Harvestable.cs`**

Append to `Harvestable.cs` (these are server-only, runtime-only — no Inspector exposure needed):
```csharp
public void SetOutputItemsRuntime(List<ItemSO> items) => _outputItems = items;
public void SetMaxHarvestCountRuntime(int n) => _maxHarvestCount = n;
public void SetIsDepletableRuntime(bool b) => _isDepletable = b;
public void SetRespawnDelayDaysRuntime(int d) => _respawnDelayDays = d;
public void SetDestructionFieldsRuntime(IReadOnlyList<ItemSO> outputs, int count, float duration)
{
    _destructionOutputs = new List<ItemSO>(outputs);
    _destructionOutputCount = count;
    _destructionDuration = duration;
}
```

These are publicly callable (not Editor-gated) because `CropHarvestable.InitializeFromCell` is a runtime path.

- [ ] **Step 3: Compile-check**

Wait for Unity to compile. There will be reference errors to `_map.TerrainGrid` (Task 6 adds the public getter) and `_map.SendDirtyCellsClientRpc` / `LinearIndex` (Task 6 adds them). Stub them temporarily on `MapController.cs`:
```csharp
// Temporary stubs — fully implemented in Task 6.
public TerrainCellGrid TerrainGrid => null;
[ClientRpc] public void SendDirtyCellsClientRpc(int[] indices, TerrainCellSaveData[] payload) { }
```

Add three helpers to `TerrainCellGrid.cs` if not already present (search the file first — these may exist under different names):
```csharp
public int LinearIndex(int x, int z) => z * Width + x;
public TerrainCell GetCellByIndex(int idx) => _cells[idx];           // by-value snapshot for serialisation
public void SetCellByIndex(int idx, TerrainCell cell) => _cells[idx] = cell;
```

(If the grid already exposes equivalents — `IndexOf`, `GetCell(int)`, `SetCell(int, ...)` — use the existing names and skip duplicates.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Farming/CropHarvestable.cs Assets/Scripts/Interactable/Harvestable.cs Assets/Scripts/World/MapSystem/MapController.cs Assets/Scripts/Terrain/TerrainCellGrid.cs
git commit -m "feat(farming): add CropHarvestable with one-shot/perennial branch + IsDepleted NetVar

CropHarvestable wraps the existing Harvestable for crop content:
- One-shot: harvest clears cell + despawns
- Perennial: harvest sets cell.TimeSinceLastWatered=0, swaps sprite, stays standing
- Save/load: InitializeFromCell(... startDepleted) reconstructs the right state
  from cell encoding — IsDepleted NetworkVariable fans out the sprite swap
- OnDestroyed (axe path) shares ClearCellAndUnregister with one-shot OnDepleted

Stub MapController.TerrainGrid + SendDirtyCellsClientRpc (real impl in next task).

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §6, §9."
```

---

## Task 5: `FarmGrowthPipeline` (pure C#) — TDD'd

References spec §4. The pipeline logic is testable in isolation by using a struct-of-state input/output. The MonoBehaviour wrapper is Task 6.

**Files:**
- Create: `Assets/Scripts/Farming/FarmGrowthPipeline.cs`
- Create: `Assets/Tests/EditMode/Farming/FarmGrowthPipelineTests.cs`

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/EditMode/Farming/FarmGrowthPipelineTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using MWI.Farming;
using MWI.Terrain;

namespace MWI.Tests.Farming
{
    public class FarmGrowthPipelineTests
    {
        private CropSO _wheat;       // one-shot, DaysToMature=4, MinMoisture=0.3
        private CropSO _appleTree;   // perennial, DaysToMature=4, RegrowDays=2

        [SetUp]
        public void SetUp()
        {
            _wheat = ScriptableObject.CreateInstance<CropSO>();
            _wheat.SetIdForTests("wheat");
            _wheat.SetDaysToMatureForTests(4);
            _wheat.SetMinMoistureForTests(0.3f);

            _appleTree = ScriptableObject.CreateInstance<CropSO>();
            _appleTree.SetIdForTests("apple");
            _appleTree.SetDaysToMatureForTests(4);
            _appleTree.SetMinMoistureForTests(0.3f);
            _appleTree.SetIsPerennialForTests(true);
            _appleTree.SetRegrowDaysForTests(2);

            CropRegistry.InitializeForTests(new[] { _wheat, _appleTree });
        }

        [TearDown]
        public void TearDown() => CropRegistry.Clear();

        [Test]
        public void GrowingCrop_WateredBelowThreshold_DoesNotAdvance()
        {
            var cell = MakeCell("wheat", growthTimer: 1f, moisture: 0.2f);
            var result = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(1f, cell.GrowthTimer);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.Stalled, result);
        }

        [Test]
        public void GrowingCrop_WateredAtThreshold_Advances()
        {
            var cell = MakeCell("wheat", growthTimer: 1f, moisture: 0.3f);
            var result = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(2f, cell.GrowthTimer);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.Grew, result);
        }

        [Test]
        public void GrowingCrop_CrossesMaturity_ReportsMatureSpawnNeeded_AndSetsReadySentinel()
        {
            var cell = MakeCell("wheat", growthTimer: 3f, moisture: 0.5f);
            var result = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(4f, cell.GrowthTimer);
            Assert.AreEqual(-1f, cell.TimeSinceLastWatered);   // sentinel "ready"
            Assert.AreEqual(FarmGrowthPipeline.Outcome.JustMatured, result);
        }

        [Test]
        public void LiveAndReady_NoOp()
        {
            var cell = MakeCell("apple", growthTimer: 4f, moisture: 0.5f, timeSinceLastWatered: -1f);
            var result = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(4f, cell.GrowthTimer);
            Assert.AreEqual(-1f, cell.TimeSinceLastWatered);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.NoOp, result);
        }

        [Test]
        public void DepletedPerennial_Watered_AdvancesRefillCounter()
        {
            var cell = MakeCell("apple", growthTimer: 4f, moisture: 0.5f, timeSinceLastWatered: 0f);
            var result = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(1f, cell.TimeSinceLastWatered);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.Refilling, result);
        }

        [Test]
        public void DepletedPerennial_HitsRegrowDays_FlipsToReady()
        {
            var cell = MakeCell("apple", growthTimer: 4f, moisture: 0.5f, timeSinceLastWatered: 1f);
            var result = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(-1f, cell.TimeSinceLastWatered);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.JustRefilled, result);
        }

        [Test]
        public void DepletedPerennial_Dry_DoesNotAdvance()
        {
            var cell = MakeCell("apple", growthTimer: 4f, moisture: 0.1f, timeSinceLastWatered: 0f);
            var result = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(0f, cell.TimeSinceLastWatered);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.Stalled, result);
        }

        [Test]
        public void OrphanCropId_ReturnsSkipped_DoesNotMutate()
        {
            var cell = MakeCell("nonexistent", growthTimer: 1f, moisture: 0.5f);
            var before = cell.GrowthTimer;
            var result = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(before, cell.GrowthTimer);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.OrphanCrop, result);
        }

        [Test]
        public void EmptyCell_NoOp()
        {
            var cell = new TerrainCell { IsPlowed = false, PlantedCropId = null };
            var result = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.NotPlanted, result);
        }

        private static TerrainCell MakeCell(string cropId, float growthTimer, float moisture, float timeSinceLastWatered = -1f)
            => new TerrainCell
            {
                IsPlowed = true,
                PlantedCropId = cropId,
                GrowthTimer = growthTimer,
                TimeSinceLastWatered = timeSinceLastWatered,
                Moisture = moisture
            };
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Test Runner → EditMode → Run All. Expected: compile errors (`FarmGrowthPipeline`, `Outcome` don't exist).

- [ ] **Step 3: Implement `FarmGrowthPipeline`**

`Assets/Scripts/Farming/FarmGrowthPipeline.cs`:
```csharp
using MWI.Terrain;

namespace MWI.Farming
{
    /// <summary>
    /// Pure C# daily-tick logic for one cell. No Unity dependencies.
    /// FarmGrowthSystem is the MonoBehaviour wrapper that calls this for every cell.
    /// See spec §4 and §9.2.
    /// </summary>
    public static class FarmGrowthPipeline
    {
        public enum Outcome
        {
            NotPlanted,    // empty cell, no work
            OrphanCrop,    // PlantedCropId not in registry — log once, skip
            Stalled,       // dry, growth/refill paused
            Grew,          // GrowthTimer += 1
            JustMatured,   // GrowthTimer crossed DaysToMature this tick — caller spawns CropHarvestable
            NoOp,          // live harvestable, ready, nothing to do
            Refilling,     // depleted perennial: TimeSinceLastWatered += 1
            JustRefilled,  // depleted perennial hit RegrowDays — caller calls harvestable.Refill()
        }

        public static Outcome AdvanceOneDay(ref TerrainCell cell)
        {
            if (!cell.IsPlowed || string.IsNullOrEmpty(cell.PlantedCropId))
                return Outcome.NotPlanted;

            var crop = CropRegistry.Get(cell.PlantedCropId);
            if (crop == null) return Outcome.OrphanCrop;

            // PHASE A — still growing
            if (cell.GrowthTimer < crop.DaysToMature)
            {
                if (cell.Moisture < crop.MinMoistureForGrowth) return Outcome.Stalled;
                cell.GrowthTimer += 1f;
                if (cell.GrowthTimer >= crop.DaysToMature)
                {
                    cell.GrowthTimer = crop.DaysToMature;
                    cell.TimeSinceLastWatered = -1f;   // sentinel: ready
                    return Outcome.JustMatured;
                }
                return Outcome.Grew;
            }

            // PHASE B — live harvestable, ready
            if (cell.TimeSinceLastWatered < 0f) return Outcome.NoOp;

            // PHASE C — live harvestable, depleted (perennial only by construction; one-shots clear the cell on harvest)
            if (cell.Moisture < crop.MinMoistureForGrowth) return Outcome.Stalled;
            cell.TimeSinceLastWatered += 1f;
            if (cell.TimeSinceLastWatered >= crop.RegrowDays)
            {
                cell.TimeSinceLastWatered = -1f;
                return Outcome.JustRefilled;
            }
            return Outcome.Refilling;
        }
    }
}
```

- [ ] **Step 4: Run tests, expect pass**

Test Runner → EditMode → Run All. Expected: 9 new tests in `FarmGrowthPipelineTests` pass. Total green.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Farming/FarmGrowthPipeline.cs Assets/Tests/EditMode/Farming/FarmGrowthPipelineTests.cs
git commit -m "feat(farming): add FarmGrowthPipeline pure-C# daily tick + tests

Three-branch pipeline (growing crop / live-and-ready / depleted-refilling)
extracted as pure C# so it can be EditMode-tested without spawning a scene.
9 tests cover threshold math, sentinel handling, orphan crops, and the
phase transitions JustMatured / JustRefilled.

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §4."
```

---

## Task 6: `FarmGrowthSystem` MonoBehaviour + `MapController` integration + `SendDirtyCellsClientRpc`

References spec §4 and §9.2 + the §11 `MapController` modifications.

**Files:**
- Create: `Assets/Scripts/Farming/FarmGrowthSystem.cs`
- Modify: `Assets/Scripts/World/MapSystem/MapController.cs`

- [ ] **Step 1: Implement `FarmGrowthSystem`**

```csharp
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Server-only daily tick. One instance per active MapController. See spec §4 + §9.2.
    /// </summary>
    public class FarmGrowthSystem : MonoBehaviour
    {
        private TerrainCellGrid _grid;
        private MapController _map;
        private readonly Dictionary<int, CropHarvestable> _activeHarvestables = new Dictionary<int, CropHarvestable>(64);
        private readonly List<int> _dirtyIndices = new List<int>(64);

        public void Initialize(TerrainCellGrid grid, MapController map)
        {
            _grid = grid;
            _map = map;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
        }

        private void OnDestroy()
        {
            if (MWI.Time.TimeManager.Instance != null)
                MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
        }

        public void RegisterHarvestable(int x, int z, CropHarvestable h) => _activeHarvestables[_grid.LinearIndex(x, z)] = h;
        public void UnregisterHarvestable(int x, int z) => _activeHarvestables.Remove(_grid.LinearIndex(x, z));

        private void HandleNewDay()
        {
            if (_grid == null) return;
            _dirtyIndices.Clear();

            for (int z = 0; z < _grid.Depth; z++)
            for (int x = 0; x < _grid.Width; x++)
            {
                ref TerrainCell cell = ref _grid.GetCellRef(x, z);
                if (!cell.IsPlowed || string.IsNullOrEmpty(cell.PlantedCropId)) continue;

                var outcome = FarmGrowthPipeline.AdvanceOneDay(ref cell);
                int idx = _grid.LinearIndex(x, z);

                switch (outcome)
                {
                    case FarmGrowthPipeline.Outcome.JustMatured:
                        SpawnCropHarvestable(x, z, CropRegistry.Get(cell.PlantedCropId), startDepleted: false);
                        _dirtyIndices.Add(idx);
                        break;
                    case FarmGrowthPipeline.Outcome.JustRefilled:
                        if (_activeHarvestables.TryGetValue(idx, out var h)) h.Refill();
                        _dirtyIndices.Add(idx);
                        break;
                    case FarmGrowthPipeline.Outcome.Grew:
                    case FarmGrowthPipeline.Outcome.Refilling:
                        _dirtyIndices.Add(idx);
                        break;
                }
            }

            if (_dirtyIndices.Count > 0)
                _map.NotifyDirtyCells(_dirtyIndices.ToArray());
        }

        /// <summary>
        /// Reconstructs harvestables from cell state. Called once after MapController.WakeUp()
        /// (covers both hibernation-wake AND save-load — see spec §9.2).
        /// </summary>
        public void PostWakeSweep()
        {
            if (_grid == null) return;
            for (int z = 0; z < _grid.Depth; z++)
            for (int x = 0; x < _grid.Width; x++)
            {
                ref TerrainCell cell = ref _grid.GetCellRef(x, z);
                if (!cell.IsPlowed || string.IsNullOrEmpty(cell.PlantedCropId)) continue;
                var crop = CropRegistry.Get(cell.PlantedCropId);
                if (crop == null) continue;
                if (cell.GrowthTimer < crop.DaysToMature) continue;

                bool startDepleted = crop.IsPerennial && cell.TimeSinceLastWatered >= 0f;
                SpawnCropHarvestable(x, z, crop, startDepleted);
            }
        }

        private void SpawnCropHarvestable(int x, int z, CropSO crop, bool startDepleted)
        {
            if (crop == null || crop.HarvestablePrefab == null)
            {
                Debug.LogError($"[FarmGrowthSystem] Cannot spawn harvestable at ({x},{z}) — crop or prefab is null.");
                return;
            }
            var pos = _grid.GetCellWorldCenter(x, z);
            var go = Instantiate(crop.HarvestablePrefab, pos, Quaternion.identity);
            var h = go.GetComponent<CropHarvestable>();
            if (h == null)
            {
                Debug.LogError($"[FarmGrowthSystem] HarvestablePrefab on {crop.name} has no CropHarvestable component.");
                Destroy(go);
                return;
            }
            // Spawn over the network FIRST so OnNetworkSpawn runs and IsDepleted's value-changed callback wires up.
            if (go.TryGetComponent<NetworkObject>(out var netObj) && !netObj.IsSpawned)
                netObj.Spawn(true);
            h.InitializeFromCell(_grid, _map, x, z, crop, startDepleted);
            RegisterHarvestable(x, z, h);
        }
    }
}
```

- [ ] **Step 2: Wire `FarmGrowthSystem` into `MapController`**

In [MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs):

Replace the temporary stubs from Task 4 with:
```csharp
[SerializeField] private MWI.Farming.FarmGrowthSystem _farmGrowthSystem;
[SerializeField] private MWI.Farming.CropVisualSpawner _cropVisualSpawner;
private readonly HashSet<int> _cellsBeingMutated = new HashSet<int>(16);

public TerrainCellGrid TerrainGrid => _terrainCellGrid; // <- adjust to actual field name in MapController
public MWI.Farming.FarmGrowthSystem FarmGrowthSystem => _farmGrowthSystem;
public bool TryReserveCell(int linearIndex) => _cellsBeingMutated.Add(linearIndex);
public void ReleaseCell(int linearIndex) => _cellsBeingMutated.Remove(linearIndex);

/// <summary>
/// Server entry point. Builds the matching payload from current cell state and fires
/// the ClientRpc so clients can update their local mirror BEFORE visual processors run.
/// All farming code calls this — never the raw ClientRpc — so callers can't forget the payload.
/// </summary>
public void NotifyDirtyCells(int[] indices)
{
    if (!IsServer || indices == null || indices.Length == 0) return;
    var payload = new TerrainCellSaveData[indices.Length];
    for (int i = 0; i < indices.Length; i++)
        payload[i] = TerrainCellSaveData.FromCell(_terrainCellGrid.GetCellByIndex(indices[i]));
    SendDirtyCellsClientRpc(indices, payload);
}

[ClientRpc]
public void SendDirtyCellsClientRpc(int[] indices, TerrainCellSaveData[] payload)
{
    // Client: update the local cell mirror first so any reader sees the new state.
    if (!IsServer)   // server has already mutated its own grid before this dispatched
        for (int i = 0; i < indices.Length; i++)
            _terrainCellGrid.SetCellByIndex(indices[i], payload[i].ToCell());

    if (_cropVisualSpawner != null) _cropVisualSpawner.OnDirtyCells(indices);
    // Future: terrain-cell visual processors can also subscribe here.
}
```

In `MapController.WakeUp()` (or whatever lifecycle method completes cell restore):
```csharp
// After the existing TerrainCell restore block:
if (_farmGrowthSystem != null)
{
    _farmGrowthSystem.Initialize(_terrainCellGrid, this);
    _farmGrowthSystem.PostWakeSweep();
}
```

- [ ] **Step 3: Manual playmode smoke**

Create a Crop_Wheat asset (Inspector: `_id="wheat"`, `_daysToMature=4`, `_minMoistureForGrowth=0.3`, `_produceItem=Item_Apple`, leave sprites empty for now). Add a `FarmGrowthSystem` component to the MapController GameObject in your test scene. In Play Mode, run a script that mutates a cell:
```csharp
ref var cell = ref mapController.TerrainGrid.GetCellRef(0, 0);
cell.IsPlowed = true;
cell.PlantedCropId = "wheat";
cell.Moisture = 1f;
cell.GrowthTimer = 0f;
cell.TimeSinceLastWatered = -1f;
```
Then call `TimeManager.Instance.AdvanceOneHour()` 24 times. After 4 days the cell's `GrowthTimer == 4`. (Harvestable spawn requires Task 4's prefab — defer the visual check to Task 7's manual test. Verify state-only here.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Farming/FarmGrowthSystem.cs Assets/Scripts/World/MapSystem/MapController.cs
git commit -m "feat(farming): wire FarmGrowthSystem on MapController + dirty-cell ClientRpc

- FarmGrowthSystem subscribes to TimeManager.OnNewDay (server-only)
- Daily pass calls FarmGrowthPipeline per planted cell, dispatches mature spawns
  and perennial refills, batches dirty cell indices into one ClientRpc
- PostWakeSweep reconstructs harvestables from cell state (covers save/load
  AND hibernation-wake)
- MapController.SendDirtyCellsClientRpc fans out indices for the visual layer
- _cellsBeingMutated HashSet prepares the planting/watering reservation seam

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §4, §9.2."
```

---

## Task 7: `CropVisualSpawner` (client-side) + sample crop prefabs

References spec §8.

**Files:**
- Create: `Assets/Scripts/Farming/CropVisualSpawner.cs`
- Create: `Assets/Prefabs/Farming/CropHarvestable_Wheat.prefab` (manual)
- Create: `Assets/Resources/Data/Farming/Crops/Crop_Wheat.asset` (manual)

- [ ] **Step 1: Implement `CropVisualSpawner`**

```csharp
using System.Collections.Generic;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Client-side cell-stage sprite renderer. See spec §8.
    /// One instance per MapController. Listens to dirty-cell ClientRpc.
    /// Hands the visual off to CropHarvestable as soon as the cell is mature.
    /// </summary>
    public class CropVisualSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject _stageSpritePrefab;   // SpriteRenderer + transform; no NetworkObject
        private readonly Dictionary<int, GameObject> _activeVisuals = new Dictionary<int, GameObject>(64);
        private TerrainCellGrid _grid;
        private MapController _map;

        public void Initialize(TerrainCellGrid grid, MapController map)
        {
            _grid = grid; _map = map;
        }

        public void OnDirtyCells(int[] indices)
        {
            if (_grid == null || indices == null) return;
            for (int i = 0; i < indices.Length; i++)
                Refresh(indices[i]);
        }

        /// <summary>Re-evaluate one cell from current grid state.</summary>
        public void Refresh(int idx)
        {
            int x = idx % _grid.Width, z = idx / _grid.Width;
            ref TerrainCell cell = ref _grid.GetCellRef(x, z);

            // No crop on this cell? Remove any visual.
            if (string.IsNullOrEmpty(cell.PlantedCropId)) { Remove(idx); return; }

            var crop = CropRegistry.Get(cell.PlantedCropId);
            if (crop == null) { Remove(idx); return; }

            // Mature → CropHarvestable owns the visual. Always remove our own sprite.
            if (cell.GrowthTimer >= crop.DaysToMature) { Remove(idx); return; }

            // Growing — show stage sprite.
            int stage = (int)cell.GrowthTimer;
            if (!_activeVisuals.TryGetValue(idx, out var go) || go == null)
            {
                go = Instantiate(_stageSpritePrefab, _grid.GetCellWorldCenter(x, z), Quaternion.identity, transform);
                _activeVisuals[idx] = go;
            }
            var sr = go.GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sr.sprite = crop.GetStageSprite(stage);
        }

        private void Remove(int idx)
        {
            if (_activeVisuals.TryGetValue(idx, out var go))
            {
                if (go != null) Destroy(go);
                _activeVisuals.Remove(idx);
            }
        }

        /// <summary>Called on map ready / late-join: rebuild all visuals from current grid state.</summary>
        public void RebuildAll()
        {
            foreach (var kv in _activeVisuals)
                if (kv.Value != null) Destroy(kv.Value);
            _activeVisuals.Clear();

            for (int z = 0; z < _grid.Depth; z++)
            for (int x = 0; x < _grid.Width; x++)
                Refresh(_grid.LinearIndex(x, z));
        }
    }
}
```

- [ ] **Step 2: Hook `CropVisualSpawner` into `MapController`**

In `MapController.WakeUp()` after `_farmGrowthSystem.PostWakeSweep()`:
```csharp
if (_cropVisualSpawner != null)
{
    _cropVisualSpawner.Initialize(_terrainCellGrid, this);
    _cropVisualSpawner.RebuildAll();   // late-joiners and post-load both rely on this initial pass
}
```

- [ ] **Step 3: Build the stage-sprite prefab**

In a test scene: `GameObject → 2D Object → Sprite`. Save it as `Assets/Prefabs/Farming/CropStageSprite.prefab`. Drag onto `CropVisualSpawner._stageSpritePrefab` in the MapController inspector.

- [ ] **Step 4: Build `CropHarvestable_Wheat.prefab`**

In a test scene: `GameObject → 2D Object → Sprite` (this becomes the renderer). Add components: `NetworkObject`, `Harvestable` (parent class — won't show because Unity will use the leaf), `CropHarvestable`. Wire:
- `_spriteRenderer` → the SpriteRenderer child.
- `_readySprite` → a placeholder ripe-wheat sprite (use any 2D sprite asset for now).
- `_depletedSprite` → null (one-shot, never seen).
- Save as `Assets/Prefabs/Farming/CropHarvestable_Wheat.prefab`.

Open the prefab and ensure ONLY `CropHarvestable` is added (not `Harvestable` separately — `CropHarvestable` IS a `Harvestable`). The base fields (`_outputItems`, `_maxHarvestCount`, etc.) will be set at runtime by `InitializeFromCell` so they can be empty in the Inspector.

- [ ] **Step 5: Build `Crop_Wheat.asset`**

`Project window → Create → Game → Farming → Crop`. Save in `Assets/Resources/Data/Farming/Crops/Crop_Wheat.asset`. Inspector:
- `_id`: `wheat`
- `_displayName`: `Wheat`
- `_daysToMature`: `4`
- `_minMoistureForGrowth`: `0.3`
- `_plantDuration`: `1`
- `_produceItem`: drag any existing `MiscItemSO` for now (placeholder).
- `_produceCount`: `1`
- `_stageSprites`: array of size 4 — assign 4 placeholder sprites.
- `_harvestablePrefab`: drag `CropHarvestable_Wheat.prefab`.

- [ ] **Step 6: Manual smoke — full grow loop**

Start the project. In Play Mode, run a script:
```csharp
ref var cell = ref mc.TerrainGrid.GetCellRef(5, 5);
cell.IsPlowed = true; cell.PlantedCropId = "wheat"; cell.Moisture = 1f; cell.GrowthTimer = 0f; cell.TimeSinceLastWatered = -1f;
mc.NotifyDirtyCells(new[] { mc.TerrainGrid.LinearIndex(5, 5) });
```
Cell shows stage 0 sprite. Call `TimeManager.Instance.AdvanceOneHour()` 24 times → stage 1 sprite. Repeat 3× more → stage 3 sprite. One more day → stage cleared, `CropHarvestable_Wheat` GameObject spawns at the cell with the ready sprite.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Farming/CropVisualSpawner.cs Assets/Scripts/World/MapSystem/MapController.cs Assets/Prefabs/Farming/ Assets/Resources/Data/Farming/Crops/Crop_Wheat.asset
git commit -m "feat(farming): CropVisualSpawner + Crop_Wheat sample + handoff to CropHarvestable

CropVisualSpawner is client-side, listens to MapController.SendDirtyCellsClientRpc,
maintains a sparse Dictionary<idx, GameObject> of growing-stage sprites. Early-exits
+ removes its sprite the moment a cell crosses DaysToMature — the CropHarvestable
takes over the visual slot.

Includes the first sample crop (Wheat, one-shot, 4-day grow) for manual playmode.

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §8."
```

---

## Task 8: `CharacterAction_PlaceCrop` + `CropPlacementManager` + `CharacterAction_WaterCrop`

References spec §5.2, §5.3, §7. Bundled because they share the placement-manager scaffolding.

**Files:**
- Create: `Assets/Scripts/Farming/CharacterAction_PlaceCrop.cs`
- Create: `Assets/Scripts/Farming/CharacterAction_WaterCrop.cs`
- Create: `Assets/Scripts/Farming/CropPlacementManager.cs`
- Modify: `Assets/Scripts/Character/Character.cs`

- [ ] **Step 1: Implement `CharacterAction_PlaceCrop`**

```csharp
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    public class CharacterAction_PlaceCrop : CharacterAction
    {
        private readonly int _cellX, _cellZ;
        private readonly CropSO _crop;
        private readonly ItemInstance _seed;
        private readonly MapController _map;
        private readonly TerrainCellGrid _grid;

        public CharacterAction_PlaceCrop(Character actor, MapController map, int cellX, int cellZ, CropSO crop, ItemInstance seed)
            : base(actor, crop != null ? crop.PlantDuration : 1f)
        {
            _map = map; _grid = map != null ? map.TerrainGrid : null;
            _cellX = cellX; _cellZ = cellZ; _crop = crop; _seed = seed;
        }

        public override string ActionName => "Plant";

        public override bool CanExecute()
        {
            if (_crop == null || _grid == null) return false;
            ref var cell = ref _grid.GetCellRef(_cellX, _cellZ);
            if (!string.IsNullOrEmpty(cell.PlantedCropId)) return false;
            // Seed-still-in-hand check is the responsibility of the ServerRpc; we trust it here.
            return true;
        }

        public override void OnStart() { /* face cell + animation */ }

        public override void OnApplyEffect()
        {
            if (_grid == null) return;
            ref var cell = ref _grid.GetCellRef(_cellX, _cellZ);
            cell.IsPlowed = true;
            cell.PlantedCropId = _crop.Id;
            cell.GrowthTimer = 0f;
            cell.TimeSinceLastWatered = -1f;

            character.CharacterEquipment?.ConsumeFromActiveHand(1);
            _map.NotifyDirtyCells(new[] { _grid.LinearIndex(_cellX, _cellZ) });
        }

        public override void OnCancel()
        {
            _map.ReleaseCell(_grid.LinearIndex(_cellX, _cellZ));
        }
    }
}
```

> If `CharacterEquipment.ConsumeFromActiveHand(int)` doesn't exist by that exact name, search `CharacterEquipment.cs` for the closest match (`ConsumeOne`, `RemoveFromActiveHand`, etc.) and use the matching API.

- [ ] **Step 2: Implement `CharacterAction_WaterCrop`**

```csharp
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    public class CharacterAction_WaterCrop : CharacterAction
    {
        private readonly int _cellX, _cellZ;
        private readonly float _moistureSetTo;
        private readonly MapController _map;
        private readonly TerrainCellGrid _grid;

        public CharacterAction_WaterCrop(Character actor, MapController map, int cellX, int cellZ, float moistureSetTo)
            : base(actor, 0.5f)
        {
            _map = map; _grid = map != null ? map.TerrainGrid : null;
            _cellX = cellX; _cellZ = cellZ; _moistureSetTo = moistureSetTo;
        }

        public override string ActionName => "Water";
        public override bool CanExecute() => _grid != null;
        public override void OnStart() { /* face cell + animation */ }

        public override void OnApplyEffect()
        {
            if (_grid == null) return;
            ref var cell = ref _grid.GetCellRef(_cellX, _cellZ);
            cell.Moisture = _moistureSetTo;
            // TimeSinceLastWatered semantics: only meaningful when cell is in perennial-refill phase.
            // We do NOT touch it here — refill catch-up is FarmGrowthSystem's job.
            _map.NotifyDirtyCells(new[] { _grid.LinearIndex(_cellX, _cellZ) });
        }
    }
}
```

- [ ] **Step 3: Implement `CropPlacementManager`**

```csharp
using Unity.Netcode;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Per-Character system. Mirrors BuildingPlacementManager. Spec §5.2 + §7.
    /// </summary>
    public class CropPlacementManager : CharacterSystem
    {
        [SerializeField] private GameObject _ghostPrefab;       // sprite-only, no NetworkObject, no collider
        [SerializeField] private float _maxRange = 5f;
        [SerializeField] private LayerMask _groundLayer;

        private GameObject _ghost;
        private SpriteRenderer _ghostSprite;
        private CropSO _activeCrop;
        private ItemInstance _activeSeed;
        private ItemInstance _activeCan;
        private MapController _activeMap;
        private Mode _mode = Mode.Off;

        private enum Mode { Off, Placing, Watering }
        public bool IsActive => _mode != Mode.Off;

        public void Initialize(Character character) { _character = character; }

        public void StartPlacement(ItemInstance seed)
        {
            if (!(seed?.ItemSO is SeedSO seedSO) || seedSO.CropToPlant == null) return;
            CancelPlacement();
            _activeSeed = seed;
            _activeCrop = seedSO.CropToPlant;
            _activeMap = MapController.GetMapAt(_character.transform.position); // adjust to project's helper
            _ghost = Instantiate(_ghostPrefab);
            _ghostSprite = _ghost.GetComponentInChildren<SpriteRenderer>();
            if (_ghostSprite != null) _ghostSprite.sprite = _activeCrop.GetStageSprite(0);
            _mode = Mode.Placing;
        }

        public void StartWatering(ItemInstance can)
        {
            if (!(can?.ItemSO is WateringCanSO canSO)) return;
            CancelPlacement();
            _activeCan = can;
            _activeMap = MapController.GetMapAt(_character.transform.position);
            _ghost = Instantiate(_ghostPrefab);   // reuse the same ghost prefab; tint/swap could be added later
            _mode = Mode.Watering;
        }

        public void CancelPlacement()
        {
            if (_ghost != null) Destroy(_ghost);
            _ghost = null; _ghostSprite = null;
            _activeCrop = null; _activeSeed = null; _activeCan = null; _activeMap = null;
            _mode = Mode.Off;
        }

        private void Update()
        {
            if (_mode == Mode.Off || _activeMap == null) return;
            if (_character == null || !_character.IsOwner) return;

            // Snap ghost to nearest cell under mouse.
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 100f, _groundLayer)) return;

            var grid = _activeMap.TerrainGrid;
            int x = grid.WorldToCellX(hit.point);   // adjust to project's helper
            int z = grid.WorldToCellZ(hit.point);
            if (!grid.InBounds(x, z)) return;
            _ghost.transform.position = grid.GetCellWorldCenter(x, z);

            ref var cell = ref grid.GetCellRef(x, z);
            bool valid = ValidateCell(ref cell);
            if (_ghostSprite != null) _ghostSprite.color = valid ? Color.white : new Color(1, 0.4f, 0.4f, 0.7f);

            if (valid && Input.GetMouseButtonDown(0))
            {
                if (_mode == Mode.Placing) RequestPlaceCropServerRpc(x, z, _activeCrop.Id);
                else                       RequestWaterCellServerRpc(x, z, ((WateringCanSO)_activeCan.ItemSO).MoistureSetTo);
                CancelPlacement();
            }
        }

        private bool ValidateCell(ref TerrainCell cell)
        {
            float dist = Vector3.Distance(_character.transform.position, _ghost.transform.position);
            if (dist > _maxRange) return false;
            if (_mode == Mode.Placing)
            {
                if (!cell.GetCurrentType().CanGrowVegetation) return false;
                return string.IsNullOrEmpty(cell.PlantedCropId);
            }
            return true; // watering can target any cell
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestPlaceCropServerRpc(int x, int z, string cropId, ServerRpcParams rpcParams = default)
        {
            var crop = CropRegistry.Get(cropId);
            if (crop == null) return;
            var grid = _activeMap.TerrainGrid;
            int idx = grid.LinearIndex(x, z);
            if (!_activeMap.TryReserveCell(idx)) return;

            ref var cell = ref grid.GetCellRef(x, z);
            if (!string.IsNullOrEmpty(cell.PlantedCropId)) { _activeMap.ReleaseCell(idx); return; }

            var actor = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(rpcParams.Receive.SenderClientId)?.GetComponent<Character>();
            if (actor == null) { _activeMap.ReleaseCell(idx); return; }

            var heldSeed = actor.CharacterEquipment?.GetActiveHandItem();
            if (!(heldSeed?.ItemSO is SeedSO seedSO) || seedSO.CropToPlant.Id != cropId) { _activeMap.ReleaseCell(idx); return; }

            actor.CharacterActions.ExecuteAction(
                new CharacterAction_PlaceCrop(actor, _activeMap, x, z, crop, heldSeed));
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestWaterCellServerRpc(int x, int z, float moistureSetTo, ServerRpcParams rpcParams = default)
        {
            var actor = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(rpcParams.Receive.SenderClientId)?.GetComponent<Character>();
            if (actor == null) return;
            actor.CharacterActions.ExecuteAction(
                new CharacterAction_WaterCrop(actor, _activeMap, x, z, moistureSetTo));
        }
    }
}
```

> Adjust `MapController.GetMapAt`, `TerrainCellGrid.WorldToCellX/Z`, `InBounds`, `GetCellWorldCenter` to whatever the project's actual helpers are named. Search the existing `BuildingPlacementManager.cs` for the equivalents and reuse.

- [ ] **Step 4: Add `CropPlacement` to `Character.cs`**

In [Character.cs](../../Assets/Scripts/Character/Character.cs):
```csharp
[SerializeField] private MWI.Farming.CropPlacementManager _cropPlacement;
public MWI.Farming.CropPlacementManager CropPlacement
{
    get
    {
        if (_cropPlacement == null) _cropPlacement = GetComponentInChildren<MWI.Farming.CropPlacementManager>();
        return _cropPlacement;
    }
}
```

In `Character.Awake()`:
```csharp
if (_cropPlacement != null) _cropPlacement.Initialize(this);
```

- [ ] **Step 5: Set up the per-character GameObject**

In the Character prefab: add a child GameObject `CropPlacementSystem`, attach `CropPlacementManager`. Drag a stage-sprite ghost prefab onto its `_ghostPrefab` field. Drag the `CropPlacementSystem` child onto the parent's `_cropPlacement` field on `Character.cs`.

- [ ] **Step 6: Manual playmode smoke**

In Play Mode with a `Item_Seed_Wheat` SeedSO instance in the player's active hand, call:
```csharp
character.CropPlacement.StartPlacement(character.CharacterEquipment.GetActiveHandItem());
```
Ghost sprite appears at mouse cursor. Click → `CharacterAction_PlaceCrop` runs for 1s. Cell mutates: `IsPlowed=true`, `PlantedCropId="wheat"`, etc. `CropVisualSpawner` shows the stage-0 sprite. Seed count decrements.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Farming/CharacterAction_PlaceCrop.cs Assets/Scripts/Farming/CharacterAction_WaterCrop.cs Assets/Scripts/Farming/CropPlacementManager.cs Assets/Scripts/Character/Character.cs
git commit -m "feat(farming): CropPlacementManager + plant/water actions

Per-character system mirroring BuildingPlacementManager. ServerRpc validates
cell + seed + reserves the cell index against double-plant races. Plant action
mutates IsPlowed/PlantedCropId/GrowthTimer/TimeSinceLastWatered=-1 and
consumes one seed; water action sets Moisture (rain/refill share this field).

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §5, §7."
```

---

## Task 9: `PlayerController` E-key dispatch (placement / tap-E / hold-E)

References spec §5.1 + §6.2.

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`

- [ ] **Step 1: Add the dispatcher fields and method**

In `PlayerController.cs`:
```csharp
private float _eHeldStartTime;
private bool _menuOpen;
private const float HoldThreshold = 0.4f;

private void HandleEKey()
{
    if (!IsOwner) return;
    var character = GetComponent<Character>();
    if (character.IsBuilding) return;

    var held = character.CharacterEquipment?.GetActiveHandItem();
    bool eDown = Input.GetKeyDown(KeyCode.E);
    bool eUp   = Input.GetKeyUp(KeyCode.E);
    bool eHeld = Input.GetKey(KeyCode.E);

    // Priority 1: a placement-active item is held → E starts placement.
    if (eDown && held?.ItemSO is MWI.Farming.SeedSO)         { character.CropPlacement.StartPlacement(held); return; }
    if (eDown && held?.ItemSO is MWI.Farming.WateringCanSO)  { character.CropPlacement.StartWatering(held); return; }

    // Priority 2: harvestable interaction (tap vs hold).
    if (eDown) _eHeldStartTime = UnityEngine.Time.unscaledTime;

    if (eHeld && !_menuOpen && UnityEngine.Time.unscaledTime - _eHeldStartTime >= HoldThreshold)
    {
        var target = character.GetClosestInteractable() as Harvestable;
        if (target != null)
        {
            UI_InteractionMenu.Open(character, target, OnMenuClosed);
            _menuOpen = true;
        }
    }
    else if (eUp && !_menuOpen)
    {
        // Tap path — Interact() routes to yield path on Harvestables.
        var target = character.GetClosestInteractable();
        target?.Interact(character);
    }
}

private void OnMenuClosed() => _menuOpen = false;
```

Call `HandleEKey()` from `PlayerController.Update()` (replacing any prior E-key handling — there should be one place only). If existing code already reads `KeyCode.E` for something else, audit those call sites and consolidate them here.

- [ ] **Step 2: Find and consolidate any other `KeyCode.E` reads in the project**

Search the codebase:
```
Grep KeyCode.E
```
Anything outside `PlayerController.cs` that reads E for player-character control violates rule #33. Move it here. UI widgets that use E for menu-internal navigation are fine and stay where they are.

- [ ] **Step 3: Manual smoke (without the menu)**

For now, `UI_InteractionMenu.Open` doesn't exist. Stub it:
```csharp
public static class UI_InteractionMenu
{
    public static void Open(Character actor, Harvestable target, System.Action onClosed)
    {
        Debug.Log($"[STUB] Open menu for {target.name}");
        onClosed?.Invoke();
    }
}
```
(Implementation arrives in Task 10. Place this stub in the same file as the eventual class so the swap is one delete.)

In Play Mode: hold seed → press E → placement mode opens. With bare hands, walk up to the wheat-`CropHarvestable` (spawned earlier), tap E → harvest action runs → wheat drops, harvestable despawns, cell clears.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterControllers/PlayerController.cs Assets/Scripts/UI/Interaction/
git commit -m "feat(farming): PlayerController E-key dispatch (placement / tap / hold)

Single owner-gated input handler:
- Seed/WateringCan held + E → placement
- Tap E (released < 0.4s) → nearest Interactable.Interact() = yield path
- Hold E (≥ 0.4s) → opens UI_InteractionMenu (stubbed; impl in next task)

Per project rule #33, this is the only place in the codebase that reads
KeyCode.E for player-character control.

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §5.1, §6.2."
```

---

## Task 10: `UI_InteractionMenu`

References spec §6.2.

**Files:**
- Create: `Assets/Scripts/UI/Interaction/UI_InteractionMenu.cs`
- Create: `Assets/Scripts/UI/Interaction/UI_InteractionOptionRow.cs`
- Create: `Assets/Resources/UI/UI_InteractionMenu.prefab` (manual)

- [ ] **Step 1: Implement `UI_InteractionOptionRow`**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Interaction
{
    public class UI_InteractionOptionRow : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private TMP_Text _outputPreview;
        [SerializeField] private TMP_Text _unavailableReason;
        [SerializeField] private CanvasGroup _canvasGroup;

        public void Bind(HarvestInteractionOption opt, System.Action<HarvestInteractionOption> onSelected)
        {
            _icon.sprite = opt.Icon;
            _label.text = opt.Label;
            _outputPreview.text = opt.OutputPreview;
            _unavailableReason.text = opt.IsAvailable ? string.Empty : opt.UnavailableReason;
            _button.interactable = opt.IsAvailable;
            _canvasGroup.alpha = opt.IsAvailable ? 1f : 0.5f;

            _button.onClick.RemoveAllListeners();
            if (opt.IsAvailable)
                _button.onClick.AddListener(() => onSelected?.Invoke(opt));
        }
    }
}
```

- [ ] **Step 2: Implement `UI_InteractionMenu`**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.UI.Interaction
{
    public class UI_InteractionMenu : MonoBehaviour
    {
        [SerializeField] private UI_InteractionOptionRow _rowPrefab;
        [SerializeField] private Transform _rowParent;

        private static UI_InteractionMenu _instance;
        private Character _actor;
        private Harvestable _target;
        private System.Action _onClosed;
        private readonly List<UI_InteractionOptionRow> _rows = new List<UI_InteractionOptionRow>();

        private static UI_InteractionMenu EnsureInstance()
        {
            if (_instance == null)
            {
                var prefab = Resources.Load<UI_InteractionMenu>("UI/UI_InteractionMenu");
                _instance = Instantiate(prefab);
                DontDestroyOnLoad(_instance.gameObject);
                _instance.gameObject.SetActive(false);
            }
            return _instance;
        }

        public static void Open(Character actor, Harvestable target, System.Action onClosed)
        {
            var menu = EnsureInstance();
            menu._actor = actor;
            menu._target = target;
            menu._onClosed = onClosed;
            menu.Rebuild();
            menu.gameObject.SetActive(true);
        }

        public static void Close()
        {
            if (_instance == null) return;
            _instance.gameObject.SetActive(false);
            _instance._onClosed?.Invoke();
            _instance._onClosed = null;
        }

        private void Rebuild()
        {
            foreach (var r in _rows) Destroy(r.gameObject);
            _rows.Clear();

            var options = _target.GetInteractionOptions(_actor);
            for (int i = 0; i < options.Count; i++)
            {
                var row = Instantiate(_rowPrefab, _rowParent);
                row.Bind(options[i], OnSelected);
                _rows.Add(row);
            }
        }

        private void OnSelected(HarvestInteractionOption opt)
        {
            if (_actor != null && opt.ActionFactory != null)
            {
                var action = opt.ActionFactory(_actor);
                if (action != null) _actor.CharacterActions.ExecuteAction(action);
            }
            Close();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }
    }
}
```

- [ ] **Step 3: Build the prefab**

`Assets/Resources/UI/UI_InteractionMenu.prefab`: a `Canvas` (Screen Space Overlay) with a centred panel + vertical layout group child as `_rowParent`. Make `UI_InteractionOptionRow` prefab with Button, Image, TMP_Texts wired to its serialised fields. Place it as a child of the menu.

- [ ] **Step 4: Remove the stub from Task 9**

Delete the stubbed `UI_InteractionMenu` static class (the one with `Debug.Log("[STUB]")`). The real namespace is `MWI.UI.Interaction.UI_InteractionMenu` — adjust the `using` in `PlayerController.cs` accordingly.

- [ ] **Step 5: Manual playmode test of all three sample crops**

(`Crop_Wheat`, `Crop_Flower`, `Crop_AppleTree`. Build `Crop_Flower` and `Crop_AppleTree` assets/prefabs now using the same recipe as Task 7, with §11 spec values.)

- Plant a wheat seed → grow → mature → tap E with bare hands picks it. Hold E shows menu with one row "Pick Wheat". ✅
- Plant a flower seed → grow → mature → tap E picks. Hold E shows menu with one row. ✅
- Plant an apple sapling → grow → mature → tap E picks 4 apples. Tree stays standing in depleted state. Wait `RegrowDays=2` days → ready again. Hold E with axe → menu shows "Pick Apple" + "Destroy" — pick "Destroy" → 4 wood drops + tree gone + cell cleared. ✅
- Hold E without axe → "Destroy" row greyed out with "Requires Axe". ✅

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/UI/Interaction/ Assets/Resources/UI/UI_InteractionMenu.prefab Assets/Scripts/Character/CharacterControllers/PlayerController.cs
git commit -m "feat(farming): UI_InteractionMenu (Hold-E)

Singleton lazy-spawned menu listing Harvestable.GetInteractionOptions.
Greyed-out unavailable rows show the reason ('Requires Axe', etc.) so the
player learns what tools unlock what.

ESC closes. Selection queues the option's action factory through CharacterActions.

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §6.2."
```

---

## Task 11: `GameLauncher` registry init + `SaveManager.ResetForNewSession` clear

References spec §3.2 + §9.3.

**Files:**
- Modify: `Assets/Scripts/Core/GameLauncher.cs`
- Modify: `Assets/Scripts/SaveLoad/SaveManager.cs`

- [ ] **Step 1: Init `CropRegistry` in `GameLauncher.LaunchSequence`**

In [GameLauncher.cs](../../Assets/Scripts/Core/GameLauncher.cs), find the existing `TerrainTypeRegistry.Initialize()` call and add immediately after:
```csharp
MWI.Farming.CropRegistry.Initialize();
```

- [ ] **Step 2: Clear `CropRegistry` in `SaveManager.ResetForNewSession`**

In [SaveManager.cs](../../Assets/Scripts/SaveLoad/SaveManager.cs), find `ResetForNewSession()` and add:
```csharp
MWI.Farming.CropRegistry.Clear();
```

- [ ] **Step 3: Smoke test — close-and-reopen project**

Stop the editor, reopen, enter Play Mode. Console: `[CropRegistry] Initialised with N crop(s).` (N matches the count of `Crop_*.asset` under `Resources/Data/Farming/Crops/`). Plant a seed, save world, exit to main menu, reload — cell state restores, harvestables reconstruct via `PostWakeSweep`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Core/GameLauncher.cs Assets/Scripts/SaveLoad/SaveManager.cs
git commit -m "feat(farming): wire CropRegistry init + per-session clear

Registry must be alive before any MapController.WakeUp() reads cells with
PlantedCropId set, otherwise the post-wake sweep silently skips planted cells.
Same ordering constraint as TerrainTypeRegistry.

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §3.2, §9.3."
```

---

## Task 12: `MacroSimulator.SimulateCropCatchUp` — TDD'd

References spec §9.4.

**Files:**
- Create: `Assets/Scripts/World/MapSystem/MacroSimulatorCropMath.cs`
- Modify: `Assets/Scripts/World/MapSystem/MacroSimulator.cs`
- Create: `Assets/Tests/EditMode/Farming/MacroSimulatorCropMathTests.cs`

- [ ] **Step 1: Write failing tests**

`Assets/Tests/EditMode/Farming/MacroSimulatorCropMathTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using MWI.Farming;
using MWI.Terrain;
using MWI.WorldSystem.Simulation;

namespace MWI.Tests.Farming
{
    public class MacroSimulatorCropMathTests
    {
        private CropSO _wheat;
        private CropSO _apple;

        [SetUp]
        public void SetUp()
        {
            _wheat = MakeCrop("wheat", days: 4, perennial: false);
            _apple = MakeCrop("apple", days: 4, perennial: true, regrow: 2);
            CropRegistry.InitializeForTests(new[] { _wheat, _apple });
        }

        [TearDown]
        public void TearDown() => CropRegistry.Clear();

        [Test]
        public void GrowingCrop_WetClimate_AdvancesByDaysPassed_ClampedAtMaturity()
        {
            var cell = MakeCell("wheat", growthTimer: 1f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 5, estimatedAvgMoisture: 0.5f);
            Assert.AreEqual(4f, cell.GrowthTimer); // clamped at DaysToMature
        }

        [Test]
        public void GrowingCrop_DryClimate_DoesNotAdvance()
        {
            var cell = MakeCell("wheat", growthTimer: 1f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 5, estimatedAvgMoisture: 0.1f);
            Assert.AreEqual(1f, cell.GrowthTimer);
        }

        [Test]
        public void DepletedPerennial_WetClimate_RefillsExactlyOnce()
        {
            var cell = MakeCell("apple", growthTimer: 4f, timeSinceLastWatered: 0f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 7, estimatedAvgMoisture: 0.5f);
            Assert.AreEqual(-1f, cell.TimeSinceLastWatered); // refilled (multi-cycle deferred)
        }

        [Test]
        public void DepletedPerennial_DryClimate_DoesNotAdvance()
        {
            var cell = MakeCell("apple", growthTimer: 4f, timeSinceLastWatered: 0f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 7, estimatedAvgMoisture: 0.1f);
            Assert.AreEqual(0f, cell.TimeSinceLastWatered);
        }

        [Test]
        public void OneShotMature_NoOfflineState_LeftAlone()
        {
            var cell = MakeCell("wheat", growthTimer: 4f, timeSinceLastWatered: -1f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 30, estimatedAvgMoisture: 0.5f);
            Assert.AreEqual(4f, cell.GrowthTimer);
            Assert.AreEqual(-1f, cell.TimeSinceLastWatered);
        }

        [Test]
        public void Orphan_NoMutation()
        {
            var cell = MakeCell("nonexistent", growthTimer: 1f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 5, estimatedAvgMoisture: 0.5f);
            Assert.AreEqual(1f, cell.GrowthTimer);
        }

        private static CropSO MakeCrop(string id, int days, bool perennial = false, int regrow = 0)
        {
            var c = ScriptableObject.CreateInstance<CropSO>();
            c.SetIdForTests(id);
            c.SetDaysToMatureForTests(days);
            c.SetMinMoistureForTests(0.3f);
            c.SetIsPerennialForTests(perennial);
            c.SetRegrowDaysForTests(regrow);
            return c;
        }

        private static TerrainCell MakeCell(string cropId, float growthTimer, float timeSinceLastWatered = -1f)
            => new TerrainCell
            {
                IsPlowed = true,
                PlantedCropId = cropId,
                GrowthTimer = growthTimer,
                TimeSinceLastWatered = timeSinceLastWatered
            };
    }
}
```

- [ ] **Step 2: Run, expect failure**

Test Runner → EditMode → Run All. Expected: compile errors (`MacroSimulatorCropMath` doesn't exist).

- [ ] **Step 3: Implement `MacroSimulatorCropMath`**

`Assets/Scripts/World/MapSystem/MacroSimulatorCropMath.cs`:
```csharp
using MWI.Farming;
using MWI.Terrain;

namespace MWI.WorldSystem.Simulation
{
    /// <summary>Pure offline catch-up math for one cell. See farming spec §9.4.</summary>
    public static class MacroSimulatorCropMath
    {
        public static void AdvanceCellOffline(ref TerrainCell cell, int daysPassed, float estimatedAvgMoisture)
        {
            if (!cell.IsPlowed || string.IsNullOrEmpty(cell.PlantedCropId)) return;
            var crop = CropRegistry.Get(cell.PlantedCropId);
            if (crop == null) return;
            if (estimatedAvgMoisture < crop.MinMoistureForGrowth) return;

            // PHASE A — still growing
            if (cell.GrowthTimer < crop.DaysToMature)
            {
                cell.GrowthTimer = System.Math.Min(cell.GrowthTimer + daysPassed, crop.DaysToMature);
                return;
            }

            // PHASE B — depleted perennial
            if (crop.IsPerennial && cell.TimeSinceLastWatered >= 0f)
            {
                cell.TimeSinceLastWatered += daysPassed;
                if (cell.TimeSinceLastWatered >= crop.RegrowDays)
                    cell.TimeSinceLastWatered = -1f;
            }
        }
    }
}
```

- [ ] **Step 4: Integrate into `MacroSimulator`**

In [MacroSimulator.cs](../../Assets/Scripts/World/MapSystem/MacroSimulator.cs), find `SimulateVegetationCatchUp` and add after it:
```csharp
public static void SimulateCropCatchUp(TerrainCellSaveData[] cells, BiomeClimateProfile climate, float hoursPassed)
{
    int daysPassed = (int)(hoursPassed / 24f);
    if (daysPassed <= 0) return;
    float avgMoisture = climate != null
        ? climate.AmbientMoisture + climate.RainProbability * 0.5f
        : 0.5f;

    for (int i = 0; i < cells.Length; i++)
    {
        // SaveData → mutable cell → SaveData round-trip (cells is the persistence carrier).
        var cell = cells[i].ToCell();
        MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed, avgMoisture);
        cells[i] = TerrainCellSaveData.FromCell(cell);
    }
}
```

Then call `SimulateCropCatchUp` in `MacroSimulator.RunCatchUp` (or whatever the orchestration method is) immediately after `SimulateVegetationCatchUp`.

- [ ] **Step 5: Run tests, expect pass**

Test Runner → EditMode → Run All. Expected: 6 new tests in `MacroSimulatorCropMathTests` pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MacroSimulatorCropMath.cs Assets/Scripts/World/MapSystem/MacroSimulator.cs Assets/Tests/EditMode/Farming/MacroSimulatorCropMathTests.cs
git commit -m "feat(farming): MacroSimulator.SimulateCropCatchUp + pure-math tests

Hibernation-only offline advancement. Phase A advances growing crops by
days-passed clamped at maturity; Phase B advances perennial refill counters
and wraps to ready (sentinel -1) after one full RegrowDays cycle (multi-cycle
deferred — spec §10).

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §9.4."
```

---

## Task 13: Sample crop content (Flower + AppleTree) + items

References spec §11 sample assets.

**Files:** assets only — no code.

- [ ] **Step 1: Create `Crop_Flower.asset`**

`Assets/Resources/Data/Farming/Crops/Crop_Flower.asset`:
- `_id`: `flower`, `_displayName`: `Flower`
- `_daysToMature`: 2, `_minMoistureForGrowth`: 0.3, `_plantDuration`: 1
- `_produceItem`: drag a placeholder item (or create `Item_Flower.asset` first)
- `_produceCount`: 1
- `_stageSprites`: array of 2 sprites
- `_harvestablePrefab`: drag `CropHarvestable_Flower.prefab` (build it next)
- `_isPerennial`: false
- `_allowDestruction`: false

- [ ] **Step 2: Build `CropHarvestable_Flower.prefab`**

Same recipe as `CropHarvestable_Wheat.prefab` from Task 7. `_readySprite` is the ripe-flower sprite; `_depletedSprite` can be null (one-shot).

- [ ] **Step 3: Create `Crop_AppleTree.asset`**

`Assets/Resources/Data/Farming/Crops/Crop_AppleTree.asset`:
- `_id`: `apple`, `_displayName`: `Apple Tree`
- `_daysToMature`: 4, `_minMoistureForGrowth`: 0.3, `_plantDuration`: 2
- `_produceItem`: drag `Item_Apple.asset` (create if missing — plain `MiscItemSO`)
- `_produceCount`: 4
- `_stageSprites`: array of 4 sprites (sapling → small tree → bigger tree → almost full)
- `_harvestablePrefab`: drag `CropHarvestable_AppleTree.prefab`
- `_isPerennial`: true, `_regrowDays`: 2
- `_allowDestruction`: true
- `_requiredDestructionTool`: drag `Item_Axe.asset` (create as `WeaponSO` if missing — placeholder stats fine)
- `_destructionOutputs`: list with one entry → drag `Item_Wood.asset` (create as plain `MiscItemSO`)
- `_destructionOutputCount`: 4
- `_destructionDuration`: 3

- [ ] **Step 4: Build `CropHarvestable_AppleTree.prefab`**

Same recipe as wheat, but **set `_depletedSprite`** (tree without apples) — load-bearing for the perennial visual. `_readySprite` is the tree-with-apples sprite.

- [ ] **Step 5: Create the seed items**

`Item_Seed_Wheat.asset`, `Item_Seed_Flower.asset`, `Item_Seed_AppleSapling.asset` — all `SeedSO`. Each points its `_cropToPlant` to the matching `Crop_*.asset`. Stash under `Assets/Resources/Data/Items/`.

- [ ] **Step 6: Create `Item_WateringCan.asset`**

`WateringCanSO` with `_moistureSetTo = 1f`.

- [ ] **Step 7: Manual playmode regression**

Re-run the Task 7/10 manual flow but with all three crops + the watering can. Skip a few in-game days for each. Verify perennial refill on AppleTree under watered conditions.

- [ ] **Step 8: Commit**

```bash
git add Assets/Resources/Data/Farming/ Assets/Resources/Data/Items/ Assets/Prefabs/Farming/
git commit -m "feat(farming): sample content — Wheat, Flower, AppleTree + seeds + can

Three crops covering the design surface:
- Wheat: one-shot, no tool, 4-day grow, drops 1 item
- Flower: yield-only, 2-day grow, demonstrates the simplest interaction
- AppleTree: perennial + destructible, 4-day grow + 2-day refill, drops 4 apples
  per harvest cycle; axe destruction yields 4 wood

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §11."
```

---

## Task 14: Acceptance pass — all 16 criteria from §12

Manual playmode work. No new code (other than removing Task 3's `[ContextMenu]` scaffolding).

**Files:**
- Modify: `Assets/Scripts/Interactable/Harvestable.cs` (remove dev `[ContextMenu]` from Task 3 step 3)

- [ ] **Step 1: Remove the dev scaffolding**

Delete the `Dev_DestroyViaLocalPlayer` `[ContextMenu]` method from `Harvestable.cs` (added in Task 3 step 3).

- [ ] **Step 2: Run all 16 acceptance criteria from spec §12**

Open the spec at [§12](../specs/2026-04-28-farming-plot-system-design.md#12-acceptance-criteria). Run each manually in a multiplayer host+client session (build the project, run two instances). Tick each criterion as it passes:

- [ ] Plant (criterion 1)
- [ ] Water (2)
- [ ] Grow (3)
- [ ] Stall (4)
- [ ] One-shot mature → harvest (5)
- [ ] Perennial mature → harvest → refill (6)
- [ ] Re-plant one-shot only (7)
- [ ] Late joiner (8)
- [ ] Hibernation (9)
- [ ] Save/load full close-and-reopen — three states verified (10)
- [ ] No allocs in tick — Profiler check (11)
- [ ] Destroy a perennial via the menu (12)
- [ ] Tap-E never destroys (13)
- [ ] Yield-only flower (14)
- [ ] Greyed-out option visibility (15)
- [ ] Placement-active item suppresses harvest input (16)

For criterion 11 (zero allocs): in Editor → Window → Analysis → Profiler, Deep Profile + Allocation Tracking on. Reproduce a daily tick (`TimeManager.AdvanceOneHour` × 24 with a populated grid). Frame containing `FarmGrowthSystem.HandleNewDay` should show `GC.Alloc = 0`. If allocations appear, the most likely culprit is `_dirtyIndices.ToArray()` — keep it (one alloc per day is acceptable per spec § 4 "no allocations in hot path"). Document the actual measurement in the commit body.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Interactable/Harvestable.cs
git commit -m "test(farming): pass all 16 acceptance criteria + remove dev scaffolding

All §12 criteria verified in host+client playmode:
- Plant/water/grow/stall (1-4)
- One-shot harvest (5), perennial harvest+refill (6), re-plant (7)
- Late-joiner (8), hibernation (9), full close-and-reopen save/load (10)
- Zero per-tick allocs profiled (11) — note: dirtyIndices.ToArray() is one
  alloc per in-game DAY, well within budget
- Destroy via menu (12), tap-E never destroys (13)
- Yield-only flower (14), greyed-out options (15), placement suppresses (16)

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §12."
```

---

## Task 15: Documentation

References project rules #28, #29, #29b.

**Files:**
- Create: `wiki/systems/farming.md`
- Create: `.agent/skills/farming/SKILL.md`
- Modify: `wiki/systems/terrain-and-weather.md` (drop the resolved farming-field open questions)

- [ ] **Step 1: Read the wiki rules and template**

```
Read wiki/CLAUDE.md
Read wiki/_templates/system.md
```

Follow the system-page template exactly: 10 required sections, frontmatter as specified, `[[wikilinks]]` for every cross-system reference, sources block listing every code path + the spec.

- [ ] **Step 2: Write `wiki/systems/farming.md`**

Use these required sections (per `wiki/CLAUDE.md` §4):
- Summary (one paragraph)
- Purpose (architecture, why)
- Responsibilities (what's in scope)
- Key classes / files
- Public API / entry points
- Data flow (the diagram from spec §2)
- Dependencies (upstream / downstream — links to `[[terrain-and-weather]]`, `[[character]]`, `[[character-equipment]]`, `[[world]]`, `[[save-load]]`, `[[world-map-hibernation]]`, `[[world-macro-simulation]]`)
- State & persistence (the table from spec §9.1)
- Known gotchas / edge cases (the spawn-order race, the registry init ordering, the `MapSaveData` disk-persistence dependency)
- Open questions / TODO (copy from spec §10)
- Change log (`- 2026-04-28 — initial implementation — claude`)

The page describes ARCHITECTURE only. Do not duplicate procedures from `.agent/skills/farming/SKILL.md` — link to it in the Sources section.

- [ ] **Step 3: Write `.agent/skills/farming/SKILL.md`**

The skill file is procedural (how-to). Cover:
- "How to add a new crop" — step-by-step (create CropSO, build prefab with CropHarvestable, add stage sprites, wire into Resources/Data/Farming/Crops, optional perennial/destruction config).
- "How to add a new destructible wild Harvestable" — set `_allowDestruction=true`, configure tool + outputs, no farming code needed.
- "Debugging a crop that won't grow" — checklist (registry initialised? cell.IsPlowed? Moisture above threshold? CropSO.MinMoistureForGrowth?).
- "Debugging a perennial that doesn't refill" — checklist.

- [ ] **Step 4: Update `wiki/systems/terrain-and-weather.md`**

Find the "Open questions / TODO" section. The farming-field placeholders (`IsPlowed`, `PlantedCropId`, etc.) should already be there as TODO items. Move them out:
```markdown
Replace:
  - [ ] Farming consumer for IsPlowed/PlantedCropId/GrowthTimer cell fields not yet built.
With:
  Resolved 2026-04-28 by [[farming]] system. See [[farming]] for the consumer.
```

Bump `updated:` frontmatter to `2026-04-28`. Append to `## Change log`:
`- 2026-04-28 — Cell farming fields now consumed by [[farming]]; SendDirtyCellsClientRpc landed. — claude`

- [ ] **Step 5: Commit**

```bash
git add wiki/systems/farming.md .agent/skills/farming/ wiki/systems/terrain-and-weather.md
git commit -m "docs(farming): wiki/systems/farming.md + skill file + terrain-weather update

- New architecture page wiki/systems/farming.md (10 required sections,
  links to spec, no procedural duplication)
- New .agent/skills/farming/SKILL.md (how-to: add crop, debug growth,
  configure destructible wild Harvestable)
- terrain-and-weather.md: resolve the farming-field open questions

Refs: docs/superpowers/specs/2026-04-28-farming-plot-system-design.md §11
project rules #28, #29b."
```

---

## Self-Review (run before declaring complete)

After all tasks ship, re-read the spec front-to-back and confirm coverage:

- §1 Problem statement → entire plan
- §2 Architecture → tasks 5, 6, 7, 8, 9, 10
- §3.1 CropSO → task 1
- §3.2 CropRegistry → tasks 1, 11
- §3.3 SeedSO → task 1
- §3.4 WateringCanSO → task 1
- §3.5 TerrainCell semantics → tasks 5, 6, 8 (no schema change — only field-semantic conventions)
- §4 FarmGrowthSystem pipeline → tasks 5, 6
- §5.1 PlayerController E-key → task 9
- §5.2 CropPlacementManager → task 8
- §5.3 CharacterAction_PlaceCrop → task 8
- §6 CropHarvestable + visual handoff → tasks 4, 7
- §6.1 Two interaction paths on Harvestable → tasks 2, 3
- §6.2 Tap E vs Hold E + UI_InteractionMenu → tasks 9, 10
- §7 Watering → task 8
- §8 CropVisualSpawner → task 7
- §9.1 What persists table → task 14 acceptance criterion 10
- §9.2 PostWakeSweep → task 6
- §9.3 Ordering → task 11
- §9.4 Catch-up → task 12
- §10 Open questions → preserved in wiki/systems/farming.md (task 15)
- §11 Files → tasks 1-13 (every file accounted for)
- §12 Acceptance → task 14
- §13 Build sequence → tasks 1-15 follow it (with deliberate ordering tweaks: dev scaffolding instead of CharacterAction_DestroyHarvestable being unreachable pre-menu)

Plan saved to `docs/superpowers/plans/2026-04-28-farming-plot-system.md`.

---

## Execution Handoff

**Plan complete and saved.** Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Best for this plan because Tasks 2 and 8 are large and benefit from being held in context as one unit.
2. **Inline Execution** — execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.

**Which approach?**
