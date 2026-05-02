# Harvestable Layered Tree Visual Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-sprite tree visual with a 3-layer composition: static trunk + MPB-tinted seasonal foliage + N procedurally-spawned fruit sprites that disappear one-by-one as the tree is harvested. Deterministic across all networked peers.

**Architecture:** Two new types (`TreeHarvestableSO` extending `HarvestableSO`, and `HarvestableLayeredVisual` sibling NetworkBehaviour on the tree prefab). Three small extensions to existing types: `TimeManager.CurrentYearProgress01` accessor, `HarvestableNetSync.RemainingYield` NetVar, and `Harvestable.Harvest()` pushing the new NetVar after each pass. All visual updates are event-driven (`OnNewDay`, `OnStateChanged`, `RemainingYield.OnValueChanged`) — zero per-frame work.

**Tech Stack:** Unity 2D-in-3D, Netcode for GameObjects (NGO), `MaterialPropertyBlock` (rule #25), NUnit EditMode tests, scene-authored prefabs.

**Spec:** [docs/superpowers/specs/2026-05-03-harvestable-layered-tree-visual-design.md](../specs/2026-05-03-harvestable-layered-tree-visual-design.md)

---

## File Structure

| Status | File | Responsibility |
|---|---|---|
| New | `Assets/Scripts/Interactable/Pure/TreeHarvestableSO.cs` | Subclass of `HarvestableSO` adding tree-specific authoring fields (trunk + foliage sprites, seasonal gradient, fruit variants, spawn rect, fruit scale). |
| New | `Assets/Scripts/Interactable/HarvestableLayeredVisual.cs` | Sibling `NetworkBehaviour` that drives the 3-layer rendering. Subscribes to `Harvestable.OnStateChanged`, `TimeManager.OnNewDay`, and `RemainingYield.OnValueChanged`. |
| Modify | `Assets/Scripts/DayNightCycle/TimeManager.cs` | Add `_daysPerYear` field + `CurrentYearProgress01` getter + a static helper for testability. |
| Modify | `Assets/Scripts/Interactable/HarvestableNetSync.cs` | Add `RemainingYield : NetworkVariable<byte>` + bridge subscription. |
| Modify | `Assets/Scripts/Interactable/Harvestable.cs` | Push `RemainingYield` NetVar after `Harvest()`, `ResetHarvestState()`, `Refill()`. |
| New | `Assets/Tests/EditMode/LayeredTreeVisual/LayeredTreeVisual.Tests.asmdef` | Test asmdef referencing `MWI.Interactable.Pure` + main asm. |
| New | `Assets/Tests/EditMode/LayeredTreeVisual/TimeManagerYearProgressTests.cs` | Tests for `CurrentYearProgress01` math (via static helper). |
| New | `Assets/Tests/EditMode/LayeredTreeVisual/TreeHarvestableSOTests.cs` | Tests for SO defaults + accessors. |
| New | `Assets/Resources/Data/Harvestables/Trees/AppleTreeSO.asset` | Smoke-test SO asset. Manual creation in Unity Editor. |
| New | `Assets/Prefabs/Harvestables/AppleTree.prefab` | Tree prefab variant with the 3-layer hierarchy. Manual creation. |
| Modify | `wiki/systems/harvestable.md` | Add "Layered tree visual" section + change-log entry. |
| Modify | `.agent/skills/harvestable-resource-node-specialist/SKILL.md` | Document the new SO + visual component. |

---

## Task 1: Add `TimeManager.CurrentYearProgress01`

**Files:**
- Modify: `Assets/Scripts/DayNightCycle/TimeManager.cs`
- Create: `Assets/Tests/EditMode/LayeredTreeVisual/LayeredTreeVisual.Tests.asmdef`
- Create: `Assets/Tests/EditMode/LayeredTreeVisual/TimeManagerYearProgressTests.cs`

- [ ] **Step 1: Create the test asmdef**

Create `Assets/Tests/EditMode/LayeredTreeVisual/LayeredTreeVisual.Tests.asmdef`:

```json
{
    "name": "LayeredTreeVisual.Tests",
    "rootNamespace": "MWI.Tests.LayeredTreeVisual",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "MWI.Interactable.Pure",
        "Assembly-CSharp"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Write the failing test**

Create `Assets/Tests/EditMode/LayeredTreeVisual/TimeManagerYearProgressTests.cs`:

```csharp
using NUnit.Framework;
using MWI.Time;

namespace MWI.Tests.LayeredTreeVisual
{
    public class TimeManagerYearProgressTests
    {
        [Test]
        public void Day1_With28DaysPerYear_ReturnsZero()
        {
            // Day 1 is the start of the year (we offset by -1 inside the helper so day 1 -> 0).
            float p = TimeManager.ComputeYearProgress01(currentDay: 1, daysPerYear: 28);
            Assert.AreEqual(0f, p, 0.0001f);
        }

        [Test]
        public void Midyear_ReturnsHalf()
        {
            // Day 15 of a 28-day year → (15-1)/28 = 0.5.
            float p = TimeManager.ComputeYearProgress01(currentDay: 15, daysPerYear: 28);
            Assert.AreEqual(0.5f, p, 0.0001f);
        }

        [Test]
        public void EndOfYear_WrapsToZero()
        {
            // Day 29 should land back on the start of the next year (28 % 28 = 0).
            float p = TimeManager.ComputeYearProgress01(currentDay: 29, daysPerYear: 28);
            Assert.AreEqual(0f, p, 0.0001f);
        }

        [Test]
        public void DaysPerYearZero_ReturnsZero_NoDivideByZero()
        {
            float p = TimeManager.ComputeYearProgress01(currentDay: 5, daysPerYear: 0);
            Assert.AreEqual(0f, p, 0.0001f);
        }

        [Test]
        public void DaysPerYearNegative_ReturnsZero()
        {
            float p = TimeManager.ComputeYearProgress01(currentDay: 5, daysPerYear: -10);
            Assert.AreEqual(0f, p, 0.0001f);
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

In Unity Editor → Test Runner (Window → General → Test Runner) → EditMode → Run All. Or via MCP: `mcp__ai-game-developer__tests-run` filtered to `LayeredTreeVisual.Tests`.

Expected: COMPILE ERROR — `TimeManager.ComputeYearProgress01` does not exist.

- [ ] **Step 4: Implement `ComputeYearProgress01` + `CurrentYearProgress01` in `TimeManager`**

Edit `Assets/Scripts/DayNightCycle/TimeManager.cs`. Add a `[Header("Year Settings")]` block + `_daysPerYear` field, and add the accessor + static helper.

After line 21 (`private int _nightStart = 21;`), add:

```csharp

        [Header("Year Settings")]
        [SerializeField, Tooltip("Number of in-game days per year. Drives CurrentYearProgress01 used by seasonal visuals (foliage color etc.).")]
        private int _daysPerYear = 28;
```

After line 35 (`public int CurrentDay { get; private set; } = 1;`), add:

```csharp

        /// <summary>Number of in-game days per year. Inspector-tunable.</summary>
        public int DaysPerYear => _daysPerYear;

        /// <summary>
        /// Continuous [0..1) year-progress derived from <see cref="CurrentDay"/>. Day 1 = 0,
        /// midyear = 0.5, end of year wraps back to 0. Used by foliage gradient sampling
        /// and any other "where are we in the year" visual driven by day-resolution data.
        /// </summary>
        public float CurrentYearProgress01 => ComputeYearProgress01(CurrentDay, _daysPerYear);

        /// <summary>
        /// Pure helper exposed for unit-testing without instantiating a MonoBehaviour.
        /// Defensive against zero / negative <paramref name="daysPerYear"/> (returns 0).
        /// </summary>
        public static float ComputeYearProgress01(int currentDay, int daysPerYear)
        {
            if (daysPerYear <= 0) return 0f;
            int dayInYear = (currentDay - 1) % daysPerYear;
            if (dayInYear < 0) dayInYear += daysPerYear;
            return dayInYear / (float)daysPerYear;
        }
```

- [ ] **Step 5: Run test to verify it passes**

Run the same EditMode tests. Expected: 5 tests pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/DayNightCycle/TimeManager.cs Assets/Tests/EditMode/LayeredTreeVisual/
git commit -m "feat(time): add CurrentYearProgress01 + DaysPerYear field

Pure-helper static method ComputeYearProgress01 keeps the math
unit-testable without instantiating the MonoBehaviour. Defensive
against zero / negative daysPerYear. Used by seasonal foliage
tinting on layered tree harvestables."
```

---

## Task 2: Create `TreeHarvestableSO`

**Files:**
- Create: `Assets/Scripts/Interactable/Pure/TreeHarvestableSO.cs`
- Create: `Assets/Tests/EditMode/LayeredTreeVisual/TreeHarvestableSOTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/LayeredTreeVisual/TreeHarvestableSOTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using MWI.Interactables;

namespace MWI.Tests.LayeredTreeVisual
{
    public class TreeHarvestableSOTests
    {
        [Test]
        public void Defaults_HaveSensibleValues()
        {
            var so = ScriptableObject.CreateInstance<TreeHarvestableSO>();
            Assert.IsNull(so.TrunkSprite);
            Assert.IsNull(so.FoliageSprite);
            Assert.IsNotNull(so.FoliageColorOverYear, "Gradient should be auto-initialised by Unity.");
            Assert.IsNotNull(so.FruitSpriteVariants);
            Assert.AreEqual(0, so.FruitSpriteVariants.Length);
            Assert.AreEqual(Rect.zero, so.FruitSpawnArea);
            Assert.AreEqual(Vector2.one, so.FruitScale);
        }

        [Test]
        public void InheritsHarvestableSOFields()
        {
            var so = ScriptableObject.CreateInstance<TreeHarvestableSO>();
            // Inherited from HarvestableSO — confirms the inheritance chain is intact.
            Assert.IsNotNull(so.HarvestOutputs);
            Assert.IsTrue(so.IsDepletable);
            Assert.AreEqual(5, so.MaxHarvestCount);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run EditMode tests. Expected: COMPILE ERROR — `TreeHarvestableSO` does not exist.

- [ ] **Step 3: Implement `TreeHarvestableSO`**

Create `Assets/Scripts/Interactable/Pure/TreeHarvestableSO.cs`:

```csharp
using UnityEngine;

namespace MWI.Interactables
{
    /// <summary>
    /// Tree-flavoured <see cref="HarvestableSO"/>. Adds the authoring surface for the
    /// 3-layer visual driven by <c>HarvestableLayeredVisual</c>:
    ///
    /// <list type="bullet">
    /// <item><see cref="TrunkSprite"/> — static silhouette under the foliage.</item>
    /// <item><see cref="FoliageSprite"/> — single sprite, MPB-tinted by <see cref="FoliageColorOverYear"/>
    ///       sampled at <c>TimeManager.CurrentYearProgress01</c>.</item>
    /// <item><see cref="FruitSpriteVariants"/> — random pick per spawned fruit. Empty = no fruit.</item>
    /// <item><see cref="FruitSpawnArea"/> — local-space rect (in foliage frame) where fruits may
    ///       spawn. <see cref="Rect.zero"/> = use the foliage sprite's bounds.</item>
    /// <item><see cref="FruitScale"/> — per-fruit scale multiplier.</item>
    /// </list>
    ///
    /// Lives in the <c>MWI.Interactable.Pure</c> asmdef alongside <see cref="HarvestableSO"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Harvestables/Tree")]
    public class TreeHarvestableSO : HarvestableSO
    {
        [Header("Layered tree visual")]
        [SerializeField] private Sprite _trunkSprite;
        [SerializeField] private Sprite _foliageSprite;
        [SerializeField] private Gradient _foliageColorOverYear = new Gradient();
        [SerializeField] private Sprite[] _fruitSpriteVariants = new Sprite[0];
        [Tooltip("Local-space rect (in the foliage Transform's frame) where fruits may spawn. Leave at Rect.zero to use the foliage sprite's own bounds.")]
        [SerializeField] private Rect _fruitSpawnArea = Rect.zero;
        [SerializeField] private Vector2 _fruitScale = Vector2.one;

        public Sprite TrunkSprite => _trunkSprite;
        public Sprite FoliageSprite => _foliageSprite;
        public Gradient FoliageColorOverYear => _foliageColorOverYear;
        public Sprite[] FruitSpriteVariants => _fruitSpriteVariants;
        public Rect FruitSpawnArea => _fruitSpawnArea;
        public Vector2 FruitScale => _fruitScale;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: 2 new tests pass + the 5 from Task 1 still pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Interactable/Pure/TreeHarvestableSO.cs Assets/Tests/EditMode/LayeredTreeVisual/TreeHarvestableSOTests.cs
git commit -m "feat(harvestable): add TreeHarvestableSO authoring surface

Subclass of HarvestableSO carrying the trunk + foliage sprites,
seasonal gradient, fruit sprite variants, spawn rect and scale.
Consumed by HarvestableLayeredVisual (next task)."
```

---

## Task 3: Add `HarvestableNetSync.RemainingYield` NetVar

**Files:**
- Modify: `Assets/Scripts/Interactable/HarvestableNetSync.cs`

This task has no unit test — the NetVar wiring is exercised by the in-Editor smoke test (Task 7).

- [ ] **Step 1: Add the NetVar + subscribe/unsubscribe bridge**

Edit `Assets/Scripts/Interactable/HarvestableNetSync.cs`. After line 35 (`public NetworkVariable<FixedString64Bytes> CropIdNet ...`), add:

```csharp

    /// <summary>Server-replicated remaining harvest count for the layered tree visual.
    /// Drives the per-fruit visibility on every peer so harvesting an apple makes the
    /// matching fruit sprite disappear. Capped at 255 — trees with MaxHarvestCount &gt; 255
    /// would clip; revisit if that ever happens (no current designer wants > 255 fruits).</summary>
    public NetworkVariable<byte> RemainingYield = new NetworkVariable<byte>(0);
```

In `OnNetworkSpawn` (line 44) after `CropIdNet.OnValueChanged += HandleCropIdChange;`, add:

```csharp
        RemainingYield.OnValueChanged += HandleAnyChange;
```

In `OnNetworkDespawn` (line 53) after `CropIdNet.OnValueChanged -= HandleCropIdChange;`, add:

```csharp
        RemainingYield.OnValueChanged -= HandleAnyChange;
```

- [ ] **Step 2: Verify it compiles**

In Unity Editor, wait for recompilation. Check the Console for errors. Or via MCP: `mcp__ai-game-developer__assets-refresh` then `mcp__ai-game-developer__console-get-logs`.

Expected: clean compile.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Interactable/HarvestableNetSync.cs
git commit -m "feat(harvestable): add RemainingYield NetVar for per-fruit visibility

One byte per harvestable, server-write. HarvestableLayeredVisual
subscribes to it so each fruit sprite can be hidden one-by-one as
the tree is harvested by player or NPC. Cap at 255 acceptable for
all current designs."
```

---

## Task 4: Push `RemainingYield` from `Harvestable`

**Files:**
- Modify: `Assets/Scripts/Interactable/Harvestable.cs`

- [ ] **Step 1: Push RemainingYield after each harvest pass**

Edit `Assets/Scripts/Interactable/Harvestable.cs`. In `Harvest()` (line 533) after the line `_currentHarvestCount++;` (line 538), add:

```csharp
        if (_netSync != null) _netSync.RemainingYield.Value = (byte)Mathf.Min(byte.MaxValue, RemainingYield);
```

- [ ] **Step 2: Push RemainingYield on reset / refill**

In `ResetHarvestState()` (line 659), after the line `_currentHarvestCount = 0;` (line 661), add:

```csharp
        if (_netSync != null) _netSync.RemainingYield.Value = (byte)Mathf.Min(byte.MaxValue, _maxHarvestCount);
```

`Refill()` is a one-liner that calls `SetReady()` → `ResetHarvestState()`, so the push above covers both refill and respawn paths automatically.

- [ ] **Step 3: Push RemainingYield on InitializeAtStage (post-load + post-spawn)**

In `InitializeAtStage` (line 799), inside the `if (_netSync != null && _crop != null)` block (around line 828), add **after** the existing 3 NetVar assignments:

```csharp
            _netSync.RemainingYield.Value = startDepleted
                ? (byte)0
                : (byte)Mathf.Min(byte.MaxValue, _maxHarvestCount);
```

This ensures runtime-spawned crops + post-load reconstruction see the right fruit count immediately.

- [ ] **Step 4: Verify it compiles**

Wait for Unity recompile. Check Console for errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Interactable/Harvestable.cs
git commit -m "feat(harvestable): push RemainingYield NetVar after harvest/reset/init

Server-side hooks in Harvest(), ResetHarvestState(), and
InitializeAtStage so HarvestableLayeredVisual sees fruit count
flips on every peer. Refill() inherits via SetReady() →
ResetHarvestState()."
```

---

## Task 5: Implement `HarvestableLayeredVisual`

**Files:**
- Create: `Assets/Scripts/Interactable/HarvestableLayeredVisual.cs`

This is the heaviest task. No unit test — verified in-Editor in Task 7.

- [ ] **Step 1: Create the file with full implementation**

Create `Assets/Scripts/Interactable/HarvestableLayeredVisual.cs`:

```csharp
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Interactables;
using MWI.Time;

/// <summary>
/// Sibling NetworkBehaviour on a tree prefab. Drives the 3-layer composition:
///
/// <list type="number">
/// <item><b>Trunk</b> — static SpriteRenderer, sprite from the SO, never tinted.</item>
/// <item><b>Foliage</b> — static SpriteRenderer, sprite from the SO, tinted via
///       MaterialPropertyBlock by <see cref="TreeHarvestableSO.FoliageColorOverYear"/>
///       sampled at <see cref="TimeManager.CurrentYearProgress01"/>. Refreshed on
///       <see cref="TimeManager.OnNewDay"/>.</item>
/// <item><b>Fruit</b> — N runtime-spawned SpriteRenderers under <see cref="_fruitContainer"/>,
///       N = <see cref="Harvestable._maxHarvestCount"/>, positioned deterministically
///       inside <see cref="TreeHarvestableSO.FruitSpawnArea"/> via a
///       <see cref="NetworkObject.NetworkObjectId"/>-seeded RNG so every peer sees the
///       same layout. Per-fruit visibility tracks <see cref="HarvestableNetSync.RemainingYield"/>.</item>
/// </list>
///
/// All updates are event-driven — zero per-frame work. MaterialPropertyBlock preserves
/// SRP batching (rule #25). Designed to coexist with the existing growth-stage scale
/// lerp in <see cref="Harvestable.ApplyVisual"/>; the trunk / foliage / fruit container
/// ride along on the root transform's localScale.
/// </summary>
public class HarvestableLayeredVisual : NetworkBehaviour
{
    [Header("Hand-wired children")]
    [SerializeField] private SpriteRenderer _trunkRenderer;
    [SerializeField] private SpriteRenderer _foliageRenderer;
    [Tooltip("Empty Transform child. Runtime-spawned fruit SpriteRenderers are parented here. Local-space FruitSpawnArea on the SO is interpreted in this Transform's frame.")]
    [SerializeField] private Transform _fruitContainer;

    private Harvestable _harvestable;
    private HarvestableNetSync _netSync;
    private TreeHarvestableSO _treeSO;
    private MaterialPropertyBlock _mpb;
    private readonly List<SpriteRenderer> _fruitInstances = new List<SpriteRenderer>();
    private bool _initialised;

    private void Awake()
    {
        _harvestable = GetComponent<Harvestable>();
        _netSync = GetComponent<HarvestableNetSync>();
        _mpb = new MaterialPropertyBlock();
    }

    public override void OnNetworkSpawn()
    {
        TryInitialise();
    }

    public override void OnNetworkDespawn()
    {
        Unsubscribe();
        DestroyFruitInstances();
        _initialised = false;
    }

    private void DestroyFruitInstances()
    {
        for (int i = 0; i < _fruitInstances.Count; i++)
        {
            if (_fruitInstances[i] != null)
                Destroy(_fruitInstances[i].gameObject);
        }
        _fruitInstances.Clear();
    }

    private void TryInitialise()
    {
        if (_initialised) return;
        if (_harvestable == null) return;
        _treeSO = _harvestable.SO as TreeHarvestableSO;
        if (_treeSO == null)
        {
            // Not a tree — disable so we don't carry overhead on rocks / crops / etc.
            enabled = false;
            return;
        }

        AssignStaticSprites();
        SpawnFruits();
        Subscribe();
        RefreshAll();

        _initialised = true;
    }

    private void AssignStaticSprites()
    {
        if (_trunkRenderer != null && _treeSO.TrunkSprite != null)
            _trunkRenderer.sprite = _treeSO.TrunkSprite;

        if (_foliageRenderer != null)
        {
            if (_treeSO.FoliageSprite != null)
            {
                _foliageRenderer.sprite = _treeSO.FoliageSprite;
                _foliageRenderer.enabled = true;
            }
            else
            {
                _foliageRenderer.enabled = false;
            }
        }
    }

    private void SpawnFruits()
    {
        if (_fruitContainer == null) return;
        if (_treeSO.FruitSpriteVariants == null || _treeSO.FruitSpriteVariants.Length == 0) return;

        // Spawn count = SO.MaxHarvestCount so late-joiners on a half-harvested tree still
        // create the full slot set; RefreshFruitVisibility then hides already-harvested
        // fruits based on RemainingYield. Cap at byte.MaxValue (NetVar is one byte).
        int count = Mathf.Min(_treeSO.MaxHarvestCount, byte.MaxValue);
        if (count <= 0) return;

        Rect area = _treeSO.FruitSpawnArea;
        if (area == Rect.zero)
            area = ResolveFoliageBoundsAsRect();

        // Deterministic seed: NetworkObjectId is identical on every peer.
        var prevState = Random.state;
        Random.InitState((int)NetworkObject.NetworkObjectId);

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"Fruit{i}");
            go.transform.SetParent(_fruitContainer, worldPositionStays: false);

            float x = Random.Range(area.xMin, area.xMax);
            float y = Random.Range(area.yMin, area.yMax);
            // Slight per-fruit Z offset so any overlap has a deterministic depth order.
            go.transform.localPosition = new Vector3(x, y, -0.001f * (i + 1));
            go.transform.localScale = new Vector3(_treeSO.FruitScale.x, _treeSO.FruitScale.y, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            int spriteIdx = Random.Range(0, _treeSO.FruitSpriteVariants.Length);
            sr.sprite = _treeSO.FruitSpriteVariants[spriteIdx];
            sr.sortingOrder = 2 + i; // unique per fruit, deterministic across peers

            _fruitInstances.Add(sr);
        }

        Random.state = prevState;
    }

    private Rect ResolveFoliageBoundsAsRect()
    {
        if (_foliageRenderer == null || _foliageRenderer.sprite == null)
            return new Rect(-0.5f, -0.5f, 1f, 1f);

        var b = _foliageRenderer.sprite.bounds;
        return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
    }

    private void Subscribe()
    {
        if (TimeManager.Instance != null)
            TimeManager.Instance.OnNewDay += RefreshFoliageColor;
        if (_harvestable != null)
            _harvestable.OnStateChanged += HandleStateChanged;
        if (_netSync != null)
            _netSync.RemainingYield.OnValueChanged += HandleYieldChanged;
    }

    private void Unsubscribe()
    {
        if (TimeManager.Instance != null)
            TimeManager.Instance.OnNewDay -= RefreshFoliageColor;
        if (_harvestable != null)
            _harvestable.OnStateChanged -= HandleStateChanged;
        if (_netSync != null)
            _netSync.RemainingYield.OnValueChanged -= HandleYieldChanged;
    }

    private void HandleStateChanged(Harvestable _) => RefreshFruitVisibility();
    private void HandleYieldChanged(byte _, byte __) => RefreshFruitVisibility();

    private void RefreshAll()
    {
        RefreshFoliageColor();
        RefreshFruitVisibility();
    }

    private void RefreshFoliageColor()
    {
        if (_foliageRenderer == null || _treeSO == null || _treeSO.FoliageColorOverYear == null) return;
        if (TimeManager.Instance == null) return;

        Color c = _treeSO.FoliageColorOverYear.Evaluate(TimeManager.Instance.CurrentYearProgress01);
        _foliageRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_Color", c);
        _foliageRenderer.SetPropertyBlock(_mpb);
    }

    private void RefreshFruitVisibility()
    {
        if (_fruitInstances.Count == 0) return;

        int visible = ResolveVisibleFruitCount();
        for (int i = 0; i < _fruitInstances.Count; i++)
        {
            if (_fruitInstances[i] != null)
                _fruitInstances[i].enabled = i < visible;
        }
    }

    private int ResolveVisibleFruitCount()
    {
        if (_harvestable == null) return 0;
        if (_harvestable.IsDepleted) return 0;

        // Crop-aware: hide all fruit until mature.
        if (_harvestable.SO is MWI.Farming.CropSO crop && _netSync != null)
        {
            if (_netSync.CurrentStage.Value < crop.DaysToMature) return 0;
        }

        if (_netSync != null) return _netSync.RemainingYield.Value;
        return _harvestable.RemainingYield;
    }
}
```

- [ ] **Step 2: Verify it compiles**

In Unity Editor, wait for recompile. Check Console.

Expected: clean compile. If you see "type or namespace 'TreeHarvestableSO' could not be found", confirm Task 2's file is in the `MWI.Interactable.Pure` asmdef path.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Interactable/HarvestableLayeredVisual.cs
git commit -m "feat(harvestable): HarvestableLayeredVisual sibling NetworkBehaviour

Drives the 3-layer tree composition: static trunk + MPB-tinted
foliage (gradient sampled at TimeManager.CurrentYearProgress01) +
N runtime-spawned fruit sprites positioned deterministically via
a NetworkObjectId-seeded RNG so every peer sees the same layout.

All updates event-driven (OnNewDay, OnStateChanged,
RemainingYield.OnValueChanged) — zero per-frame work. Disables
itself on non-tree harvestables so rocks / plain crops pay
nothing."
```

---

## Task 6: Author the `AppleTreeSO` asset + `AppleTree.prefab`

**Files:**
- Create: `Assets/Resources/Data/Harvestables/Trees/AppleTreeSO.asset` (in-Editor)
- Create: `Assets/Prefabs/Harvestables/AppleTree.prefab` (in-Editor)

This task is hand-authored in the Unity Editor. The end goal is one runnable test fixture so Task 7 can verify the visual.

- [ ] **Step 1: Create the SO asset**

In Unity Project window: navigate to `Assets/Resources/Data/Harvestables/`. Right-click → Create → Game → Harvestables → Tree. Name it `AppleTreeSO`. Move it into a `Trees/` subfolder if not already created.

Fill in the Inspector:
- **Identity:** Id = `tree_apple`, DisplayName = `Apple Tree`
- **Yield:** add one `HarvestableOutputEntry` { Item = Apple ItemSO (existing), Count = 1 }
- **Depletion:** IsDepletable = true, MaxHarvestCount = 5, RespawnDelayDays = 1
- **Layered tree visual:**
  - TrunkSprite = a tree-trunk sprite (use any placeholder, e.g. one of the existing tree art assets — find via `mcp__ai-game-developer__assets-find` filter `t:Sprite tree`)
  - FoliageSprite = a foliage sprite
  - FoliageColorOverYear = paint a 4-stop gradient: 0.0 light green (spring), 0.25 dark green (summer), 0.5 orange (autumn), 0.75 brown (late autumn), 1.0 light green again (loops)
  - FruitSpriteVariants = 1–3 apple sprites
  - FruitSpawnArea = leave at `Rect.zero` (use foliage bounds)
  - FruitScale = (0.3, 0.3) to keep apples small relative to foliage

- [ ] **Step 2: Create the prefab**

In Project: navigate to `Assets/Prefabs/Harvestables/`. Right-click → Create → Prefab Variant of an existing tree prefab if one exists, otherwise:

1. In the scene Hierarchy: right-click → Create Empty, name it `AppleTree`.
2. Add components: `Harvestable`, `HarvestableNetSync`, `HarvestableLayeredVisual`, `NetworkObject`, `BoxCollider` (sized to the trunk for interaction zone).
3. Create three children:
   - `Trunk` (empty → add `SpriteRenderer`, sortingOrder = 0)
   - `Foliage` (empty → add `SpriteRenderer`, sortingOrder = 1, position above Trunk on Y axis)
   - `FruitContainer` (empty → no components)
4. On the root: assign `_so` field of `Harvestable` to `AppleTreeSO`. Set `_maxHarvestCount` to 5 (matches SO).
5. On `HarvestableLayeredVisual`: wire `_trunkRenderer`, `_foliageRenderer`, `_fruitContainer` to the matching children.
6. Drag the GameObject from Hierarchy into `Assets/Prefabs/Harvestables/`. Name = `AppleTree`. Delete the scene instance.

- [ ] **Step 3: Register the prefab as a NetworkPrefab**

Open the `NetworkManager` in the scene → `NetworkConfig` → `NetworkPrefabs` list. Add `AppleTree.prefab` (or add it to the existing `NetworkPrefabsList` asset if the project uses one — check existing harvestable prefabs for the pattern).

- [ ] **Step 4: Manually verify the prefab opens cleanly**

Open the prefab in Prefab Mode. Check Inspector for missing references on `HarvestableLayeredVisual` (warning if any field is unassigned). Save.

- [ ] **Step 5: Commit**

```bash
git add Assets/Resources/Data/Harvestables/Trees/AppleTreeSO.asset Assets/Resources/Data/Harvestables/Trees/AppleTreeSO.asset.meta
git add Assets/Prefabs/Harvestables/AppleTree.prefab Assets/Prefabs/Harvestables/AppleTree.prefab.meta
# Plus any NetworkPrefabsList.asset changes
git commit -m "asset(harvestable): AppleTreeSO + AppleTree.prefab smoke fixture

Apple-tree authoring: 5 fruits, foliage gradient looping
spring-summer-autumn-spring, 1-3 apple sprite variants. Prefab
wires Trunk/Foliage/FruitContainer children to
HarvestableLayeredVisual."
```

---

## Task 7: In-Editor smoke test

**Files:** None (verification only).

- [ ] **Step 1: Launch the game in single-player and validate visual**

1. Open a scene that has a `MapController` + `NetworkManager` + a player spawn.
2. Drag an `AppleTree.prefab` instance into the scene. Position it in player view.
3. Press Play (Host mode if there's a multiplayer entry point).
4. Visual checklist:
   - [ ] Trunk sprite visible.
   - [ ] Foliage sprite visible, tinted by the gradient (current day's color).
   - [ ] 5 fruit sprites visible inside the foliage area.
   - [ ] Walk up to the tree, hold E, choose "Pick Apple" — fruit count drops to 4 immediately, and one fruit sprite disappears.
   - [ ] Repeat 4 more times — tree empties out, last fruit disappears.
5. Stop play.

- [ ] **Step 2: Validate seasonal tinting**

1. Press Play. Open the in-game Time debug panel (or use the dev console).
2. Skip days using `SkipToHour` or the time-skip controller until `CurrentDay` traverses one full year (28 days).
3. Watch the foliage tint shift through the gradient.
4. Verify it loops back at day 29.
5. Stop play.

- [ ] **Step 3: Validate multiplayer determinism**

1. Build a standalone host build. Run it as host.
2. Run the Editor as a client connected to the host.
3. Both peers should see the **identical** apple positions and apple sprite picks (because NetworkObjectId is the same).
4. Have the host harvest one apple — both peers should see the same fruit disappear.
5. Have the client harvest one — same check.
6. Disconnect and reconnect the client — late-joiner should see the current `RemainingYield` on spawn (the NGO initial-sync handles this).

- [ ] **Step 4: Validate non-tree harvestables are unaffected**

1. Find an existing `Tree.prefab` (the legacy single-sprite one) or any non-tree harvestable in a scene.
2. Press Play, walk up, harvest it.
3. Confirm the existing visual path still works (visualRoot toggle / sprite swap).

- [ ] **Step 5: Commit any tuning changes**

If you tweaked the SO gradient or FruitSpawnArea during testing:

```bash
git add Assets/Resources/Data/Harvestables/Trees/AppleTreeSO.asset
git commit -m "tune(harvestable): apple tree smoke-test polish

Adjust gradient stops / spawn area / fruit scale based on in-Editor
verification."
```

If no tuning needed, skip this commit.

---

## Task 8: Update wiki + SKILL.md

**Files:**
- Modify: `wiki/systems/harvestable.md`
- Modify: `.agent/skills/harvestable-resource-node-specialist/SKILL.md`

- [ ] **Step 1: Read `wiki/CLAUDE.md` first**

Per project rule #29b: always read `wiki/CLAUDE.md` before any wiki op. It governs frontmatter, naming, wikilinks, sources.

```bash
cat wiki/CLAUDE.md
```

- [ ] **Step 2: Update `wiki/systems/harvestable.md`**

- Bump `updated:` frontmatter to `2026-05-03`.
- Append to `## Change log`:
  ```
  - 2026-05-03 — added layered tree visual subsystem (TreeHarvestableSO + HarvestableLayeredVisual + RemainingYield NetVar) — claude
  ```
- Add a new subsection "Layered tree visual" inside the architecture section describing:
  - The 3 layers (trunk / foliage / fruit).
  - Where the data lives (`TreeHarvestableSO`).
  - How fruit determinism works (NetworkObjectId seed).
  - How seasonal color works (`TimeManager.CurrentYearProgress01` → Gradient).
  - Link to the SKILL.md update for procedural details.

- [ ] **Step 3: Update `.agent/skills/harvestable-resource-node-specialist/SKILL.md`**

Add a "Layered tree visual" section near the bottom (before any "Open questions" or trailing notes). Document:
- The new types and their files (`TreeHarvestableSO`, `HarvestableLayeredVisual`).
- How to author a new tree (the workflow from the spec).
- How `RemainingYield` flows: server `Harvest()` → NetVar → all-peer `HandleYieldChanged` → `RefreshFruitVisibility`.
- The networking matrix from the spec (table form).
- Note that the system disables itself on non-tree harvestables so existing rocks / crops are unaffected.

- [ ] **Step 4: Commit**

```bash
git add wiki/systems/harvestable.md .agent/skills/harvestable-resource-node-specialist/SKILL.md
git commit -m "docs(harvestable): document layered tree visual

Wiki page change-log + new architecture subsection. SKILL.md
gains a 'Layered tree visual' procedural section covering the
authoring workflow + networking matrix."
```

---

## Closing

After Task 8 is committed and pushed:

- All EditMode tests pass (Task 1 + Task 2).
- `AppleTree.prefab` renders correctly in single-player + host/client (Task 7).
- Wiki + SKILL.md are in sync with the code.

Next-steps follow-ups (out of scope for this plan, surface as TODOs in the spec's "Open questions" section if not already there):
- Runtime-spawned trees → needs `TreeRegistry` mirror of `CropRegistry`.
- Discrete `Season` enum on `TimeManager` (deferred per farming spec).
- Per-fruit colour curve to harmonise with seasonal foliage (e.g. apples darker in winter).
- Poisson-disk fruit placement if random clustering becomes a problem.
