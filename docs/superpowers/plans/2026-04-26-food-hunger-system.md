# Food & Hunger System — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** [docs/superpowers/specs/2026-04-26-food-hunger-system-design.md](../specs/2026-04-26-food-hunger-system-design.md)

**Goal:** Add a `NeedHunger` that decays once per `DayPhase` transition and a `FoodSO : ConsumableSO` subtype that restores it. Both player (carry → press **E**) and NPC (autonomous GOAP) use the same `CharacterUseConsumableAction` pipeline.

**Architecture:** Per-character `NeedHunger` POCO (mirrors `NeedSocial`) subscribed to `TimeManager.OnPhaseChanged`. Eating dispatches via a new `virtual ConsumableInstance.ApplyEffect(Character)` (Open/Closed). Persistence piggybacks the existing `NeedsSaveData`. `MacroSimulator` gets a hunger branch in `SimulateNPCCatchUp`. NPC food-finding scans `CommercialBuilding.GetItemsInStorageFurniture()` for `FoodInstance`.

**Tech Stack:** Unity 2D-in-3D, C#, NGO (Netcode for GameObjects), NUnit EditMode tests, GOAP backward-search planner.

> **MCP availability note:** Tasks 7 (prefab edit), 11 (FoodSO asset creation), and all "Run EditMode tests" / "Smoke-test in Play mode" steps assume Unity MCP is connected. If MCP is offline, the executing worker must pause and hand control back to the user to perform those steps in the Unity Editor manually.

---

## Pre-flight

- [ ] **P1: Read the spec end-to-end** before touching any code.
  Read: [docs/superpowers/specs/2026-04-26-food-hunger-system-design.md](../specs/2026-04-26-food-hunger-system-design.md).
- [ ] **P2: Confirm clean working tree.**
  Run: `git status`
  Expected: only the `Assets/Scripts/Character/CharacterAwareness.cs` modification noted in the brief. No staged changes.
- [ ] **P3: Verify the EditMode test runner works on the existing test suite.**
  In Unity Editor → Window → General → Test Runner → EditMode → Run All. Expected: `WageCalculatorTests` and `HarvesterCreditCalculatorTests` pass. If they fail, stop and investigate before adding new tests on top of broken infra.

---

## Task 1: `FoodCategory` enum + `ConsumableInstance.ApplyEffect` virtual

**Files:**
- Create: `Assets/Resources/Data/Item/FoodCategory.cs`
- Modify: `Assets/Scripts/Item/ConsumableInstance.cs`

- [ ] **Step 1.1: Create the FoodCategory enum.**

```csharp
// Assets/Resources/Data/Item/FoodCategory.cs
public enum FoodCategory
{
    Raw,
    Cooked,
    Preserved
}
```

- [ ] **Step 1.2: Add the `ApplyEffect` virtual to `ConsumableInstance`.**

Open `Assets/Scripts/Item/ConsumableInstance.cs`. After the `AddEffect` method (currently the last method), add:

```csharp
/// <summary>
/// Applies this consumable's runtime effect to the given character.
/// Default no-op; subclasses (FoodInstance, PotionInstance, …) override.
/// Called from <see cref="Character.UseConsumable"/> after the use animation completes.
/// </summary>
public virtual void ApplyEffect(Character character)
{
    // No-op default. Specific consumable subtypes override.
}
```

- [ ] **Step 1.3: Compile.**
  Switch to Unity. Wait for recompile. Console expected: zero errors. If errors appear, stop and fix.

- [ ] **Step 1.4: Commit.**

```bash
git add "Assets/Resources/Data/Item/FoodCategory.cs" "Assets/Resources/Data/Item/FoodCategory.cs.meta" "Assets/Scripts/Item/ConsumableInstance.cs"
git commit -m "feat(items): add FoodCategory enum + ConsumableInstance.ApplyEffect virtual"
```

---

## Task 2: `FoodSO` and `FoodInstance`

**Files:**
- Create: `Assets/Resources/Data/Item/FoodSO.cs`
- Create: `Assets/Scripts/Item/FoodInstance.cs`

- [ ] **Step 2.1: Create `FoodSO`.**

```csharp
// Assets/Resources/Data/Item/FoodSO.cs
using UnityEngine;

[CreateAssetMenu(fileName = "FoodSO", menuName = "Scriptable Objects/Items/Food")]
public class FoodSO : ConsumableSO
{
    [Header("Food Settings")]
    [Tooltip("How many points of NeedHunger this food restores when consumed.")]
    [SerializeField] private float _hungerRestored = 30f;

    [Tooltip("Category of food. Used by future cooking/quality systems; ignored in v1.")]
    [SerializeField] private FoodCategory _foodCategory = FoodCategory.Raw;

    public float HungerRestored => _hungerRestored;
    public FoodCategory FoodCategory => _foodCategory;

    public override System.Type InstanceType => typeof(FoodInstance);
    public override ItemInstance CreateInstance() => new FoodInstance(this);
}
```

- [ ] **Step 2.2: Create `FoodInstance`.**

```csharp
// Assets/Scripts/Item/FoodInstance.cs
using UnityEngine;

[System.Serializable]
public class FoodInstance : ConsumableInstance
{
    public FoodSO FoodData => _itemSO as FoodSO;

    public FoodInstance(FoodSO data) : base(data)
    {
    }

    public override void ApplyEffect(Character character)
    {
        if (character == null)
        {
            Debug.LogWarning("<color=orange>[FoodInstance]</color> ApplyEffect called with null character.");
            return;
        }

        if (FoodData == null)
        {
            Debug.LogError($"<color=red>[FoodInstance]</color> {CustomizedName} has no FoodSO. Skipping effect.");
            return;
        }

        if (character.CharacterNeeds == null)
        {
            Debug.LogWarning($"<color=orange>[FoodInstance]</color> {character.CharacterName} has no CharacterNeeds component.");
            return;
        }

        var hunger = character.CharacterNeeds.GetNeed<NeedHunger>();
        if (hunger == null)
        {
            Debug.LogWarning($"<color=orange>[FoodInstance]</color> {character.CharacterName} has no NeedHunger. Was it registered in CharacterNeeds.Start?");
            return;
        }

        hunger.IncreaseValue(FoodData.HungerRestored);
        Debug.Log($"<color=green>[FoodInstance]</color> {character.CharacterName} ate {CustomizedName} → +{FoodData.HungerRestored} hunger.");
    }
}
```

> Note: `NeedHunger` and `CharacterNeeds.GetNeed<T>` don't exist yet — Unity will fail to compile until Tasks 3 and 4 land. That's intentional; we'll fix compile in Task 3.

- [ ] **Step 2.3: Do NOT compile yet** (will fail). Move directly to Task 3 to land `NeedHunger` and `GetNeed<T>` so the compiler catches up.

