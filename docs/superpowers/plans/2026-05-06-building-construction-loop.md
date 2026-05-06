# Building Construction Loop — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the silent auto-complete construction state with an owner-driven loop — scaffolding visual, item drop on the footprint, continuous tick-based Construct action, leftover eviction, multiplayer-safe and persisted.

**Architecture:** Same building prefab carries `_constructionVisualRoot` ↔ `_completedVisualRoot`; `Building.HandleStateChanged` toggles. A sibling `ConstructionSiteScanner` MonoBehaviour ticks server-side at 2 Hz to update a networked progress meter. A new `CharacterAction_Continuous` base + `CharacterAction_FinishConstruction` consume items per tick from `BuildingZone` and call `Building.Finalize()` when progress hits 1. Persistence extends `BuildingSaveData`.

**Tech Stack:** Unity 2022+, NGO (Netcode for GameObjects), C#, NUnit (EditMode tests), Unity Editor MCP for prefab edits.

**Spec:** [docs/superpowers/specs/2026-05-06-building-construction-loop-design.md](../specs/2026-05-06-building-construction-loop-design.md)

---

## File Structure

### New files

| Path | Purpose |
|------|---------|
| `Assets/Scripts/World/Buildings/Construction/DeliveredMaterialEntry.cs` | `INetworkSerializable` struct — one delivered-material slot for `NetworkList`. |
| `Assets/Scripts/World/Buildings/Construction/DeliveredMaterialEntryDTO.cs` | Plain `[Serializable]` DTO — save-data twin (AssetGuid-keyed, version-resilient). |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_Continuous.cs` | Abstract base — condition-terminated, server-ticked, cancel-on-movement. |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_FinishConstruction.cs` | Concrete continuous action — per-tick item consumption + `Finalize` on progress 1. |
| `Assets/Scripts/World/Buildings/Construction/ConstructionSiteScanner.cs` | Server-only 2 Hz scanner — observational meter feeder. |
| `Assets/Scripts/World/Buildings/Construction/BuildingInteractable.cs` | Player-facing interactable surface; Phase 1 hosts "Finish Construction". |
| `Assets/Scripts/Character/Skills/SkillId.cs` | Enum used by the builder-skill stub on `Character`. |
| `Assets/Tests/EditMode/Construction/Construction.Tests.asmdef` | Test assembly definition. |
| `Assets/Tests/EditMode/Construction/DeliveredMaterialEntrySerializationTests.cs` | Network struct round-trip. |
| `Assets/Tests/EditMode/Construction/ConstructionProgressMathTests.cs` | Progress formula (clamped sum). |
| `Assets/Tests/EditMode/Construction/PerimeterMathTests.cs` | `NearestPerimeterPoint` geometry. |
| `Assets/Tests/EditMode/Construction/ContinuousActionDispatchTests.cs` | `CharacterActions.ExecuteAction` dispatches to tick routine. |
| `Assets/Tests/EditMode/Construction/BuildingSaveDataConstructionTests.cs` | Save-data round-trip with new fields. |

### Modified files

| Path | Why |
|------|-----|
| `Assets/Scripts/World/Buildings/Building.cs` | Add visual roots, `ConstructionProgress`, `DeliveredMaterials`, `Finalize`, `EvictLeftoversToPerimeter`, defer default furniture, expose `GetPhysicalItemsInCollider`. |
| `Assets/Scripts/Character/CharacterActions/CharacterActions.cs` | Dispatch continuous actions through a tick routine. |
| `Assets/Scripts/Character/Character.cs` | Add `GetSkillLevelOrZero(SkillId)` stub. |
| `Assets/Scripts/World/MapSystem/MapRegistry.cs` (where `BuildingSaveData` lives) | Add `ConstructionProgress` + `DeliveredMaterialsDTO` fields. |
| `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` | Route click on `BuildingInteractable` → queue `CharacterAction_FinishConstruction`. |
| `Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs` | Live progress, delivered breakdown, "Force Finish" dev button. |

### Prefab updates (Unity Editor MCP)

All building prefabs in `Assets/Resources/...` (any prefab whose root carries a `Building` component) require:
1. Two child GameObjects authored: `_constructionVisualRoot` (scaffolding placeholder) and `_completedVisualRoot` (existing renderers re-parented under it).
2. A sibling `ConstructionSiteScanner` component on the building root.
3. The two roots wired into `Building`'s SerializeField slots.

Prefabs are batch-edited as a separate task at the end so script-side changes can be verified first.

---

## Phase A — Foundation primitives

### Task 1: `DeliveredMaterialEntry` network struct

**Files:**
- Create: `Assets/Scripts/World/Buildings/Construction/DeliveredMaterialEntry.cs`
- Create: `Assets/Tests/EditMode/Construction/Construction.Tests.asmdef`
- Test: `Assets/Tests/EditMode/Construction/DeliveredMaterialEntrySerializationTests.cs`

- [ ] **Step 1.1: Write the failing test**

Create `Assets/Tests/EditMode/Construction/Construction.Tests.asmdef`:

```json
{
    "name": "Construction.Tests",
    "rootNamespace": "",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "Unity.Netcode.Runtime",
        "Unity.Netcode.Components"
    ],
    "includePlatforms": [ "Editor" ],
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

Note: needs to reference the runtime asmdef that contains `DeliveredMaterialEntry` once it exists — add that asmdef name to `references` after Step 1.3 if the project's runtime scripts live in a named asmdef. If runtime scripts use the default Assembly-CSharp, no extra reference is needed.

Create `Assets/Tests/EditMode/Construction/DeliveredMaterialEntrySerializationTests.cs`:

```csharp
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;

public class DeliveredMaterialEntrySerializationTests
{
    [Test]
    public void RoundTrips_RequirementIndex_AndDelivered()
    {
        var original = new DeliveredMaterialEntry
        {
            RequirementIndex = 3,
            Delivered = 17
        };

        // BufferSerializer<T>'s constructor is internal to Unity.Netcode. External
        // assemblies must drive the round-trip via the public WriteNetworkSerializable
        // / ReadNetworkSerializable extension points (which themselves invoke the
        // struct's NetworkSerialize override under the hood).
        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteNetworkSerializable(original);

        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out DeliveredMaterialEntry roundtripped);

        Assert.AreEqual(original.RequirementIndex, roundtripped.RequirementIndex);
        Assert.AreEqual(original.Delivered, roundtripped.Delivered);
    }

    [Test]
    public void Equality_BasedOnFields()
    {
        var a = new DeliveredMaterialEntry { RequirementIndex = 1, Delivered = 5 };
        var b = new DeliveredMaterialEntry { RequirementIndex = 1, Delivered = 5 };
        var c = new DeliveredMaterialEntry { RequirementIndex = 1, Delivered = 6 };

        Assert.IsTrue(a.Equals(b));
        Assert.IsFalse(a.Equals(c));
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }
}
```

- [ ] **Step 1.2: Run test to verify it fails**

Use Unity MCP `tests-run` with `testFilter: "DeliveredMaterialEntrySerializationTests"` and `testMode: "EditMode"`.
Expected: FAIL with "type or namespace 'DeliveredMaterialEntry' could not be found".

- [ ] **Step 1.3: Write minimal implementation**

Create `Assets/Scripts/World/Buildings/Construction/DeliveredMaterialEntry.cs`:

```csharp
using System;
using Unity.Netcode;

/// <summary>
/// One delivered-material slot replicated through Building.DeliveredMaterials NetworkList.
/// RequirementIndex is the position in Building._constructionRequirements (compact —
/// avoids replicating ItemSO refs/strings every change).
/// </summary>
[Serializable]
public struct DeliveredMaterialEntry : INetworkSerializable, IEquatable<DeliveredMaterialEntry>
{
    public int RequirementIndex;
    public int Delivered;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref RequirementIndex);
        serializer.SerializeValue(ref Delivered);
    }

    public bool Equals(DeliveredMaterialEntry other)
        => RequirementIndex == other.RequirementIndex && Delivered == other.Delivered;

    public override bool Equals(object obj)
        => obj is DeliveredMaterialEntry e && Equals(e);

    public override int GetHashCode()
        => unchecked((RequirementIndex * 397) ^ Delivered);
}
```

- [ ] **Step 1.4: Run test to verify it passes**

Run via Unity MCP `tests-run` with `testFilter: "DeliveredMaterialEntrySerializationTests"`.
Expected: 2/2 PASS.

- [ ] **Step 1.5: Commit**

```bash
git add Assets/Scripts/World/Buildings/Construction/DeliveredMaterialEntry.cs Assets/Tests/EditMode/Construction/
git commit -m "feat(construction): add DeliveredMaterialEntry network struct + tests"
```

---

### Task 2: `DeliveredMaterialEntryDTO` save-data twin

**Files:**
- Create: `Assets/Scripts/World/Buildings/Construction/DeliveredMaterialEntryDTO.cs`

This is a trivial sibling — no test required (it's a plain serializable POCO; round-trip is covered later by `BuildingSaveData` tests in Task 13).

- [ ] **Step 2.1: Create the DTO**

```csharp
using System;