- [ ] **Step 2.4: Commit (still uncompiled — we commit after Task 3).** Skip commit until end of Task 3.

---

## Task 3: `NeedHunger` math (TDD) — pure decay + restore + IsStarving

**Files:**
- Create: `Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs`
- Create: `Assets/Tests/EditMode/NeedHungerMathTests.cs`
- Modify (or create): `Assets/Tests/EditMode/WagesAndPerformance.Tests.asmdef` references — actually we'll create a separate test asmdef to avoid coupling.
- Create: `Assets/Tests/EditMode/Hunger.Tests.asmdef`

> **TDD note:** `NeedHunger` has two halves: (a) pure math for value/threshold/IsStarving transitions — testable in EditMode, (b) Unity wiring (subscribe to `TimeManager.OnPhaseChanged`, GOAP integration) — covered in Task 6 + Play-mode validation.

- [ ] **Step 3.1: Create the EditMode test asmdef.**

```json
// Assets/Tests/EditMode/Hunger.Tests.asmdef
{
    "name": "Hunger.Tests",
    "rootNamespace": "MWI.Tests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "GUID:<runtime-assembly-guid>"
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

> **Concrete instruction for the GUID line above:** open `Assets/Tests/EditMode/WagesAndPerformance.Tests.asmdef` in the Inspector (single-click in the Project window). The "Assembly Definition References" section shows what runtime assemblies it depends on. The runtime code for `NeedHunger` lives in the default `Assembly-CSharp` (no `.asmdef` wraps `Assets/Scripts/Character/CharacterNeeds/`). When the default assembly is the target, the test asmdef typically references it via `"references": ["UnityEngine.TestRunner", "UnityEditor.TestRunner", "Assembly-CSharp"]` — but `Assembly-CSharp` cannot be referenced by GUID.
>
> **Therefore:** if `NeedHunger` lands in the default `Assembly-CSharp` (which is what `Assets/Scripts/...` defaults to with no `.asmdef`), the test asmdef must NOT set `overrideReferences: true` and instead leave it false so it auto-references `Assembly-CSharp`. Replace the asmdef body with:

```json
{
    "name": "Hunger.Tests",
    "rootNamespace": "MWI.Tests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
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

- [ ] **Step 3.2: Write the failing test file.**

```csharp
// Assets/Tests/EditMode/NeedHungerMathTests.cs
using NUnit.Framework;

namespace MWI.Tests
{
    /// <summary>
    /// Tests pure value/transition logic of NeedHunger — no Unity scene needed.
    /// We construct a NeedHunger with a null Character (its math doesn't dereference it).
    /// </summary>
    public class NeedHungerMathTests
    {
        [Test]
        public void StartValueClampedToMax()
        {
            var hunger = new NeedHunger(null, startValue: 999f);
            Assert.AreEqual(100f, hunger.CurrentValue);
        }

        [Test]
        public void StartValueClampedToZero()
        {
            var hunger = new NeedHunger(null, startValue: -10f);
            Assert.AreEqual(0f, hunger.CurrentValue);
        }

        [Test]
        public void DecreaseValue_ReducesByAmount()
        {
            var hunger = new NeedHunger(null, startValue: 80f);
            hunger.DecreaseValue(25f);
            Assert.AreEqual(55f, hunger.CurrentValue);
        }

        [Test]
        public void DecreaseValue_ClampsAtZero()
        {
            var hunger = new NeedHunger(null, startValue: 10f);
            hunger.DecreaseValue(50f);
            Assert.AreEqual(0f, hunger.CurrentValue);
        }

        [Test]
        public void IncreaseValue_ClampsAtMax()
        {
            var hunger = new NeedHunger(null, startValue: 90f);
            hunger.IncreaseValue(50f);
            Assert.AreEqual(100f, hunger.CurrentValue);
        }

        [Test]
        public void IsStarving_FalseWhenAboveZero()
        {
            var hunger = new NeedHunger(null, startValue: 5f);
            Assert.IsFalse(hunger.IsStarving);
        }

        [Test]
        public void IsStarving_TrueWhenAtZero()
        {
            var hunger = new NeedHunger(null, startValue: 0f);
            Assert.IsTrue(hunger.IsStarving);
        }

        [Test]
        public void OnStarvingChanged_FiresOnceWhenHittingZero()
        {
            var hunger = new NeedHunger(null, startValue: 10f);
            int trueCount = 0, falseCount = 0;
            hunger.OnStarvingChanged += isStarving =>
            {
                if (isStarving) trueCount++; else falseCount++;
            };

            hunger.DecreaseValue(5f); // 5 — not starving yet
            Assert.AreEqual(0, trueCount);

            hunger.DecreaseValue(5f); // 0 — starving now, event must fire once
            Assert.AreEqual(1, trueCount);

            hunger.DecreaseValue(5f); // still 0 — must NOT fire again
            Assert.AreEqual(1, trueCount);

            hunger.IncreaseValue(20f); // back to 20 — must fire false once
            Assert.AreEqual(1, falseCount);

            hunger.IncreaseValue(10f); // still above 0 — must NOT fire again
            Assert.AreEqual(1, falseCount);
        }

        [Test]
        public void OnValueChanged_FiresWithNewValue()
        {
            var hunger = new NeedHunger(null, startValue: 50f);
            float lastObserved = -1f;
            hunger.OnValueChanged += v => lastObserved = v;

            hunger.IncreaseValue(10f);
            Assert.AreEqual(60f, lastObserved);

            hunger.DecreaseValue(20f);
            Assert.AreEqual(40f, lastObserved);
        }

        [Test]
        public void IsLow_TrueAtOrBelowThreshold()
        {
            var hunger = new NeedHunger(null, startValue: 30f);
            Assert.IsTrue(hunger.IsLow());

            hunger.IncreaseValue(1f);
            Assert.IsFalse(hunger.IsLow());
        }
    }
}
```

- [ ] **Step 3.3: Run tests — expect failure.**
  Use the Unity MCP `tests-run` tool with `mode: EditMode` filter `Hunger.Tests`. Or in Editor: Test Runner → EditMode → Run All.
  Expected: compile error (`NeedHunger` does not exist). That's a valid TDD red — proceed to make it green.

- [ ] **Step 3.4: Implement `NeedHunger`.**

```csharp
// Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public class NeedHunger : CharacterNeed
{
    [Header("Hunger Settings")]
    private const float DEFAULT_MAX = 100f;
    private const float DEFAULT_START = 80f;
    private const float DEFAULT_LOW_THRESHOLD = 30f;
    private const float DEFAULT_DECAY_PER_PHASE = 25f;
    private const float DEFAULT_SEARCH_COOLDOWN = 15f;

    private float _currentValue;
    private float _maxValue = DEFAULT_MAX;
    private float _lowThreshold = DEFAULT_LOW_THRESHOLD;
    private float _decayPerPhase = DEFAULT_DECAY_PER_PHASE;
    private float _searchCooldown = DEFAULT_SEARCH_COOLDOWN;
    private float _lastSearchTime = -999f;
    private bool _isStarving;
    private bool _phaseSubscribed;

    /// <summary>Fires whenever CurrentValue changes (passes the new value). HUD subscribes.</summary>
    public event Action<float> OnValueChanged;

    /// <summary>Fires only on transitions of IsStarving (true when value first hits 0; false when it rises above 0).</summary>
    public event Action<bool> OnStarvingChanged;

    public float MaxValue => _maxValue;
    public bool IsStarving => _isStarving;

    public NeedHunger(Character character, float startValue = DEFAULT_START) : base(character)
    {
        _currentValue = Mathf.Clamp(startValue, 0f, _maxValue);
        _isStarving = _currentValue <= 0f;
        TrySubscribeToPhase();
    }

    public override float CurrentValue
    {
        get => _currentValue;
        set
        {
            float clamped = Mathf.Clamp(value, 0f, _maxValue);
            if (Mathf.Approximately(clamped, _currentValue)) return;
            _currentValue = clamped;
            OnValueChanged?.Invoke(_currentValue);
            UpdateStarvingFlag();
        }
    }

    public void IncreaseValue(float amount) => CurrentValue = _currentValue + amount;
    public void DecreaseValue(float amount) => CurrentValue = _currentValue - amount;

    public bool IsLow() => _currentValue <= _lowThreshold;

    public void SetCooldown() => _lastSearchTime = UnityEngine.Time.time;

    private void UpdateStarvingFlag()
    {
        bool nowStarving = _currentValue <= 0f;
        if (nowStarving == _isStarving) return;
        _isStarving = nowStarving;
        OnStarvingChanged?.Invoke(_isStarving);
    }

    /// <summary>
    /// Subscribes to TimeManager.OnPhaseChanged. Called by the constructor; also re-callable by
    /// CharacterNeeds.Start if TimeManager wasn't ready at character spawn.
    /// </summary>
    public void TrySubscribeToPhase()
    {
        if (_phaseSubscribed) return;
        if (MWI.Time.TimeManager.Instance == null) return;
        MWI.Time.TimeManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        _phaseSubscribed = true;
    }

    public void UnsubscribeFromPhase()
    {
        if (!_phaseSubscribed) return;
        if (MWI.Time.TimeManager.Instance != null)
            MWI.Time.TimeManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        _phaseSubscribed = false;
    }

    private void HandlePhaseChanged(MWI.Time.DayPhase _)
    {
        try
        {
            DecreaseValue(_decayPerPhase);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // ─────────────────────────────── GOAP / IsActive ───────────────────────────────

    public override bool IsActive()
    {
        if (_character == null) return false;
        if (_character.Controller is PlayerController) return false;
        if (UnityEngine.Time.time - _lastSearchTime < _searchCooldown) return false;
        return IsLow();
    }

    public override float GetUrgency() => _maxValue - _currentValue;

    public override GoapGoal GetGoapGoal()
    {
        return new GoapGoal("Eat", new Dictionary<string, bool> { { "isHungry", false } }, (int)GetUrgency());
    }

    /// <summary>
    /// Returns the GOAP action chain to satisfy hunger. Implemented in Task 11.
    /// For Task 3, return an empty list so the pure-math tests don't fail to compile.
    /// </summary>
    public override List<GoapAction> GetGoapActions()
    {
        // Filled in Task 11 once GoapAction_GoToFood + GoapAction_Eat exist.
        return new List<GoapAction>();
    }
}
```

- [ ] **Step 3.5: Add `GetNeed<T>()` to `CharacterNeeds`.**

Open `Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs`. After the `AllNeeds` property add:

```csharp
/// <summary>
/// Typed accessor for needs. Returns null if no need of type T is registered.
/// </summary>
public T GetNeed<T>() where T : CharacterNeed
{
    foreach (var need in _allNeeds)
    {
        if (need is T typed) return typed;
    }
    return null;
}
```

- [ ] **Step 3.6: Register `NeedHunger` in `CharacterNeeds.Start()`.**

In the same file, in the existing `Start()` method, after the line `_allNeeds.Add(new NeedJob(_character));` add:

```csharp
        var hunger = new NeedHunger(_character);
        // Re-attempt subscription in case TimeManager wasn't ready in NeedHunger ctor.
        hunger.TrySubscribeToPhase();
        _allNeeds.Add(hunger);
```

Also in `OnDestroy()`, before unsubscribing from `OnNewDay`, add:

```csharp
        var hunger = GetNeed<NeedHunger>();
        if (hunger != null) hunger.UnsubscribeFromPhase();
```

- [ ] **Step 3.7: Compile.**
  Switch to Unity. Console expected: zero errors. Tasks 1–3 collectively now compile.

- [ ] **Step 3.8: Run EditMode tests.**
  Test Runner → EditMode → Hunger.Tests → Run All.
  Expected: all 10 tests pass.

- [ ] **Step 3.9: Commit Tasks 2 + 3.**

(Task 1 was already committed at Step 1.4. This commit covers FoodSO + FoodInstance from Task 2 plus everything in Task 3.)

```bash
git add "Assets/Resources/Data/Item/FoodSO.cs" "Assets/Resources/Data/Item/FoodSO.cs.meta" "Assets/Scripts/Item/FoodInstance.cs" "Assets/Scripts/Item/FoodInstance.cs.meta" "Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs" "Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs.meta" "Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs" "Assets/Tests/EditMode/Hunger.Tests.asmdef" "Assets/Tests/EditMode/Hunger.Tests.asmdef.meta" "Assets/Tests/EditMode/NeedHungerMathTests.cs" "Assets/Tests/EditMode/NeedHungerMathTests.cs.meta"
git commit -m "feat(needs): NeedHunger + FoodSO/FoodInstance"
```

---

## Task 4: `Character.UseConsumable` implementation

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs:970-973`
- Modify: `Assets/Scripts/Character/CharacterBodyPartsController/HandsController.cs` (add `ClearCarriedItem()` if not present)

- [ ] **Step 4.1: Verify `HandsController.ClearCarriedItem` (or equivalent) exists.**
  Open `Assets/Scripts/Character/CharacterBodyPartsController/HandsController.cs`. Search for `_carriedItem = null` or `ClearCarried`.
  - If a method already clears `_carriedItem` and `_carriedVisual`, note its name and use it in Step 4.3.
  - If no such method exists, add one:

```csharp
/// <summary>
/// Clears the currently carried item. Used by consume-from-hand flow (food, potion).
/// Does NOT spawn a WorldItem (unlike <see cref="CharacterDropItem"/>) — the item is destroyed.
/// </summary>
public void ClearCarriedItem()
{
    _carriedItem = null;
    if (_carriedVisual != null)
    {
        Destroy(_carriedVisual);
        _carriedVisual = null;
    }
}
```

Place it after the `IsCarrying` property.

- [ ] **Step 4.2: Implement `Character.UseConsumable`.**

Open `Assets/Scripts/Character/Character.cs`. Replace the stub:

```csharp
    public void UseConsumable(ConsumableInstance consumable)
    {
        // TODO: Implémenter
    }
```

with:

```csharp
    public void UseConsumable(ConsumableInstance consumable)
    {
        if (consumable == null)
        {
            Debug.LogWarning($"<color=orange>[Character]</color> {CharacterName} UseConsumable called with null instance.");
            return;
        }

        try
        {
            consumable.ApplyEffect(this);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            return;
        }

        var so = consumable.ConsumableData;
        if (so == null || !so.DestroyOnUse) return;

        // Remove the item from wherever it lives (hands and/or inventory).
        var hands = CharacterVisual?.BodyPartsController?.HandsController;
        if (hands != null && hands.IsCarrying && hands.CarriedItem == consumable)
        {
            hands.ClearCarriedItem();
        }

        if (CharacterInventory != null)
        {
            CharacterInventory.RemoveItem(consumable);
        }
    }
```

> Confirm `CharacterInventory` exposes a `RemoveItem(ItemInstance)` method. If the actual name differs (e.g., `Remove`, `RemoveInstance`), use that.

- [ ] **Step 4.3: Compile.** Console expected: zero errors.

- [ ] **Step 4.4: Commit.**

```bash
git add "Assets/Scripts/Character/Character.cs" "Assets/Scripts/Character/CharacterBodyPartsController/HandsController.cs"
git commit -m "feat(consumables): wire Character.UseConsumable through ApplyEffect virtual"
```

---

## Task 5: Player E-key consume input

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`

- [ ] **Step 5.1: Add the E-key handler inside the existing `IsOwner && !devMode` block.**

In `PlayerController.Update()`, find the existing block for `KeyCode.G` (around line 109). Add immediately after it:

```csharp
                // --- E: Consume the item currently carried in hands if it's a ConsumableInstance. ---
                // Routed through CharacterUseConsumableAction (rule #22 player-NPC parity).
                if (Input.GetKeyDown(KeyCode.E))
                {
                    HandleConsumeCarriedItem();
                }
```

- [ ] **Step 5.2: Add the handler method below `HandleDropCarriedItem`.**

```csharp
    /// <summary>
    /// Consumes the item currently carried in hands if it's a ConsumableInstance.
    /// No-op if hands are empty, item is not consumable, or another action is already running.
    /// </summary>
    private void HandleConsumeCarriedItem()
    {
        var hands = _character?.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.IsCarrying) return;

        if (hands.CarriedItem is not ConsumableInstance consumable)
        {
            Debug.Log($"<color=yellow>[PlayerCtrl]</color> Carried item is not a consumable — E ignored.");
            return;
        }

        if (_character.CharacterActions == null) return;
        if (_character.CharacterActions.CurrentAction != null) return;

        _character.CharacterActions.ExecuteAction(new CharacterUseConsumableAction(_character, consumable));
    }
```

- [ ] **Step 5.3: Compile.** Console expected: zero errors.

- [ ] **Step 5.4: Smoke-test in Play mode (manual).**
  Enter Play mode → spawn the player on the test map → use Dev-Mode (Ctrl+Click or `/devmode` chat) to spawn a `FoodSO`-backed item in the player's hands (you'll need a `FoodSO` asset created via right-click → Create → Scriptable Objects → Items → Food first; create a `Bread.asset` with `_hungerRestored = 30`). Press **E**. Expected: animation plays, `+30 hunger` console log, item disappears from hands.
  - If no `FoodSO` asset exists yet, create one in `Assets/Resources/Data/Item/Food/Bread.asset` for testing purposes.