/// <summary>
/// Save-data twin of DeliveredMaterialEntry. Keys by ItemSO AssetGuid (not requirement
/// index) so the snapshot survives a designer-time edit to _constructionRequirements
/// ordering between save and load.
/// </summary>
[Serializable]
public class DeliveredMaterialEntryDTO
{
    public string ItemAssetGuid;
    public int Delivered;
}
```

- [ ] **Step 2.2: Commit**

```bash
git add Assets/Scripts/World/Buildings/Construction/DeliveredMaterialEntryDTO.cs
git commit -m "feat(construction): add DeliveredMaterialEntryDTO save-data twin"
```

---

### Task 3: `SkillId` enum + `Character.GetSkillLevelOrZero` stub

**Files:**
- Create: `Assets/Scripts/Character/Skills/SkillId.cs`
- Modify: `Assets/Scripts/Character/Character.cs` — add stub method

- [ ] **Step 3.1: Create the enum**

```csharp
// Assets/Scripts/Character/Skills/SkillId.cs
public enum SkillId
{
    Builder = 0,
    // Future: Crafting, Combat, Foraging, etc.
}
```

- [ ] **Step 3.2: Add the stub on Character**

Locate `Character.cs` (`Assets/Scripts/Character/Character.cs`). Add this method anywhere in the public-method block:

```csharp
/// <summary>
/// Returns the actor's current level for a given skill, or 0 if the skill system is
/// not yet wired (Phase 1). Phase 2 wires this into the actual skill component.
/// Used by CharacterAction_FinishConstruction's per-tick consume budget formula.
/// </summary>
public int GetSkillLevelOrZero(SkillId skill)
{
    // Phase 1 stub — returns 0 for everyone. Replace with skill-component lookup
    // when the builder-skill system lands (Phase 2).
    return 0;
}
```

- [ ] **Step 3.3: Verify compiles**

Use Unity MCP `assets-refresh` (forces compilation), then `console-get-logs` to verify zero compile errors.
Expected: no compile errors.

- [ ] **Step 3.4: Commit**

```bash
git add Assets/Scripts/Character/Skills/SkillId.cs Assets/Scripts/Character/Character.cs
git commit -m "feat(character): add SkillId enum + GetSkillLevelOrZero stub"
```

---

### Task 4: `CharacterAction_Continuous` abstract base

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_Continuous.cs`

No test for this task alone — its behaviour is exercised by Task 5 (dispatch test) and Task 12 (concrete action test).

- [ ] **Step 4.1: Create the base class**

```csharp
using UnityEngine;

/// <summary>
/// Sibling of <see cref="CharacterAction"/> for actions that are condition-terminated
/// rather than timer-terminated.
///
/// The runner ticks <see cref="OnTick"/> at <see cref="TickIntervalSeconds"/>; when
/// OnTick returns true, the action finishes. <see cref="CharacterAction.OnApplyEffect"/>
/// is sealed to a no-op — continuous actions implement everything in <see cref="OnTick"/>.
///
/// Default <see cref="CharacterAction.AllowsMovementDuringAction"/> = false (inherited),
/// so any movement intent (player WASD, NPC re-route) cancels via the existing
/// <c>CharacterGameController</c> path.
///
/// Authored 2026-05-06 — see docs/superpowers/specs/2026-05-06-building-construction-loop-design.md.
/// </summary>
public abstract class CharacterAction_Continuous : CharacterAction
{
    /// <summary>Server tick cadence. Default 1 Hz; subclasses may override.</summary>
    public float TickIntervalSeconds { get; protected set; } = 1f;

    /// <summary>
    /// Server-ticked. Return true when the terminating condition has been met
    /// (the runner will then call <see cref="CharacterAction.Finish"/>).
    /// </summary>
    public abstract bool OnTick();

    protected CharacterAction_Continuous(Character character) : base(character, duration: 0f) { }

    /// <summary>
    /// Continuous actions never use a fixed duration; OnTick replaces OnApplyEffect.
    /// Sealed to prevent accidental subclass overrides re-introducing duration semantics.
    /// </summary>
    public sealed override void OnApplyEffect() { /* no-op */ }
}
```

- [ ] **Step 4.2: Verify compiles**

Use Unity MCP `assets-refresh` + `console-get-logs`.
Expected: no compile errors.

- [ ] **Step 4.3: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_Continuous.cs
git commit -m "feat(character-actions): add CharacterAction_Continuous abstract base"
```

---

### Task 5: Dispatch continuous actions from `CharacterActions.ExecuteAction`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterActions.cs`
- Test: `Assets/Tests/EditMode/Construction/ContinuousActionDispatchTests.cs`

The existing flow branches on `Duration <= 0` (instant) vs `Duration > 0` (timed coroutine). We add a third branch: `action is CharacterAction_Continuous c` → start `ActionContinuousTickRoutine(c)`.

- [ ] **Step 5.1: Write the failing test**

Continuous-action dispatch is hard to fully test without a Character + scene, so we test the *pure decision logic* via a trivial fake. Create `Assets/Tests/EditMode/Construction/ContinuousActionDispatchTests.cs`:

```csharp
using NUnit.Framework;

public class ContinuousActionDispatchTests
{
    private class FakeContinuousAction : CharacterAction_Continuous
    {
        public int TickCount;
        public int TerminateAfterTicks = 3;

        public FakeContinuousAction() : base(character: null) { }

        public override void OnStart() { }

        public override bool OnTick()
        {
            TickCount++;
            return TickCount >= TerminateAfterTicks;
        }
    }

    [Test]
    public void OnTick_ReportsContinue_UntilTerminationCondition()
    {
        var action = new FakeContinuousAction { TerminateAfterTicks = 3 };

        Assert.IsFalse(action.OnTick(), "First tick should keep ticking");
        Assert.IsFalse(action.OnTick(), "Second tick should keep ticking");
        Assert.IsTrue(action.OnTick(), "Third tick should terminate");
    }

    [Test]
    public void OnApplyEffect_IsSealedNoOp()
    {
        var action = new FakeContinuousAction();
        Assert.DoesNotThrow(() => action.OnApplyEffect());
    }

    [Test]
    public void Duration_Defaults_To_Zero()
    {
        var action = new FakeContinuousAction();
        Assert.AreEqual(0f, action.Duration);
    }
}
```

- [ ] **Step 5.2: Run test to verify it fails on missing class**