- [ ] **Step 5.5: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterControllers/PlayerController.cs"
git commit -m "feat(input): KeyCode.E consume-carried-item handler in PlayerController (rule #33)"
```

---

## Task 6: `UI_HungerBar` script

**Files:**
- Create: `Assets/UI/Player HUD/UI_HungerBar.cs`

- [ ] **Step 6.1: Create the script.**

```csharp
// Assets/UI/Player HUD/UI_HungerBar.cs
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hunger HUD bar. Subscribes to NeedHunger.OnValueChanged and pushes fill / starving-flash
/// values into an instanced material. Mirrors UI_HealthBar's shader-based pattern, but
/// targets a NeedHunger (POCO) instead of CharacterPrimaryStats. Uses unscaled time per rule #26.
/// </summary>
public class UI_HungerBar : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image _barImage;
    [SerializeField] private TextMeshProUGUI _valueText;

    [Header("Animation")]
    [SerializeField] private float _fillAnimationDuration = 0.25f;

    [Header("Starving Flash")]
    [SerializeField] private float _starvingFlashPeriod = 0.5f;

    [Header("Value Text")]
    [SerializeField] private Color _normalTextColor = Color.white;
    [SerializeField] private Color _lowTextColor = Color.red;
    [SerializeField] private float _lowTextThreshold = 0.3f;

    private NeedHunger _target;
    private Material _instancedMaterial;
    private Coroutine _fillCoroutine;
    private Coroutine _starveCoroutine;
    private float _currentFill;

    private static readonly int ID_FillAmount = Shader.PropertyToID("_FillAmount");
    private static readonly int ID_HealFlash = Shader.PropertyToID("_HealFlash");

    public void Initialize(NeedHunger hunger)
    {
        Cleanup();
        _target = hunger;

        if (_target == null) return;

        if (_barImage != null && _instancedMaterial == null)
        {
            _instancedMaterial = new Material(_barImage.material);
            _barImage.material = _instancedMaterial;
        }

        _target.OnValueChanged += HandleValueChanged;
        _target.OnStarvingChanged += HandleStarvingChanged;

        _currentFill = GetFillRatio();
        SnapToShader();
        UpdateValueText();

        if (_target.IsStarving) StartStarveFlash();
    }

    private void OnDestroy()
    {
        Cleanup();
        if (_instancedMaterial != null) Destroy(_instancedMaterial);
    }

    private void Cleanup()
    {
        if (_target == null) return;
        _target.OnValueChanged -= HandleValueChanged;
        _target.OnStarvingChanged -= HandleStarvingChanged;
        _target = null;

        if (_starveCoroutine != null)
        {
            StopCoroutine(_starveCoroutine);
            _starveCoroutine = null;
        }
        if (_fillCoroutine != null)
        {
            StopCoroutine(_fillCoroutine);
            _fillCoroutine = null;
        }
    }

    private void HandleValueChanged(float _newValue)
    {
        if (_fillCoroutine != null) StopCoroutine(_fillCoroutine);
        _fillCoroutine = StartCoroutine(AnimateFill());
        UpdateValueText();
    }

    private void HandleStarvingChanged(bool isStarving)
    {
        if (isStarving) StartStarveFlash();
        else StopStarveFlash();
    }

    private void StartStarveFlash()
    {
        if (_starveCoroutine != null) return;
        _starveCoroutine = StartCoroutine(StarveFlashRoutine());
    }

    private void StopStarveFlash()
    {
        if (_starveCoroutine != null) StopCoroutine(_starveCoroutine);
        _starveCoroutine = null;
        if (_instancedMaterial != null) _instancedMaterial.SetFloat(ID_HealFlash, 0f);
    }

    private IEnumerator AnimateFill()
    {
        float startFill = _currentFill;
        float targetFill = GetFillRatio();
        float elapsed = 0f;
        while (elapsed < _fillAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _fillAnimationDuration);
            _currentFill = Mathf.Lerp(startFill, targetFill, Mathf.SmoothStep(0f, 1f, t));
            if (_instancedMaterial != null) _instancedMaterial.SetFloat(ID_FillAmount, _currentFill);
            yield return null;
        }
        _currentFill = targetFill;
        if (_instancedMaterial != null) _instancedMaterial.SetFloat(ID_FillAmount, _currentFill);
        _fillCoroutine = null;
    }

    private IEnumerator StarveFlashRoutine()
    {
        while (true)
        {
            float elapsed = 0f;
            while (elapsed < _starvingFlashPeriod)
            {
                elapsed += Time.unscaledDeltaTime;
                float flash = Mathf.Sin((elapsed / _starvingFlashPeriod) * Mathf.PI);
                if (_instancedMaterial != null) _instancedMaterial.SetFloat(ID_HealFlash, flash);
                yield return null;
            }
        }
    }

    private float GetFillRatio()
    {
        if (_target == null || _target.MaxValue <= 0f) return 0f;
        return _target.CurrentValue / _target.MaxValue;
    }

    private void SnapToShader()
    {
        if (_instancedMaterial == null) return;
        _instancedMaterial.SetFloat(ID_FillAmount, _currentFill);
    }

    private void UpdateValueText()
    {
        if (_valueText == null || _target == null) return;
        int current = Mathf.RoundToInt(_target.CurrentValue);
        int max = Mathf.RoundToInt(_target.MaxValue);
        _valueText.text = $"{current} / {max}";
        _valueText.color = GetFillRatio() <= _lowTextThreshold ? _lowTextColor : _normalTextColor;
    }
}
```

- [ ] **Step 6.2: Wire `UI_PlayerInfo` to initialize the hunger bar.**

Open `Assets/Scripts/UI/UI_PlayerInfo.cs`. After the `[SerializeField] private UI_HealthBar _staminaBar;` line, add:

```csharp
    [SerializeField] private UI_HungerBar _hungerBar;
```

In `Initialize(...)`, after the stamina-bar block, add:

```csharp
        if (_hungerBar != null && characterComponent.CharacterNeeds != null)
        {
            var hunger = characterComponent.CharacterNeeds.GetNeed<NeedHunger>();
            if (hunger != null) _hungerBar.Initialize(hunger);
        }
```

- [ ] **Step 6.3: Compile.** Console expected: zero errors.

- [ ] **Step 6.4: Commit.**

```bash
git add "Assets/UI/Player HUD/UI_HungerBar.cs" "Assets/UI/Player HUD/UI_HungerBar.cs.meta" "Assets/Scripts/UI/UI_PlayerInfo.cs"
git commit -m "feat(hud): UI_HungerBar widget bound to NeedHunger.OnValueChanged"
```

---

## Task 7: Wire the hunger bar into the prefab via MCP Unity

**Files:**
- Modify: `Assets/UI/Player HUD/UI_PlayerInfo.prefab`

> This task uses the Unity MCP tools (`assets-prefab-open`, `gameobject-duplicate`, `gameobject-component-add`, etc.). It does NOT modify text source files.

- [ ] **Step 7.1: Open the prefab in edit mode via MCP.**
  Use: `mcp__ai-game-developer__assets-prefab-open` with the prefab GUID for `Assets/UI/Player HUD/UI_PlayerInfo.prefab` (find via `assets-find` filter `t:Prefab UI_PlayerInfo`).

- [ ] **Step 7.2: Duplicate the existing stamina-bar child as the hunger bar template.**
  - Find the `_staminaBar` GameObject inside the prefab via `gameobject-find` (search by name, e.g. `"StaminaBar"` or whatever it's named in the hierarchy).
  - Use `gameobject-duplicate` to clone it. Rename the duplicate to `HungerBar`.
  - Position it just below the stamina bar (adjust the RectTransform anchored Y by −20 to −30 px depending on existing spacing).

- [ ] **Step 7.3: Replace the duplicated `UI_HealthBar` script with `UI_HungerBar`.**
  - Use `gameobject-component-destroy` to remove the cloned `UI_HealthBar` component from the new `HungerBar` GameObject.
  - Use `gameobject-component-add` to add `UI_HungerBar` to it.
  - Use `gameobject-component-modify` to wire the `_barImage` and `_valueText` SerializeFields (point them at the same child Image and TMP_Text the cloned bar uses).

- [ ] **Step 7.4: Wire `UI_PlayerInfo._hungerBar` on the prefab root.**
  - Use `gameobject-component-modify` on the prefab's `UI_PlayerInfo` script to set `_hungerBar` to the new `HungerBar` GameObject's `UI_HungerBar` component.

- [ ] **Step 7.5: Save and close the prefab.**
  - `mcp__ai-game-developer__assets-prefab-save`
  - `mcp__ai-game-developer__assets-prefab-close`

- [ ] **Step 7.6: Smoke-test in Play mode.**
  Enter Play mode. Verify the hunger bar is visible in the HUD and shows `80 / 100` at spawn (the `NeedHunger` default startValue).

- [ ] **Step 7.7: Commit.**

```bash
git add "Assets/UI/Player HUD/UI_PlayerInfo.prefab"
git commit -m "feat(hud): wire UI_HungerBar into UI_PlayerInfo prefab"
```

---

## Task 8: `MacroSimulator` hunger catch-up (TDD where pure, integration otherwise)

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MacroSimulator.cs:272-283`
- Add a static helper method in MacroSimulator for testability: `ApplyHungerCatchUp(float currentValue, float decayPerHour, float hoursPassed)`.
- Create: `Assets/Tests/EditMode/MacroSimulatorHungerTests.cs`