(If Task 4 hasn't been committed yet, this would still fail. After Task 4 it should pass without further changes — the dispatcher modification is what we need to add and verify next.)

Run via Unity MCP `tests-run`, filter `"ContinuousActionDispatchTests"`.
Expected: PASS (because we're testing the abstract contract; dispatcher comes next).

- [ ] **Step 5.3: Modify `CharacterActions.ExecuteAction` dispatch**

Open `Assets/Scripts/Character/CharacterActions/CharacterActions.cs`. Locate the block that starts at `// 2. GESTION DU FLUX (Instantané vs Temporisé)` (around line 53). Replace the existing branch:

```csharp
        // 2. GESTION DU FLUX (Instantané vs Temporisé vs Continu)
        if (action is CharacterAction_Continuous continuous)
        {
            // Continuous actions tick until OnTick() returns true. No fixed duration.
            _actionRoutine = StartCoroutine(ActionContinuousTickRoutine(continuous));
        }
        else if (action.Duration <= 0)
        {
            try
            {
                if (IsServer || action is CharacterVisualProxyAction)
                    action.OnApplyEffect();
                else
                    action.OnApplyEffect();

                action.Finish();
            }
            catch (Exception e)
            {
                Debug.LogError($"[CharacterActions] Erreur Action Instantanée: {e.Message}");
                CleanupAction();
            }
        }
        else
        {
            _actionRoutine = StartCoroutine(ActionTimerRoutine(_currentAction));
        }
```

Then add the new routine near `ActionTimerRoutine` (around line 521):

```csharp
private IEnumerator ActionContinuousTickRoutine(CharacterAction_Continuous action)
{
    if (action == null) yield break;

    var wait = new WaitForSeconds(action.TickIntervalSeconds);

    while (true)
    {
        // External cancellation safeguard — if CleanupAction nulled out _currentAction
        // (combat, incapacitation, movement cancel), exit cleanly.
        if (_currentAction != action) yield break;

        bool finished;
        try
        {
            // Per Rule #18: only the server runs OnTick. Clients see the effects via
            // NetworkVariable/NetworkList replication from inside OnTick.
            finished = IsServer ? action.OnTick() : false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[CharacterActions] Erreur OnTick action continue '{action?.ActionName}' on '{_character?.CharacterName}':");
            Debug.LogException(e);
            CleanupAction();
            yield break;
        }

        if (finished)
        {
            try { action.Finish(); }
            catch (Exception e)
            {
                Debug.LogError($"[CharacterActions] Erreur Finish action continue '{action?.ActionName}':");
                Debug.LogException(e);
                CleanupAction();
            }
            yield break;
        }

        yield return wait;
    }
}
```

- [ ] **Step 5.4: Verify the dispatch tests still pass + no compile errors**

Run via Unity MCP `tests-run`, filter `"ContinuousActionDispatchTests"`.
Use `console-get-logs` to verify zero compile errors.
Expected: 3/3 PASS.

- [ ] **Step 5.5: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterActions.cs Assets/Tests/EditMode/Construction/ContinuousActionDispatchTests.cs
git commit -m "feat(character-actions): dispatch continuous actions through tick routine"
```

---

## Phase B — Building extensions

### Task 6: Pure progress-formula tests + helper

**Files:**
- Test: `Assets/Tests/EditMode/Construction/ConstructionProgressMathTests.cs`
- Modify (later in Task 9): `Assets/Scripts/World/Buildings/Building.cs` — add `ComputeProgress` helper

We test the formula in isolation before wiring it into Building. Since `CraftingIngredient` references `ItemSO` (a `ScriptableObject` — Unity-tied), we test against a plain int dictionary structure that mirrors the formula.

- [ ] **Step 6.1: Write the failing test**

```csharp
// Assets/Tests/EditMode/Construction/ConstructionProgressMathTests.cs
using NUnit.Framework;
using System.Collections.Generic;

public class ConstructionProgressMathTests
{
    // Compute progress = clamped sum(min(deliveredᵢ, requiredᵢ)) / sum(requiredᵢ).
    // Mirrors Building.ComputeProgress (Task 9). Test the formula in isolation
    // so we can refactor Building without re-running PlayMode tests.
    private static float Compute(int[] required, int[] delivered)
    {
        int totalRequired = 0;
        int totalSatisfied = 0;
        for (int i = 0; i < required.Length; i++)
        {
            int r = required[i];
            int d = i < delivered.Length ? delivered[i] : 0;
            totalRequired += r;
            totalSatisfied += System.Math.Min(d, r);
        }
        if (totalRequired <= 0) return 1f; // empty requirements → already complete
        return UnityEngine.Mathf.Clamp01((float)totalSatisfied / totalRequired);
    }

    [Test] public void EmptyRequirements_ReturnsOne()
        => Assert.AreEqual(1f, Compute(new int[0], new int[0]));

    [Test] public void NothingDelivered_ReturnsZero()
        => Assert.AreEqual(0f, Compute(new[] { 100 }, new[] { 0 }));

    [Test] public void HalfDelivered_ReturnsHalf()
        => Assert.AreEqual(0.5f, Compute(new[] { 100 }, new[] { 50 }));

    [Test] public void OverDelivered_ClampsToOne()
        => Assert.AreEqual(1f, Compute(new[] { 100 }, new[] { 200 }));

    [Test] public void MultiType_PartialAcrossTypes()
    {
        // 50/100 logs (0.5) + 30/100 stones (0.3) → satisfied=80, required=200 → 0.4
        Assert.AreEqual(0.4f, Compute(new[] { 100, 100 }, new[] { 50, 30 }));
    }

    [Test] public void MultiType_OneOverdeliveredOneEmpty()
    {
        // 200/100 logs (clamped to 100) + 0/50 stones → 100/150 = 0.6666…
        var p = Compute(new[] { 100, 50 }, new[] { 200, 0 });
        Assert.AreEqual(100f / 150f, p, 0.0001f);
    }
}
```

- [ ] **Step 6.2: Run test to verify it passes**

The math is self-contained in this test file — it should pass on first run. We're locking the algorithm in a test before mirroring it in `Building.ComputeProgress` in Task 9.

Run via Unity MCP `tests-run`, filter `"ConstructionProgressMathTests"`.
Expected: 6/6 PASS.

- [ ] **Step 6.3: Commit**

```bash
git add Assets/Tests/EditMode/Construction/ConstructionProgressMathTests.cs
git commit -m "test(construction): pin progress formula in standalone test"
```

---

### Task 7: Pure perimeter-math tests

**Files:**
- Test: `Assets/Tests/EditMode/Construction/PerimeterMathTests.cs`

`NearestPerimeterPoint` will be implemented on `Building` in Task 9. We pin the math in a test now using the same axis-aligned-bounds shape `BoxCollider.bounds` returns.

- [ ] **Step 7.1: Write the failing test**

```csharp
// Assets/Tests/EditMode/Construction/PerimeterMathTests.cs
using NUnit.Framework;
using UnityEngine;

public class PerimeterMathTests
{
    // Mirrors Building.NearestPerimeterPoint (Task 9). Pure math on Bounds + Vector3.
    // Returns the point on the AABB surface nearest to `inside`, plus the outward normal.
    private static (Vector3 point, Vector3 normal) Nearest(Bounds bounds, Vector3 inside)
    {
        // Distance from `inside` to each face plane.
        float dxMin = inside.x - bounds.min.x;
        float dxMax = bounds.max.x - inside.x;
        float dzMin = inside.z - bounds.min.z;
        float dzMax = bounds.max.z - inside.z;

        // We project to the nearest of the 4 vertical faces (we keep Y stable; floor stays floor).
        float minDist = dxMin;
        Vector3 normal = Vector3.left;
        Vector3 face = new Vector3(bounds.min.x, inside.y, inside.z);

        if (dxMax < minDist) { minDist = dxMax; normal = Vector3.right;   face = new Vector3(bounds.max.x, inside.y, inside.z); }
        if (dzMin < minDist) { minDist = dzMin; normal = Vector3.back;    face = new Vector3(inside.x, inside.y, bounds.min.z); }
        if (dzMax < minDist) {                  normal = Vector3.forward; face = new Vector3(inside.x, inside.y, bounds.max.z); }

        return (face, normal);
    }

    private static Bounds Box(float minX, float minZ, float maxX, float maxZ, float y = 0f)
        => new Bounds(
            center: new Vector3((minX + maxX) * 0.5f, y, (minZ + maxZ) * 0.5f),
            size:   new Vector3(maxX - minX, 1f, maxZ - minZ));

    [Test] public void Centre_NearestIsAnyFace_ButValid()
    {
        // 10x10 box centred at origin, item at centre — any face is equidistant; we just
        // assert the result lies on the box surface.
        var b = Box(-5f, -5f, 5f, 5f);
        var r = Nearest(b, Vector3.zero);
        Assert.IsTrue(Mathf.Abs(r.point.x) == 5f || Mathf.Abs(r.point.z) == 5f);
    }

    [Test] public void OffCentreEastward_PicksEastFace()
    {
        var b = Box(-5f, -5f, 5f, 5f);
        var r = Nearest(b, new Vector3(3f, 0f, 0f));
        Assert.AreEqual(5f, r.point.x);
        Assert.AreEqual(Vector3.right, r.normal);
    }

    [Test] public void OffCentreNorthward_PicksNorthFace()
    {
        var b = Box(-5f, -5f, 5f, 5f);
        var r = Nearest(b, new Vector3(0f, 0f, 4f));
        Assert.AreEqual(5f, r.point.z);
        Assert.AreEqual(Vector3.forward, r.normal);
    }

    [Test] public void NonOriginBox_PicksCorrectFace()
    {
        var b = Box(10f, 10f, 20f, 20f);
        var r = Nearest(b, new Vector3(11f, 0f, 15f));
        Assert.AreEqual(10f, r.point.x);
        Assert.AreEqual(Vector3.left, r.normal);
    }
}
```

- [ ] **Step 7.2: Run test to verify it passes**

Run via Unity MCP `tests-run`, filter `"PerimeterMathTests"`.
Expected: 4/4 PASS.

- [ ] **Step 7.3: Commit**

```bash
git add Assets/Tests/EditMode/Construction/PerimeterMathTests.cs
git commit -m "test(construction): pin nearest-perimeter-point math"
```

---

### Task 8: Add visual roots, NetworkVariable, NetworkList to Building

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Building.cs`

Adds fields only. Wiring happens in Task 9.

- [ ] **Step 8.1: Add SerializeFields, NetworkVariable, NetworkList**

Open `Assets/Scripts/World/Buildings/Building.cs`. Inside the class body, in the `[Header("Construction")]` section (around line 60), expand to:

```csharp
[Header("Construction")]
[SerializeField] protected List<CraftingIngredient> _constructionRequirements = new List<CraftingIngredient>();
protected Dictionary<ItemSO, int> _contributedMaterials = new Dictionary<ItemSO, int>();

[Tooltip("Child GameObject holding the scaffolding renderers/colliders shown while UnderConstruction. Active iff CurrentState == UnderConstruction.")]
[SerializeField] protected GameObject _constructionVisualRoot;

[Tooltip("Child GameObject holding the finished-building renderers/colliders shown after Complete. Active iff CurrentState == Complete.")]
[SerializeField] protected GameObject _completedVisualRoot;

/// <summary>
/// 0..1 progress towards completion. Server-write, everyone-read. Updated by
/// ConstructionSiteScanner (observational, between deliveries) and by
/// CharacterAction_FinishConstruction.OnTick (authoritative, during the action).
/// Reset to 0 at construction start; frozen at 1 after Complete.
/// </summary>
public NetworkVariable<float> ConstructionProgress = new NetworkVariable<float>(
    0f,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);

/// <summary>
/// Per-requirement delivered counts, replicated to clients so UIs can show per-type
/// breakdown without server-side _contributedMaterials access. Indexed by position in
/// _constructionRequirements. Server-write only.
/// </summary>
public NetworkList<DeliveredMaterialEntry> DeliveredMaterials;
```

In `Awake()` (or directly in field-initialization for `NetworkList`), `NetworkList` must be instantiated before `OnNetworkSpawn`. Initialize it in field declaration:

```csharp
public NetworkList<DeliveredMaterialEntry> DeliveredMaterials = new NetworkList<DeliveredMaterialEntry>(
    new DeliveredMaterialEntry[0],
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);
```

- [ ] **Step 8.2: Verify compiles**

Use Unity MCP `assets-refresh` + `console-get-logs`.
Expected: no compile errors.

- [ ] **Step 8.3: Commit**

```bash
git add Assets/Scripts/World/Buildings/Building.cs
git commit -m "feat(building): add construction visual roots + progress NetworkVariable + DeliveredMaterials NetworkList"
```

---

### Task 9: Building wiring — visual swap, ComputeProgress, GetPhysicalItemsInCollider, Finalize, EvictLeftoversToPerimeter, defer default furniture

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Building.cs`

Five separate changes; each gets its own step + commit.

- [ ] **Step 9.1: Wire visual swap into HandleStateChanged**

Find the existing `HandleStateChanged` method (around line 405). Modify so it always toggles the visual roots first, then does the existing `Complete`-only side-effects:

```csharp
private void HandleStateChanged(MWI.WorldSystem.BuildingState previousValue, MWI.WorldSystem.BuildingState newValue)
{
    // Visual swap runs on every peer (client + server), every state change.
    ApplyConstructionVisuals(newValue);

    if (newValue == MWI.WorldSystem.BuildingState.Complete)
    {
        OnConstructionComplete?.Invoke();

        // Sync state to CommunityData so hibernation save data stays accurate
        if (IsServer && MWI.WorldSystem.MapRegistry.Instance != null)
        {
            foreach (var comm in MWI.WorldSystem.MapRegistry.Instance.GetAllCommunities())
            {
                var entry = comm.ConstructedBuildings.Find(b => b.BuildingId == BuildingId);
                if (entry != null)
                {
                    entry.State = MWI.WorldSystem.BuildingState.Complete;
                    entry.ConstructionProgress = 1f;
                    break;
                }
            }
        }
    }
}

/// <summary>
/// Toggles _constructionVisualRoot vs _completedVisualRoot based on the current state.
/// Idempotent — safe to call repeatedly. Each peer runs this locally on every
/// _currentState.OnValueChanged (registered in Start).
/// </summary>
private void ApplyConstructionVisuals(MWI.WorldSystem.BuildingState state)
{
    bool underConstruction = (state == MWI.WorldSystem.BuildingState.UnderConstruction);

    if (_constructionVisualRoot != null && _constructionVisualRoot.activeSelf != underConstruction)
        _constructionVisualRoot.SetActive(underConstruction);

    if (_completedVisualRoot != null && _completedVisualRoot.activeSelf == underConstruction)
        _completedVisualRoot.SetActive(!underConstruction);
}
```

Also call `ApplyConstructionVisuals(_currentState.Value)` once at the end of `Start()` so peers that joined the session AFTER the building's initial state is set still get the correct visual immediately:

```csharp
protected virtual void Start()
{
    // ... (existing body unchanged) ...

    // Apply initial visual state — late-joiners need this; HandleStateChanged only fires
    // on subsequent changes.
    ApplyConstructionVisuals(_currentState.Value);
}
```

- [ ] **Step 9.2: Defer TrySpawnDefaultFurniture until Complete**

Find `OnNetworkSpawn` (around line 255). Replace the unconditional server-side `TrySpawnDefaultFurniture()` call with a state-gated call + a hook in the state-change path:

```csharp
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();

    if (IsServer && NetworkBuildingId.Value.IsEmpty)
    {
        // ... existing GUID derivation logic unchanged ...
    }

    ConfigureNavMeshObstacles();

    // Server-only: spawn default furniture *only* after Complete. Pre-Complete spawns
    // would put usable furniture inside an unfinished building (visually inside the
    // scaffolding) and create operational gameplay before the construction loop
    // finishes. The state-change handler kicks the spawn when state flips to Complete.
    if (IsServer && _currentState.Value == MWI.WorldSystem.BuildingState.Complete)
    {
        TrySpawnDefaultFurniture();
    }
}
```

Then in `HandleStateChanged`, inside the `Complete` branch, add the call after the `OnConstructionComplete` invocation:

```csharp
if (newValue == MWI.WorldSystem.BuildingState.Complete)
{
    OnConstructionComplete?.Invoke();

    // Server-only post-completion side effects.
    if (IsServer)
    {
        // Default-furniture spawn was deferred from OnNetworkSpawn until completion —
        // run it now (idempotent via _defaultFurnitureSpawned guard).
        try { TrySpawnDefaultFurniture(); }
        catch (System.Exception e) { Debug.LogException(e, this); }

        // Eject any leftover items still on the footprint (over-delivered or wrong-type).
        try { EvictLeftoversToPerimeter(); }
        catch (System.Exception e) { Debug.LogException(e, this); }
    }

    // ... existing CommunityData sync unchanged ...
}
```

- [ ] **Step 9.3: Add `GetPhysicalItemsInCollider(Collider)` helper + refactor existing zone variant to call into it**

Locate `GetPhysicalItemsInZone` (around line 499). Add a sibling method that takes a `Collider` directly, and refactor the existing zone method to delegate to it:

```csharp
/// <summary>
/// Returns physical, uncarried WorldItems whose colliders overlap the BoxCollider
/// passed in. Allocation-light: uses a reused Collider[] buffer.
/// Server-side use: ConstructionSiteScanner passes _buildingZone, EvictLeftoversToPerimeter
/// passes _buildingZone, GetPhysicalItemsInZone delegates here with zone.GetComponent<BoxCollider>().
/// </summary>
private static readonly Collider[] _itemOverlapBuffer = new Collider[64];

public List<WorldItem> GetPhysicalItemsInCollider(Collider collider, List<WorldItem> resultBuffer = null)
{
    var items = resultBuffer ?? new List<WorldItem>();
    items.Clear();
    if (collider == null) return items;
    if (!(collider is BoxCollider boxCol)) return items; // only BoxCollider supported

    Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
    Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;
    Quaternion rot = boxCol.transform.rotation;

    int count = Physics.OverlapBoxNonAlloc(center, halfExtents, _itemOverlapBuffer, rot, Physics.AllLayers, QueryTriggerInteraction.Collide);
    for (int i = 0; i < count; i++)
    {
        var col = _itemOverlapBuffer[i];
        if (col == null) continue;

        var worldItem = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
        if (worldItem != null && !worldItem.IsBeingCarried && !items.Contains(worldItem))
        {
            items.Add(worldItem);
        }
    }
    return items;
}

/// <summary>
/// Existing zone-shaped overload, retained for compatibility with delivery / storage
/// zone consumers. Delegates to GetPhysicalItemsInCollider via the zone's BoxCollider.
/// </summary>
public List<WorldItem> GetPhysicalItemsInZone(Zone zone)
{
    var items = new List<WorldItem>();
    if (zone == null) return items;
    var boxCol = zone.GetComponent<BoxCollider>();
    return GetPhysicalItemsInCollider(boxCol, items);
}
```

- [ ] **Step 9.4: Add `Finalize()` + `ComputeProgress` helper + `NearestPerimeterPoint` + `EvictLeftoversToPerimeter`**

Append these methods to the class:

```csharp
/// <summary>
/// Server-only. Mirrors the formula tested in ConstructionProgressMathTests.
/// Reads _constructionRequirements + _contributedMaterials and returns
/// clamped sum(min(deliveredᵢ, requiredᵢ)) / sum(requiredᵢ).
/// </summary>
public float ComputeProgress()
{
    if (_constructionRequirements == null || _constructionRequirements.Count == 0) return 1f;

    int totalRequired = 0;
    int totalSatisfied = 0;
    for (int i = 0; i < _constructionRequirements.Count; i++)
    {
        var req = _constructionRequirements[i];
        if (req == null || req.Item == null) continue;
        int r = req.Amount;
        int d = _contributedMaterials.TryGetValue(req.Item, out int v) ? v : 0;
        totalRequired += r;
        totalSatisfied += System.Math.Min(d, r);
    }
    if (totalRequired <= 0) return 1f;
    return Mathf.Clamp01((float)totalSatisfied / totalRequired);
}

/// <summary>
/// Server-only. Atomic transition from UnderConstruction to Complete:
/// flips the state (which fires HandleStateChanged → visual swap +
/// TrySpawnDefaultFurniture + EvictLeftoversToPerimeter automatically).
///
/// Idempotent: a second call when already Complete is a silent no-op.
///
/// Called by CharacterAction_FinishConstruction.OnTick when progress hits 1.
/// </summary>
public void Finalize()
{
    if (!IsServer) return;
    if (_currentState.Value == MWI.WorldSystem.BuildingState.Complete) return;

    _currentState.Value = MWI.WorldSystem.BuildingState.Complete;
    if (ConstructionProgress.Value < 1f) ConstructionProgress.Value = 1f;
    Debug.Log($"<color=green>[Building.Construction]</color> {buildingName} completed by Finalize().");
}

/// <summary>
/// Returns the point on the AABB perimeter (vertical faces only — Y is preserved)
/// nearest to `inside`, plus the outward face normal. Pure math; mirrors
/// PerimeterMathTests.
/// </summary>
private static (Vector3 point, Vector3 normal) NearestPerimeterPoint(Bounds bounds, Vector3 inside)
{
    float dxMin = inside.x - bounds.min.x;
    float dxMax = bounds.max.x - inside.x;
    float dzMin = inside.z - bounds.min.z;
    float dzMax = bounds.max.z - inside.z;

    float minDist = dxMin;
    Vector3 normal = Vector3.left;
    Vector3 face = new Vector3(bounds.min.x, inside.y, inside.z);

    if (dxMax < minDist) { minDist = dxMax; normal = Vector3.right;   face = new Vector3(bounds.max.x, inside.y, inside.z); }
    if (dzMin < minDist) { minDist = dzMin; normal = Vector3.back;    face = new Vector3(inside.x, inside.y, bounds.min.z); }
    if (dzMax < minDist) {                  normal = Vector3.forward; face = new Vector3(inside.x, inside.y, bounds.max.z); }

    return (face, normal);
}

/// <summary>
/// Server-only. After Complete, evicts any remaining WorldItems on the footprint
/// to just outside its perimeter so they don't clip into the finished building's
/// interior. Snaps to NavMesh when possible, otherwise free-falls onto the eject
/// point.
/// </summary>
private void EvictLeftoversToPerimeter()
{
    if (!IsServer) return;
    if (_buildingZone == null) return;

    var leftovers = GetPhysicalItemsInCollider(_buildingZone);
    if (leftovers == null || leftovers.Count == 0) return;

    Bounds bounds = _buildingZone.bounds;
    foreach (var item in leftovers)
    {
        if (item == null || item.IsBeingCarried) continue;

        try
        {
            var (point, normal) = NearestPerimeterPoint(bounds, item.transform.position);
            Vector3 ejectPoint = point + normal * 0.5f;

            if (UnityEngine.AI.NavMesh.SamplePosition(ejectPoint, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
                item.transform.position = hit.position;
            else
                item.transform.position = ejectPoint;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
        }
    }
}
```

- [ ] **Step 9.5: Verify compiles + run all Construction tests**

Use Unity MCP `assets-refresh` + `console-get-logs` to verify zero compile errors.
Run via Unity MCP `tests-run`, filter `"Construction"` (matches asmdef name).
Expected: all Phase A + Phase B tests PASS.

- [ ] **Step 9.6: Commit**

```bash
git add Assets/Scripts/World/Buildings/Building.cs
git commit -m "feat(building): visual swap, ComputeProgress, Finalize, leftover eviction, deferred default furniture"
```

---

## Phase C — Scanner

### Task 10: `ConstructionSiteScanner` MonoBehaviour

**Files:**
- Create: `Assets/Scripts/World/Buildings/Construction/ConstructionSiteScanner.cs`

This is exercised by manual PlayMode + the multiplayer testing matrix. There's no clean EditMode test (it depends on `Building` + a real scene + Physics), so we rely on the math/serialization tests written earlier and PlayMode verification later.

- [ ] **Step 10.1: Create the scanner**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Server-only sub-component on a Building. While the building is UnderConstruction,
/// ticks at 2 Hz: scans physical WorldItems inside Building.BuildingZone, buckets by
/// ItemSO, updates Building.ConstructionProgress + Building.DeliveredMaterials.
///
/// Purely observational — does not consume items. Item consumption happens inside
/// CharacterAction_FinishConstruction.OnTick (per-tick) and via Building.EvictLeftovers
/// on completion.
///
/// Authored 2026-05-06 — see docs/superpowers/specs/2026-05-06-building-construction-loop-design.md.
/// </summary>
[RequireComponent(typeof(Building))]
public class ConstructionSiteScanner : MonoBehaviour
{
    [Tooltip("Server tick cadence in seconds. Default 0.5s (2 Hz).")]
    [SerializeField] private float _tickIntervalSeconds = 0.5f;

    private Building _building;
    private float _tickTimer;

    // Reused per Rule #34 — zero per-tick allocation.
    private readonly List<WorldItem> _scratchItems = new List<WorldItem>(64);
    private readonly Dictionary<ItemSO, int> _bucketCache = new Dictionary<ItemSO, int>(8);

    private void Awake() => _building = GetComponent<Building>();

    private void Update()
    {
        if (_building == null) return;
        if (!_building.IsServer) return;
        if (!_building.IsUnderConstruction) return;

        _tickTimer += Time.deltaTime;
        if (_tickTimer < _tickIntervalSeconds) return;
        _tickTimer = 0f;

        try { Tick(); }
        catch (System.Exception e) { Debug.LogException(e, this); }
    }

    private void Tick()
    {
        var zone = _building.BuildingZone;
        if (zone == null) return;

        // Scan physical items in the footprint.
        _building.GetPhysicalItemsInCollider(zone, _scratchItems);

        // Bucket by ItemSO, summing item Amounts.
        _bucketCache.Clear();
        foreach (var item in _scratchItems)
        {
            if (item == null || item.IsBeingCarried) continue;
            var so = item.ItemSO;
            if (so == null) continue;
            int amt = item.Amount;
            if (_bucketCache.TryGetValue(so, out int existing)) _bucketCache[so] = existing + amt;
            else _bucketCache[so] = amt;
        }

        // Compare against requirements; update DeliveredMaterials NetworkList by index.
        var reqs = _building.ConstructionRequirements;
        if (reqs == null) return;

        // Step 1: build target entries (server-side throwaway list — small, idle GC OK).
        // Replicate only when a value actually changed.
        var list = _building.DeliveredMaterials;
        for (int i = 0; i < reqs.Count; i++)
        {
            var req = reqs[i];
            if (req == null || req.Item == null) continue;

            int delivered = _bucketCache.TryGetValue(req.Item, out int b) ? Mathf.Min(b, req.Amount) : 0;
            UpsertDeliveredEntry(list, i, delivered);
        }

        // Recompute progress & write only on meaningful change.
        // Note: ComputeProgress reads _contributedMaterials, which is the consume-time
        // ledger written by the action; for pre-action display we mirror the logic by
        // summing list entries against required amounts.
        float progress = ComputeProgressFromList(reqs, list);
        if (Mathf.Abs(progress - _building.ConstructionProgress.Value) > 0.001f)
        {
            _building.ConstructionProgress.Value = Mathf.Clamp01(progress);
        }
    }

    private static void UpsertDeliveredEntry(NetworkList<DeliveredMaterialEntry> list, int reqIndex, int delivered)
    {
        for (int j = 0; j < list.Count; j++)
        {
            var entry = list[j];
            if (entry.RequirementIndex == reqIndex)
            {
                if (entry.Delivered != delivered)
                {
                    list[j] = new DeliveredMaterialEntry { RequirementIndex = reqIndex, Delivered = delivered };
                }
                return;
            }
        }
        list.Add(new DeliveredMaterialEntry { RequirementIndex = reqIndex, Delivered = delivered });
    }

    private static float ComputeProgressFromList(System.Collections.Generic.IReadOnlyList<CraftingIngredient> reqs, NetworkList<DeliveredMaterialEntry> list)
    {
        int totalRequired = 0;
        int totalSatisfied = 0;
        for (int i = 0; i < reqs.Count; i++)
        {
            var r = reqs[i];
            if (r == null || r.Item == null) continue;
            totalRequired += r.Amount;

            int delivered = 0;
            for (int j = 0; j < list.Count; j++)
            {
                var e = list[j];
                if (e.RequirementIndex == i) { delivered = e.Delivered; break; }
            }
            totalSatisfied += Mathf.Min(delivered, r.Amount);
        }
        if (totalRequired <= 0) return 1f;
        return Mathf.Clamp01((float)totalSatisfied / totalRequired);
    }
}
```

Note: this references `NetworkList<DeliveredMaterialEntry>` directly — needs `using Unity.Netcode;` at top.

- [ ] **Step 10.2: Add the using directive at the top**

```csharp
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
```

- [ ] **Step 10.3: Verify compiles**

`assets-refresh` + `console-get-logs`.
Expected: no compile errors.

- [ ] **Step 10.4: Commit**

```bash
git add Assets/Scripts/World/Buildings/Construction/ConstructionSiteScanner.cs
git commit -m "feat(construction): add ConstructionSiteScanner server-tick observer"
```

---

## Phase D — Interactable + Action

### Task 11: `BuildingInteractable` skeleton

**Files:**
- Create: `Assets/Scripts/World/Buildings/Construction/BuildingInteractable.cs`

Minimal Phase 1 surface — exposes "Finish Construction" when `IsUnderConstruction` and the requester's `CharacterId` matches `Building.PlacedByCharacterId`. Stub seats for future Abandon / Sell / OpenInterior.

- [ ] **Step 11.1: Create the interactable**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-facing interactable surface on a Building. Phase 1 exposes only "Finish
/// Construction" when the actor is the building's placer (PlacedByCharacterId match).
/// Stub seats: Abandon, Sell, OpenInterior — wired in later phases.
///
/// Players queue actions via PlayerController routing (Rule #33). NPCs (Phase 2)
/// reach the same actions through GOAP.
///
/// Authored 2026-05-06.
/// </summary>
[RequireComponent(typeof(Building))]
public class BuildingInteractable : MonoBehaviour
{
    public enum InteractionId
    {
        None = 0,
        FinishConstruction = 1,
        // Phase 2 stubs:
        // Abandon = 100,
        // Sell = 101,
        // OpenInterior = 102,
    }

    private Building _building;

    private void Awake() => _building = GetComponent<Building>();

    /// <summary>
    /// Returns the InteractionIds available to the requesting actor right now.
    /// Caller fills the result list (no per-call allocation; reuse a buffer).
    /// </summary>
    public void GetAvailableInteractions(Character actor, List<InteractionId> result)
    {
        if (result == null) return;
        result.Clear();
        if (_building == null || actor == null) return;

        if (_building.IsUnderConstruction && IsOwner(actor))
        {
            result.Add(InteractionId.FinishConstruction);
        }
    }

    /// <summary>
    /// True iff the actor's CharacterId matches Building.PlacedByCharacterId.
    /// Phase 2 will broaden to include co-owners / community manager authority.
    /// </summary>
    public bool IsOwner(Character actor)
    {
        if (_building == null || actor == null) return false;
        var placedBy = _building.PlacedByCharacterId.Value.ToString();
        if (string.IsNullOrEmpty(placedBy)) return false;
        return placedBy == actor.CharacterId;
    }

    /// <summary>
    /// Player-input entry point. Looks up the action class for the InteractionId,
    /// instantiates it, and queues via the actor's CharacterActions.ExecuteAction.
    /// Server-RPC dispatch happens inside the action itself when needed.
    /// </summary>
    public bool TryQueueInteraction(InteractionId id, Character actor)
    {
        if (_building == null || actor == null) return false;

        switch (id)
        {
            case InteractionId.FinishConstruction:
                if (!_building.IsUnderConstruction) return false;
                if (!IsOwner(actor)) return false;
                var action = new CharacterAction_FinishConstruction(actor, _building);
                return actor.CharacterActions != null && actor.CharacterActions.ExecuteAction(action);

            default:
                return false;
        }
    }
}
```

- [ ] **Step 11.2: Verify compiles (will fail until Task 12 lands)**

Expected at this point: compile error referencing `CharacterAction_FinishConstruction` (defined next). That's fine — leave the file in place; the next task makes it compile.

- [ ] **Step 11.3: Stage but don't commit yet**

```bash
git add Assets/Scripts/World/Buildings/Construction/BuildingInteractable.cs
```

We commit after Task 12 lands so the tree is never broken on a commit boundary.

---

### Task 12: `CharacterAction_FinishConstruction`

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_FinishConstruction.cs`

- [ ] **Step 12.1: Create the action**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Continuous, condition-terminated, cancel-on-movement action. Per server tick:
///   1. Compute consume budget = 1 + actor.GetSkillLevelOrZero(SkillId.Builder) / N
///   2. For each pending requirement, consume up to budget units from
///      Building.BuildingZone (despawn matching WorldItems) and the actor's inventory.
///   3. Recompute progress; write Building.ConstructionProgress + DeliveredMaterials.
///   4. If progress >= 1f → Building.Finalize() and return true (action ends).
///   5. If consumed nothing this tick → stallTicks++; return true once stall limit hit.
///
/// Authored 2026-05-06 — see docs/superpowers/specs/2026-05-06-building-construction-loop-design.md.
/// </summary>
public class CharacterAction_FinishConstruction : CharacterAction_Continuous
{
    /// <summary>Skill formula denominator (Phase 1 unused — actor.GetSkillLevelOrZero == 0).</summary>
    public const int SkillBudgetDivisor = 5;

    /// <summary>Auto-exit after this many no-consume ticks (~5s at 1 Hz default).</summary>
    public const int MaxStallTicks = 5;

    private readonly Building _target;
    private int _stallTicks;
    private readonly List<WorldItem> _scratch = new List<WorldItem>(32);

    public override string ActionName => "Finish Construction";

    public CharacterAction_FinishConstruction(Character character, Building target) : base(character)
    {
        _target = target;
        TickIntervalSeconds = 1f;
    }

    public override bool CanExecute()
    {
        if (_target == null || character == null) return false;
        if (!_target.IsUnderConstruction) return false;
        // Owner gate (Phase 1: only the placer can finalize)
        var placedBy = _target.PlacedByCharacterId.Value.ToString();
        if (string.IsNullOrEmpty(placedBy)) return false;
        if (placedBy != character.CharacterId) return false;
        // Spatial gate
        if (!IsActorInsideBuildingZone()) return false;
        return true;
    }

    public override void OnStart()
    {
        _stallTicks = 0;
    }

    public override bool OnTick()
    {
        if (_target == null || character == null) return true;

        // Re-validate every tick (state, ownership, position).
        if (!_target.IsUnderConstruction) return true;
        var placedBy = _target.PlacedByCharacterId.Value.ToString();
        if (string.IsNullOrEmpty(placedBy) || placedBy != character.CharacterId) return true;
        if (!IsActorInsideBuildingZone()) return true;

        int budget = 1 + (character.GetSkillLevelOrZero(SkillId.Builder) / SkillBudgetDivisor);
        int totalConsumed = 0;

        var reqs = _target.ConstructionRequirements;
        if (reqs == null || reqs.Count == 0) return true;

        for (int i = 0; i < reqs.Count && budget > 0; i++)
        {
            var req = reqs[i];
            if (req == null || req.Item == null) continue;

            int currentDelivered = _target.ContributedMaterials.TryGetValue(req.Item, out int v) ? v : 0;
            int needed = req.Amount - currentDelivered;
            if (needed <= 0) continue;

            int take = Mathf.Min(needed, budget);

            int fromZone = ConsumeFromZone(_target.BuildingZone, req.Item, take);
            int fromInv = ConsumeFromActorInventory(character, req.Item, take - fromZone);
            int consumed = fromZone + fromInv;

            if (consumed > 0)
            {
                _target.ContributeMaterial(req.Item, consumed); // existing method bumps _contributedMaterials
            }

            totalConsumed += consumed;
            budget -= consumed;
        }

        // Update networked meter.
        float progress = _target.ComputeProgress();
        if (Mathf.Abs(progress - _target.ConstructionProgress.Value) > 0.001f)
        {
            _target.ConstructionProgress.Value = Mathf.Clamp01(progress);
        }

        if (progress >= 1f)
        {
            _target.Finalize();
            return true; // done
        }

        if (totalConsumed == 0)
        {
            _stallTicks++;
            if (_stallTicks >= MaxStallTicks) return true; // graceful exit
        }
        else
        {
            _stallTicks = 0;
        }

        return false;
    }

    public override void OnCancel()
    {
        // No rollback — already-consumed credits stay locked. Owner can re-engage.
        _stallTicks = 0;
    }

    // ────────────────────── Helpers ──────────────────────

    private bool IsActorInsideBuildingZone()
    {
        if (_target == null || _target.BuildingZone == null) return false;
        var bounds = _target.BuildingZone.bounds;
        return bounds.Contains(character.transform.position);
    }

    /// <summary>
    /// Despawns up to `amount` WorldItems whose ItemSO matches `target` from the zone.
    /// Returns the actual amount consumed. Server-only API.
    /// </summary>
    private int ConsumeFromZone(Collider zoneCollider, ItemSO target, int amount)
    {
        if (amount <= 0 || zoneCollider == null || target == null) return 0;

        _target.GetPhysicalItemsInCollider(zoneCollider, _scratch);

        int consumed = 0;
        for (int i = 0; i < _scratch.Count && consumed < amount; i++)
        {
            var w = _scratch[i];
            if (w == null || w.IsBeingCarried) continue;
            if (w.ItemSO != target) continue;

            int take = Mathf.Min(w.Amount, amount - consumed);
            if (take >= w.Amount)
            {
                // Whole stack consumed — despawn the WorldItem.
                consumed += w.Amount;
                try
                {
                    var no = w.GetComponent<Unity.Netcode.NetworkObject>();
                    if (no != null && no.IsSpawned) no.Despawn(destroy: true);
                    else GameObject.Destroy(w.gameObject);
                }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            else
            {
                // Partial stack — decrement the WorldItem's amount.
                w.Amount -= take;
                consumed += take;
            }
        }
        return consumed;
    }

    /// <summary>
    /// Server-only. Bonus path for owners who carry construction items in inventory —
    /// consume from inventory if the zone runs short. Phase 1: stub returns 0 to avoid
    /// touching CharacterEquipment until that integration is verified by hand. Wire
    /// the real inventory pull in a follow-up task once the zone path is proven.
    /// </summary>
    private int ConsumeFromActorInventory(Character actor, ItemSO target, int amount)
    {
        // Phase 1: stub. Wire in CharacterEquipment.RemoveItem(target, amount) after
        // PlayMode-MP verification confirms the zone path is working end-to-end.
        return 0;
    }
}
```

- [ ] **Step 12.2: Verify compiles**

`assets-refresh` + `console-get-logs`.
Expected: no compile errors anywhere — `BuildingInteractable` (staged from Task 11) now has its dependency.

- [ ] **Step 12.3: Commit Phase D together**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_FinishConstruction.cs
# (Task 11's BuildingInteractable was already staged)
git commit -m "feat(construction): BuildingInteractable + CharacterAction_FinishConstruction continuous build"
```

---

## Phase E — Persistence

### Task 13: Extend `BuildingSaveData` with progress + delivered materials

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapRegistry.cs` (where `BuildingSaveData` is defined)
- Test: `Assets/Tests/EditMode/Construction/BuildingSaveDataConstructionTests.cs`

- [ ] **Step 13.1: Locate `BuildingSaveData`**

Use Unity MCP `script-read` on `Assets/Scripts/World/MapSystem/MapRegistry.cs`. Find the `BuildingSaveData` class definition. If it lives in a separate file in the same folder, read that one instead. Note the existing fields (BuildingId, PrefabId, Position, Rotation, State, ConstructionProgress if any, ContributedMaterials list).

- [ ] **Step 13.2: Write the failing round-trip test**

```csharp
// Assets/Tests/EditMode/Construction/BuildingSaveDataConstructionTests.cs
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class BuildingSaveDataConstructionTests
{
    [Test]
    public void RoundTrip_ConstructionProgress_PreservedToJson()
    {
        var dto = new MWI.WorldSystem.BuildingSaveData
        {
            BuildingId = "test-id",
            PrefabId = "Hut_A",
            State = MWI.WorldSystem.BuildingState.UnderConstruction,
            ConstructionProgress = 0.42f,
            DeliveredMaterials = new List<DeliveredMaterialEntryDTO>
            {
                new DeliveredMaterialEntryDTO { ItemAssetGuid = "abc123", Delivered = 50 },
                new DeliveredMaterialEntryDTO { ItemAssetGuid = "def456", Delivered = 25 },
            }
        };

        string json = JsonUtility.ToJson(dto);
        var parsed = JsonUtility.FromJson<MWI.WorldSystem.BuildingSaveData>(json);

        Assert.AreEqual("test-id", parsed.BuildingId);
        Assert.AreEqual(MWI.WorldSystem.BuildingState.UnderConstruction, parsed.State);
        Assert.AreEqual(0.42f, parsed.ConstructionProgress, 0.0001f);
        Assert.AreEqual(2, parsed.DeliveredMaterials.Count);
        Assert.AreEqual("abc123", parsed.DeliveredMaterials[0].ItemAssetGuid);
        Assert.AreEqual(50, parsed.DeliveredMaterials[0].Delivered);
    }

    [Test]
    public void RoundTrip_EmptyDeliveredMaterials_DoesNotThrow()
    {
        var dto = new MWI.WorldSystem.BuildingSaveData
        {
            BuildingId = "empty-id",
            PrefabId = "Hut_B",
            State = MWI.WorldSystem.BuildingState.Complete,
            ConstructionProgress = 1f,
            DeliveredMaterials = new List<DeliveredMaterialEntryDTO>()
        };

        Assert.DoesNotThrow(() =>
        {
            string json = JsonUtility.ToJson(dto);
            var parsed = JsonUtility.FromJson<MWI.WorldSystem.BuildingSaveData>(json);
            Assert.AreEqual(0, parsed.DeliveredMaterials.Count);
        });
    }
}
```

- [ ] **Step 13.3: Run test — expected to fail**

Run via Unity MCP `tests-run`, filter `"BuildingSaveDataConstructionTests"`.
Expected: FAIL (fields don't exist yet).

- [ ] **Step 13.4: Add fields to `BuildingSaveData`**

In `MapRegistry.cs`, find the `BuildingSaveData` class (`[System.Serializable]`-attributed). Add the new fields:

```csharp
/// <summary>
/// Persisted progress meter snapshot. Pre-warms the UI on map wake-up so the meter
/// doesn't blink to 0 between MapController.WakeUp and the next ConstructionSiteScanner
/// tick. The next scanner tick is the source of truth — this is a UX hint only.
/// </summary>
public float ConstructionProgress;

/// <summary>
/// Per-requirement delivered counts (keyed by ItemSO AssetGuid for ordering-resilience).
/// Round-tripped on save/load; restored into Building._contributedMaterials.
/// </summary>
public List<DeliveredMaterialEntryDTO> DeliveredMaterials = new List<DeliveredMaterialEntryDTO>();
```

Also extend `BuildingSaveData.FromBuilding(Building, Vector3)` to populate them:

```csharp
public static BuildingSaveData FromBuilding(Building b, Vector3 mapOrigin)
{
    var data = new BuildingSaveData
    {
        // ... existing assignments unchanged ...
        ConstructionProgress = b.ConstructionProgress.Value,
        DeliveredMaterials = new List<DeliveredMaterialEntryDTO>(),
    };

#if UNITY_EDITOR
    // AssetGuid is only resolvable in-editor; runtime needs an alternative path
    // (e.g. ItemSO.AssetId field). For Phase 1 we serialize an empty list at runtime
    // and rely on item-on-ground persistence — which already round-trips through the
    // standard WorldItem save pipeline — to rebuild the meter on next scanner tick.
    // The DTO snapshot is a UX hint; not source of truth.
    foreach (var kv in b.ContributedMaterials)
    {
        if (kv.Key == null) continue;
        string guid = UnityEditor.AssetDatabase.GetAssetPath(kv.Key) is string p && !string.IsNullOrEmpty(p)
            ? UnityEditor.AssetDatabase.AssetPathToGUID(p)
            : null;
        if (string.IsNullOrEmpty(guid)) continue;
        data.DeliveredMaterials.Add(new DeliveredMaterialEntryDTO { ItemAssetGuid = guid, Delivered = kv.Value });
    }
#endif

    return data;
}
```

Note the `#if UNITY_EDITOR` guard: at standalone runtime the AssetDatabase isn't available. We accept that the DTO snapshot is empty in standalone builds — the WorldItem-on-ground persistence already handles rebuilding the meter via the next scanner tick. If/when a runtime-resolvable ItemSO.AssetId field is introduced, this branch can be replaced with that lookup.

If `BuildingSaveData.Restore(...)` exists (a method that pulls fields back into a `Building` instance), add an inverse branch that copies `data.ConstructionProgress` into `b.ConstructionProgress.Value` and resolves `DeliveredMaterials` via `AssetDatabase.GUIDToAssetPath` → `AssetDatabase.LoadAssetAtPath<ItemSO>` (under the same editor guard). For runtime-only restore (player builds), skip the dictionary restore and let the scanner rebuild on its first tick.

- [ ] **Step 13.5: Run test — expected to pass**

Run via Unity MCP `tests-run`, filter `"BuildingSaveDataConstructionTests"`.
Expected: 2/2 PASS.

- [ ] **Step 13.6: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MapRegistry.cs Assets/Tests/EditMode/Construction/BuildingSaveDataConstructionTests.cs
git commit -m "feat(building-save): persist ConstructionProgress + DeliveredMaterials snapshot"
```

---

## Phase F — Wiring

### Task 14: PlayerController hookup for `BuildingInteractable`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`

Per Rule #33, all player input that controls the player character lives in `PlayerController.cs`. We add a click-on-building-interactable path that calls `BuildingInteractable.TryQueueInteraction(FinishConstruction, _character)`.

- [ ] **Step 14.1: Inspect existing input flow in PlayerController**

Use Unity MCP `script-read` on `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`. Identify the existing right-click / click-to-target / interaction-input block (where existing actions like `CharacterStartInteraction` are queued).

- [ ] **Step 14.2: Add the construction-interaction path**

Inside `PlayerController.Update()` (gated on `IsOwner`), in the existing left-click / interaction block, add a raycast that checks for `BuildingInteractable`:

```csharp
// Inside PlayerController.Update(), gated on IsOwner.
// Insert near the existing click-handling block.

if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
{
    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
    if (Physics.Raycast(ray, out RaycastHit hit, 100f))
    {
        var interactable = hit.collider.GetComponentInParent<BuildingInteractable>();
        if (interactable != null && interactable.IsOwner(_character))
        {
            // Phase 1: only one available interaction (FinishConstruction). Phase 2 will
            // open a context menu when GetAvailableInteractions returns 2+ entries.
            _scratchInteractions.Clear();
            interactable.GetAvailableInteractions(_character, _scratchInteractions);
            if (_scratchInteractions.Count == 1)
            {
                interactable.TryQueueInteraction(_scratchInteractions[0], _character);
                return; // input consumed
            }
        }
    }
}
```

Add a field for the scratch buffer at the top of the class (Rule #34 — no per-frame alloc):

```csharp
private readonly System.Collections.Generic.List<BuildingInteractable.InteractionId> _scratchInteractions = new System.Collections.Generic.List<BuildingInteractable.InteractionId>(4);
```

`IsPointerOverUI()` is presumed to be the existing helper for `EventSystem.current.IsPointerOverGameObject()` — if it doesn't exist, add it locally.

- [ ] **Step 14.3: Verify compiles**

`assets-refresh` + `console-get-logs`.
Expected: no compile errors.

- [ ] **Step 14.4: Commit**

```bash
git add Assets/Scripts/Character/CharacterControllers/PlayerController.cs
git commit -m "feat(player-input): route click on BuildingInteractable to construction action"
```

---

### Task 15: `BuildingInspectorView` dev tool extensions

**Files:**
- Modify: `Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs`

Surface live progress, delivered breakdown, and a "Force Finish" dev button (Rule #28).

- [ ] **Step 15.1: Read the existing inspector view**

Use Unity MCP `script-read` on the file. Identify the existing render block that already shows construction state + pending materials.

- [ ] **Step 15.2: Add progress + delivered + Force Finish**

Inside the construction section of the inspector, add (using the project's existing IMGUI / DevMode panel pattern — match the surrounding code style):

```csharp
// Construction section (inside the existing IsUnderConstruction branch):
GUILayout.Label($"Progress: {(_target.ConstructionProgress.Value * 100f):F1}%");

GUILayout.Label("Delivered:");
var reqs = _target.ConstructionRequirements;
var list = _target.DeliveredMaterials;
for (int i = 0; i < reqs.Count; i++)
{
    var req = reqs[i];
    if (req == null || req.Item == null) continue;
    int delivered = 0;
    for (int j = 0; j < list.Count; j++)
        if (list[j].RequirementIndex == i) { delivered = list[j].Delivered; break; }
    GUILayout.Label($"  {req.Item.ItemName}: {delivered} / {req.Amount}");
}

if (GUILayout.Button("Force Finish (DEV)") && _target.IsServer)
{
    _target.Finalize();
}
```

Match the actual button / label primitive used by the existing panel (search the file for `GUILayout.Button` / `EditorGUILayout` / a project-specific helper).

- [ ] **Step 15.3: Verify compiles**

`assets-refresh` + `console-get-logs`.
Expected: no compile errors.

- [ ] **Step 15.4: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs
git commit -m "feat(devmode): live construction progress + Force Finish in BuildingInspectorView"
```

---

## Phase G — Prefab updates (Unity Editor MCP)

### Task 16: Author scaffolding visual on a single representative prefab

**Files:**
- Prefab: pick one Building prefab as a reference (e.g. `Hut_A` or whichever simplest building exists in `Assets/Resources/Building/...`).

The goal: prove the visual swap works end-to-end before rolling across all prefabs.

- [ ] **Step 16.1: List existing building prefabs**

Use Unity MCP `assets-find` with `t:Prefab` filter scoped to building folders. Note the paths.

- [ ] **Step 16.2: Open the representative prefab**

Use Unity MCP `assets-prefab-open` on the chosen path.

- [ ] **Step 16.3: Add `_completedVisualRoot` child**

Use `gameobject-create` to make a new empty child of the prefab root named `CompletedVisual`. Use `gameobject-find` to locate the existing child renderers (the meshes that currently constitute the building's appearance), then `gameobject-set-parent` to re-parent each under the new `CompletedVisual` node.

- [ ] **Step 16.4: Add `_constructionVisualRoot` child**

Use `gameobject-create` to add a sibling named `ConstructionVisual`. Inside, author placeholder scaffolding — a few box meshes scaled and positioned to suggest a wood frame. Concrete steps (one of many valid approaches):

1. `gameobject-create` → `Scaffold_Beam_1` under `ConstructionVisual`, scale `(0.2, 3, 0.2)`, position roughly at one footprint corner.
2. Repeat for 3 more corner posts.
3. `gameobject-create` → `Scaffold_PlatformBoard` under `ConstructionVisual`, scale `(footprint_x, 0.1, footprint_z) × 0.5`, position at half-height.
4. Apply the project's existing wood material via `gameobject-component-modify` on each `MeshRenderer` (or use `assets-find-built-in` for a default brown).

The exact form is up to the designer — what matters is that the GameObject tree exists at `ConstructionVisual` for the swap to find.

- [ ] **Step 16.5: Wire the SerializeField references**

Use `gameobject-component-modify` on the `Building` component to set:
- `_constructionVisualRoot` → reference to the `ConstructionVisual` child.
- `_completedVisualRoot` → reference to the `CompletedVisual` child.

- [ ] **Step 16.6: Add the `ConstructionSiteScanner` component**

Use `gameobject-component-add` on the prefab root → `ConstructionSiteScanner`.

- [ ] **Step 16.7: Add the `BuildingInteractable` component**

Use `gameobject-component-add` on the prefab root → `BuildingInteractable`.

- [ ] **Step 16.8: Save the prefab**

Use Unity MCP `assets-prefab-save` and `assets-prefab-close`.

- [ ] **Step 16.9: PlayMode smoke test**

Manual verification:
1. Open the main scene that hosts a player. Use `editor-application-set-state` to enter Play mode.
2. Use the existing Build menu / dev tool to place this building.
3. Verify scaffolding visual renders, completed visual is hidden.
4. Drop required items (use dev tool to spawn items) onto the footprint.
5. Verify `ConstructionProgress` ticks up in the BuildingInspectorView.
6. Use the "Force Finish (DEV)" button to flip to Complete.
7. Verify scaffolding hides, completed visual appears, leftover items eject to perimeter.
8. Use `console-get-logs` to confirm no errors during the lifecycle.
9. Use `screenshot-game-view` to capture before/after for the implementation record.

- [ ] **Step 16.10: Commit**

```bash
git add Assets/Resources/.../<chosen_prefab>.prefab
git commit -m "asset(building): scaffolding + scanner + interactable on <prefab_name> reference prefab"
```

---

### Task 17: Roll the same authoring across all building prefabs

**Files:**
- All Building prefabs in `Assets/Resources/...`.

- [ ] **Step 17.1: Enumerate remaining prefabs**

`assets-find` with the filter for `t:Prefab` scoped to the building resources folder. Build a list of all prefabs that have a `Building`-derived component on the root and don't yet carry the new visual roots / scanner / interactable.

- [ ] **Step 17.2: For each remaining prefab, repeat Steps 16.2–16.8**

This is mechanical but must be done. Per prefab:
1. `assets-prefab-open`
2. Author/restructure visual roots
3. Wire SerializeField references
4. Add `ConstructionSiteScanner` + `BuildingInteractable`
5. `assets-prefab-save`
6. `assets-prefab-close`

- [ ] **Step 17.3: Final compile + console check**

`assets-refresh` + `console-get-logs`. Expected: zero errors / warnings.

- [ ] **Step 17.4: Commit**

```bash
git add Assets/Resources/
git commit -m "asset(building): scaffolding + scanner + interactable on all building prefabs"
```

---

## Phase H — Documentation

### Task 18: Update `.agent/skills/` SKILL.md files (Rule #28)

**Files:**
- Modify: `.agent/skills/building/SKILL.md` (or create if absent)
- Modify: `.agent/skills/character-actions/SKILL.md` (or create if absent)
- Create if absent: `.agent/skills/construction/SKILL.md`

- [ ] **Step 18.1: Update `.agent/skills/building/SKILL.md`**

Open the file. Add a new "Construction Loop" section documenting:
- The state machine (`UnderConstruction` → `Complete`).
- `_constructionVisualRoot` / `_completedVisualRoot` authoring requirement.
- `ConstructionSiteScanner` sibling component.
- `BuildingInteractable` interaction surface.
- The `Finalize` / `EvictLeftoversToPerimeter` server-side methods.
- The `ContributeMaterial` / `ComputeProgress` API surface.
- Persistence fields (`ConstructionProgress`, `DeliveredMaterials` DTO).

Keep it procedural ("how to author a new building prefab to support the construction loop"). Cross-reference the spec.

- [ ] **Step 18.2: Update `.agent/skills/character-actions/SKILL.md`**

Add a new section documenting `CharacterAction_Continuous`:
- When to inherit from it (condition-terminated, not timer-based).
- The `OnTick` contract (return true to finish).
- `TickIntervalSeconds`.
- Default `AllowsMovementDuringAction = false` and the cancel-on-movement implication.
- Concrete example: `CharacterAction_FinishConstruction`.

- [ ] **Step 18.3: Commit**

```bash
git add .agent/skills/
git commit -m "docs(skills): document construction loop + CharacterAction_Continuous"
```

---

### Task 19: Update `wiki/systems/` pages (Rule #29b)

**Files:**
- Modify: `wiki/systems/building.md` (existing — bump `updated:` + change-log entry)
- Modify: `wiki/systems/character-actions.md` (existing — bump `updated:` + change-log entry)
- Create if absent: `wiki/systems/construction.md`

- [ ] **Step 19.1: Read `wiki/CLAUDE.md` for schema rules**

Required by the project rules — fronts and naming etc.

- [ ] **Step 19.2: Update `wiki/systems/building.md`**

- Bump `updated:` frontmatter to `2026-05-06`.
- Add a `## Change log` line: `- 2026-05-06 — added construction loop (visual swap, scanner, Finalize, leftover eviction) — claude`.
- Refresh the Public API section to include `ComputeProgress`, `Finalize`, `EvictLeftoversToPerimeter`, `GetPhysicalItemsInCollider`, `ConstructionProgress`, `DeliveredMaterials`.
- Refresh `depends_on` / `depended_on_by` / `related` if cross-system relationships changed (BuildingInteractable, ConstructionSiteScanner are now depended_on_by Building's prefab authoring contract).
- Cross-reference the new `wiki/systems/construction.md` page.

- [ ] **Step 19.3: Update `wiki/systems/character-actions.md`**

- Bump `updated:`.
- Change-log line about `CharacterAction_Continuous` base.
- Public API: add `CharacterAction_Continuous` and `CharacterAction_FinishConstruction`.

- [ ] **Step 19.4: Create `wiki/systems/construction.md`**

Use the wiki's existing system-page template (under `wiki/_templates/`). Sections:
- Purpose
- Responsibilities
- Key classes / files
- Public API (Building.Finalize, Scanner, Interactable, FinishConstruction action)
- Data flow (placement → delivery → action → completion → eviction)
- Dependencies (Building, CharacterAction_Continuous, WorldItem, BuildingSaveData)
- State & persistence
- Gotchas (the 2 Hz scan vs trigger-driven tradeoff; theft dynamics; AssetGuid-only-in-editor save path)
- Open questions (Phase 2 scope)
- Change log

Cross-link to the design doc in `Sources`.

- [ ] **Step 19.5: Commit**

```bash
git add wiki/systems/
git commit -m "docs(wiki): construction system page + building/character-actions page updates"
```

---

### Task 20: Update specialist agents (Rule #29)

**Files:**
- Modify: `.claude/agents/building-furniture-specialist.md`
- Modify: `.claude/agents/character-system-specialist.md`

- [ ] **Step 20.1: Update `building-furniture-specialist.md`**

Append to the agent's domain description: knowledge of the construction loop — `_constructionVisualRoot` / `_completedVisualRoot` authoring, `ConstructionSiteScanner` ticks, `BuildingInteractable` action exposure, `Finalize` ordering (state-flip-first then side-effects), `EvictLeftoversToPerimeter`, persistence fields.

- [ ] **Step 20.2: Update `character-system-specialist.md`**

Append: `CharacterAction_Continuous` base class — when to inherit, the `OnTick` contract, the cancel-on-movement default. Note the new dispatcher branch in `CharacterActions.ExecuteAction`.

- [ ] **Step 20.3: Commit**

```bash
git add .claude/agents/
git commit -m "docs(agents): extend specialists for construction loop + continuous actions"
```

---

## Final verification

### Task 21: End-to-end multiplayer matrix

This is manual PlayMode verification. No code changes; just confirm everything in the spec § Testing § PlayMode—Multiplayer matrix passes.

- [ ] **Step 21.1: Host places, Client joins** — verify scaffolding, meter, finalize, post-build state on both peers.
- [ ] **Step 21.2: Client places, Host watches** — owner-only finalize blocks Host's Finish; Client's Finish succeeds.
- [ ] **Step 21.3: Client A places, Client B drops** — B's deliveries count; B blocked from Finish; A succeeds.
- [ ] **Step 21.4: Late join (Client C joins mid-build)** — visual + meter + delivered list correct on first frame.
- [ ] **Step 21.5: Save mid-build → reload** — state, meter, items in zone all restored. Re-engage and finish.
- [ ] **Step 21.6: Hibernate mid-build** — re-enter map, state resumes exactly as left.

For each scenario: capture `screenshot-game-view` and `console-get-logs` to attach to the PR.

- [ ] **Step 21.7: Profiler check (Rule #34)**

Spawn 10 simultaneous under-construction buildings on one map. Use Unity Profiler in Deep Profile + Allocation Tracking mode for ~30 seconds.
- Verify scanner cost stays under 0.1 ms/frame total.
- Verify `GC.Alloc` per scanner tick is 0.
- Verify NetworkVariable updates are 0 when no items move (idle traffic).

If any threshold is violated: file a follow-up entry in `wiki/projects/optimisation-backlog.md` with the measured cost; do NOT block this plan unless the regression is severe (>1ms/frame).

- [ ] **Step 21.8: Final commit**

```bash
git commit --allow-empty -m "test(construction): MP matrix + persistence + profiler verification complete"
```

---

## Self-Review Checklist (one-time, after writing this plan)

- [x] Spec coverage — every Section 1-5 item maps to at least one task.
- [x] No placeholders — every code block is complete; no "TBD" / "etc." / "implement appropriate".
- [x] Type consistency — `DeliveredMaterialEntry`, `DeliveredMaterialEntryDTO`, `BuildingInteractable.InteractionId`, `CharacterAction_Continuous.OnTick`, `Building.Finalize`, `Building.ComputeProgress`, `Building.GetPhysicalItemsInCollider`, `Building.EvictLeftoversToPerimeter`, `Building.ConstructionProgress`, `Building.DeliveredMaterials`, `SkillId.Builder`, `Character.GetSkillLevelOrZero` — names used identically across all tasks.
- [x] Dependency order — Phase A primitives precede Phase B; Phase D's interactable holds back its commit until the action class lands; prefab edits land last so script changes can be verified first.

---

## Out-of-scope (Phase 2 explicit)

The following are **not** in this plan and require separate specs:

- NPC owner GOAP autonomy (free-time goal "CompleteOwnedBuilding", harvestable perception query, shop search, money/tool gates).
- Community-manager city-management console furniture issuing builds.
- Dedicated `JobBuilder` job class.
- Multi-owner / co-owner support.
- Auto-eviction of orphaned construction sites whose owner deleted their profile.
- Real wiring of `CharacterAction_FinishConstruction.ConsumeFromActorInventory` (Phase 1 stub returns 0).
- Runtime AssetId for `ItemSO` to fix the standalone-build save snapshot path.