- [ ] **Step 8.1: Write the failing test.**

```csharp
// Assets/Tests/EditMode/MacroSimulatorHungerTests.cs
using NUnit.Framework;
using MWI.Time;

namespace MWI.Tests
{
    public class MacroSimulatorHungerTests
    {
        // 25 per phase * 4 phases per day = 100 / 24 hours
        private const float DECAY_PER_HOUR = 100f / 24f;

        [Test]
        public void NoTimePassed_ReturnsCurrent()
        {
            float result = MacroSimulator.ApplyHungerCatchUp(80f, DECAY_PER_HOUR, 0f);
            Assert.AreEqual(80f, result, 0.01f);
        }

        [Test]
        public void OneFullDay_DecaysBy100ClampedToZero()
        {
            float result = MacroSimulator.ApplyHungerCatchUp(80f, DECAY_PER_HOUR, 24f);
            Assert.AreEqual(0f, result, 0.01f);
        }

        [Test]
        public void HalfDay_DecaysBy50()
        {
            float result = MacroSimulator.ApplyHungerCatchUp(80f, DECAY_PER_HOUR, 12f);
            Assert.AreEqual(30f, result, 0.01f);
        }

        [Test]
        public void NegativeOrZeroResult_ClampsToZero()
        {
            float result = MacroSimulator.ApplyHungerCatchUp(10f, DECAY_PER_HOUR, 24f);
            Assert.AreEqual(0f, result, 0.01f);
        }
    }
}
```

- [ ] **Step 8.2: Run tests — expect failure** (`MacroSimulator.ApplyHungerCatchUp` does not exist).

- [ ] **Step 8.3: Add the static helper to `MacroSimulator`.**

In `Assets/Scripts/World/MapSystem/MacroSimulator.cs`, inside the class, add (after `TickZoneMotion`):

```csharp
        /// <summary>
        /// Pure math: returns the new hunger value after `hoursPassed` hours of decay,
        /// clamped to [0, ∞). Public + static so EditMode tests can exercise it.
        /// </summary>
        public static float ApplyHungerCatchUp(float currentValue, float decayPerHour, float hoursPassed)
        {
            if (hoursPassed <= 0f) return currentValue;
            float result = currentValue - decayPerHour * hoursPassed;
            return result < 0f ? 0f : result;
        }
```

- [ ] **Step 8.4: Wire it into `SimulateNPCCatchUp`.**

In the same file, in the `SimulateNPCCatchUp` method (currently has a `NeedSocial` branch around line 277), add a new `else if`:

```csharp
                else if (need.NeedType == "NeedHunger")
                {
                    // 100 hunger per day = 100/24 per hour. Match NeedHunger's _decayPerPhase=25 default.
                    const float drainRatePerHour = 100f / 24f;
                    need.Value = ApplyHungerCatchUp(need.Value, drainRatePerHour, hoursPassed);
                }
```

- [ ] **Step 8.5: Run EditMode tests.** Test Runner → EditMode → MacroSimulatorHungerTests. Expected: 4 tests pass.

- [ ] **Step 8.6: Compile + commit.**

```bash
git add "Assets/Scripts/World/MapSystem/MacroSimulator.cs" "Assets/Tests/EditMode/MacroSimulatorHungerTests.cs" "Assets/Tests/EditMode/MacroSimulatorHungerTests.cs.meta"
git commit -m "feat(macrosim): hunger catch-up — linear decay during hibernation"
```

---

## Task 9: NPC GOAP foundation — `GoapAction_GoToFood`

**Files:**
- Create: `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToFood.cs`

> Pattern reference: `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToSourceStorage.cs` and `GoapAction_GoToBoss.cs`. Read both before writing this file.

- [ ] **Step 9.1: Read the two reference files** (open in editor, scan for ~3 minutes). Note the constructor signature, OnEnter/OnTick/IsValidTarget pattern, and how they integrate with `MoveToTarget`. The new action mirrors them.

- [ ] **Step 9.2: Create the action.**

```csharp
// Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToFood.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Walks the NPC into the interaction zone of a StorageFurniture that contains a FoodInstance.
/// Successor: <see cref="GoapAction_Eat"/>.
/// </summary>
public class GoapAction_GoToFood : GoapAction_MoveToTarget
{
    private readonly StorageFurniture _foodSource;

    public GoapAction_GoToFood(StorageFurniture foodSource) : base()
    {
        _foodSource = foodSource;
        cost = 1f;

        // Effects: marks the NPC as "atFood" so GoapAction_Eat can chain.
        effects.Add("atFood", true);
    }

    public override bool CheckPreconditions(GoapAgent agent) => _foodSource != null;

    protected override Vector3 GetDestination()
    {
        if (_foodSource == null) return Vector3.zero;
        return _foodSource.transform.position;
    }

    public override bool IsCompleted(GoapAgent agent)
    {
        if (_foodSource == null) return false;
        var character = agent?.Character;
        if (character == null) return false;

        // Use the established proximity API.
        var interactable = _foodSource.GetComponent<InteractableObject>();
        if (interactable == null)
        {
            Debug.LogWarning($"<color=orange>[GoapAction_GoToFood]</color> {_foodSource.name} has no InteractableObject — falling back to base distance check.");
            return base.IsCompleted(agent);
        }

        return interactable.IsCharacterInInteractionZone(character);
    }
}
```

> Confirm `GoapAction_MoveToTarget`'s exact API by reading `Assets/Scripts/AI/GOAP/Actions/Base/GoapAction_MoveToTarget.cs`. Adjust signatures to match (e.g., `GetDestination` may be virtual or abstract, `cost` may be a property). Do not invent shapes.

- [ ] **Step 9.3: Compile.** Console expected: zero errors. (Will fail if `GoapAction_Eat` referenced — but Task 9 only references the class shape, not the type. If your pattern requires a forward reference, defer compile to Task 10.)

- [ ] **Step 9.4: Commit.**

```bash
git add "Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToFood.cs" "Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToFood.cs.meta"
git commit -m "feat(goap): GoapAction_GoToFood — walk to food-bearing storage"
```

---

## Task 10: NPC GOAP — `GoapAction_Eat`

**Files:**
- Create: `Assets/Scripts/AI/GOAP/Actions/GoapAction_Eat.cs`

> Pattern reference: `Assets/Scripts/AI/GOAP/Actions/GoapAction_TakeFromSourceFurniture.cs` (shows how to extract from a StorageFurniture slot) + `GoapAction_ExecuteCharacterAction.cs` (shows how to wrap a CharacterAction inside a GOAP action).

- [ ] **Step 10.1: Read both reference files** (open in editor). Note especially:
  - How `GoapAction_TakeFromSourceFurniture` removes the item from the slot.
  - How `GoapAction_ExecuteCharacterAction` enqueues an action and waits for completion.

- [ ] **Step 10.2: Create the action.**

```csharp
// Assets/Scripts/AI/GOAP/Actions/GoapAction_Eat.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pulls a FoodInstance out of a StorageFurniture slot, then enqueues
/// CharacterUseConsumableAction on the NPC. Mirrors the player's E-key path so
/// player and NPC consume through the same code (rule #22 parity).
/// </summary>
public class GoapAction_Eat : GoapAction
{
    private readonly StorageFurniture _foodSource;
    private FoodInstance _extracted;
    private bool _actionStarted;

    public GoapAction_Eat(StorageFurniture foodSource)
    {
        _foodSource = foodSource;
        cost = 1f;

        preconditions.Add("atFood", true);
        effects.Add("isHungry", false);
    }

    public override bool CheckPreconditions(GoapAgent agent)
    {
        if (_foodSource == null) return false;
        if (agent?.Character == null) return false;

        // Verify food is still in the furniture (another NPC may have grabbed it).
        return TryFindFood(out _);
    }

    public override bool PerformAction(GoapAgent agent)
    {
        var character = agent?.Character;
        if (character == null) return false;

        if (!_actionStarted)
        {
            if (!TryFindFood(out _extracted)) return false;

            // Remove from storage slot.
            // NOTE: confirm exact StorageFurniture API for "take item from slot" — see
            // GoapAction_TakeFromSourceFurniture for the canonical pattern.
            if (!_foodSource.TryRemoveItem(_extracted))
            {
                Debug.LogWarning($"<color=orange>[GoapAction_Eat]</color> Failed to remove {_extracted.CustomizedName} from {_foodSource.name} slot.");
                return false;
            }

            character.CharacterActions.ExecuteAction(new CharacterUseConsumableAction(character, _extracted));
            _actionStarted = true;
            return true; // still running
        }

        // Wait for the consume action to finish.
        if (character.CharacterActions.CurrentAction == null)
        {
            return false; // action complete — GoapAgent will see effects applied
        }

        return true; // still running
    }

    public override bool IsCompleted(GoapAgent agent)
    {
        if (!_actionStarted) return false;
        return agent.Character.CharacterActions.CurrentAction == null;
    }

    private bool TryFindFood(out FoodInstance food)
    {
        food = null;
        if (_foodSource == null) return false;

        foreach (var slot in _foodSource.ItemSlots)
        {
            if (slot?.Item is FoodInstance fi)
            {
                food = fi;
                return true;
            }
        }
        return false;
    }
}
```

> The exact `CheckPreconditions` / `PerformAction` / `IsCompleted` signatures depend on the project's `GoapAction` base class. Read `Assets/Scripts/AI/GOAP/Actions/Base/GoapAction.cs` (or wherever the abstract base lives) and match its pattern exactly. Adjust the methods above to fit. Do not invent.

- [ ] **Step 10.3: Wire `NeedHunger.GetGoapActions`.**

Open `Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs`. Replace the placeholder `GetGoapActions()` returning empty list with:

```csharp
    public override List<GoapAction> GetGoapActions()
    {
        var actions = new List<GoapAction>();
        if (_character == null) return actions;

        // Find a food source in the NPC's job (preferred) or home building.
        var sources = new List<CommercialBuilding>();
        var jobBuilding = _character.CharacterJob?.AssignedBuilding;
        if (jobBuilding != null) sources.Add(jobBuilding);
        // Add fallback building lookup here once verified — for v1 we only use the assigned building.
        // (Avoid global building scans — perf trap. See spec.)

        foreach (var building in sources)
        {
            foreach (var (furniture, item) in building.GetItemsInStorageFurniture())
            {
                if (item?.ItemSO is FoodSO)
                {
                    actions.Add(new GoapAction_GoToFood(furniture));
                    actions.Add(new GoapAction_Eat(furniture));
                    _lastSearchTime = UnityEngine.Time.time;
                    return actions;
                }
            }
        }

        // No food found anywhere. Cooldown to avoid GOAP spam.
        _lastSearchTime = UnityEngine.Time.time;
        return actions; // empty
    }
```

- [ ] **Step 10.4: Compile.** Fix any signature mismatches against the actual `GoapAction` / `StorageFurniture` API.

- [ ] **Step 10.5: Smoke-test in Play mode.**
  Spawn an NPC. Drop their hunger to ≤ 30 via Dev-Mode (or wait for natural decay). Place a `FoodSO`-backed item in a `StorageFurniture` slot inside the NPC's job building. Expected: NPC walks to the storage, plays the consume animation, hunger restored, slot now empty.

- [ ] **Step 10.6: Commit.**

```bash
git add "Assets/Scripts/AI/GOAP/Actions/GoapAction_Eat.cs" "Assets/Scripts/AI/GOAP/Actions/GoapAction_Eat.cs.meta" "Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs"
git commit -m "feat(goap): GoapAction_Eat + NeedHunger food-source resolver"
```

---

## Task 11: Author one default `FoodSO` asset for testing

**Files:**
- Create: `Assets/Resources/Data/Item/Food/Bread.asset` (via Unity Editor right-click)

- [ ] **Step 11.1: Create folder + asset via MCP.**
  - `assets-create-folder`: parent `Assets/Resources/Data/Item/`, name `Food`.
  - In Unity Editor (or via MCP `assets-find` → instantiate-from-template if available): right-click in `Assets/Resources/Data/Item/Food/` → Create → Scriptable Objects → Items → Food. Name it `Bread`.
  - Set fields: `_hungerRestored = 30`, `_foodCategory = Cooked`, `_destroyOnUse = true`. Other ItemSO fields: `itemId = "food_bread"`, `itemName = "Bread"`, `description = "A simple loaf. Restores 30 hunger."`, `icon = <pick a placeholder sprite>`, weight = Light, material = None, tier = 0.

- [ ] **Step 11.2: Smoke-test:** Use Dev-Mode to spawn `Bread` in player hands. Press **E**. Expected: hunger goes from 80 to 100, bread vanishes. Hunger bar updates.

- [ ] **Step 11.3: Commit.**

```bash
git add "Assets/Resources/Data/Item/Food" "Assets/Resources/Data/Item/Food.meta"
git commit -m "data(items): add Bread sample FoodSO asset for testing"
```

---

## Task 12: Update `wiki/systems/character-needs.md`

**Files:**
- Modify: `wiki/systems/character-needs.md` (or create if missing)

- [ ] **Step 12.1: Read `wiki/CLAUDE.md`** for frontmatter rules (rule #29b).

- [ ] **Step 12.2: Locate the page.**
  Run: `ls wiki/systems/`
  - If `character-needs.md` exists, modify it.
  - If not, copy `wiki/_templates/system.md` to `wiki/systems/character-needs.md` and fill all 10 required sections.

- [ ] **Step 12.3: Required edits to the page (whether modifying or creating):**
  - Bump `updated:` to today's date.
  - Append to `## Change log`: `- 2026-04-26 — added NeedHunger (phase-tick decay, IsStarving event) + FoodSO consumable subtype + GoapAction_Eat/GoToFood — claude`
  - Update `## Public API` section to list `NeedHunger.IsStarving`, `OnStarvingChanged`, `OnValueChanged`, `IncreaseValue/DecreaseValue`, `TrySubscribeToPhase/UnsubscribeFromPhase`.
  - Add cross-link under `related:` to `wiki/systems/items.md` (or wherever the consumable taxonomy lives).
  - In `## Sources`, link to `Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs`, `Assets/Resources/Data/Item/FoodSO.cs`, `Assets/Scripts/Item/FoodInstance.cs`.

- [ ] **Step 12.4: Commit.**

```bash
git add wiki/systems/character-needs.md
git commit -m "docs(wiki): document NeedHunger + FoodSO in character-needs system page"
```

---

## Task 13: SKILL.md update (rule #28)

**Files:**
- `.agent/skills/character-needs/SKILL.md` (modify or create)

- [ ] **Step 13.1: Locate or create the skill file.**
  Run: `ls .agent/skills/character-needs/` (may not exist).
  - If exists, modify.
  - If not, run `ls .agent/skills/skill-creator/SKILL.md` to refresh on the template, then create `.agent/skills/character-needs/SKILL.md`.

- [ ] **Step 13.2: Required content** (see existing skills for shape):
  - Purpose: dynamic per-character needs subscribed to time/events; provider pattern for GOAP integration.
  - Public API: `CharacterNeeds.AllNeeds`, `GetNeed<T>()`, plus per-need APIs (NeedSocial, NeedJob, NeedToWearClothing, **NeedHunger**).
  - Events: `NeedHunger.OnValueChanged(float)`, `NeedHunger.OnStarvingChanged(bool)`, etc.
  - Dependencies: `TimeManager` (for phase-based decay subscriptions), `CharacterAction` (for need-driven actions like eating), `MacroSimulator` (for offline catch-up).
  - Integration points: GOAP via `GetGoapGoal()`/`GetGoapActions()`, persistence via `NeedsSaveData`.

- [ ] **Step 13.3: Commit.**

```bash
git add .agent/skills/character-needs/SKILL.md
git commit -m "docs(skill): document NeedHunger in character-needs SKILL.md (rule #28)"
```

---

## Task 14: Agent file updates (rule #29)

**Files:**
- `.claude/agents/character-system-specialist.md`
- `.claude/agents/npc-ai-specialist.md`
- `.claude/agents/item-inventory-specialist.md`

- [ ] **Step 14.1: Add a one-line bullet under each agent's "knowledge" or "responsibilities" list:**
  - `character-system-specialist`: "NeedHunger (phase-decay + IsStarving event) and Character.UseConsumable wiring through ConsumableInstance.ApplyEffect virtual."
  - `npc-ai-specialist`: "GoapAction_GoToFood + GoapAction_Eat — NPC food acquisition from CommercialBuilding storage furniture."
  - `item-inventory-specialist`: "FoodSO : ConsumableSO subtype — _hungerRestored + FoodCategory enum; FoodInstance.ApplyEffect."

- [ ] **Step 14.2: Verify all three agents still have `model: opus` in their frontmatter** (rule #always-opus).

- [ ] **Step 14.3: Commit.**

```bash
git add .claude/agents/character-system-specialist.md .claude/agents/npc-ai-specialist.md .claude/agents/item-inventory-specialist.md
git commit -m "docs(agents): note NeedHunger/FoodSO/GoapAction_Eat in relevant specialists"
```

---

## Task 15: Final validation pass

Run the full validation checklist from the spec. Each unchecked item below corresponds to one row in the spec's "Validation checklist" section.

- [ ] **15.1 — Phase decay rate:** Skip 4 phases via Dev-Mode `/devmode` time-skip → confirm hunger drops by 100 (full empty).
- [ ] **15.2 — Player eat (E):** Carry `Bread` → press E → animation plays → +30 hunger → bread removed.
- [ ] **15.3 — Empty hands E:** Press E with empty hands → no error, no spam.
- [ ] **15.4 — E during another action:** Mid-attack press E → no-op, no exception.
- [ ] **15.5 — NPC eats:** Hungry NPC + food in their job storage → walks → eats → hunger restored → slot empty.
- [ ] **15.6 — NPC no food:** Hungry NPC + empty storage → no infinite GOAP loop (cooldown holds).
- [ ] **15.7 — Persistence (player):** Eat through portal → hunger value survives → exit & return → still that value.
- [ ] **15.8 — Persistence (bed):** Sleep at bed → confirm `NeedHunger` value in save file.
- [ ] **15.9 — Hibernation catch-up:** Use Dev-Mode to hibernate a map for 3 in-game days → wake it → confirm NPC hunger = max(0, oldValue − 3 × 100).
- [ ] **15.10 — Host↔Client networking:** Two-player session → only the local player's bar shows, NPC hunger doesn't leak to client.
- [ ] **15.11 — HUD bar event-driven:** Add a temporary `Debug.Log` inside `UI_HungerBar.HandleValueChanged` → confirm it fires only on change, not per frame.
- [ ] **15.12 — HUD unscaled time:** Pause game (timeScale = 0) → confirm starve flash continues.
- [ ] **15.13 — `OnStarvingChanged` exactness:** Watch via test or Debug.Log: triggers exactly once on entry to 0 and exactly once on exit.
- [ ] **15.14 — Defensive coding:** Boot the game with `TimeManager` disabled in scene → no exception spam, NPC just doesn't decay.

If any check fails, fix and re-run from that point. Do not declare done until all 14 pass.

- [ ] **15.15 — Final commit (if any fixes were made during validation).** Otherwise no commit; just declare validation passed.

---

## Self-review against spec

After completing all tasks:

- Spec section "Files added (6)" → covered by Tasks 1, 2, 3, 6, 9, 10. ✅
- Spec section "Files modified (7) + 1 prefab" → covered by Tasks 1, 3, 4, 5, 6, 7, 8. ✅
- Spec section "Wiki & SKILL updates" → covered by Tasks 12, 13, 14. ✅
- Spec section "Data flow → Phase decay" → Task 3 (subscribe + handler) + Task 15.1 validation.
- Spec section "Data flow → Player eat" → Tasks 4 + 5 + Task 15.2.
- Spec section "Data flow → NPC eat" → Tasks 9 + 10 + Task 15.5.
- Spec section "Data flow → Save/load" → Already free; Tasks 15.7/15.8 validate.
- Spec section "Data flow → MacroSim catch-up" → Task 8.
- Spec section "Networking" → No code task (no new NetworkVariable/RPC); Task 15.10 validates.
- Spec section "HUD" → Tasks 6 + 7.
- Spec section "Tuning defaults" → Task 3 (`NeedHunger` constants) + Task 11 (Bread SO).
- Spec section "Validation checklist" → Task 15 (all 14 items).

No gaps detected.
