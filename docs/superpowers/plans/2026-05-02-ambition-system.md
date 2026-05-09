# Ambition System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Sims-3-style "Ambition" subsystem to every `Character` (player or NPC). One active life-goal at a time, composed of authored Quests, each composed of generic Task primitives. NPC AI pursues it via a new BT branch (priority 5.5) gated by Hunger+Sleep being satisfied; players see it as a normal `IQuest` plus a small chain-progress widget. Persists, networks, survives controller switches and hibernation.

**Architecture:** New `CharacterAmbition` subsystem on the root `Character`. Three-layer authoring (`AmbitionSO` → `QuestSO` → polymorphic `TaskBase`). Reuses existing `IQuest` infrastructure for save/sync/UI by introducing `AmbitionQuest : IQuest` as the bridge. New BT nodes drive behavior; tasks dispatch via Pattern A (`CharacterAction` enqueue) or Pattern B (transient GOAP goal).

**Tech Stack:** Unity 2022+, C#, NGO (server-authoritative), NUnit (EditMode tests in `Assets/Tests/EditMode/Ambition/`), uGUI + TMP, Unity `[SerializeReference]` for inline polymorphic Task authoring.

**Spec:** [docs/superpowers/specs/2026-05-02-ambition-system-design.md](../specs/2026-05-02-ambition-system-design.md)

**Testing approach:** Pure-logic units (`AmbitionContext`, state-machine transitions, parameter binding, save DTOs) get NUnit EditMode tests via `tests-run` MCP tool. Scene/network/UI/BT integration gets manual Play-mode smokes per project rule #27 with `Debug.Log` checkpoints. All BT-touching tests run with `NPCDebug.VerboseAmbition = false` to verify no log-spam path is hot per rule #34.

**World scale reminder (rule #32):** 11 Unity units = 1.67 m. The Ambition system itself does not use spatial distances, but `Task_MoveToZone` consumes `IWorldZone` radii authored at this scale.

---

## File Structure

### Files created

**Core Ambition module — `Assets/Scripts/Character/Ambition/`**
- `CharacterAmbition.cs` — root subsystem (`CharacterSystem` + `ICharacterSaveData<AmbitionSaveData>` + `NetworkBehaviour`-side hooks via the parent Character). Owns `Current`, `History`, `Set/ClearAmbition`, events.
- `AmbitionSO.cs` — base `ScriptableObject` + `AmbitionParameterDef` + `ContextValueKind` enum.
- `AmbitionInstance.cs` — runtime state class (SO + step index + step quest + context + assigned day).
- `AmbitionContext.cs` — typed bag wrapper with serializability check + read/write API.
- `CompletedAmbition.cs` — history record (SO + final context + completion day + reason).
- `CompletionReason.cs` — enum (`Completed | ClearedByScript | Failed (reserved)`).
- `ControllerKind.cs` — enum (`Player | NPC`).
- `AmbitionRegistry.cs` — lazy-init asset registry (rule #18 late-joiner-safe pattern).
- `QuestRegistry.cs` — same pattern for `QuestSO`.
- `AmbitionSettings.cs` — single global `ScriptableObject` with `GatingNeeds : List<NeedSO>`.
- `NetworkAmbitionSnapshot.cs` — compact `INetworkSerializable` for the `NetworkVariable`.

**Save DTOs — `Assets/Scripts/Character/Ambition/Save/`**
- `AmbitionSaveData.cs` — top-level DTO.
- `ContextEntryDTO.cs` — one context key+value pair.
- `CompletedAmbitionDTO.cs` — one history record DTO.
- `TaskStateDTO.cs` — one task's per-pursuit state.

**Quest layer — `Assets/Scripts/Character/Ambition/Quests/`**
- `IAmbitionStepQuest.cs` — bridge interface extending `IQuest`.
- `AmbitionQuest.cs` — concrete `IQuest` runtime instance.
- `QuestSO.cs` — authored quest definition (with `TaskOrderingMode`).
- `TaskOrderingMode.cs` — enum (`Sequential | Parallel | AnyOf`).

**Task layer — `Assets/Scripts/Character/Ambition/Tasks/`**
- `TaskBase.cs` — polymorphic `[Serializable]` base.
- `TaskStatus.cs` — enum.
- `Task_KillCharacter.cs` (Pattern A).
- `Task_TalkToCharacter.cs` (Pattern A).
- `Task_HarvestTarget.cs` (Pattern A).
- `Task_MoveToZone.cs` (Pattern B).
- `Task_GatherItem.cs` (Pattern B).
- `Task_DeliverItem.cs` (Pattern B).
- `Task_WaitDays.cs` (none — listener only).

**Parameter binding — `Assets/Scripts/Character/Ambition/Bindings/`**
- `TaskParameterBinding.cs` — abstract `<T>`.
- `StaticBinding.cs`.
- `ContextBinding.cs`.
- `RuntimeQueryBinding.cs` — abstract; concrete subclasses live next to the Task that uses them.
- `EligibleLoverQuery.cs` — first concrete subclass, used by `Quest_FindLover`.

**BT integration — `Assets/Scripts/AI/BehaviourTree/`**
- `Conditions/BTCond_CanPursueAmbition.cs`.
- `Actions/BTAction_PursueAmbitionStep.cs`.

**GOAP integration — `Assets/Scripts/AI/GOAP/`**
- (modify existing) `CharacterGoapController.cs` — add `RegisterTransientGoal` / `UnregisterTransientGoal`.

**UI — `Assets/Scripts/UI/Ambition/` and `Assets/UI/Ambition/`**
- `Assets/Scripts/UI/Ambition/UI_AmbitionTracker.cs`.
- `Assets/UI/Ambition/UI_AmbitionTracker.prefab`.

**Dev-Mode inspector — `Assets/Scripts/Debug/DevMode/Modules/Inspectors/`**
- `AmbitionInspectorView.cs`.
- (modify existing) `CharacterInspectorView.cs` — register the new sub-tab.

**Authored content — `Assets/Resources/Data/Ambitions/`**
- `AmbitionSettings.asset`.
- `Ambition_HaveAFamily.asset`.
- `Ambition_HaveTwoKids.asset`.
- `Ambition_Murder.asset`.
- `Quests/Quest_FindLover.asset`.
- `Quests/Quest_HaveChildWithLover.asset`.
- `Quests/Quest_AssassinateTarget.asset`.

**Tests — `Assets/Tests/EditMode/Ambition/`**
- `AmbitionContextTests.cs`.
- `AmbitionStateMachineTests.cs`.
- `ContextBindingTests.cs`.
- `ParameterBindingTests.cs`.
- `QuestOrderingTests.cs`.
- `BTCondCanPursueAmbitionTests.cs`.
- `AmbitionSaveRoundTripTests.cs`.

**Documentation**
- `.agent/skills/ambition-system/SKILL.md`.
- `wiki/systems/ambition-system.md`.
- (modify) `wiki/systems/ai-goap.md` — redirect "ultimate goals" examples.
- (modify) `wiki/systems/quest-system.md` — cross-link `AmbitionQuest`.
- (optional, evaluate at end of plan) `.claude/agents/ambition-system-specialist.md`.

### Files modified

- `Assets/Scripts/Character/Character.cs` — add `[SerializeField] CharacterAmbition _characterAmbition`, `CharacterAmbition` property, auto-assign in `Awake()`. Add `OnControllerSwitching` hook in `SwitchToPlayer` and `SwitchToNPC` (line 853 + 869).
- `Assets/Scripts/Character/CharacterPersistence/CharacterDataCoordinator.cs` — register `CharacterAmbition` in priority slot between `CharacterQuestLog` and `CharacterSchedule`.
- `Assets/Scripts/AI/NPCBehaviourTree.cs` — insert new BT branch at priority 5.5.
- `Assets/Scripts/AI/GOAP/CharacterGoapController.cs` — add transient-goal API.
- `Assets/Scripts/World/HibernatedNPCData.cs` — add `Ambition : AmbitionSaveData` field.
- `Assets/Resources/Prefabs/Character/[character prefabs]` — add `CharacterAmbition` child GameObject (manual editor step at the end of Phase 5).
- `Assets/Scripts/UI/PlayerUI.cs` — instantiate / wire `UI_AmbitionTracker` on local-player initialization.
- `Assets/Scripts/Debug/DevMode/Inspectors/CharacterInspectorView.cs` — register the new `AmbitionInspectorView` tab.

---

## Pre-flight (one-time, before Phase 1)

### Task 0: Verify the spec is committed and create the test folder

**Files:**
- Create: `Assets/Tests/EditMode/Ambition/.keep`

- [ ] **Step 0.1: Verify spec is on disk and committed**

Run:

```bash
git log --oneline docs/superpowers/specs/2026-05-02-ambition-system-design.md | head -3
```

Expected: at least one commit containing the spec.

- [ ] **Step 0.2: Create the EditMode test folder skeleton**

Use the `assets-create-folder` MCP tool with parent `Assets/Tests/EditMode/Ambition`.

- [ ] **Step 0.3: Add a `.keep` so the folder commits cleanly**

Use the Write tool to create `Assets/Tests/EditMode/Ambition/.keep` with empty content.

- [ ] **Step 0.4: Refresh the asset database**

Run `assets-refresh` MCP tool, then `console-get-logs`. Expect zero compile errors.

- [ ] **Step 0.5: Commit**

```bash
git add Assets/Tests/EditMode/Ambition/.keep
git commit -m "test(ambition): scaffold test folder"
```

---

## Phase 1 — Foundation enums and value types

These are pure-logic types with zero Unity dependency where possible. Each one gets an NUnit test before any other work depends on it. Order matters: `ContextValueKind`, `CompletionReason`, `ControllerKind`, `TaskStatus` first (no dependencies), then the small classes that use them.

### Task 1: `ContextValueKind` enum

**Files:**
- Create: `Assets/Scripts/Character/Ambition/AmbitionSO.cs` (just the enum for now; the SO body lands in Task 7)

- [ ] **Step 1.1: Use the `assets-create-folder` MCP tool**

Parent: `Assets/Scripts/Character/Ambition`. This folder must exist before any `script-update-or-create` call writes into it.

- [ ] **Step 1.2: Write the enum**

Use Write tool to create `Assets/Scripts/Character/Ambition/AmbitionSO.cs`:

```csharp
namespace MWI.Ambition
{
    /// <summary>
    /// Discriminator for AmbitionContext entries. The save layer routes serialization
    /// per kind: Character → CharacterId UUID, Primitive → string-encoded value,
    /// Enum → string-encoded name, ItemSO/AmbitionSO/QuestSO/NeedSO → asset GUID,
    /// Zone → IWorldZone GUID.
    /// </summary>
    public enum ContextValueKind
    {
        Character,
        Primitive,
        Enum,
        ItemSO,
        AmbitionSO,
        QuestSO,
        NeedSO,
        Zone
    }
}
```

- [ ] **Step 1.3: Refresh and verify**

Run `assets-refresh` then `console-get-logs`. Expect zero errors.

- [ ] **Step 1.4: Commit**

```bash
git add Assets/Scripts/Character/Ambition/AmbitionSO.cs
git commit -m "feat(ambition): add ContextValueKind enum"
```

### Task 2: `CompletionReason`, `ControllerKind`, `TaskStatus` enums

**Files:**
- Create: `Assets/Scripts/Character/Ambition/CompletionReason.cs`
- Create: `Assets/Scripts/Character/Ambition/ControllerKind.cs`
- Create: `Assets/Scripts/Character/Ambition/Tasks/TaskStatus.cs`

- [ ] **Step 2.1: Create Tasks folder**

Use `assets-create-folder` with parent `Assets/Scripts/Character/Ambition/Tasks`.

- [ ] **Step 2.2: Write `CompletionReason.cs`**

```csharp
namespace MWI.Ambition
{
    /// <summary>
    /// Why a CompletedAmbition record exists. v1 uses Completed and ClearedByScript only.
    /// Failed is reserved for a future iteration that adds auto-fail predicates.
    /// </summary>
    public enum CompletionReason
    {
        Completed,
        ClearedByScript,
        Failed
    }
}
```

- [ ] **Step 2.3: Write `ControllerKind.cs`**

```csharp
namespace MWI.Ambition
{
    /// <summary>
    /// Argument to TaskBase.OnControllerSwitching. Tells a task whether the Character
    /// is becoming Player-driven or NPC-driven so it can clean up the appropriate
    /// in-flight driver (queued CharacterAction or transient GOAP goal).
    /// </summary>
    public enum ControllerKind
    {
        Player,
        NPC
    }
}
```

- [ ] **Step 2.4: Write `Tasks/TaskStatus.cs`**

```csharp
namespace MWI.Ambition
{
    /// <summary>
    /// Per-tick result of TaskBase.Tick. Failed is reserved for a future
    /// iteration; v1 tasks never return it (zombie-tolerance per the patient
    /// lifecycle model).
    /// </summary>
    public enum TaskStatus
    {
        Running,
        Completed,
        Failed
    }
}
```

- [ ] **Step 2.5: Refresh and verify zero errors**

Run `assets-refresh` then `console-get-logs`.

- [ ] **Step 2.6: Commit**

```bash
git add Assets/Scripts/Character/Ambition/CompletionReason.cs Assets/Scripts/Character/Ambition/ControllerKind.cs Assets/Scripts/Character/Ambition/Tasks/TaskStatus.cs
git commit -m "feat(ambition): add CompletionReason, ControllerKind, TaskStatus enums"
```

### Task 3: `AmbitionContext` (pure value type with NUnit test)

**Files:**
- Create: `Assets/Scripts/Character/Ambition/AmbitionContext.cs`
- Create: `Assets/Tests/EditMode/Ambition/AmbitionContextTests.cs`

- [ ] **Step 3.1: Write the failing test first**

Use Write tool to create `Assets/Tests/EditMode/Ambition/AmbitionContextTests.cs`:

```csharp
using System;
using NUnit.Framework;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class AmbitionContextTests
    {
        [Test]
        public void Set_Then_Get_RoundTrip_Primitive()
        {
            var ctx = new AmbitionContext();
            ctx.Set("count", 7);
            Assert.AreEqual(7, ctx.Get<int>("count"));
        }

        [Test]
        public void Get_Missing_Throws()
        {
            var ctx = new AmbitionContext();
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => ctx.Get<int>("nope"));
        }

        [Test]
        public void TryGet_Missing_Returns_False()
        {
            var ctx = new AmbitionContext();
            bool found = ctx.TryGet<int>("nope", out var v);
            Assert.IsFalse(found);
            Assert.AreEqual(default(int), v);
        }

        [Test]
        public void Set_NonSerializableType_Throws()
        {
            var ctx = new AmbitionContext();
            Assert.Throws<InvalidOperationException>(
                () => ctx.Set("k", new System.Text.StringBuilder("nope")));
        }

        [Test]
        public void Set_Object_With_Character_Runtime_Type_Allowed()
        {
            // Regression: the type check looks at the runtime value type, not the generic T.
            // Caller may pass <object> when iterating a polymorphic collection.
            var ctx = new AmbitionContext();
            // We don't have a real Character here in EditMode — verify that types
            // declared via AmbitionContext.IsSerializableValueKind succeed.
            // Test with a primitive boxed as object.
            object boxedInt = 42;
            Assert.DoesNotThrow(() => ctx.Set<object>("k", boxedInt));
            Assert.AreEqual(42, ctx.Get<int>("k"));
        }
    }
}
```

- [ ] **Step 3.2: Run the test and confirm it fails**

Use the `tests-run` MCP tool with `mode: "EditMode"`, `assembly: "MWI.Tests"` (or whichever assembly the test folder lives in — verify via Glob `Assets/Tests/EditMode/*.asmdef`). Expect compile failure since `AmbitionContext` doesn't exist.

- [ ] **Step 3.3: Write `AmbitionContext.cs`**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Typed bag of context values shared across an ambition's quest chain. Each step
    /// quest reads/writes via the bag (e.g. Quest_FindLover writes context["Lover"];
    /// Quest_HaveChildWithLover reads it). Set-time validation rejects non-serializable
    /// values so authors hit the wall in editor, not at production save time.
    /// </summary>
    [Serializable]
    public sealed class AmbitionContext
    {
        private readonly Dictionary<string, object> _values = new();

        public T Get<T>(string key)
        {
            if (!_values.TryGetValue(key, out var v))
                throw new KeyNotFoundException($"Ambition context has no key '{key}'.");
            return (T)v;
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (_values.TryGetValue(key, out var v) && v is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public void Set<T>(string key, T value)
        {
            // Check the runtime type of the value, not the generic parameter T,
            // so ctx.Set<object>("k", someCharacter) classifies as Character.
            var runtimeType = value?.GetType() ?? typeof(T);
            if (!IsSerializableValueKind(runtimeType))
                throw new InvalidOperationException(
                    $"Ambition context value of type {runtimeType.Name} is not serializable. "
                    + "Allowed: primitives, enums, Character, ScriptableObject subclasses, IWorldZone.");
            _values[key] = value;
        }

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public IReadOnlyDictionary<string, object> AsReadOnly() => _values;

        /// <summary>
        /// Returns true if values of the given type are allowed in the ambition context.
        /// The runtime save layer relies on this set; expanding it requires extending
        /// the ContextEntryDTO serialization switch in AmbitionSaveData.
        /// </summary>
        public static bool IsSerializableValueKind(Type t)
        {
            if (t == null) return false;
            if (t.IsPrimitive) return true;          // int, float, bool, etc.
            if (t == typeof(string)) return true;
            if (t.IsEnum) return true;
            if (typeof(ScriptableObject).IsAssignableFrom(t)) return true;
            // Character and IWorldZone live in Assembly-CSharp; we identify them by name
            // to avoid a dependency loop from this file. Save layer handles the full check.
            if (t.FullName == "Character") return true;
            if (typeof(MWI.WorldSystem.IWorldZone).IsAssignableFrom(t)) return true;
            return false;
        }
    }
}
```

- [ ] **Step 3.4: Refresh + run tests**

Run `assets-refresh`, then `tests-run` with `mode: "EditMode"`, `class: "MWI.Tests.Ambition.AmbitionContextTests"`. Expect 5/5 pass.

- [ ] **Step 3.5: Commit**

```bash
git add Assets/Scripts/Character/Ambition/AmbitionContext.cs Assets/Tests/EditMode/Ambition/AmbitionContextTests.cs
git commit -m "feat(ambition): add AmbitionContext with NUnit coverage"
```

### Task 4: `CompletedAmbition` runtime record

**Files:**
- Create: `Assets/Scripts/Character/Ambition/CompletedAmbition.cs`

- [ ] **Step 4.1: Write the file**

Note that `AmbitionSO` doesn't exist yet; reference it as a type that will be defined in Task 7. Forward references inside the same assembly are fine in C#.

```csharp
using System;

namespace MWI.Ambition
{
    /// <summary>
    /// History record pushed onto CharacterAmbition.History when an ambition transitions
    /// out of Active. Carries enough info for downstream consumers (dialogue triggers,
    /// reputation effects, dev inspector) to render or react to the achievement.
    /// </summary>
    [Serializable]
    public sealed class CompletedAmbition
    {
        public AmbitionSO SO;
        public AmbitionContext FinalContext;
        public int CompletedDay;
        public CompletionReason Reason;

        public CompletedAmbition() { }

        public CompletedAmbition(AmbitionSO so, AmbitionContext finalContext, int completedDay, CompletionReason reason)
        {
            SO = so;
            FinalContext = finalContext;
            CompletedDay = completedDay;
            Reason = reason;
        }
    }
}
```

- [ ] **Step 4.2: Refresh, expect compile error referencing missing `AmbitionSO`**

Run `assets-refresh` then `console-get-logs`. Expect a compile error mentioning `AmbitionSO`. That's expected — the next task creates it.

- [ ] **Step 4.3: Skip commit until AmbitionSO compiles**

We commit in Task 7 once the symbol resolves. Move on.

### Task 5: `AmbitionInstance` runtime record

**Files:**
- Create: `Assets/Scripts/Character/Ambition/AmbitionInstance.cs`

- [ ] **Step 5.1: Write the file**

```csharp
using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Runtime state of an active ambition on a Character. Owned by CharacterAmbition.
    /// CurrentStepQuest mirrors a CharacterQuestLog entry for the active step.
    /// </summary>
    [Serializable]
    public sealed class AmbitionInstance
    {
        public AmbitionSO SO;
        public int CurrentStepIndex;
        public IAmbitionStepQuest CurrentStepQuest;
        public AmbitionContext Context;
        public int AssignedDay;

        public AmbitionInstance() { Context = new AmbitionContext(); }

        public bool IsLastStep => SO != null && CurrentStepIndex >= SO.Quests.Count - 1;
        public int TotalSteps => SO != null ? SO.Quests.Count : 0;
        public float Progress01
        {
            get
            {
                if (SO == null || SO.Quests.Count == 0) return 0f;
                return (float)CurrentStepIndex / SO.Quests.Count;
            }
        }
    }
}
```

- [ ] **Step 5.2: Skip commit, AmbitionSO and IAmbitionStepQuest still missing**

Both compile errors will resolve in Tasks 6 and 7.

### Task 6: `IAmbitionStepQuest` bridge interface (declaration only — no concrete yet)

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Quests/IAmbitionStepQuest.cs`

- [ ] **Step 6.1: Create the Quests folder**

Use `assets-create-folder` parent `Assets/Scripts/Character/Ambition/Quests`.

- [ ] **Step 6.2: Write the interface**

```csharp
using MWI.Quests;

namespace MWI.Ambition
{
    /// <summary>
    /// Bridge contract: an IQuest that can also be ticked by the Behaviour Tree
    /// (NPC side) and bound to an AmbitionContext. Lives in CharacterQuestLog like
    /// any other IQuest, but the BT may also drive its tasks directly. See spec
    /// section "Behaviour Tree Integration" — TickActiveTasks delegates to the
    /// concrete QuestSO ordering policy.
    /// </summary>
    public interface IAmbitionStepQuest : IQuest
    {
        void BindContext(AmbitionContext ctx);
        TaskStatus TickActiveTasks(Character npc);
        void Cancel();
        void OnControllerSwitching(Character npc, ControllerKind goingTo);
    }
}
```

- [ ] **Step 6.3: Refresh, check that `MWI.Quests.IQuest` resolves**

Run `assets-refresh` then `console-get-logs`. The compile error from Tasks 4 and 5 about `AmbitionSO` should still be present, but `IQuest` should resolve. If `MWI.Quests.IQuest` doesn't resolve, grep for the actual namespace:

```bash
grep -rn "interface IQuest" Assets/Scripts | head -3
```

and update the `using` line to match.

- [ ] **Step 6.4: Skip commit until next task**

### Task 7: `AmbitionSO` body + `AmbitionParameterDef`

**Files:**
- Modify: `Assets/Scripts/Character/Ambition/AmbitionSO.cs` (replace contents)

- [ ] **Step 7.1: Replace file contents**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Discriminator for AmbitionContext entries. See spec — drives save layer routing.
    /// </summary>
    public enum ContextValueKind
    {
        Character,
        Primitive,
        Enum,
        ItemSO,
        AmbitionSO,
        QuestSO,
        NeedSO,
        Zone
    }

    /// <summary>
    /// Declares one input slot on an AmbitionSO that callers must fill via SetAmbition.
    /// Validation in AmbitionSO.ValidateParameters checks each Required key is present
    /// and matches the declared Kind.
    /// </summary>
    [Serializable]
    public class AmbitionParameterDef
    {
        public string Key;
        public ContextValueKind Kind;
        public bool Required = true;
    }

    /// <summary>
    /// Authored top-level life goal. Holds an ordered chain of QuestSO steps, optional
    /// input parameters filled at SetAmbition time, and the OverridesSchedule flag that
    /// gates whether ambition pursuit can pre-empt the NPC's scheduled work shift.
    /// Subclasses override ValidateParameters to enforce per-ambition rules.
    /// </summary>
    public abstract class AmbitionSO : ScriptableObject
    {
        [SerializeField] private string _displayName;
        [TextArea, SerializeField] private string _description;
        [SerializeField] private Sprite _icon;
        [SerializeField] private bool _overridesSchedule;
        [SerializeField] private List<QuestSO> _quests = new();
        [SerializeField] private List<AmbitionParameterDef> _parameters = new();

        public string DisplayName => _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public bool OverridesSchedule => _overridesSchedule;
        public IReadOnlyList<QuestSO> Quests => _quests;
        public IReadOnlyList<AmbitionParameterDef> Parameters => _parameters;

        /// <summary>
        /// Default validation: every Required parameter present and the value classifies
        /// under the declared Kind. Subclasses can override to add cross-parameter rules
        /// (e.g. Ambition_Murder ensures Target is alive at assignment time).
        /// </summary>
        public virtual bool ValidateParameters(IReadOnlyDictionary<string, object> p)
        {
            if (_parameters == null) return true;
            foreach (var def in _parameters)
            {
                if (def == null || string.IsNullOrEmpty(def.Key)) continue;
                bool has = p != null && p.TryGetValue(def.Key, out var val) && val != null;
                if (def.Required && !has)
                {
                    Debug.LogError($"[AmbitionSO] {name} requires parameter '{def.Key}' but none was provided.");
                    return false;
                }
            }
            return true;
        }
    }
}
```

- [ ] **Step 7.2: `QuestSO` is still missing — define a stub**

Modify `Assets/Scripts/Character/Ambition/Quests/IAmbitionStepQuest.cs` is fine, but we need a `QuestSO` symbol. Create the stub now and fully populate in Task 9.

Use Write to create `Assets/Scripts/Character/Ambition/Quests/QuestSO.cs`:

```csharp
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Placeholder — full body lands in Task 9. Currently a bare ScriptableObject
    /// so AmbitionSO.Quests compiles.
    /// </summary>
    public class QuestSO : ScriptableObject
    {
    }
}
```

- [ ] **Step 7.3: Refresh, verify zero compile errors**

Run `assets-refresh` then `console-get-logs`. All forward references should now resolve.

- [ ] **Step 7.4: Commit**

```bash
git add Assets/Scripts/Character/Ambition/AmbitionSO.cs Assets/Scripts/Character/Ambition/AmbitionInstance.cs Assets/Scripts/Character/Ambition/CompletedAmbition.cs Assets/Scripts/Character/Ambition/Quests/IAmbitionStepQuest.cs Assets/Scripts/Character/Ambition/Quests/QuestSO.cs
git commit -m "feat(ambition): add AmbitionSO, AmbitionInstance, CompletedAmbition, IAmbitionStepQuest"
```

### Task 8: `TaskOrderingMode` enum + `TaskBase` abstract

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Quests/TaskOrderingMode.cs`
- Create: `Assets/Scripts/Character/Ambition/Tasks/TaskBase.cs`

- [ ] **Step 8.1: Write `TaskOrderingMode.cs`**

```csharp
namespace MWI.Ambition
{
    /// <summary>
    /// How a QuestSO sequences its child Tasks.
    /// Sequential — one at a time, in list order; Quest completes when the last task does.
    /// Parallel    — all tick simultaneously; Quest completes when all are Completed.
    /// AnyOf       — all tick simultaneously; Quest completes when any one is Completed
    ///               (remaining tasks are Cancel-led).
    /// </summary>
    public enum TaskOrderingMode
    {
        Sequential,
        Parallel,
        AnyOf
    }
}
```

- [ ] **Step 8.2: Write `TaskBase.cs`**

```csharp
using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Polymorphic, [SerializeReference]-friendly base for atomic verb primitives.
    /// Subclasses (Task_KillCharacter, Task_TalkToCharacter, etc.) implement the
    /// behavior. Each task is bound once via Bind(ctx) when the host AmbitionQuest
    /// is issued; thereafter Tick is called by the BT (NPC side) or its world-state
    /// listeners fire (player side). See spec section "Task → behavior translation".
    /// </summary>
    [Serializable]
    public abstract class TaskBase
    {
        /// <summary>Resolve TaskParameterBindings against the context. Called once on issue / on load.</summary>
        public abstract void Bind(AmbitionContext ctx);

        /// <summary>BT-side per-tick. Idempotent: should re-evaluate world state and only act if needed.</summary>
        public abstract TaskStatus Tick(Character npc, AmbitionContext ctx);

        /// <summary>Called when the task is being unwound (ambition cleared, replaced, or AnyOf sibling won).</summary>
        public abstract void Cancel();

        /// <summary>Hook for switching driver patterns at controller flip. Default: no-op.</summary>
        public virtual void OnControllerSwitching(Character npc, ControllerKind goingTo) { }

        /// <summary>Persist mid-pursuit state. Default: no state. Override to return a JSON / fixed-shape string.</summary>
        public virtual string SerializeState() => string.Empty;

        /// <summary>Restore mid-pursuit state from the SerializeState payload.</summary>
        public virtual void DeserializeState(string s) { }

        /// <summary>Subscribe to world-state events that fire even when the BT isn't ticking the task (player path).</summary>
        public virtual void RegisterCompletionListeners(Character npc, AmbitionContext ctx) { }

        /// <summary>Drop the listeners subscribed in RegisterCompletionListeners.</summary>
        public virtual void UnregisterCompletionListeners(Character npc) { }

        /// <summary>True iff the bound parameters resolved successfully and the task is ready to Tick.</summary>
        public virtual bool IsReady => true;
    }
}
```

- [ ] **Step 8.3: Refresh, verify zero compile errors**

- [ ] **Step 8.4: Commit**

```bash
git add Assets/Scripts/Character/Ambition/Quests/TaskOrderingMode.cs Assets/Scripts/Character/Ambition/Tasks/TaskBase.cs
git commit -m "feat(ambition): add TaskOrderingMode and TaskBase polymorphic base"
```

### Task 9: Full `QuestSO` body

**Files:**
- Modify: `Assets/Scripts/Character/Ambition/Quests/QuestSO.cs` (replace contents)

- [ ] **Step 9.1: Replace contents**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Authored quest definition. Owns a polymorphic list of Tasks (inline-authored via
    /// [SerializeReference] in the inspector — no per-task asset bloat) and an Ordering
    /// policy that controls how the runtime AmbitionQuest ticks them.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Ambition/QuestSO", fileName = "Quest_New")]
    public class QuestSO : ScriptableObject
    {
        [SerializeField] private string _displayName;
        [TextArea, SerializeField] private string _description;
        [SerializeReference] private List<TaskBase> _tasks = new();
        [SerializeField] private TaskOrderingMode _ordering = TaskOrderingMode.Sequential;

        public string DisplayName => _displayName;
        public string Description => _description;
        public IReadOnlyList<TaskBase> Tasks => _tasks;
        public TaskOrderingMode Ordering => _ordering;
    }
}
```

- [ ] **Step 9.2: Refresh, verify zero errors**

- [ ] **Step 9.3: Commit**

```bash
git add Assets/Scripts/Character/Ambition/Quests/QuestSO.cs
git commit -m "feat(ambition): flesh out QuestSO with task list and ordering"
```

---

## Phase 2 — Parameter binding hierarchy

Parameters on a Task are wrapped in a `TaskParameterBinding<T>` so they can come from one of three sources: static, context key, or runtime query. This phase ships the abstract base and the static + context bindings; the runtime-query binding ships in Phase 13 alongside the first concrete subclass that uses it (`EligibleLoverQuery` for `Quest_FindLover`).

### Task 10: `TaskParameterBinding<T>` abstract + `StaticBinding<T>`

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Bindings/TaskParameterBinding.cs`
- Create: `Assets/Scripts/Character/Ambition/Bindings/StaticBinding.cs`

- [ ] **Step 10.1: Create the Bindings folder**

Use `assets-create-folder` parent `Assets/Scripts/Character/Ambition/Bindings`.

- [ ] **Step 10.2: Write `TaskParameterBinding.cs`**

```csharp
using System;

namespace MWI.Ambition
{
    /// <summary>
    /// One typed input slot on a Task. Subclasses resolve to a concrete value from
    /// (a) a static authored value, (b) a key in the AmbitionContext, or (c) a
    /// runtime query that picks dynamically from world state.
    /// Marked [Serializable] so [SerializeReference] in the host Task can author
    /// the subclass choice in the inspector.
    /// </summary>
    [Serializable]
    public abstract class TaskParameterBinding<T>
    {
        public abstract T Resolve(AmbitionContext ctx);

        /// <summary>
        /// True iff resolution will succeed (e.g. the context key exists, the runtime
        /// query has a non-null result). Used by Task.IsReady.
        /// </summary>
        public abstract bool CanResolve(AmbitionContext ctx);
    }
}
```

- [ ] **Step 10.3: Write `StaticBinding.cs`**

```csharp
using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Author-time constant. Useful for Task_WaitDays.Days = 7 or numeric tuning;
    /// rarely used for Character/Zone references which usually come from context.
    /// </summary>
    [Serializable]
    public class StaticBinding<T> : TaskParameterBinding<T>
    {
        public T Value;

        public override T Resolve(AmbitionContext ctx) => Value;
        public override bool CanResolve(AmbitionContext ctx) => Value != null || typeof(T).IsValueType;
    }
}
```

- [ ] **Step 10.4: Refresh, verify zero errors**

- [ ] **Step 10.5: Commit**

```bash
git add Assets/Scripts/Character/Ambition/Bindings/TaskParameterBinding.cs Assets/Scripts/Character/Ambition/Bindings/StaticBinding.cs
git commit -m "feat(ambition): add TaskParameterBinding and StaticBinding"
```

### Task 11: `ContextBinding<T>` + tests

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Bindings/ContextBinding.cs`
- Create: `Assets/Tests/EditMode/Ambition/ContextBindingTests.cs`

- [ ] **Step 11.1: Write the failing test first**

```csharp
using NUnit.Framework;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class ContextBindingTests
    {
        [Test]
        public void Resolve_ReadsValueFromContextKey()
        {
            var ctx = new AmbitionContext();
            ctx.Set("Days", 7);
            var binding = new ContextBinding<int> { Key = "Days" };
            Assert.AreEqual(7, binding.Resolve(ctx));
        }

        [Test]
        public void CanResolve_FalseWhenKeyMissing()
        {
            var ctx = new AmbitionContext();
            var binding = new ContextBinding<int> { Key = "Days" };
            Assert.IsFalse(binding.CanResolve(ctx));
        }

        [Test]
        public void CanResolve_TrueWhenKeyPresent()
        {
            var ctx = new AmbitionContext();
            ctx.Set("Days", 7);
            var binding = new ContextBinding<int> { Key = "Days" };
            Assert.IsTrue(binding.CanResolve(ctx));
        }
    }
}
```

- [ ] **Step 11.2: Run test, expect 3 fails (`ContextBinding` not defined)**

Run `tests-run` with `class: "MWI.Tests.Ambition.ContextBindingTests"`. Expected: compile fail or runtime fail referencing missing type.

- [ ] **Step 11.3: Write `ContextBinding.cs`**

```csharp
using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Reads its value from the AmbitionContext at Resolve time. The canonical pattern
    /// for inter-step parameter passing — Step 1 writes context["Lover"], Step 2 reads
    /// it via ContextBinding<Character>("Lover").
    /// </summary>
    [Serializable]
    public class ContextBinding<T> : TaskParameterBinding<T>
    {
        public string Key;

        public override T Resolve(AmbitionContext ctx) => ctx.Get<T>(Key);
        public override bool CanResolve(AmbitionContext ctx)
        {
            return ctx != null && !string.IsNullOrEmpty(Key) && ctx.TryGet<T>(Key, out _);
        }
    }
}
```

- [ ] **Step 11.4: Run tests, expect 3/3 pass**

- [ ] **Step 11.5: Commit**

```bash
git add Assets/Scripts/Character/Ambition/Bindings/ContextBinding.cs Assets/Tests/EditMode/Ambition/ContextBindingTests.cs
git commit -m "feat(ambition): add ContextBinding with tests"
```

### Task 12: `RuntimeQueryBinding<T>` abstract

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Bindings/RuntimeQueryBinding.cs`

- [ ] **Step 12.1: Write the file**

```csharp
using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Picks its value at runtime from world state (e.g. "any eligible lover in this
    /// map"). Concrete subclasses override Resolve to run the query; on success, the
    /// resolved value is written back into ctx[WriteKey] so downstream tasks can read
    /// it via a ContextBinding. Subclasses live next to the Task they support.
    /// </summary>
    [Serializable]
    public abstract class RuntimeQueryBinding<T> : TaskParameterBinding<T>
    {
        public string WriteKey;

        protected abstract T Query(Character npc, AmbitionContext ctx);

        public override T Resolve(AmbitionContext ctx)
        {
            // Resolve without an npc reference: re-read from the cache key written
            // during the first npc-bound resolution. Tasks that need fresh queries
            // should call ResolveWithCharacter.
            if (!string.IsNullOrEmpty(WriteKey) && ctx != null && ctx.TryGet<T>(WriteKey, out var cached))
                return cached;
            return default;
        }

        public T ResolveWithCharacter(Character npc, AmbitionContext ctx)
        {
            // If we already wrote a value, re-use it (idempotent across save/load,
            // controller switches, and BT re-ticks).
            if (!string.IsNullOrEmpty(WriteKey) && ctx != null && ctx.TryGet<T>(WriteKey, out var cached) && cached != null)
                return cached;

            var picked = Query(npc, ctx);
            if (picked != null && !string.IsNullOrEmpty(WriteKey) && ctx != null)
                ctx.Set(WriteKey, picked);
            return picked;
        }

        public override bool CanResolve(AmbitionContext ctx)
        {
            if (string.IsNullOrEmpty(WriteKey) || ctx == null) return false;
            return ctx.TryGet<T>(WriteKey, out var cached) && cached != null;
        }
    }
}
```

- [ ] **Step 12.2: Refresh, verify zero errors**

- [ ] **Step 12.3: Commit**

```bash
git add Assets/Scripts/Character/Ambition/Bindings/RuntimeQueryBinding.cs
git commit -m "feat(ambition): add RuntimeQueryBinding base"
```

---

## Phase 3 — Registries and settings

Static registries serve two roles: (1) load-time SO lookup by GUID for the save layer, (2) editor / dev-tool population. Both registries follow the project's lazy-init pattern (`feedback_lazy_static_registry_pattern.md`) so late-joining clients that bypass `GameLauncher.LaunchSequence` still see populated data.

### Task 13: `AmbitionRegistry` (lazy-init)

**Files:**
- Create: `Assets/Scripts/Character/Ambition/AmbitionRegistry.cs`

- [ ] **Step 13.1: Write the file**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Lazy-init registry of all AmbitionSO assets in the project. Late-joining clients
    /// skip GameLauncher.LaunchSequence; the lazy Get() ensures they still see populated
    /// data (see feedback_lazy_static_registry_pattern.md).
    /// </summary>
    public static class AmbitionRegistry
    {
        private static Dictionary<string, AmbitionSO> _byGuid;
        private static Dictionary<AmbitionSO, string> _toGuid;

        public static AmbitionSO Get(string guid)
        {
            EnsureLoaded();
            return _byGuid.TryGetValue(guid, out var so) ? so : null;
        }

        public static string GetGuid(AmbitionSO so)
        {
            if (so == null) return null;
            EnsureLoaded();
            return _toGuid.TryGetValue(so, out var guid) ? guid : null;
        }

        public static IReadOnlyCollection<AmbitionSO> All
        {
            get
            {
                EnsureLoaded();
                return _byGuid.Values;
            }
        }

        private static void EnsureLoaded()
        {
            if (_byGuid != null) return;
            _byGuid = new Dictionary<string, AmbitionSO>();
            _toGuid = new Dictionary<AmbitionSO, string>();
            // Resources path matches the spec's authored asset layout.
            var all = Resources.LoadAll<AmbitionSO>("Data/Ambitions");
            foreach (var so in all)
            {
                if (so == null) continue;
                // Use the asset's instance ID as a stable key in builds; in the editor
                // we prefer the AssetDatabase GUID (resolved via the editor-only helper
                // in CharacterAmbition save layer). Fall back to name for ID purposes.
                string id = so.name;
                if (string.IsNullOrEmpty(id)) continue;
                _byGuid[id] = so;
                _toGuid[so] = id;
            }
        }

        /// <summary>Test seam — clears cached state so tests can repopulate from a stub.</summary>
        public static void ResetForTests() { _byGuid = null; _toGuid = null; }
    }
}
```

- [ ] **Step 13.2: Refresh, verify zero errors**

- [ ] **Step 13.3: Commit**

```bash
git add Assets/Scripts/Character/Ambition/AmbitionRegistry.cs
git commit -m "feat(ambition): add AmbitionRegistry with lazy-init"
```

### Task 14: `QuestRegistry` (lazy-init)

**Files:**
- Create: `Assets/Scripts/Character/Ambition/QuestRegistry.cs`

- [ ] **Step 14.1: Write the file**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Lazy-init registry of QuestSO assets used by the ambition system. Same pattern
    /// as AmbitionRegistry. Existing IQuest subtypes (BuildingTask, BuyOrder, etc.)
    /// are NOT in this registry — they live in their own job-system pipeline.
    /// </summary>
    public static class QuestRegistry
    {
        private static Dictionary<string, QuestSO> _byGuid;
        private static Dictionary<QuestSO, string> _toGuid;

        public static QuestSO Get(string guid)
        {
            EnsureLoaded();
            return _byGuid.TryGetValue(guid, out var so) ? so : null;
        }

        public static string GetGuid(QuestSO so)
        {
            if (so == null) return null;
            EnsureLoaded();
            return _toGuid.TryGetValue(so, out var guid) ? guid : null;
        }

        public static IReadOnlyCollection<QuestSO> All
        {
            get
            {
                EnsureLoaded();
                return _byGuid.Values;
            }
        }

        private static void EnsureLoaded()
        {
            if (_byGuid != null) return;
            _byGuid = new Dictionary<string, QuestSO>();
            _toGuid = new Dictionary<QuestSO, string>();
            var all = Resources.LoadAll<QuestSO>("Data/Ambitions/Quests");
            foreach (var so in all)
            {
                if (so == null) continue;
                string id = so.name;
                if (string.IsNullOrEmpty(id)) continue;
                _byGuid[id] = so;
                _toGuid[so] = id;
            }
        }

        public static void ResetForTests() { _byGuid = null; _toGuid = null; }
    }
}
```

- [ ] **Step 14.2: Refresh, verify zero errors**

- [ ] **Step 14.3: Commit**

```bash
git add Assets/Scripts/Character/Ambition/QuestRegistry.cs
git commit -m "feat(ambition): add QuestRegistry with lazy-init"
```

### Task 15: `AmbitionSettings` global config SO

**Files:**
- Create: `Assets/Scripts/Character/Ambition/AmbitionSettings.cs`

- [ ] **Step 15.1: Write the SO type**

```csharp
using System.Collections.Generic;
using UnityEngine;
using MWI.Needs;

namespace MWI.Ambition
{
    /// <summary>
    /// Global per-project tuning for the Ambition system. Lives as a single asset under
    /// Assets/Resources/Data/Ambitions/AmbitionSettings.asset; the asset itself is
    /// authored by hand in Phase 13.
    ///
    /// GatingNeeds — the set of NeedSO types whose IsActive() blocks ambition pursuit.
    /// v1: Hunger + Sleep only. Adding to this list does NOT require code changes.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Ambition/AmbitionSettings", fileName = "AmbitionSettings")]
    public class AmbitionSettings : ScriptableObject
    {
        [SerializeField] private List<NeedSO> _gatingNeeds = new();
        public IReadOnlyList<NeedSO> GatingNeeds => _gatingNeeds;

        private static AmbitionSettings _instance;
        public static AmbitionSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<AmbitionSettings>("Data/Ambitions/AmbitionSettings");
                if (_instance == null)
                {
                    Debug.LogError("[AmbitionSettings] No AmbitionSettings asset found at Resources/Data/Ambitions/AmbitionSettings. Create it via the asset menu.");
                }
                return _instance;
            }
        }

        public static void ResetForTests() => _instance = null;
    }
}
```

- [ ] **Step 15.2: Verify `MWI.Needs.NeedSO` resolves**

If the namespace is different in the project, grep:

```bash
grep -rn "class NeedSO" Assets/Scripts | head -3
```

and update the `using` line accordingly.

- [ ] **Step 15.3: Refresh, verify zero errors**

- [ ] **Step 15.4: Commit**

```bash
git add Assets/Scripts/Character/Ambition/AmbitionSettings.cs
git commit -m "feat(ambition): add AmbitionSettings with GatingNeeds list"
```

---

## Phase 4 — `AmbitionQuest` concrete bridge implementation

`AmbitionQuest` is the concrete `IAmbitionStepQuest` (and therefore `IQuest`) — the single class that lives in `CharacterQuestLog` while also being tickable by the BT.

### Task 16: `AmbitionQuest` skeleton + IQuest plumbing

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Quests/AmbitionQuest.cs`

- [ ] **Step 16.1: Inspect existing `IQuest` for the exact members to implement**

Run:

```bash
grep -n "interface IQuest" Assets/Scripts -r
```

then read the file containing it. Note the exact properties/methods/events on `IQuest` and `IQuestTarget`. The plan below assumes the canonical shape from `2026-04-23-quest-system-design.md`; if the actual members differ, adjust the signatures during implementation.

- [ ] **Step 16.2: Write the file**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using MWI.Quests;

namespace MWI.Ambition
{
    /// <summary>
    /// Runtime IQuest produced from a QuestSO when a CharacterAmbition advances onto a
    /// step. Bridges the IQuest data world (CharacterQuestLog, save, sync, HUD) and the
    /// IAmbitionStepQuest behavior world (BT-tickable, context-bound). One instance per
    /// active step.
    /// </summary>
    public class AmbitionQuest : IAmbitionStepQuest
    {
        private readonly QuestSO _so;
        private readonly Character _issuerAndReceiver; // self-issued
        private readonly List<TaskBase> _tasks;        // copies of SO tasks (state-bearing)
        private AmbitionContext _ctx;
        private QuestState _state = QuestState.NotStarted;

        public AmbitionQuest(QuestSO so, Character self, AmbitionContext ctx)
        {
            _so = so;
            _issuerAndReceiver = self;
            _ctx = ctx;
            _tasks = CloneTasksFromSO(so);
        }

        // ── IQuest ─────────────────────────────────────────────────
        public string Title => _so != null ? _so.DisplayName : "(unknown)";
        public string Description => _so != null ? _so.Description : "";
        public Character Issuer => _issuerAndReceiver;
        public Character Receiver => _issuerAndReceiver;
        public QuestState State => _state;
        public bool IsAmbitionStep => true;

        public event Action<IQuest> OnStateChanged;

        public void SetState(QuestState newState)
        {
            if (_state == newState) return;
            _state = newState;
            OnStateChanged?.Invoke(this);
        }

        // ── IAmbitionStepQuest ─────────────────────────────────────
        public void BindContext(AmbitionContext ctx)
        {
            _ctx = ctx;
            foreach (var t in _tasks) t?.Bind(_ctx);
            foreach (var t in _tasks) t?.RegisterCompletionListeners(_issuerAndReceiver, _ctx);
            if (_state == QuestState.NotStarted) SetState(QuestState.Running);
        }

        public TaskStatus TickActiveTasks(Character npc)
        {
            if (_so == null || _tasks.Count == 0) return TaskStatus.Completed;
            switch (_so.Ordering)
            {
                case TaskOrderingMode.Sequential: return TickSequential(npc);
                case TaskOrderingMode.Parallel:   return TickParallel(npc);
                case TaskOrderingMode.AnyOf:      return TickAnyOf(npc);
                default: return TaskStatus.Failed;
            }
        }

        public void Cancel()
        {
            foreach (var t in _tasks)
            {
                t?.UnregisterCompletionListeners(_issuerAndReceiver);
                t?.Cancel();
            }
            SetState(QuestState.Cancelled);
        }

        public void OnControllerSwitching(Character npc, ControllerKind goingTo)
        {
            foreach (var t in _tasks) t?.OnControllerSwitching(npc, goingTo);
        }

        // Read access for save / debug.
        public IReadOnlyList<TaskBase> Tasks => _tasks;
        public QuestSO SourceSO => _so;

        private TaskStatus TickSequential(Character npc)
        {
            for (int i = 0; i < _tasks.Count; i++)
            {
                var t = _tasks[i];
                if (t == null) continue;
                var s = t.Tick(npc, _ctx);
                if (s == TaskStatus.Failed) return TaskStatus.Failed;
                if (s == TaskStatus.Running) return TaskStatus.Running;
                // Completed — move to next task.
            }
            CompleteAndNotify();
            return TaskStatus.Completed;
        }

        private TaskStatus TickParallel(Character npc)
        {
            bool anyRunning = false;
            for (int i = 0; i < _tasks.Count; i++)
            {
                var t = _tasks[i];
                if (t == null) continue;
                var s = t.Tick(npc, _ctx);
                if (s == TaskStatus.Failed) return TaskStatus.Failed;
                if (s == TaskStatus.Running) anyRunning = true;
            }
            if (anyRunning) return TaskStatus.Running;
            CompleteAndNotify();
            return TaskStatus.Completed;
        }

        private TaskStatus TickAnyOf(Character npc)
        {
            for (int i = 0; i < _tasks.Count; i++)
            {
                var t = _tasks[i];
                if (t == null) continue;
                var s = t.Tick(npc, _ctx);
                if (s == TaskStatus.Failed) return TaskStatus.Failed;
                if (s == TaskStatus.Completed)
                {
                    // Cancel all the others.
                    for (int j = 0; j < _tasks.Count; j++)
                        if (j != i && _tasks[j] != null) _tasks[j].Cancel();
                    CompleteAndNotify();
                    return TaskStatus.Completed;
                }
            }
            return TaskStatus.Running;
        }

        private void CompleteAndNotify()
        {
            foreach (var t in _tasks) t?.UnregisterCompletionListeners(_issuerAndReceiver);
            SetState(QuestState.Completed);
        }

        private static List<TaskBase> CloneTasksFromSO(QuestSO so)
        {
            var result = new List<TaskBase>();
            if (so == null) return result;
            foreach (var src in so.Tasks)
            {
                if (src == null) { result.Add(null); continue; }
                // SerializeReference -> JSON round trip preserves type. Cheap clone.
                var json = UnityEngine.JsonUtility.ToJson(src);
                var clone = (TaskBase)System.Activator.CreateInstance(src.GetType());
                UnityEngine.JsonUtility.FromJsonOverwrite(json, clone);
                result.Add(clone);
            }
            return result;
        }
    }
}
```

- [ ] **Step 16.3: Confirm `MWI.Quests.IQuest` member shape matches**

Read the actual `IQuest` interface file (located via Step 16.1) and compare. If `Title` is named `Name` instead, etc., update. **Do not commit until names match exactly** — the compiler is the source of truth here.

- [ ] **Step 16.4: Refresh and resolve any compile errors**

Common adjustments: `QuestState` enum may live under a different namespace; `IsAmbitionStep` may need to be added to `IQuest` itself in Phase 11 (Player UI). For now, mark it as a public AmbitionQuest property only if `IQuest` doesn't already have a generic flag.

- [ ] **Step 16.5: Commit**

```bash
git add Assets/Scripts/Character/Ambition/Quests/AmbitionQuest.cs
git commit -m "feat(ambition): add AmbitionQuest bridging IAmbitionStepQuest -> IQuest"
```

### Task 17: Quest ordering NUnit tests

**Files:**
- Create: `Assets/Tests/EditMode/Ambition/QuestOrderingTests.cs`

- [ ] **Step 17.1: Write the test file**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class QuestOrderingTests
    {
        // Stub task: completes after N ticks.
        private class CountTask : TaskBase
        {
            public int CompleteAfterTicks = 1;
            public int Ticks;
            public bool Cancelled;
            public override void Bind(AmbitionContext ctx) { }
            public override TaskStatus Tick(Character npc, AmbitionContext ctx)
            {
                Ticks++;
                return Ticks >= CompleteAfterTicks ? TaskStatus.Completed : TaskStatus.Running;
            }
            public override void Cancel() { Cancelled = true; }
        }

        // Note: these tests run TickActiveTasks via reflection / private ctor since
        // AmbitionQuest needs a QuestSO. We construct a minimal SO via ScriptableObject.CreateInstance.

        private QuestSO MakeQuest(TaskOrderingMode mode, params TaskBase[] tasks)
        {
            var so = ScriptableObject.CreateInstance<QuestSO>();
            // Reflection injection — QuestSO uses [SerializeField] private fields.
            var taskField = typeof(QuestSO).GetField("_tasks",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            taskField.SetValue(so, new List<TaskBase>(tasks));
            var orderField = typeof(QuestSO).GetField("_ordering",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            orderField.SetValue(so, mode);
            return so;
        }

        [Test]
        public void Sequential_AdvancesTaskByTask()
        {
            var t1 = new CountTask { CompleteAfterTicks = 1 };
            var t2 = new CountTask { CompleteAfterTicks = 2 };
            var so = MakeQuest(TaskOrderingMode.Sequential, t1, t2);
            var q = new AmbitionQuest(so, null, new AmbitionContext());
            q.BindContext(new AmbitionContext());

            Assert.AreEqual(TaskStatus.Running, q.TickActiveTasks(null)); // t1 done, t2 first tick (Running)
            Assert.AreEqual(TaskStatus.Completed, q.TickActiveTasks(null)); // t2 second tick — done
        }

        [Test]
        public void Parallel_CompletesWhenAllDone()
        {
            var t1 = new CountTask { CompleteAfterTicks = 1 };
            var t2 = new CountTask { CompleteAfterTicks = 2 };
            var so = MakeQuest(TaskOrderingMode.Parallel, t1, t2);
            var q = new AmbitionQuest(so, null, new AmbitionContext());
            q.BindContext(new AmbitionContext());

            Assert.AreEqual(TaskStatus.Running, q.TickActiveTasks(null)); // t1 done, t2 still running
            Assert.AreEqual(TaskStatus.Completed, q.TickActiveTasks(null)); // both done
        }

        [Test]
        public void AnyOf_CompletesOnFirstAndCancelsOthers()
        {
            var t1 = new CountTask { CompleteAfterTicks = 1 };
            var t2 = new CountTask { CompleteAfterTicks = 5 };
            var so = MakeQuest(TaskOrderingMode.AnyOf, t1, t2);
            var q = new AmbitionQuest(so, null, new AmbitionContext());
            q.BindContext(new AmbitionContext());

            Assert.AreEqual(TaskStatus.Completed, q.TickActiveTasks(null));
            Assert.IsTrue(t2.Cancelled, "AnyOf should Cancel siblings of the winner.");
        }
    }
}
```

- [ ] **Step 17.2: Run tests, debug any reflection / member-name mismatches**

Use `tests-run` with `class: "MWI.Tests.Ambition.QuestOrderingTests"`. Fix any compile errors (likely around `AmbitionQuest` constructor signature, or the `Character npc` first-arg type if Character lives in a different assembly).

If the EditMode test assembly cannot reference `Character`, change the test's `Tick(null, ctx)` call to use `null as Character`. The CountTask stub doesn't actually use the npc.

- [ ] **Step 17.3: Verify 3/3 pass**

- [ ] **Step 17.4: Commit**

```bash
git add Assets/Tests/EditMode/Ambition/QuestOrderingTests.cs
git commit -m "test(ambition): cover Sequential/Parallel/AnyOf quest ordering"
```

---

## Phase 5 — `CharacterAmbition` core (state machine)

This is the orchestration centerpiece. It owns the `AmbitionInstance`, the `History` list, the public Set/Clear API, and the event surface. It does **not** own behavior driving — that's the BT branch in Phase 9.

### Task 18: `CharacterAmbition` skeleton with subsystem registration

**Files:**
- Create: `Assets/Scripts/Character/Ambition/CharacterAmbition.cs`

- [ ] **Step 18.1: Inspect a reference subsystem for the registration pattern**

Read `Assets/Scripts/Character/CharacterQuestLog/*.cs` (or whichever sibling subsystem is closest in shape — use `grep -l "class CharacterSystem" Assets/Scripts/Character/*.cs` to find the base class). Match its lifecycle hooks (`Awake`, `OnEnable`, `Initialize(Character)`, etc.).

- [ ] **Step 18.2: Write the skeleton**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Per-Character life-goal subsystem. Identical for player and NPC; consumer
    /// flips with controller switch (Character.SwitchToPlayer/NPC).
    /// Server-authoritative — Set/Clear mutations gated by IsServer; replication
    /// via NetworkVariable + ClientRpc fan-out (lands in Phase 7).
    /// </summary>
    public class CharacterAmbition : CharacterSystem
    {
        // ── Active state ───────────────────────────────────────────
        private AmbitionInstance _current;
        public AmbitionInstance Current => _current;
        public bool HasActive => _current != null;
        public float CurrentProgress01 => _current != null ? _current.Progress01 : 0f;

        // ── History ────────────────────────────────────────────────
        private readonly List<CompletedAmbition> _history = new();
        public IReadOnlyList<CompletedAmbition> History => _history;

        // ── Events ─────────────────────────────────────────────────
        public event Action<AmbitionInstance> OnAmbitionSet;
        public event Action<AmbitionInstance, int> OnStepAdvanced; // (instance, newStepIndex)
        public event Action<CompletedAmbition> OnAmbitionCompleted;
        public event Action<CompletedAmbition> OnAmbitionCleared;

        // ── Public API ─────────────────────────────────────────────
        public virtual void SetAmbition(AmbitionSO so, IReadOnlyDictionary<string, object> parameters = null)
        {
            // Server gate lands in Phase 7. For now allow direct call so EditMode tests work.
            DoSetAmbition(so, parameters);
        }

        public virtual void ClearAmbition()
        {
            DoClearAmbition(CompletionReason.ClearedByScript);
        }

        // ── Internal ───────────────────────────────────────────────
        protected void DoSetAmbition(AmbitionSO so, IReadOnlyDictionary<string, object> parameters)
        {
            if (so == null)
            {
                Debug.LogError($"[CharacterAmbition] SetAmbition called with null SO on {name}.");
                return;
            }
            if (!so.ValidateParameters(parameters ?? EmptyDict))
            {
                Debug.LogError($"[CharacterAmbition] SetAmbition aborted: parameter validation failed for {so.name}.");
                return;
            }

            // Replacement: clear current first so it lands in history with ClearedByScript.
            if (_current != null) DoClearAmbition(CompletionReason.ClearedByScript);

            _current = new AmbitionInstance
            {
                SO = so,
                CurrentStepIndex = 0,
                Context = BuildContextFromParameters(so, parameters),
                AssignedDay = ResolveCurrentDay()
            };
            IssueStepQuest(0);
            OnAmbitionSet?.Invoke(_current);
        }

        protected void DoClearAmbition(CompletionReason reason)
        {
            if (_current == null) return;
            var snap = new CompletedAmbition(_current.SO, _current.Context, ResolveCurrentDay(), reason);
            CancelStepQuest();
            _current = null;
            _history.Add(snap);
            if (reason == CompletionReason.Completed) OnAmbitionCompleted?.Invoke(snap);
            else OnAmbitionCleared?.Invoke(snap);
        }

        protected void IssueStepQuest(int stepIndex)
        {
            if (_current == null || _current.SO == null) return;
            var so = _current.SO;
            if (stepIndex < 0 || stepIndex >= so.Quests.Count) return;
            var qso = so.Quests[stepIndex];
            if (qso == null) return;

            var aq = new AmbitionQuest(qso, AsCharacter(), _current.Context);
            _current.CurrentStepQuest = aq;
            _current.CurrentStepIndex = stepIndex;

            aq.OnStateChanged += HandleStepStateChanged;
            aq.BindContext(_current.Context);

            // Add to CharacterQuestLog so player UI / save / sync pick it up.
            var log = AsCharacter()?.CharacterQuestLog;
            log?.AddClaimedQuest(aq);
        }

        protected void CancelStepQuest()
        {
            if (_current?.CurrentStepQuest == null) return;
            var aq = _current.CurrentStepQuest;
            aq.OnStateChanged -= HandleStepStateChanged;
            aq.Cancel();
            var log = AsCharacter()?.CharacterQuestLog;
            log?.RemoveClaimedQuest(aq);
            _current.CurrentStepQuest = null;
        }

        private void HandleStepStateChanged(MWI.Quests.IQuest q)
        {
            if (_current == null || q != _current.CurrentStepQuest) return;
            if (q.State != MWI.Quests.QuestState.Completed) return;

            // Detach the old listener and remove from quest log.
            var aq = _current.CurrentStepQuest;
            aq.OnStateChanged -= HandleStepStateChanged;
            var log = AsCharacter()?.CharacterQuestLog;
            log?.RemoveClaimedQuest(aq);
            _current.CurrentStepQuest = null;

            // Last step? -> Completed transition. Otherwise advance.
            if (_current.CurrentStepIndex >= _current.SO.Quests.Count - 1)
            {
                DoClearAmbition(CompletionReason.Completed);
            }
            else
            {
                int next = _current.CurrentStepIndex + 1;
                IssueStepQuest(next);
                OnStepAdvanced?.Invoke(_current, next);
            }
        }

        // ── Helpers ────────────────────────────────────────────────
        private static readonly Dictionary<string, object> EmptyDict = new();

        protected virtual int ResolveCurrentDay()
        {
            var c = AsCharacter();
            return c?.TimeManager != null ? c.TimeManager.CurrentDay : 0;
        }

        private Character AsCharacter() => GetComponentInParent<Character>();

        private static AmbitionContext BuildContextFromParameters(
            AmbitionSO so, IReadOnlyDictionary<string, object> parameters)
        {
            var ctx = new AmbitionContext();
            if (parameters == null) return ctx;
            foreach (var kvp in parameters)
            {
                try { ctx.Set(kvp.Key, kvp.Value); }
                catch (Exception e) { Debug.LogException(e); }
            }
            return ctx;
        }

        // ── Test seam ──────────────────────────────────────────────
        // Allows EditMode tests to pump state without instantiating a real Character.
        internal void TEST_ForceState(AmbitionInstance instance) { _current = instance; }
        internal void TEST_ForceHistory(IEnumerable<CompletedAmbition> list)
        { _history.Clear(); _history.AddRange(list); }
    }
}
```

- [ ] **Step 18.3: Verify `CharacterSystem` base class shape matches**

Read the `CharacterSystem` base class (run `grep -n "abstract class CharacterSystem\|class CharacterSystem" Assets/Scripts -r`). If it requires specific lifecycle members (e.g. a virtual `Initialize(Character)`), add them.

- [ ] **Step 18.4: Verify `CharacterQuestLog.AddClaimedQuest` / `RemoveClaimedQuest` names**

Run:

```bash
grep -n "AddClaimedQuest\|RemoveClaimedQuest\|public void Claim\|public void Add" Assets/Scripts/Character/CharacterQuestLog/*.cs | head
```

If the actual API uses different names (`Claim(quest)` / `Abandon(quest)` / etc.), adjust `IssueStepQuest` and `CancelStepQuest`. **Do not commit until names match exactly.**

- [ ] **Step 18.5: Refresh, expect compile errors only on Character/CharacterQuestLog member names**

Fix the names per Step 18.4.

- [ ] **Step 18.6: Commit**

```bash
git add Assets/Scripts/Character/Ambition/CharacterAmbition.cs
git commit -m "feat(ambition): add CharacterAmbition state-machine skeleton"
```

### Task 19: State-machine NUnit tests (transition coverage)

**Files:**
- Create: `Assets/Tests/EditMode/Ambition/AmbitionStateMachineTests.cs`

These tests cannot instantiate a full `Character` in EditMode (NetworkBehaviour dependency). We test the state-machine logic by extending `CharacterAmbition` with a test subclass that overrides `IssueStepQuest` and `CancelStepQuest` to skip the `CharacterQuestLog` interaction.

- [ ] **Step 19.1: Write the test file**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class AmbitionStateMachineTests
    {
        private class FakeQuestSO : QuestSO { }
        private class FakeAmbitionSO : AmbitionSO { }

        // Test seam — replaces IssueStepQuest/CancelStepQuest to bypass Character access.
        // Uses a fake quest object instead.
        private class TestableCharacterAmbition : CharacterAmbition
        {
            public List<int> IssuedSteps = new();
            public int Cancellations;

            // Override via reflection access to private methods isn't possible without
            // making them virtual. Mark protected->virtual in the base class for this test.
            // For now: simulate by directly manipulating internal state via TEST_ForceState
            // after each transition checkpoint.
        }

        private static AmbitionSO TwoStepAmbition(out QuestSO q1, out QuestSO q2)
        {
            q1 = ScriptableObject.CreateInstance<FakeQuestSO>();
            q2 = ScriptableObject.CreateInstance<FakeQuestSO>();
            var amb = ScriptableObject.CreateInstance<FakeAmbitionSO>();
            // Inject quest list via reflection on the private _quests field.
            var f = typeof(AmbitionSO).GetField("_quests",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f.SetValue(amb, new List<QuestSO> { q1, q2 });
            return amb;
        }

        [Test]
        public void Initial_State_Is_Inactive()
        {
            var go = new GameObject("ambition_test");
            var amb = go.AddComponent<CharacterAmbition>();
            Assert.IsFalse(amb.HasActive);
            Assert.IsNull(amb.Current);
            Assert.AreEqual(0, amb.History.Count);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Progress01_ReturnsZero_WhenInactive()
        {
            var go = new GameObject("ambition_test");
            var amb = go.AddComponent<CharacterAmbition>();
            Assert.AreEqual(0f, amb.CurrentProgress01);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Progress01_AdvancesAcrossSteps()
        {
            // Simulate state directly via TEST_ForceState to bypass the
            // CharacterQuestLog wiring (which needs a real Character).
            var so = TwoStepAmbition(out _, out _);
            var go = new GameObject("ambition_test");
            var amb = go.AddComponent<CharacterAmbition>();

            var inst = new AmbitionInstance { SO = so, CurrentStepIndex = 0 };
            amb.TEST_ForceState(inst);
            Assert.AreEqual(0f, amb.CurrentProgress01);

            inst.CurrentStepIndex = 1;
            Assert.AreEqual(0.5f, amb.CurrentProgress01);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void History_StartsEmpty_OnFreshAmbition()
        {
            var go = new GameObject("ambition_test");
            var amb = go.AddComponent<CharacterAmbition>();
            Assert.IsEmpty(amb.History);
            Object.DestroyImmediate(go);
        }
    }
}
```

- [ ] **Step 19.2: Run tests**

`tests-run` with `class: "MWI.Tests.Ambition.AmbitionStateMachineTests"`. The full `SetAmbition` happy-path test is deferred to Phase 14 (Play-mode smoke) since it depends on `CharacterQuestLog.AddClaimedQuest`. The four tests here cover the state-property surface.

- [ ] **Step 19.3: Commit**

```bash
git add Assets/Tests/EditMode/Ambition/AmbitionStateMachineTests.cs
git commit -m "test(ambition): cover initial state and Progress01 math"
```

### Task 20: Wire `CharacterAmbition` into `Character.cs`

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`

- [ ] **Step 20.1: Add the `[SerializeField]` field**

In `Character.cs` Sub-Systems region (around line 85, just before the `#endregion`), insert:

```csharp
[SerializeField] private MWI.Ambition.CharacterAmbition _characterAmbition;
```

- [ ] **Step 20.2: Add the property**

In the Properties region (after `CharacterCinematicState` at line 295), insert:

```csharp
public MWI.Ambition.CharacterAmbition CharacterAmbition =>
    TryGet<MWI.Ambition.CharacterAmbition>(out var sAmb) ? sAmb : _characterAmbition;
```

- [ ] **Step 20.3: Add the auto-assign in `Awake`**

In `Awake()` (around line 570, after the existing `if (_characterOrders == null) ...` lines), insert:

```csharp
if (_characterAmbition == null)
    _characterAmbition = GetComponentInChildren<MWI.Ambition.CharacterAmbition>();
```

- [ ] **Step 20.4: Refresh, verify zero compile errors**

- [ ] **Step 20.5: Commit**

```bash
git add Assets/Scripts/Character/Character.cs
git commit -m "feat(ambition): wire CharacterAmbition into Character facade"
```

### Task 21: Add `CharacterAmbition` child GameObject to all Character prefabs

**Files:**
- Modify: every Character prefab under `Assets/Prefabs/Character/`

This is a manual editor step. The MCP tool sequence:

- [ ] **Step 21.1: List Character prefabs**

```bash
find Assets/Prefabs/Character -name "*.prefab" 2>&1
```

- [ ] **Step 21.2: For each Character prefab — open, add child GameObject with CharacterAmbition, save, close**

For each path returned, run this sequence:

1. `assets-prefab-open` with the prefab path.
2. `gameobject-create` with name `Subsystem_Ambition`, parent = root of the prefab. Position/rotation/scale = defaults.
3. `gameobject-component-add` on the new child, component type `MWI.Ambition.CharacterAmbition`.
4. `assets-prefab-save`.
5. `assets-prefab-close`.

After each prefab, run `console-get-logs` and check for missing-script or component-add errors.

- [ ] **Step 21.3: Verify in one prefab that `Character._characterAmbition` auto-resolves**

Open one Character prefab and check the root `Character` component's Inspector — the `_characterAmbition` field should now reference the new child GameObject's `CharacterAmbition` component (auto-assigned via `GetComponentInChildren` on `Awake`, or you can drag it manually for editor-time clarity).

- [ ] **Step 21.4: Commit**

```bash
git add Assets/Prefabs/Character
git commit -m "feat(ambition): add CharacterAmbition child to Character prefabs"
```

---

## Phase 6 — Persistence

### Task 22: Save DTOs

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Save/AmbitionSaveData.cs`
- Create: `Assets/Scripts/Character/Ambition/Save/ContextEntryDTO.cs`
- Create: `Assets/Scripts/Character/Ambition/Save/CompletedAmbitionDTO.cs`
- Create: `Assets/Scripts/Character/Ambition/Save/TaskStateDTO.cs`

- [ ] **Step 22.1: Create Save folder**

`assets-create-folder` parent `Assets/Scripts/Character/Ambition/Save`.

- [ ] **Step 22.2: Write `ContextEntryDTO.cs`**

```csharp
using System;

namespace MWI.Ambition
{
    [Serializable]
    public class ContextEntryDTO
    {
        public string Key;
        public ContextValueKind Kind;
        public string SerializedValue; // CharacterId UUID, asset GUID, primitive string, enum name
    }
}
```

- [ ] **Step 22.3: Write `TaskStateDTO.cs`**

```csharp
using System;

namespace MWI.Ambition
{
    [Serializable]
    public class TaskStateDTO
    {
        public int TaskIndexInQuest;
        public string SerializedState;
    }
}
```

- [ ] **Step 22.4: Write `CompletedAmbitionDTO.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace MWI.Ambition
{
    [Serializable]
    public class CompletedAmbitionDTO
    {
        public string AmbitionSOGuid;
        public List<ContextEntryDTO> FinalContext = new();
        public int CompletedDay;
        public CompletionReason Reason;
    }
}
```

- [ ] **Step 22.5: Write `AmbitionSaveData.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace MWI.Ambition
{
    [Serializable]
    public class AmbitionSaveData
    {
        // Active state. Empty / null when Inactive.
        public string ActiveAmbitionSOGuid;
        public List<ContextEntryDTO> Context = new();
        public int CurrentStepIndex;
        public List<TaskStateDTO> TaskStates = new();
        public int AssignedDay;

        // History (always populated, may be empty).
        public List<CompletedAmbitionDTO> History = new();
    }
}
```

- [ ] **Step 22.6: Refresh, verify zero errors**

- [ ] **Step 22.7: Commit**

```bash
git add Assets/Scripts/Character/Ambition/Save
git commit -m "feat(ambition): add save DTOs"
```

### Task 23: `CharacterAmbition.ICharacterSaveData` Export

**Files:**
- Modify: `Assets/Scripts/Character/Ambition/CharacterAmbition.cs`

- [ ] **Step 23.1: Inspect existing `ICharacterSaveData<T>` shape**

Run:

```bash
grep -n "interface ICharacterSaveData" Assets/Scripts -r
```

Read the interface to see exact method names (`ExportSaveData()` vs `Save()`, etc.) and the priority field.

- [ ] **Step 23.2: Add the interface and implementation**

Modify the class declaration line in `CharacterAmbition.cs` to add the interface:

```csharp
public class CharacterAmbition : CharacterSystem, ICharacterSaveData<AmbitionSaveData>
```

(Adjust namespace and exact name to match Step 23.1 output.)

Then add — at the bottom of the class, before the test seams — the Export method. Adjust the method name + signature to match the actual interface:

```csharp
// ── ICharacterSaveData<AmbitionSaveData> ───────────────────────
public AmbitionSaveData ExportSaveData()
{
    var dto = new AmbitionSaveData();
    foreach (var h in _history) dto.History.Add(SerializeCompleted(h));
    if (_current != null && _current.SO != null)
    {
        dto.ActiveAmbitionSOGuid = AmbitionRegistry.GetGuid(_current.SO);
        dto.Context = SerializeContext(_current.Context);
        dto.CurrentStepIndex = _current.CurrentStepIndex;
        dto.TaskStates = SerializeActiveTasks(_current.CurrentStepQuest);
        dto.AssignedDay = _current.AssignedDay;
    }
    return dto;
}

private static List<ContextEntryDTO> SerializeContext(AmbitionContext ctx)
{
    var list = new List<ContextEntryDTO>();
    if (ctx == null) return list;
    foreach (var kvp in ctx.AsReadOnly())
    {
        var entry = new ContextEntryDTO { Key = kvp.Key };
        var v = kvp.Value;
        if (v == null) { entry.Kind = ContextValueKind.Primitive; entry.SerializedValue = null; }
        else if (v is Character c)
        {
            entry.Kind = ContextValueKind.Character;
            entry.SerializedValue = c.CharacterId;
        }
        else if (v is MWI.WorldSystem.IWorldZone z)
        {
            entry.Kind = ContextValueKind.Zone;
            entry.SerializedValue = z.ZoneGuid;
        }
        else if (v is AmbitionSO amb) { entry.Kind = ContextValueKind.AmbitionSO; entry.SerializedValue = AmbitionRegistry.GetGuid(amb); }
        else if (v is QuestSO qs)     { entry.Kind = ContextValueKind.QuestSO; entry.SerializedValue = QuestRegistry.GetGuid(qs); }
        else if (v is ItemSO it)      { entry.Kind = ContextValueKind.ItemSO; entry.SerializedValue = it.name; }
        else if (v is MWI.Needs.NeedSO ns) { entry.Kind = ContextValueKind.NeedSO; entry.SerializedValue = ns.name; }
        else if (v.GetType().IsEnum)  { entry.Kind = ContextValueKind.Enum; entry.SerializedValue = $"{v.GetType().FullName}|{v}"; }
        else                          { entry.Kind = ContextValueKind.Primitive; entry.SerializedValue = v.ToString(); }
        list.Add(entry);
    }
    return list;
}

private CompletedAmbitionDTO SerializeCompleted(CompletedAmbition src)
{
    return new CompletedAmbitionDTO
    {
        AmbitionSOGuid = AmbitionRegistry.GetGuid(src.SO),
        FinalContext = SerializeContext(src.FinalContext),
        CompletedDay = src.CompletedDay,
        Reason = src.Reason
    };
}

private static List<TaskStateDTO> SerializeActiveTasks(IAmbitionStepQuest stepQuest)
{
    var list = new List<TaskStateDTO>();
    if (stepQuest is not AmbitionQuest aq) return list;
    for (int i = 0; i < aq.Tasks.Count; i++)
    {
        var t = aq.Tasks[i];
        if (t == null) continue;
        var s = t.SerializeState();
        if (string.IsNullOrEmpty(s)) continue;
        list.Add(new TaskStateDTO { TaskIndexInQuest = i, SerializedState = s });
    }
    return list;
}
```

- [ ] **Step 23.3: Verify `Character.CharacterId` and `IWorldZone.ZoneGuid` exist**

Match the actual member names. If `IWorldZone` doesn't have a `ZoneGuid` property, fall back to `IWorldZone.Id` or whatever exists. Do **not** invent a member.

- [ ] **Step 23.4: Refresh, fix names, verify zero errors**

- [ ] **Step 23.5: Commit**

```bash
git add Assets/Scripts/Character/Ambition/CharacterAmbition.cs
git commit -m "feat(ambition): add ICharacterSaveData export"
```

### Task 24: `CharacterAmbition.ICharacterSaveData` Import + deferred-bind queue

**Files:**
- Modify: `Assets/Scripts/Character/Ambition/CharacterAmbition.cs`

- [ ] **Step 24.1: Add deferred-bind queue field**

At the top of the class (near `_history`):

```csharp
// Queue of (key, kind, serializedValue) pairs whose Character refs couldn't resolve at load.
// Retried whenever Character.OnCharacterSpawned fires for any character.
private readonly List<(string key, ContextValueKind kind, string serializedValue, AmbitionContext targetCtx)> _deferredBindings = new();
```

- [ ] **Step 24.2: Subscribe to `OnCharacterSpawned`**

Add to `OnEnable` / `OnDisable` (or whichever lifecycle hooks the project uses). Use the static event on `Character`:

```csharp
private void OnEnable()
{
    Character.OnCharacterSpawned += HandleCharacterSpawned;
}

private void OnDisable()
{
    Character.OnCharacterSpawned -= HandleCharacterSpawned;
}

private void HandleCharacterSpawned(Character c)
{
    if (_deferredBindings.Count == 0) return;
    for (int i = _deferredBindings.Count - 1; i >= 0; i--)
    {
        var d = _deferredBindings[i];
        if (d.kind != ContextValueKind.Character) continue;
        if (d.serializedValue != c.CharacterId) continue;
        d.targetCtx?.Set(d.key, c);
        _deferredBindings.RemoveAt(i);
    }
}
```

- [ ] **Step 24.3: Add the Import method**

```csharp
public void ImportSaveData(AmbitionSaveData dto)
{
    if (dto == null) return;
    _history.Clear();
    foreach (var dtoH in dto.History)
        _history.Add(DeserializeCompleted(dtoH));

    if (string.IsNullOrEmpty(dto.ActiveAmbitionSOGuid))
    {
        _current = null;
        return;
    }

    var so = AmbitionRegistry.Get(dto.ActiveAmbitionSOGuid);
    if (so == null)
    {
        Debug.LogError($"[CharacterAmbition] Saved AmbitionSO '{dto.ActiveAmbitionSOGuid}' not found in registry. Clearing.");
        _current = null;
        return;
    }

    var ctx = DeserializeContext(dto.Context, target: null); // target filled below
    _current = new AmbitionInstance
    {
        SO = so,
        CurrentStepIndex = Mathf.Clamp(dto.CurrentStepIndex, 0, so.Quests.Count - 1),
        Context = ctx,
        AssignedDay = dto.AssignedDay
    };
    // Wire deferred-binding targets to the live context now that it exists.
    for (int i = 0; i < _deferredBindings.Count; i++)
    {
        var d = _deferredBindings[i];
        if (d.targetCtx == null) _deferredBindings[i] = (d.key, d.kind, d.serializedValue, ctx);
    }

    IssueStepQuest(_current.CurrentStepIndex);

    // Restore mid-task state on the freshly issued AmbitionQuest.
    if (_current.CurrentStepQuest is AmbitionQuest aq && dto.TaskStates != null)
    {
        foreach (var ts in dto.TaskStates)
        {
            if (ts.TaskIndexInQuest < 0 || ts.TaskIndexInQuest >= aq.Tasks.Count) continue;
            aq.Tasks[ts.TaskIndexInQuest]?.DeserializeState(ts.SerializedState);
        }
    }

    SafetyCheckOnLoaded();
}

private CompletedAmbition DeserializeCompleted(CompletedAmbitionDTO dto)
{
    var so = AmbitionRegistry.Get(dto.AmbitionSOGuid);
    var ctx = DeserializeContext(dto.FinalContext, target: null);
    return new CompletedAmbition(so, ctx, dto.CompletedDay, dto.Reason);
}

private AmbitionContext DeserializeContext(List<ContextEntryDTO> entries, AmbitionContext target)
{
    var ctx = target ?? new AmbitionContext();
    if (entries == null) return ctx;
    foreach (var e in entries)
    {
        switch (e.Kind)
        {
            case ContextValueKind.Character:
                var c = Character.FindByUUID(e.SerializedValue);
                if (c != null) ctx.Set(e.Key, c);
                else _deferredBindings.Add((e.Key, e.Kind, e.SerializedValue, ctx));
                break;
            case ContextValueKind.Zone:
                var z = MWI.WorldSystem.WorldZoneRegistry.Get(e.SerializedValue);
                if (z != null) ctx.Set(e.Key, z);
                break;
            case ContextValueKind.AmbitionSO:
                var amb = AmbitionRegistry.Get(e.SerializedValue);
                if (amb != null) ctx.Set(e.Key, amb);
                break;
            case ContextValueKind.QuestSO:
                var qs = QuestRegistry.Get(e.SerializedValue);
                if (qs != null) ctx.Set(e.Key, qs);
                break;
            case ContextValueKind.ItemSO:
                // Defer to ItemRegistry if it exists in the project. Otherwise drop with a warning.
                Debug.LogWarning($"[CharacterAmbition] ItemSO context resolution not yet wired for key '{e.Key}'.");
                break;
            case ContextValueKind.NeedSO:
                Debug.LogWarning($"[CharacterAmbition] NeedSO context resolution not yet wired for key '{e.Key}'.");
                break;
            case ContextValueKind.Enum:
                if (!string.IsNullOrEmpty(e.SerializedValue))
                {
                    var parts = e.SerializedValue.Split('|');
                    if (parts.Length == 2)
                    {
                        var t = System.Type.GetType(parts[0]);
                        if (t != null && System.Enum.TryParse(t, parts[1], out var ev))
                            ctx.Set(e.Key, ev);
                    }
                }
                break;
            case ContextValueKind.Primitive:
                if (e.SerializedValue == null) break;
                // Best-effort: int -> float -> bool -> string.
                if (int.TryParse(e.SerializedValue, out var i)) ctx.Set(e.Key, i);
                else if (float.TryParse(e.SerializedValue, out var f)) ctx.Set(e.Key, f);
                else if (bool.TryParse(e.SerializedValue, out var b)) ctx.Set(e.Key, b);
                else ctx.Set(e.Key, e.SerializedValue);
                break;
        }
    }
    return ctx;
}

private void SafetyCheckOnLoaded()
{
    if (_current == null) return;

    var p = new Dictionary<string, object>(_current.Context.AsReadOnly());
    if (!_current.SO.ValidateParameters(p))
    {
        Debug.LogError($"[CharacterAmbition] Saved ambition {_current.SO.name} failed parameter validation on load. Clearing.");
        DoClearAmbition(CompletionReason.ClearedByScript);
    }
}
```

- [ ] **Step 24.4: Refresh, fix any naming mismatches (especially `WorldZoneRegistry.Get`)**

If `WorldZoneRegistry` doesn't exist, replace with the actual zone-lookup utility (grep `class WorldZoneRegistry\|class IWorldZone` to confirm).

- [ ] **Step 24.5: Commit**

```bash
git add Assets/Scripts/Character/Ambition/CharacterAmbition.cs
git commit -m "feat(ambition): add ICharacterSaveData import with deferred-bind queue"
```

### Task 25: Save round-trip NUnit test (no Character, just DTO + context)

**Files:**
- Create: `Assets/Tests/EditMode/Ambition/AmbitionSaveRoundTripTests.cs`

- [ ] **Step 25.1: Write the test file**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class AmbitionSaveRoundTripTests
    {
        [Test]
        public void Context_Primitive_RoundTrip()
        {
            // Fwd: build context with a primitive, serialize via the same code path
            // CharacterAmbition uses, deserialize, expect identical.
            var ctx = new AmbitionContext();
            ctx.Set("Days", 7);
            ctx.Set("Mode", "fight");

            // We re-use the public ContextEntryDTO shape directly. The full pipeline
            // (CharacterAmbition.SerializeContext) requires private access; this test
            // covers the intent of round-tripping primitives via the DTO contract.

            var dtos = new List<ContextEntryDTO>
            {
                new() { Key = "Days", Kind = ContextValueKind.Primitive, SerializedValue = "7" },
                new() { Key = "Mode", Kind = ContextValueKind.Primitive, SerializedValue = "fight" }
            };

            var ctx2 = new AmbitionContext();
            foreach (var e in dtos)
            {
                if (int.TryParse(e.SerializedValue, out var i)) ctx2.Set(e.Key, i);
                else ctx2.Set(e.Key, e.SerializedValue);
            }

            Assert.AreEqual(7, ctx2.Get<int>("Days"));
            Assert.AreEqual("fight", ctx2.Get<string>("Mode"));
        }

        [Test]
        public void CompletedAmbition_DTO_RoundTrip_PreservesDayAndReason()
        {
            var dto = new CompletedAmbitionDTO
            {
                AmbitionSOGuid = "Ambition_Murder",
                CompletedDay = 47,
                Reason = CompletionReason.Completed,
                FinalContext = new List<ContextEntryDTO>()
            };

            // JsonUtility round-trip mirrors what the SaveFileHandler does.
            var json = UnityEngine.JsonUtility.ToJson(dto);
            var dto2 = UnityEngine.JsonUtility.FromJson<CompletedAmbitionDTO>(json);

            Assert.AreEqual("Ambition_Murder", dto2.AmbitionSOGuid);
            Assert.AreEqual(47, dto2.CompletedDay);
            Assert.AreEqual(CompletionReason.Completed, dto2.Reason);
        }
    }
}
```

- [ ] **Step 25.2: Run tests, expect 2/2 pass**

- [ ] **Step 25.3: Commit**

```bash
git add Assets/Tests/EditMode/Ambition/AmbitionSaveRoundTripTests.cs
git commit -m "test(ambition): cover DTO + context round-trip"
```

### Task 26: Register `CharacterAmbition` in `CharacterDataCoordinator`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterPersistence/CharacterDataCoordinator.cs` (or wherever the priority list lives)

- [ ] **Step 26.1: Find the export/import priority list**

```bash
grep -n "ICharacterSaveData\|CharacterQuestLog\|priority" Assets/Scripts/Character/CharacterPersistence/*.cs
```

- [ ] **Step 26.2: Add `CharacterAmbition` to the priority list, between `CharacterQuestLog` and `CharacterSchedule`**

Insert in both Export and Import flows. The exact code depends on the existing pattern — copy the shape used by `CharacterQuestLog` and substitute `CharacterAmbition` + `AmbitionSaveData`.

- [ ] **Step 26.3: Refresh, verify zero errors**

- [ ] **Step 26.4: Commit**

```bash
git add Assets/Scripts/Character/CharacterPersistence
git commit -m "feat(ambition): register CharacterAmbition in CharacterDataCoordinator"
```

### Task 27: Add `Ambition` to `HibernatedNPCData`

**Files:**
- Modify: `Assets/Scripts/World/HibernatedNPCData.cs` (or actual location — grep)

- [ ] **Step 27.1: Find the file**

```bash
grep -rln "class HibernatedNPCData" Assets/Scripts
```

- [ ] **Step 27.2: Add the field**

Add as a sibling of the other `*SaveData` fields:

```csharp
public MWI.Ambition.AmbitionSaveData Ambition;
```

- [ ] **Step 27.3: Plumb in the hibernation export/restore code**

Find the code that builds a `HibernatedNPCData` from a Character (search for other `*SaveData` assignments in the same area). Add:

```csharp
data.Ambition = character.CharacterAmbition?.ExportSaveData();
```

And in the restore path, pass it back:

```csharp
character.CharacterAmbition?.ImportSaveData(data.Ambition);
```

- [ ] **Step 27.4: Refresh, verify zero errors**

- [ ] **Step 27.5: Commit**

```bash
git add Assets/Scripts/World/HibernatedNPCData.cs Assets/Scripts/World
git commit -m "feat(ambition): persist ambition state across hibernation"
```

---

## Phase 7 — Networking

### Task 28: `NetworkAmbitionSnapshot` INetworkSerializable

**Files:**
- Create: `Assets/Scripts/Character/Ambition/NetworkAmbitionSnapshot.cs`

- [ ] **Step 28.1: Write the file**

```csharp
using Unity.Collections;
using Unity.Netcode;

namespace MWI.Ambition
{
    /// <summary>
    /// Compact, always-on replicated state of a Character's active ambition.
    /// Read by everyone (so other players' UI can show their visible ambitions in
    /// the dev inspector); written only by the server. The full DTO (with context,
    /// task states, history) is fanned out via ClientRpc on demand.
    /// </summary>
    public struct NetworkAmbitionSnapshot : INetworkSerializable, System.IEquatable<NetworkAmbitionSnapshot>
    {
        public bool HasActive;
        public FixedString64Bytes AmbitionSOGuid;
        public int CurrentStepIndex;
        public int TotalSteps;
        public float Progress01;
        public bool OverridesSchedule;

        public static NetworkAmbitionSnapshot Inactive => new()
        {
            HasActive = false,
            AmbitionSOGuid = default,
            CurrentStepIndex = 0,
            TotalSteps = 0,
            Progress01 = 0f,
            OverridesSchedule = false
        };

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref HasActive);
            s.SerializeValue(ref AmbitionSOGuid);
            s.SerializeValue(ref CurrentStepIndex);
            s.SerializeValue(ref TotalSteps);
            s.SerializeValue(ref Progress01);
            s.SerializeValue(ref OverridesSchedule);
        }

        public bool Equals(NetworkAmbitionSnapshot o)
            => HasActive == o.HasActive
            && AmbitionSOGuid.Equals(o.AmbitionSOGuid)
            && CurrentStepIndex == o.CurrentStepIndex
            && TotalSteps == o.TotalSteps
            && Mathf.Approximately(Progress01, o.Progress01)
            && OverridesSchedule == o.OverridesSchedule;
    }
}
```

- [ ] **Step 28.2: Add `using UnityEngine;` for `Mathf.Approximately`**

Insert at the top: `using UnityEngine;`.

- [ ] **Step 28.3: Refresh, verify zero errors**

- [ ] **Step 28.4: Commit**

```bash
git add Assets/Scripts/Character/Ambition/NetworkAmbitionSnapshot.cs
git commit -m "feat(ambition): add NetworkAmbitionSnapshot INetworkSerializable"
```

### Task 29: NetworkVariable on `CharacterAmbition` + ServerRpc/ClientRpc

**Files:**
- Modify: `Assets/Scripts/Character/Ambition/CharacterAmbition.cs`

`CharacterAmbition` is a `CharacterSystem` (not directly `NetworkBehaviour`). The pattern other systems use: their parent `Character` is the NetworkBehaviour; the subsystem talks to the network through helpers. Read `Assets/Scripts/Character/CharacterQuestLog/*.cs` for the canonical pattern, then mirror.

- [ ] **Step 29.1: Decide approach by inspecting `CharacterQuestLog`**

```bash
grep -n "NetworkVariable\|ServerRpc\|ClientRpc" Assets/Scripts/Character/CharacterQuestLog/*.cs | head -20
```

If `CharacterQuestLog` itself extends `NetworkBehaviour` directly (despite being a subsystem), follow the same — make `CharacterAmbition` extend `NetworkBehaviour` (or the project's wrapping helper). If it routes through `Character`, do the same.

- [ ] **Step 29.2: Update class declaration to NetworkBehaviour-aware base**

This will commonly look like:

```csharp
public class CharacterAmbition : CharacterSystem, ICharacterSaveData<AmbitionSaveData>
```

If `CharacterSystem` already extends `NetworkBehaviour`, you're done. If not, change to:

```csharp
public class CharacterAmbition : NetworkBehaviour, ICharacterSaveData<AmbitionSaveData>
```

(Rip out any `CharacterSystem` references if it's not a NetworkBehaviour. Inspect what `CharacterSystem` does — if it just provides `Character Owner` access, replicate that with `GetComponentInParent<Character>()`.)

- [ ] **Step 29.3: Add the NetworkVariable**

In the field block:

```csharp
public NetworkVariable<NetworkAmbitionSnapshot> Snapshot = new(
    NetworkAmbitionSnapshot.Inactive,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server);
```

- [ ] **Step 29.4: Update `DoSetAmbition` and `DoClearAmbition` to write the snapshot**

After successful `_current` change in `DoSetAmbition`, write:

```csharp
Snapshot.Value = BuildSnapshot();
```

After `_current = null` in `DoClearAmbition`, write:

```csharp
Snapshot.Value = NetworkAmbitionSnapshot.Inactive;
```

After `OnStepAdvanced` invocation in `HandleStepStateChanged`, also write:

```csharp
Snapshot.Value = BuildSnapshot();
```

Add the helper:

```csharp
private NetworkAmbitionSnapshot BuildSnapshot()
{
    if (_current == null || _current.SO == null) return NetworkAmbitionSnapshot.Inactive;
    return new NetworkAmbitionSnapshot
    {
        HasActive = true,
        AmbitionSOGuid = AmbitionRegistry.GetGuid(_current.SO) ?? string.Empty,
        CurrentStepIndex = _current.CurrentStepIndex,
        TotalSteps = _current.TotalSteps,
        Progress01 = _current.Progress01,
        OverridesSchedule = _current.SO.OverridesSchedule
    };
}
```

- [ ] **Step 29.5: Add ServerRpc / route public Set/Clear via RPC for non-server callers**

Replace the existing `SetAmbition` / `ClearAmbition` with:

```csharp
public override void SetAmbition(AmbitionSO so, IReadOnlyDictionary<string, object> parameters = null)
{
    if (IsServer)
    {
        DoSetAmbition(so, parameters);
        return;
    }
    var guid = AmbitionRegistry.GetGuid(so);
    if (string.IsNullOrEmpty(guid))
    {
        Debug.LogError($"[CharacterAmbition] SetAmbition: SO {so?.name} has no registry GUID.");
        return;
    }
    SetAmbitionServerRpc(guid);
}

[ServerRpc(RequireOwnership = false)]
private void SetAmbitionServerRpc(FixedString64Bytes guid)
{
    var so = AmbitionRegistry.Get(guid.ToString());
    if (so == null)
    {
        Debug.LogError($"[CharacterAmbition] ServerRpc: unknown SO GUID {guid}.");
        return;
    }
    DoSetAmbition(so, null);
}

public override void ClearAmbition()
{
    if (IsServer) { DoClearAmbition(CompletionReason.ClearedByScript); return; }
    ClearAmbitionServerRpc();
}

[ServerRpc(RequireOwnership = false)]
private void ClearAmbitionServerRpc()
{
    DoClearAmbition(CompletionReason.ClearedByScript);
}
```

(Note: the parameter dict can't cleanly cross the wire — most ambition assignments today come from server-side scripts or the dev inspector, both of which run server-side. Client-side parameter assignment is deferred — log a warning and ignore parameters in the client Rpc path.)

- [ ] **Step 29.6: Add ClientRpc full-snapshot fan-out (for fresh joiners)**

Add these RPC pair:

```csharp
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    if (IsServer && _current != null) Snapshot.Value = BuildSnapshot();
}

[ClientRpc]
public void RequestFullSyncClientRpc(ClientRpcParams rpcParams = default)
{
    // Receiving a request to push our full state. Client side: noop. Server side
    // would respond with a fan-out — but the snapshot already covers the visible
    // surface for v1; full DTO sync lives on the dev-inspector path (Task 32).
}
```

- [ ] **Step 29.7: Refresh, verify zero compile errors**

Common fixes: `IsServer` requires the type to be a `NetworkBehaviour`, so Step 29.2 must be correct. If `RequireOwnership = false` is rejected, drop it and let the server gate handle it.

- [ ] **Step 29.8: Commit**

```bash
git add Assets/Scripts/Character/Ambition/CharacterAmbition.cs
git commit -m "feat(ambition): server-authoritative Set/Clear with NetworkVariable snapshot"
```

### Task 30: History on-demand RPC

**Files:**
- Modify: `Assets/Scripts/Character/Ambition/CharacterAmbition.cs`

History is bigger than the snapshot and changes rarely. We don't push it on every change — clients request it explicitly (dev inspector, dialogue trigger).

- [ ] **Step 30.1: Add a small DTO for the wire**

In `CharacterAmbition.cs` (or a new file `Assets/Scripts/Character/Ambition/Save/HistoryNetDTO.cs`):

```csharp
using System;
using Unity.Collections;
using Unity.Netcode;

namespace MWI.Ambition
{
    public struct HistoryEntryNet : INetworkSerializable
    {
        public FixedString64Bytes AmbitionSOGuid;
        public int CompletedDay;
        public int Reason; // CompletionReason cast to int

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref AmbitionSOGuid);
            s.SerializeValue(ref CompletedDay);
            s.SerializeValue(ref Reason);
        }
    }
}
```

- [ ] **Step 30.2: Add the request RPC and the response RPC**

Append in `CharacterAmbition.cs`:

```csharp
[ServerRpc(RequireOwnership = false)]
public void RequestHistoryServerRpc(ServerRpcParams rpcParams = default)
{
    var sender = rpcParams.Receive.SenderClientId;
    var arr = new HistoryEntryNet[_history.Count];
    for (int i = 0; i < _history.Count; i++)
    {
        arr[i] = new HistoryEntryNet
        {
            AmbitionSOGuid = AmbitionRegistry.GetGuid(_history[i].SO) ?? string.Empty,
            CompletedDay = _history[i].CompletedDay,
            Reason = (int)_history[i].Reason
        };
    }
    var clientParams = new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { sender } }
    };
    DeliverHistoryClientRpc(arr, clientParams);
}

public event System.Action<HistoryEntryNet[]> OnHistoryDelivered;

[ClientRpc]
private void DeliverHistoryClientRpc(HistoryEntryNet[] entries, ClientRpcParams rpcParams = default)
{
    OnHistoryDelivered?.Invoke(entries);
}
```

- [ ] **Step 30.3: Refresh, verify zero errors**

- [ ] **Step 30.4: Commit**

```bash
git add Assets/Scripts/Character/Ambition/CharacterAmbition.cs Assets/Scripts/Character/Ambition/Save/HistoryNetDTO.cs
git commit -m "feat(ambition): on-demand history RPC for dev inspector"
```

---

## Phase 8 — Task primitives

### Task 31: `CharacterGoap.RegisterTransientGoal` / `UnregisterTransientGoal`

**Files:**
- Modify: `Assets/Scripts/AI/GOAP/CharacterGoapController.cs`

Pattern B tasks need to inject a transient `GoapGoal` into the planner, and the BT branch above (priority 5.5) needs to fall through so GOAP at priority 6 picks it up.

- [ ] **Step 31.1: Read existing controller**

```bash
grep -n "GoapGoal\|public.*Goal\|RegisterGoal" Assets/Scripts/AI/GOAP/CharacterGoapController.cs | head -20
```

- [ ] **Step 31.2: Add the transient pool**

In `CharacterGoapController`, add:

```csharp
private readonly System.Collections.Generic.List<MWI.AI.GoapGoal> _transientGoals = new();

public void RegisterTransientGoal(MWI.AI.GoapGoal goal)
{
    if (goal == null || _transientGoals.Contains(goal)) return;
    _transientGoals.Add(goal);
}

public void UnregisterTransientGoal(MWI.AI.GoapGoal goal)
{
    if (goal == null) return;
    _transientGoals.Remove(goal);
}
```

- [ ] **Step 31.3: Mix the transient pool into the existing goal-collection method**

Find the method that collects active goals (the one called by the GOAP planner — usually returns `IEnumerable<GoapGoal>` from active needs). Modify it to also include `_transientGoals`. Keep iteration order: needs first, then transient (so a hunger goal can preempt an ambition-driven gather goal).

- [ ] **Step 31.4: Refresh, verify zero errors**

- [ ] **Step 31.5: Commit**

```bash
git add Assets/Scripts/AI/GOAP/CharacterGoapController.cs
git commit -m "feat(goap): expose transient-goal injection for ambition tasks"
```

### Task 32: `Task_KillCharacter` (Pattern A)

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Tasks/Task_KillCharacter.cs`

- [ ] **Step 32.1: Write the file**

```csharp
using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Pattern A — drives an attack action through Character.CharacterCombat.
    /// Bound parameter: Target (Character). Completion: Target.OnDeath fires.
    /// </summary>
    [Serializable]
    public class Task_KillCharacter : TaskBase
    {
        [UnityEngine.SerializeReference] public TaskParameterBinding<Character> Target;

        private Character _resolvedTarget;
        private Character _subscribedTarget;
        private bool _completed;
        private bool _attackEnqueued;

        public override void Bind(AmbitionContext ctx)
        {
            _resolvedTarget = Target?.Resolve(ctx);
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (_completed) return TaskStatus.Completed;
            if (_resolvedTarget == null) return TaskStatus.Running;
            if (_resolvedTarget.IsAlive() == false) { _completed = true; return TaskStatus.Completed; }
            if (_attackEnqueued && npc.CharacterActions.CurrentAction != null)
                return TaskStatus.Running;

            // Idempotent: only enqueue if no current action targets this Character.
            // Action class name is project-specific — verify with grep before committing.
            // CharacterAction_Attack(target) is a placeholder for the real attack action.
            // If a CharacterAction_AttackTarget already exists, use it; otherwise fall back to
            // npc.CharacterCombat.AttackTarget(_resolvedTarget) directly.
            npc.CharacterCombat?.RequestAttack(_resolvedTarget);
            _attackEnqueued = true;
            return TaskStatus.Running;
        }

        public override void Cancel()
        {
            UnregisterCompletionListeners(_subscribedTarget);
        }

        public override void RegisterCompletionListeners(Character npc, AmbitionContext ctx)
        {
            if (_resolvedTarget == null) return;
            _subscribedTarget = _resolvedTarget;
            _subscribedTarget.OnDeath += HandleTargetDeath;
        }

        public override void UnregisterCompletionListeners(Character npc)
        {
            if (_subscribedTarget == null) return;
            _subscribedTarget.OnDeath -= HandleTargetDeath;
            _subscribedTarget = null;
        }

        public override void OnControllerSwitching(Character npc, ControllerKind goingTo)
        {
            // Cancel any in-flight attack action so the new controller starts clean.
            if (_attackEnqueued && npc?.CharacterActions != null)
            {
                npc.CharacterActions.CancelCurrentIfTargeting(_resolvedTarget);
            }
            _attackEnqueued = false;
        }

        private void HandleTargetDeath(Character _) => _completed = true;
    }
}
```

- [ ] **Step 32.2: Verify `CharacterCombat.RequestAttack` and `CharacterActions.CancelCurrentIfTargeting` names**

Grep:

```bash
grep -n "public void Attack\|public void Request\|CancelCurrent" Assets/Scripts/Character/CharacterCombat/*.cs Assets/Scripts/Character/CharacterActions/*.cs
```

Substitute the actual names. If `CancelCurrentIfTargeting` doesn't exist, replace with the simpler `npc.CharacterActions.Cancel(npc.CharacterActions.CurrentAction)`. If neither exists, log a warning and document a follow-up.

- [ ] **Step 32.3: Refresh, fix names, verify zero errors**

- [ ] **Step 32.4: Commit**

```bash
git add Assets/Scripts/Character/Ambition/Tasks/Task_KillCharacter.cs
git commit -m "feat(ambition): Task_KillCharacter (Pattern A)"
```

### Task 33: `Task_TalkToCharacter` (Pattern A)

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Tasks/Task_TalkToCharacter.cs`

- [ ] **Step 33.1: Write the file**

```csharp
using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Pattern A — drives a dialogue interaction. Bound parameter: Target. Completion:
    /// CharacterInteraction.OnInteractionEnded fires for the bound target.
    /// </summary>
    [Serializable]
    public class Task_TalkToCharacter : TaskBase
    {
        [UnityEngine.SerializeReference] public TaskParameterBinding<Character> Target;

        private Character _resolvedTarget;
        private bool _completed;
        private bool _interactionEnqueued;
        private Character _subscribedSelf;

        public override void Bind(AmbitionContext ctx)
        {
            _resolvedTarget = Target?.Resolve(ctx);
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (_completed) return TaskStatus.Completed;
            if (_resolvedTarget == null) return TaskStatus.Running;
            if (_interactionEnqueued && npc.CharacterInteraction.IsInteracting)
                return TaskStatus.Running;

            // Idempotent: only enqueue if not already interacting.
            npc.CharacterInteraction?.RequestStartInteraction(_resolvedTarget);
            _interactionEnqueued = true;
            return TaskStatus.Running;
        }

        public override void Cancel() => UnregisterCompletionListeners(null);

        public override void RegisterCompletionListeners(Character npc, AmbitionContext ctx)
        {
            if (npc?.CharacterInteraction == null) return;
            _subscribedSelf = npc;
            _subscribedSelf.CharacterInteraction.OnInteractionEnded += HandleInteractionEnded;
        }

        public override void UnregisterCompletionListeners(Character npc)
        {
            if (_subscribedSelf?.CharacterInteraction == null) return;
            _subscribedSelf.CharacterInteraction.OnInteractionEnded -= HandleInteractionEnded;
            _subscribedSelf = null;
        }

        public override void OnControllerSwitching(Character npc, ControllerKind goingTo) { /* dialogue is its own state machine */ }

        private void HandleInteractionEnded(Character partner)
        {
            if (partner == _resolvedTarget) _completed = true;
        }
    }
}
```

- [ ] **Step 33.2: Verify `CharacterInteraction.OnInteractionEnded` and `RequestStartInteraction` member names**

```bash
grep -n "OnInteractionEnded\|public.*Interaction\|RequestStart\|StartInteraction" Assets/Scripts/Character/CharacterInteraction/*.cs
```

Substitute. If `RequestStartInteraction` doesn't exist, build the action manually: `npc.CharacterActions.Enqueue(new CharacterStartInteraction(_resolvedTarget))` — using the actual class name.

- [ ] **Step 33.3: Refresh, fix, commit**

```bash
git add Assets/Scripts/Character/Ambition/Tasks/Task_TalkToCharacter.cs
git commit -m "feat(ambition): Task_TalkToCharacter (Pattern A)"
```

### Task 34: `Task_HarvestTarget` (Pattern A)

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Tasks/Task_HarvestTarget.cs`

- [ ] **Step 34.1: Write the file**

```csharp
using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Pattern A — drives a harvest action on the bound Harvestable. Completion:
    /// Harvestable.IsDepleted flips true (listener on Harvestable.OnStateChanged).
    /// </summary>
    [Serializable]
    public class Task_HarvestTarget : TaskBase
    {
        [UnityEngine.SerializeReference] public TaskParameterBinding<MWI.WorldSystem.Harvestable> Target;

        private MWI.WorldSystem.Harvestable _resolved;
        private bool _completed;
        private bool _enqueued;
        private MWI.WorldSystem.Harvestable _subscribed;

        public override void Bind(AmbitionContext ctx)
        {
            _resolved = Target?.Resolve(ctx);
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (_completed) return TaskStatus.Completed;
            if (_resolved == null) return TaskStatus.Running;
            if (_resolved.IsDepleted) { _completed = true; return TaskStatus.Completed; }
            if (_enqueued && npc.CharacterActions.CurrentAction != null) return TaskStatus.Running;

            npc.CharacterActions.Enqueue(new CharacterHarvestAction(_resolved));
            _enqueued = true;
            return TaskStatus.Running;
        }

        public override void Cancel() => UnregisterCompletionListeners(null);

        public override void RegisterCompletionListeners(Character npc, AmbitionContext ctx)
        {
            if (_resolved == null) return;
            _subscribed = _resolved;
            _subscribed.OnStateChanged += HandleStateChanged;
        }

        public override void UnregisterCompletionListeners(Character npc)
        {
            if (_subscribed == null) return;
            _subscribed.OnStateChanged -= HandleStateChanged;
            _subscribed = null;
        }

        public override void OnControllerSwitching(Character npc, ControllerKind goingTo)
        {
            if (_enqueued && npc?.CharacterActions?.CurrentAction is CharacterHarvestAction)
                npc.CharacterActions.CancelCurrent();
            _enqueued = false;
        }

        private void HandleStateChanged(MWI.WorldSystem.Harvestable h)
        {
            if (h.IsDepleted) _completed = true;
        }
    }
}
```

- [ ] **Step 34.2: Verify `CharacterHarvestAction`, `CharacterActions.CancelCurrent`, `Harvestable.OnStateChanged` exist**

```bash
grep -rn "class CharacterHarvestAction\|class HarvestAction\|CancelCurrent\|OnStateChanged" Assets/Scripts | head
```

Substitute exact names. If `CharacterHarvestAction` lives in a different namespace, add the appropriate `using` line.

- [ ] **Step 34.3: Refresh, fix, commit**

```bash
git add Assets/Scripts/Character/Ambition/Tasks/Task_HarvestTarget.cs
git commit -m "feat(ambition): Task_HarvestTarget (Pattern A)"
```

### Task 35: `Task_MoveToZone` (Pattern B)

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Tasks/Task_MoveToZone.cs`

- [ ] **Step 35.1: Write the file**

```csharp
using System;
using MWI.AI;

namespace MWI.Ambition
{
    /// <summary>
    /// Pattern B — injects a transient GOAP goal "in target zone" and lets GOAP plan
    /// the movement. Completion: NPC transform inside the zone radius.
    /// </summary>
    [Serializable]
    public class Task_MoveToZone : TaskBase
    {
        [UnityEngine.SerializeReference] public TaskParameterBinding<MWI.WorldSystem.IWorldZone> Zone;

        private MWI.WorldSystem.IWorldZone _resolved;
        private GoapGoal _goal;
        private Character _injected;

        public override void Bind(AmbitionContext ctx)
        {
            _resolved = Zone?.Resolve(ctx);
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (_resolved == null) return TaskStatus.Running;

            if (IsInZone(npc, _resolved))
            {
                if (_goal != null) DropGoal();
                return TaskStatus.Completed;
            }

            if (_goal == null)
            {
                _goal = new GoapGoal(name: $"InZone_{_resolved.GetType().Name}", priority: 5);
                _goal.DesiredState[$"inZone_{_resolved.GetHashCode()}"] = true;
                npc.CharacterGoap?.RegisterTransientGoal(_goal);
                _injected = npc;
            }
            return TaskStatus.Running;
        }

        public override void Cancel() => DropGoal();

        public override void OnControllerSwitching(Character npc, ControllerKind goingTo)
        {
            if (goingTo == ControllerKind.Player) DropGoal();
        }

        private void DropGoal()
        {
            if (_goal != null && _injected?.CharacterGoap != null)
                _injected.CharacterGoap.UnregisterTransientGoal(_goal);
            _goal = null;
            _injected = null;
        }

        private static bool IsInZone(Character npc, MWI.WorldSystem.IWorldZone zone)
        {
            if (npc == null || zone == null) return false;
            var c = (UnityEngine.Vector3)zone.Center;
            var d = npc.transform.position - c;
            return d.sqrMagnitude <= zone.Radius * zone.Radius;
        }
    }
}
```

- [ ] **Step 35.2: Verify `IWorldZone.Center` / `Radius` member names**

```bash
grep -n "interface IWorldZone\|class.*Zone" Assets/Scripts/World -r | head
grep -n "Center\|Radius" Assets/Scripts/World/IWorldZone*.cs 2>&1 | head
```

If the actual properties are e.g. `Position` + `Bounds.size`, adjust accordingly.

- [ ] **Step 35.3: Refresh, fix, commit**

```bash
git add Assets/Scripts/Character/Ambition/Tasks/Task_MoveToZone.cs
git commit -m "feat(ambition): Task_MoveToZone (Pattern B)"
```

### Task 36: `Task_GatherItem` (Pattern B)

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Tasks/Task_GatherItem.cs`

- [ ] **Step 36.1: Write the file**

```csharp
using System;
using MWI.AI;

namespace MWI.Ambition
{
    /// <summary>
    /// Pattern B — gather Count instances of Item into the NPC's inventory via GOAP.
    /// Completion: inventory check finds at least Count.
    /// </summary>
    [Serializable]
    public class Task_GatherItem : TaskBase
    {
        [UnityEngine.SerializeReference] public TaskParameterBinding<ItemSO> Item;
        public int Count = 1;

        private ItemSO _resolved;
        private GoapGoal _goal;
        private Character _injected;

        public override void Bind(AmbitionContext ctx)
        {
            _resolved = Item?.Resolve(ctx);
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (_resolved == null) return TaskStatus.Running;
            int have = npc.CharacterEquipment?.GetInventory()?.CountOf(_resolved) ?? 0;
            if (have >= Count)
            {
                if (_goal != null) DropGoal();
                return TaskStatus.Completed;
            }

            if (_goal == null)
            {
                _goal = new GoapGoal(name: $"Gather_{_resolved.name}_{Count}", priority: 5);
                _goal.DesiredState[$"hasItem_{_resolved.name}"] = true;
                npc.CharacterGoap?.RegisterTransientGoal(_goal);
                _injected = npc;
            }
            return TaskStatus.Running;
        }

        public override string SerializeState()
        {
            // Persist the count we're gathering toward. Allow restore-after-load to
            // continue from the same target count.
            return Count.ToString();
        }

        public override void DeserializeState(string s)
        {
            if (int.TryParse(s, out var n)) Count = n;
        }

        public override void Cancel() => DropGoal();

        public override void OnControllerSwitching(Character npc, ControllerKind goingTo)
        {
            if (goingTo == ControllerKind.Player) DropGoal();
        }

        private void DropGoal()
        {
            if (_goal != null && _injected?.CharacterGoap != null)
                _injected.CharacterGoap.UnregisterTransientGoal(_goal);
            _goal = null;
            _injected = null;
        }
    }
}
```

- [ ] **Step 36.2: Verify `Inventory.CountOf` exists**

```bash
grep -n "CountOf\|GetCount\|public int Count" Assets/Scripts/Items/Inventory*.cs 2>&1 | head
```

Substitute with the actual count-by-SO API.

- [ ] **Step 36.3: Refresh, fix, commit**

```bash
git add Assets/Scripts/Character/Ambition/Tasks/Task_GatherItem.cs
git commit -m "feat(ambition): Task_GatherItem (Pattern B) with mid-task save"
```

### Task 37: `Task_DeliverItem` (Pattern B)

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Tasks/Task_DeliverItem.cs`

- [ ] **Step 37.1: Write the file**

```csharp
using System;
using MWI.AI;

namespace MWI.Ambition
{
    /// <summary>
    /// Pattern B — recipient should hold at least one of Item, originating from this
    /// NPC. GOAP plans gather + travel + give. Completion: recipient inventory check.
    /// </summary>
    [Serializable]
    public class Task_DeliverItem : TaskBase
    {
        [UnityEngine.SerializeReference] public TaskParameterBinding<ItemSO> Item;
        [UnityEngine.SerializeReference] public TaskParameterBinding<Character> Recipient;

        private ItemSO _itemResolved;
        private Character _recipientResolved;
        private GoapGoal _goal;
        private Character _injected;

        public override void Bind(AmbitionContext ctx)
        {
            _itemResolved = Item?.Resolve(ctx);
            _recipientResolved = Recipient?.Resolve(ctx);
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (_itemResolved == null || _recipientResolved == null) return TaskStatus.Running;
            int recipientHas = _recipientResolved.CharacterEquipment?.GetInventory()?.CountOf(_itemResolved) ?? 0;
            if (recipientHas >= 1)
            {
                if (_goal != null) DropGoal();
                return TaskStatus.Completed;
            }

            if (_goal == null)
            {
                _goal = new GoapGoal(name: $"Deliver_{_itemResolved.name}_to_{_recipientResolved.CharacterName}", priority: 5);
                _goal.DesiredState[$"delivered_{_itemResolved.name}_{_recipientResolved.CharacterId}"] = true;
                npc.CharacterGoap?.RegisterTransientGoal(_goal);
                _injected = npc;
            }
            return TaskStatus.Running;
        }

        public override void Cancel() => DropGoal();

        public override void OnControllerSwitching(Character npc, ControllerKind goingTo)
        {
            if (goingTo == ControllerKind.Player) DropGoal();
        }

        private void DropGoal()
        {
            if (_goal != null && _injected?.CharacterGoap != null)
                _injected.CharacterGoap.UnregisterTransientGoal(_goal);
            _goal = null;
            _injected = null;
        }
    }
}
```

- [ ] **Step 37.2: Refresh, verify, commit**

```bash
git add Assets/Scripts/Character/Ambition/Tasks/Task_DeliverItem.cs
git commit -m "feat(ambition): Task_DeliverItem (Pattern B)"
```

### Task 38: `Task_WaitDays` (listener-only)

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Tasks/Task_WaitDays.cs`

- [ ] **Step 38.1: Write the file**

```csharp
using System;

namespace MWI.Ambition
{
    /// <summary>
    /// No driver — just waits for the calendar to advance. Listener-only completion via
    /// TimeManager.OnDayChanged.
    /// </summary>
    [Serializable]
    public class Task_WaitDays : TaskBase
    {
        public int Days = 1;
        private int _startDay = -1;
        private bool _completed;

        public override void Bind(AmbitionContext ctx) { /* nothing to resolve */ }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (_completed) return TaskStatus.Completed;
            if (_startDay < 0)
            {
                _startDay = npc.TimeManager?.CurrentDay ?? 0;
                return TaskStatus.Running;
            }
            int now = npc.TimeManager?.CurrentDay ?? _startDay;
            if (now - _startDay >= Days) { _completed = true; return TaskStatus.Completed; }
            return TaskStatus.Running;
        }

        public override void Cancel() { _completed = true; }

        public override string SerializeState() => _startDay.ToString();
        public override void DeserializeState(string s) { int.TryParse(s, out _startDay); }
    }
}
```

- [ ] **Step 38.2: Refresh, verify, commit**

```bash
git add Assets/Scripts/Character/Ambition/Tasks/Task_WaitDays.cs
git commit -m "feat(ambition): Task_WaitDays (listener-only)"
```

---

## Phase 9 — Behaviour Tree integration

### Task 39: `BTCond_CanPursueAmbition`

**Files:**
- Create: `Assets/Scripts/AI/BehaviourTree/Conditions/BTCond_CanPursueAmbition.cs`
- Create: `Assets/Tests/EditMode/Ambition/BTCondCanPursueAmbitionTests.cs`

- [ ] **Step 39.1: Read sibling condition for the pattern**

```bash
grep -l "class BTCond_HasScheduledActivity\|abstract class BTCondition\|abstract class BTCond" Assets/Scripts/AI/BehaviourTree -r
```

Mirror its lifecycle (`Evaluate(Character npc) -> bool`).

- [ ] **Step 39.2: Write the condition**

```csharp
using MWI.Ambition;
using MWI.Schedule;

namespace MWI.AI
{
    /// <summary>
    /// True iff the Character has an active ambition AND no important need is currently
    /// active in GOAP AND (the ambition overrides the schedule OR the schedule is free).
    /// Slot in the BT at priority 5.5 — between Schedule (5) and GOAP (6).
    /// </summary>
    public class BTCond_CanPursueAmbition : BTCondition
    {
        public override bool Evaluate(Character npc)
        {
            if (npc == null) return false;
            var amb = npc.CharacterAmbition;
            if (amb == null || !amb.HasActive) return false;

            // Important needs must be satisfied (Hunger + Sleep for v1).
            var settings = AmbitionSettings.Instance;
            if (settings != null && settings.GatingNeeds != null)
            {
                foreach (var needSO in settings.GatingNeeds)
                {
                    if (needSO == null) continue;
                    var need = npc.CharacterNeeds?.GetNeed(needSO);
                    if (need != null && need.IsActive()) return false;
                }
            }

            // Schedule yield logic.
            bool overridesSchedule = amb.Current?.SO?.OverridesSchedule == true;
            if (!overridesSchedule)
            {
                var sched = npc.CharacterSchedule;
                if (sched != null && sched.CurrentActivity != ScheduleActivity.None)
                    return false;
            }

            // Active step quest must be Running (not waiting on advance plumbing).
            var stepQuest = amb.Current?.CurrentStepQuest;
            if (stepQuest == null) return false;
            return stepQuest.State == MWI.Quests.QuestState.Running;
        }
    }
}
```

- [ ] **Step 39.3: Verify `CharacterNeeds.GetNeed(NeedSO)` exists**

```bash
grep -n "GetNeed\|public.*Need.*Get" Assets/Scripts/Character/CharacterNeeds/*.cs
```

If the lookup is by enum or by Type instead, adjust both `BTCond_CanPursueAmbition` and `AmbitionSettings.GatingNeeds` accordingly (e.g. switch `List<NeedSO>` to `List<NeedKind>` if the project uses an enum).

- [ ] **Step 39.4: Verify `ScheduleActivity` enum**

```bash
grep -n "enum ScheduleActivity" Assets/Scripts -r
```

Ensure `None` is a member; if the "free" value is named differently (e.g. `Free`, `Idle`), substitute.

- [ ] **Step 39.5: Write the truth-table tests**

```csharp
using NUnit.Framework;
using UnityEngine;
using MWI.AI;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class BTCondCanPursueAmbitionTests
    {
        // The full 32-combo truth table requires a real Character with subsystems.
        // EditMode-friendly subset: cover the cleanly-isolatable cases that don't
        // need needs/schedule (which are runtime systems). Remaining combos are
        // covered by the Phase 14 PlayMode smoke + the BT_RespectsOverridesScheduleFlag
        // integration test.
        [Test]
        public void Returns_False_When_Npc_Null()
        {
            var cond = new BTCond_CanPursueAmbition();
            Assert.IsFalse(cond.Evaluate(null));
        }
    }
}
```

(The full 32-combo coverage requires runtime subsystems and lives as a manual smoke in Phase 14. The unit test is intentionally minimal — it's documenting the entry-point null guard.)

- [ ] **Step 39.6: Refresh, run tests, commit**

```bash
git add Assets/Scripts/AI/BehaviourTree/Conditions/BTCond_CanPursueAmbition.cs Assets/Tests/EditMode/Ambition/BTCondCanPursueAmbitionTests.cs
git commit -m "feat(ambition): BTCond_CanPursueAmbition + minimal null-guard test"
```

### Task 40: `BTAction_PursueAmbitionStep`

**Files:**
- Create: `Assets/Scripts/AI/BehaviourTree/Actions/BTAction_PursueAmbitionStep.cs`

- [ ] **Step 40.1: Read sibling action for the pattern**

```bash
grep -l "class BTAction_Work\|abstract class BTAction" Assets/Scripts/AI/BehaviourTree -r
```

- [ ] **Step 40.2: Write the action**

```csharp
using MWI.Ambition;

namespace MWI.AI
{
    /// <summary>
    /// Drives the active step's tasks. Thin shim — TickActiveTasks lives on
    /// IAmbitionStepQuest and dispatches per the QuestSO Ordering policy. Step
    /// advancement is driven by IAmbitionStepQuest.OnStateChanged in
    /// CharacterAmbition; this action only reports up to the BT.
    /// </summary>
    public class BTAction_PursueAmbitionStep : BTAction
    {
        public override BTNodeStatus Execute(Character npc)
        {
            var step = npc?.CharacterAmbition?.Current?.CurrentStepQuest;
            if (step == null) return BTNodeStatus.Failure;

            var status = step.TickActiveTasks(npc);
            return status switch
            {
                TaskStatus.Running   => BTNodeStatus.Running,
                TaskStatus.Completed => BTNodeStatus.Success,
                TaskStatus.Failed    => BTNodeStatus.Failure,
                _                    => BTNodeStatus.Running,
            };
        }
    }
}
```

- [ ] **Step 40.3: Refresh, fix any namespace mismatches (`BTAction`, `BTNodeStatus`), commit**

```bash
git add Assets/Scripts/AI/BehaviourTree/Actions/BTAction_PursueAmbitionStep.cs
git commit -m "feat(ambition): BTAction_PursueAmbitionStep shim"
```

### Task 41: Wire the new branch into `NPCBehaviourTree`

**Files:**
- Modify: `Assets/Scripts/AI/NPCBehaviourTree.cs`

- [ ] **Step 41.1: Read the existing tree composition**

Open `NPCBehaviourTree.cs` and find the `BTSelector` that holds the priority list (around line 93 per the spec). It will look like an array / list of `(Condition, Action)` pairs in priority order.

- [ ] **Step 41.2: Insert the new branch between Schedule and GOAP**

Find the Schedule entry. Insert immediately after it:

```csharp
new BTSequence(
    new BTCond_CanPursueAmbition(),
    new BTAction_PursueAmbitionStep()
),
```

(Adjust the construction to match the actual `BTSelector` / `BTSequence` API in the project — could be method calls, builders, or a `[SerializeReference]` list.)

- [ ] **Step 41.3: Refresh, verify zero errors, commit**

```bash
git add Assets/Scripts/AI/NPCBehaviourTree.cs
git commit -m "feat(ambition): slot ambition branch at BT priority 5.5"
```

---

## Phase 10 — Controller-switch handoff

### Task 42: Hook into `Character.SwitchToPlayer` and `SwitchToNPC`

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`

- [ ] **Step 42.1: Add a helper method**

In `Character.cs` (after `SwitchToNPC` definition, line ~878), add:

```csharp
private void NotifyAmbitionControllerSwitch(MWI.Ambition.ControllerKind goingTo)
{
    var step = CharacterAmbition?.Current?.CurrentStepQuest;
    step?.OnControllerSwitching(this, goingTo);
}
```

- [ ] **Step 42.2: Call it at the top of each switch method**

In `SwitchToPlayer()` (line 853), as the very first line:

```csharp
NotifyAmbitionControllerSwitch(MWI.Ambition.ControllerKind.Player);
```

In `SwitchToNPC()` (line 869), as the very first line:

```csharp
NotifyAmbitionControllerSwitch(MWI.Ambition.ControllerKind.NPC);
```

- [ ] **Step 42.3: Refresh, verify zero errors, commit**

```bash
git add Assets/Scripts/Character/Character.cs
git commit -m "feat(ambition): notify active step quest on controller switch"
```

---

## Phase 11 — Player UI

### Task 43: Mark `AmbitionQuest` as ambition-step in the existing `IQuest` HUD path

**Files:**
- Possibly modify: `Assets/Scripts/Quests/IQuest.cs` (only if needed for filter sorting)
- (No code change if `AmbitionQuest.IsAmbitionStep` is sufficient)

- [ ] **Step 43.1: Decide if `IQuest` needs the flag too**

If the existing `CharacterQuestLog` HUD iterates `IQuest`, downcasting to read `IsAmbitionStep` works — but the HUD code must know to check. Read the HUD code:

```bash
grep -rn "ClaimedQuests\|FocusedQuest" Assets/Scripts/UI -l | head -3
```

If the HUD iterates as `IQuest`, add `bool IsAmbitionStep => false;` as a default-implemented property on `IQuest` (C# 8 default interface members) — or add a new method `IQuest.IsAmbitionStepHint()` returning false by default.

- [ ] **Step 43.2: If you added a property to IQuest, refresh and verify zero errors**

If no IQuest change is needed (the HUD already handles untyped quests fine), skip.

- [ ] **Step 43.3: Commit**

```bash
git add Assets/Scripts/Quests/IQuest.cs 2>/dev/null; git commit -m "feat(ambition): mark ambition quests in IQuest layer (if applicable)" || echo "No-op skipped"
```

### Task 44: `UI_AmbitionTracker` script

**Files:**
- Create: `Assets/Scripts/UI/Ambition/UI_AmbitionTracker.cs`

- [ ] **Step 44.1: Create the folder**

`assets-create-folder` parent `Assets/Scripts/UI/Ambition`.

- [ ] **Step 44.2: Write the script**

```csharp
using MWI.Ambition;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI
{
    /// <summary>
    /// HUD widget that renders the local player's active ambition: title, current
    /// step name, completed-out-of-total. Hidden when the local Character has no
    /// active ambition. Uses unscaled time per rule #26 — survives game pause.
    /// </summary>
    public class UI_AmbitionTracker : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _currentStep;
        [SerializeField] private TMP_Text _progressText;
        [SerializeField] private Image _progressBar;

        private Character _localCharacter;
        private CharacterAmbition _ambition;

        public void Bind(Character localPlayer)
        {
            Unbind();
            _localCharacter = localPlayer;
            _ambition = localPlayer?.CharacterAmbition;
            if (_ambition == null) { Hide(); return; }
            _ambition.OnAmbitionSet += HandleSet;
            _ambition.OnStepAdvanced += HandleStepAdvanced;
            _ambition.OnAmbitionCompleted += HandleEnded;
            _ambition.OnAmbitionCleared += HandleEnded;
            Refresh();
        }

        public void Unbind()
        {
            if (_ambition == null) return;
            _ambition.OnAmbitionSet -= HandleSet;
            _ambition.OnStepAdvanced -= HandleStepAdvanced;
            _ambition.OnAmbitionCompleted -= HandleEnded;
            _ambition.OnAmbitionCleared -= HandleEnded;
            _ambition = null;
        }

        private void OnDestroy() => Unbind();

        private void HandleSet(AmbitionInstance _) => Refresh();
        private void HandleStepAdvanced(AmbitionInstance _, int __) => Refresh();
        private void HandleEnded(CompletedAmbition _) => Refresh();

        private void Refresh()
        {
            if (_ambition == null || !_ambition.HasActive) { Hide(); return; }
            var inst = _ambition.Current;
            if (inst?.SO == null) { Hide(); return; }

            if (_root != null) _root.SetActive(true);
            if (_title != null) _title.text = inst.SO.DisplayName;
            if (_currentStep != null)
            {
                var qs = inst.SO.Quests[inst.CurrentStepIndex];
                _currentStep.text = qs != null ? qs.DisplayName : "(unknown step)";
            }
            int completed = inst.CurrentStepIndex;
            int total = inst.TotalSteps;
            if (_progressText != null) _progressText.text = $"{completed} / {total}";
            if (_progressBar != null) _progressBar.fillAmount = inst.Progress01;
        }

        private void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }
    }
}
```

- [ ] **Step 44.3: Refresh, verify zero errors, commit**

```bash
git add Assets/Scripts/UI/Ambition/UI_AmbitionTracker.cs
git commit -m "feat(ambition): UI_AmbitionTracker HUD widget script"
```

### Task 45: Build the `UI_AmbitionTracker.prefab`

**Files:**
- Create: `Assets/UI/Ambition/UI_AmbitionTracker.prefab`

- [ ] **Step 45.1: Create folders if missing**

`assets-create-folder` parents `Assets/UI/Ambition`.

- [ ] **Step 45.2: Build the prefab via Unity MCP tools**

Use `gameobject-create` for an empty root, then `gameobject-component-add` for `RectTransform`, `Image` (background panel), and the `UI_AmbitionTracker` script. Add child `Text` (TMP) elements for `_title`, `_currentStep`, `_progressText` and a child `Image` for `_progressBar`. Wire serialized references via `gameobject-component-modify`.

Detailed sub-steps:

1. `gameobject-create` name `UI_AmbitionTracker`, no parent (will be saved as prefab root).
2. `gameobject-component-add` `RectTransform` (auto on UI). Set anchors top-right.
3. `gameobject-component-add` `Image` for background. Set color to a translucent dark.
4. `gameobject-component-add` `MWI.UI.UI_AmbitionTracker`.
5. `gameobject-create` child `Title`, parent the root. Add `TextMeshProUGUI`. Set placeholder text "Have a Family".
6. `gameobject-create` child `CurrentStep` similarly.
7. `gameobject-create` child `Progress`. Add `TextMeshProUGUI`.
8. `gameobject-create` child `ProgressBar`. Add `Image` set to filled type Horizontal.
9. `gameobject-component-modify` on the root's `UI_AmbitionTracker` — wire `_root` to the root, `_title` / `_currentStep` / `_progressText` / `_progressBar` to the children.
10. `assets-prefab-create` saving to `Assets/UI/Ambition/UI_AmbitionTracker.prefab`.
11. `gameobject-destroy` to clean up the scene-tree GO once the prefab is created.

- [ ] **Step 45.3: Verify the prefab persists, commit**

```bash
git add Assets/UI/Ambition
git commit -m "feat(ambition): UI_AmbitionTracker prefab"
```

### Task 46: Wire `UI_AmbitionTracker` into `PlayerUI`

**Files:**
- Modify: `Assets/Scripts/UI/PlayerUI.cs` (or actual location — grep)

- [ ] **Step 46.1: Find PlayerUI**

```bash
grep -l "class PlayerUI" Assets/Scripts/UI -r
```

- [ ] **Step 46.2: Add field + initialization**

In `PlayerUI`:

```csharp
[SerializeField] private MWI.UI.UI_AmbitionTracker _ambitionTracker;

// In the existing Initialize(GameObject playerObj) method, after the existing
// local-character-binding code:
public void Initialize(GameObject playerObj)
{
    // ... existing code ...
    var c = playerObj.GetComponent<Character>();
    if (_ambitionTracker != null) _ambitionTracker.Bind(c);
}
```

- [ ] **Step 46.3: Drag the tracker prefab into the `PlayerUI` prefab in the editor**

Manual step: open `Assets/UI/PlayerUI.prefab` (or whichever PlayerUI prefab the project uses), drag `UI_AmbitionTracker.prefab` into the appropriate child UI position, and assign its component reference to the `_ambitionTracker` field.

- [ ] **Step 46.4: Refresh, verify zero errors, commit**

```bash
git add Assets/Scripts/UI/PlayerUI.cs Assets/UI
git commit -m "feat(ambition): wire UI_AmbitionTracker into PlayerUI"
```

---

## Phase 12 — Dev-Mode inspector

### Task 47: `AmbitionInspectorView`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Modules/Inspectors/AmbitionInspectorView.cs`

- [ ] **Step 47.1: Read the existing inspector pattern**

```bash
grep -rln "class CharacterInspectorView\|abstract class.*InspectorView\|IInspectorView" Assets/Scripts/Debug
```

- [ ] **Step 47.2: Write the inspector**

```csharp
using System.Linq;
using MWI.Ambition;
using UnityEngine;

namespace MWI.Debug
{
    /// <summary>
    /// Sub-tab on CharacterInspectorView showing the active ambition and history.
    /// Buttons: Set Ambition (dropdown), Clear Ambition, Force Advance Step (debug).
    /// </summary>
    public class AmbitionInspectorView : ICharacterSubTab
    {
        public string Title => "Ambition";

        public void Render(Character target)
        {
            if (target == null) { GUILayout.Label("No character selected."); return; }
            var amb = target.CharacterAmbition;
            if (amb == null) { GUILayout.Label("No CharacterAmbition subsystem."); return; }

            // Active.
            GUILayout.Label("=== Active ===", EditorishHeader);
            if (!amb.HasActive)
            {
                GUILayout.Label("(none)");
            }
            else
            {
                var inst = amb.Current;
                GUILayout.Label($"SO: {inst.SO?.name ?? "<null>"}");
                GUILayout.Label($"Step: {inst.CurrentStepIndex} / {inst.TotalSteps - 1}");
                GUILayout.Label($"Progress: {inst.Progress01:P0}");
                GUILayout.Label($"OverridesSchedule: {(inst.SO?.OverridesSchedule == true)}");
                GUILayout.Label($"AssignedDay: {inst.AssignedDay}");
                GUILayout.Label("Context:");
                if (inst.Context != null)
                {
                    foreach (var kv in inst.Context.AsReadOnly())
                        GUILayout.Label($"  {kv.Key} = {kv.Value}");
                }
            }

            // Buttons.
            GUILayout.Space(8);
            GUILayout.Label("=== Actions ===", EditorishHeader);
            RenderSetAmbitionDropdown(amb);
            if (amb.HasActive && GUILayout.Button("Clear Ambition")) amb.ClearAmbition();
            if (amb.HasActive && GUILayout.Button("Force Advance Step (debug)"))
                ForceAdvanceStepDebug(amb);

            // History.
            GUILayout.Space(8);
            GUILayout.Label($"=== History ({amb.History.Count}) ===", EditorishHeader);
            foreach (var h in amb.History)
                GUILayout.Label($"Day {h.CompletedDay} — {h.SO?.name ?? "<null>"} ({h.Reason})");
        }

        private static GUIStyle EditorishHeader =>
            new(GUI.skin.label) { fontStyle = FontStyle.Bold };

        private static void RenderSetAmbitionDropdown(CharacterAmbition amb)
        {
            var all = AmbitionRegistry.All.ToList();
            if (all.Count == 0) { GUILayout.Label("No AmbitionSO assets in registry."); return; }
            GUILayout.Label("Set Ambition:");
            foreach (var so in all)
            {
                if (GUILayout.Button(so.name)) amb.SetAmbition(so);
            }
        }

        private static void ForceAdvanceStepDebug(CharacterAmbition amb)
        {
            var stepQuest = amb.Current?.CurrentStepQuest;
            if (stepQuest is AmbitionQuest aq) aq.SetState(MWI.Quests.QuestState.Completed);
        }
    }
}
```

- [ ] **Step 47.3: Verify `ICharacterSubTab` interface name**

```bash
grep -n "interface ICharacterSubTab\|abstract class CharacterSubTab" Assets/Scripts/Debug -r
```

If the interface uses different members (e.g. `OnGUI(target)` instead of `Render(target)`), substitute. If sub-tab plumbing uses an enum + switch instead of a polymorphic interface, you may instead extend `CharacterInspectorView` directly with a new tab enum value.

- [ ] **Step 47.4: Refresh, fix interface mismatches, commit**

```bash
git add Assets/Scripts/Debug/DevMode/Modules/Inspectors/AmbitionInspectorView.cs
git commit -m "feat(ambition): dev-mode AmbitionInspectorView"
```

### Task 48: Register the sub-tab in `CharacterInspectorView`

**Files:**
- Modify: `Assets/Scripts/Debug/DevMode/Modules/Inspectors/CharacterInspectorView.cs`

- [ ] **Step 48.1: Find the registration point**

Read the file. Look for a `_subTabs` list, a `CharacterSubTab` enum, or a switch statement enumerating sub-tab kinds.

- [ ] **Step 48.2: Register `AmbitionInspectorView`**

Add it to whichever collection drives sub-tabs. Likely:

```csharp
_subTabs.Add(new AmbitionInspectorView());
```

(or extend the enum + switch).

- [ ] **Step 48.3: Refresh, verify, commit**

```bash
git add Assets/Scripts/Debug/DevMode/Modules/Inspectors/CharacterInspectorView.cs
git commit -m "feat(ambition): register AmbitionInspectorView sub-tab"
```

---

## Phase 13 — Sample SO content

We need the first concrete `RuntimeQueryBinding` (`EligibleLoverQuery`), one concrete `AmbitionSO` subclass per shape, three `QuestSO` assets, three `AmbitionSO` assets, and the global `AmbitionSettings` asset.

### Task 49: `EligibleLoverQuery` runtime query

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Bindings/EligibleLoverQuery.cs`

- [ ] **Step 49.1: Write the file**

```csharp
using System;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Picks an eligible romantic partner for the calling NPC, scoped to the same
    /// MapController. Used by Quest_FindLover. Writes the choice to context["Lover"]
    /// so downstream tasks (Quest_HaveChildWithLover) can read it.
    ///
    /// Heuristic for v1: any other Character on the same map who is alive, not
    /// already partnered (per CharacterRelation), and within an opposite-sex
    /// preference (extend later for non-binary). Picks the highest-affinity
    /// candidate the NPC has.
    /// </summary>
    [Serializable]
    public class EligibleLoverQuery : RuntimeQueryBinding<Character>
    {
        protected override Character Query(Character npc, AmbitionContext ctx)
        {
            if (npc == null) return null;
            var map = npc.GetComponentInParent<MWI.WorldSystem.MapController>();
            if (map == null) return null;

            Character best = null;
            float bestScore = float.MinValue;
            foreach (var other in UnityEngine.Object.FindObjectsByType<Character>(FindObjectsSortMode.None))
            {
                if (other == null || other == npc) continue;
                if (!other.IsAlive()) continue;
                if (other.GetComponentInParent<MWI.WorldSystem.MapController>() != map) continue;
                // Bail if either side already has a partner. CharacterRelation API
                // is project-specific — adjust the check during implementation.
                if (npc.CharacterRelation?.HasPartner == true) return null;
                if (other.CharacterRelation?.HasPartner == true) continue;
                float score = npc.CharacterRelation?.GetAffinityWith(other) ?? 0f;
                if (score > bestScore) { bestScore = score; best = other; }
            }
            return best;
        }
    }
}
```

- [ ] **Step 49.2: Verify `CharacterRelation.HasPartner` and `GetAffinityWith` member names**

```bash
grep -n "HasPartner\|GetAffinity\|public.*Partner\|public float Get" Assets/Scripts/Character/CharacterRelation/*.cs
```

If the project uses different names (e.g. `IsRomanticallyEngaged`, `Compatibility`), substitute. If the methods don't exist yet, fall back to a simpler "any character with affinity > 0" heuristic and document a follow-up TODO in the v1 backlog.

- [ ] **Step 49.3: Refresh, fix, commit**

```bash
git add Assets/Scripts/Character/Ambition/Bindings/EligibleLoverQuery.cs
git commit -m "feat(ambition): EligibleLoverQuery runtime binding"
```

### Task 50: Three concrete `AmbitionSO` subclasses

**Files:**
- Create: `Assets/Scripts/Character/Ambition/AmbitionTypes/Ambition_HaveAFamily.cs`
- Create: `Assets/Scripts/Character/Ambition/AmbitionTypes/Ambition_Murder.cs`
- Create: `Assets/Scripts/Character/Ambition/AmbitionTypes/Ambition_HaveTwoKids.cs`

- [ ] **Step 50.1: Create folder**

`assets-create-folder` parent `Assets/Scripts/Character/Ambition/AmbitionTypes`.

- [ ] **Step 50.2: Write `Ambition_HaveAFamily.cs`**

```csharp
using UnityEngine;

namespace MWI.Ambition
{
    [CreateAssetMenu(menuName = "MWI/Ambition/Ambition_HaveAFamily", fileName = "Ambition_HaveAFamily")]
    public class Ambition_HaveAFamily : AmbitionSO
    {
        // No required parameters — Lover is picked at runtime by Quest_FindLover.
    }
}
```

- [ ] **Step 50.3: Write `Ambition_Murder.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    [CreateAssetMenu(menuName = "MWI/Ambition/Ambition_Murder", fileName = "Ambition_Murder")]
    public class Ambition_Murder : AmbitionSO
    {
        public override bool ValidateParameters(IReadOnlyDictionary<string, object> p)
        {
            // Default rule check (Required keys present + types).
            if (!base.ValidateParameters(p)) return false;
            // Cross-parameter rule: Target must be alive at assignment.
            if (p != null && p.TryGetValue("Target", out var v) && v is Character c)
            {
                if (!c.IsAlive())
                {
                    Debug.LogError("[Ambition_Murder] Target is not alive at assignment.");
                    return false;
                }
            }
            return true;
        }
    }
}
```

- [ ] **Step 50.4: Write `Ambition_HaveTwoKids.cs`**

```csharp
using UnityEngine;

namespace MWI.Ambition
{
    [CreateAssetMenu(menuName = "MWI/Ambition/Ambition_HaveTwoKids", fileName = "Ambition_HaveTwoKids")]
    public class Ambition_HaveTwoKids : AmbitionSO { }
}
```

- [ ] **Step 50.5: Refresh, verify zero errors, commit**

```bash
git add Assets/Scripts/Character/Ambition/AmbitionTypes
git commit -m "feat(ambition): concrete Ambition_HaveAFamily/Murder/HaveTwoKids"
```

### Task 51: Author the sample SO assets

**Files:**
- Create: `Assets/Resources/Data/Ambitions/AmbitionSettings.asset`
- Create: `Assets/Resources/Data/Ambitions/Ambition_HaveAFamily.asset`
- Create: `Assets/Resources/Data/Ambitions/Ambition_Murder.asset`
- Create: `Assets/Resources/Data/Ambitions/Ambition_HaveTwoKids.asset`
- Create: `Assets/Resources/Data/Ambitions/Quests/Quest_FindLover.asset`
- Create: `Assets/Resources/Data/Ambitions/Quests/Quest_HaveChildWithLover.asset`
- Create: `Assets/Resources/Data/Ambitions/Quests/Quest_AssassinateTarget.asset`

This is a series of editor-driven asset-creation steps, each doing roughly: create asset via the right `[CreateAssetMenu]`, fill its fields, save. Use `script-execute` for any field-population that's awkward through the inspector chain.

- [ ] **Step 51.1: Create the folder structure**

`assets-create-folder` parent `Assets/Resources/Data/Ambitions`, then again for `Quests`.

- [ ] **Step 51.2: Create `AmbitionSettings.asset`**

Use `script-execute`:

```csharp
public static class CreateAmbitionSettings
{
    public static void Run()
    {
        var asset = UnityEngine.ScriptableObject.CreateInstance<MWI.Ambition.AmbitionSettings>();
        // GatingNeeds list is filled manually in Step 51.3 because we need NeedSO refs.
        UnityEditor.AssetDatabase.CreateAsset(asset, "Assets/Resources/Data/Ambitions/AmbitionSettings.asset");
        UnityEditor.AssetDatabase.SaveAssets();
    }
}
```

- [ ] **Step 51.3: Drag NeedSO_Hunger and NeedSO_Sleep into `AmbitionSettings.GatingNeeds`**

Manual editor step: open the new asset, drag the existing `NeedSO_Hunger` + `NeedSO_Sleep` assets into the `_gatingNeeds` list. (Project paths: grep `find Assets -name "NeedSO_*.asset"` to locate them.)

- [ ] **Step 51.4: Create `Quest_FindLover.asset` with one Task_TalkToCharacter using EligibleLoverQuery**

Use `script-execute`:

```csharp
public static class CreateQuestFindLover
{
    public static void Run()
    {
        var so = UnityEngine.ScriptableObject.CreateInstance<MWI.Ambition.QuestSO>();
        // Configure via reflection because the fields are private.
        var titleField = typeof(MWI.Ambition.QuestSO).GetField("_displayName",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        titleField.SetValue(so, "Find a lover");

        var descField = typeof(MWI.Ambition.QuestSO).GetField("_description",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        descField.SetValue(so, "Court an eligible character on this map.");

        var orderingField = typeof(MWI.Ambition.QuestSO).GetField("_ordering",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        orderingField.SetValue(so, MWI.Ambition.TaskOrderingMode.Sequential);

        var task = new MWI.Ambition.Task_TalkToCharacter
        {
            Target = new MWI.Ambition.EligibleLoverQuery { WriteKey = "Lover" }
        };

        var tasksField = typeof(MWI.Ambition.QuestSO).GetField("_tasks",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        tasksField.SetValue(so, new System.Collections.Generic.List<MWI.Ambition.TaskBase> { task });

        UnityEditor.AssetDatabase.CreateAsset(so, "Assets/Resources/Data/Ambitions/Quests/Quest_FindLover.asset");
        UnityEditor.AssetDatabase.SaveAssets();
    }
}
```

- [ ] **Step 51.5: Create `Quest_HaveChildWithLover.asset`**

Mirror Step 51.4. Replace the task with `Task_WaitDays { Days = 30 }` for v1 (a placeholder until a real "have a child with X" pipeline is shipped — flag this as a backlog item). Keep `WriteKey` semantics consistent.

```csharp
var task = new MWI.Ambition.Task_WaitDays { Days = 30 };
```

(In a real iteration, this becomes `Task_HaveChildWith` with a `ContextBinding<Character>("Lover")`. The placeholder keeps the chain advancing for the smoke test.)

- [ ] **Step 51.6: Create `Quest_AssassinateTarget.asset`**

Mirror Step 51.4 with `Task_KillCharacter` reading `ContextBinding<Character>("Target")`:

```csharp
var task = new MWI.Ambition.Task_KillCharacter
{
    Target = new MWI.Ambition.ContextBinding<Character> { Key = "Target" }
};
```

- [ ] **Step 51.7: Create the three Ambition assets**

For each, `script-execute` something like:

```csharp
public static class CreateAmbitionHaveAFamily
{
    public static void Run()
    {
        var so = UnityEngine.ScriptableObject.CreateInstance<MWI.Ambition.Ambition_HaveAFamily>();
        var dn = typeof(MWI.Ambition.AmbitionSO).GetField("_displayName",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        dn.SetValue(so, "Have a Family");

        var desc = typeof(MWI.Ambition.AmbitionSO).GetField("_description",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        desc.SetValue(so, "Find a partner and have a child with them.");

        var ovr = typeof(MWI.Ambition.AmbitionSO).GetField("_overridesSchedule",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        ovr.SetValue(so, false);

        var quests = typeof(MWI.Ambition.AmbitionSO).GetField("_quests",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var q1 = UnityEditor.AssetDatabase.LoadAssetAtPath<MWI.Ambition.QuestSO>(
            "Assets/Resources/Data/Ambitions/Quests/Quest_FindLover.asset");
        var q2 = UnityEditor.AssetDatabase.LoadAssetAtPath<MWI.Ambition.QuestSO>(
            "Assets/Resources/Data/Ambitions/Quests/Quest_HaveChildWithLover.asset");
        quests.SetValue(so, new System.Collections.Generic.List<MWI.Ambition.QuestSO> { q1, q2 });

        UnityEditor.AssetDatabase.CreateAsset(so, "Assets/Resources/Data/Ambitions/Ambition_HaveAFamily.asset");
        UnityEditor.AssetDatabase.SaveAssets();
    }
}
```

Repeat with appropriate quest lists + `OverridesSchedule = true` for `Ambition_Murder` (one quest: `Quest_AssassinateTarget`, parameters list `[{ Key = "Target", Kind = Character, Required = true }]`), and three quests for `Ambition_HaveTwoKids` (one `Quest_FindLover` + two `Quest_HaveChildWithLover`).

- [ ] **Step 51.8: Refresh, commit**

```bash
git add Assets/Resources/Data/Ambitions
git commit -m "feat(ambition): author sample Quest and Ambition SO assets"
```

---

## Phase 14 — Manual smoke + documentation

### Task 52: Manual smoke 1 — `Ambition_HaveAFamily` on a farmer

- [ ] **Step 52.1: Open a Play-mode scene with at least 3 NPCs**

Use `scene-open` for `Assets/Scenes/GameScene.unity` (or whichever scene has populated NPCs).

- [ ] **Step 52.2: Enter Play mode and assign**

`editor-application-set-state` → `Play`. Then via the dev-mode panel (Ctrl+Click + ESC + the new Ambition tab):
1. Open Dev Mode (`/devmode` or the toggle).
2. Pick a farmer NPC via the Inspect tab.
3. Switch to the Ambition sub-tab.
4. Click `Ambition_HaveAFamily`.

- [ ] **Step 52.3: Observe behavior over a few in-game days**

Use `console-get-logs` to follow `OnAmbitionSet`, `OnStepAdvanced`, `OnAmbitionCompleted` events. Drop hunger to `IsActive` and confirm the BT yields to needs (the farmer should eat first). Restore hunger and confirm the ambition-pursuit branch resumes.

- [ ] **Step 52.4: Document any failures and fix**

If the BT branch never fires: verify `BTCond_CanPursueAmbition.Evaluate` returns true under expected conditions (add `Debug.Log` statements, gated by `NPCDebug.VerboseAmbition`).

If parameter validation fails: cross-check `AmbitionRegistry.GetGuid` returns non-null for the SO.

- [ ] **Step 52.5: Commit any test fixes**

```bash
git add -p
git commit -m "fix(ambition): smoke-test fixups"
```

### Task 53: Manual smoke 2 — `Ambition_Murder` with `OverridesSchedule = true`

- [ ] **Step 53.1: Pick a worker NPC during their work shift**

While Play mode is running and `BTCond_HasScheduledActivity` is gating the worker into Schedule branch, pick the worker via dev tools.

- [ ] **Step 53.2: Assign `Ambition_Murder` via dev tools, target = another NPC**

The dev inspector's Set Ambition button currently doesn't take parameters; expose a follow-up: a tiny scratch field on `AmbitionInspectorView` for `Target` selection (or hard-code via `script-execute` for v1):

```csharp
var target = ... // pick another character
victimNpc.CharacterAmbition.SetAmbition(
    UnityEditor.AssetDatabase.LoadAssetAtPath<MWI.Ambition.AmbitionSO>(
        "Assets/Resources/Data/Ambitions/Ambition_Murder.asset"),
    new System.Collections.Generic.Dictionary<string, object> { { "Target", target } }
);
```

- [ ] **Step 53.3: Observe**

The murderer NPC should leave their shift (overrides schedule) and pursue the target across the map until the kill completes, then transition to Inactive.

- [ ] **Step 53.4: Save / reload mid-pursuit**

Use the bed save / portal save and reload. Verify the ambition resumes with same step + context after load.

- [ ] **Step 53.5: Document and commit fixes**

### Task 54: Manual smoke 3 — Mid-pursuit `SwitchToPlayer`

- [ ] **Step 54.1: While murder smoke is running mid-pursuit, switch the NPC to Player**

Use `Character.SwitchToPlayer` via the dev inspector's existing context menu (line 1075 in Character.cs).

- [ ] **Step 54.2: Verify HUD pickup**

The `UI_AmbitionTracker` widget should appear instantly with current chain progress (1 / 1 for Murder, since it's a single-step ambition — the bar shows whatever progress). The active step quest should appear in the player's existing Quest tracker HUD too.

- [ ] **Step 54.3: Walk the player to the target and kill manually**

Use normal player input. The `Task_KillCharacter` completion listener (subscribed to `OnDeath`) fires regardless of who delivered the killing blow. The ambition should transition to Completed.

- [ ] **Step 54.4: Verify history entry**

Open the dev inspector — History should show one entry with `Reason = Completed`.

- [ ] **Step 54.5: Commit any fixes**

### Task 55: Skill documentation

**Files:**
- Create: `.agent/skills/ambition-system/SKILL.md`

- [ ] **Step 55.1: Create folder**

```bash
mkdir -p .agent/skills/ambition-system
```

- [ ] **Step 55.2: Write the SKILL.md following the template at `.agent/skills/skill-creator/SKILL.md`**

Sections:
- Purpose
- Public API (`CharacterAmbition` Set/Clear, events)
- Authoring workflow (create AmbitionSO, drop QuestSOs in, inline Tasks)
- Task primitive catalog (the 7 v1 primitives)
- Pattern A vs Pattern B selection guide
- Persistence + network sync notes
- Controller-switch handoff contract
- Common gotchas (zombie ambitions, parameter validation, SO GUID stability)
- Cross-references: link to spec, wiki page, related agents.

Length: 200–400 lines. Concrete code snippets where helpful.

- [ ] **Step 55.3: Commit**

```bash
git add .agent/skills/ambition-system
git commit -m "docs(ambition): SKILL.md"
```

### Task 56: Wiki system page

**Files:**
- Create: `wiki/systems/ambition-system.md`

- [ ] **Step 56.1: Read `wiki/CLAUDE.md` first** (per memory note `feedback_wiki_vs_skills_scope.md`)

- [ ] **Step 56.2: Use `wiki/_templates/system.md` as the starting structure**

```bash
ls wiki/_templates 2>&1
```

Match the required ten sections (Purpose, Responsibilities, Key classes, Public API, Data flow, Dependencies, State & persistence, Gotchas, Open questions, Change log) per rule #29b.

- [ ] **Step 56.3: Cross-link to the spec, SKILL.md, and the related systems**

`depends_on`: ai-goap, quest-system, character-system, character-needs, character-schedule, save-persistence, network-architecture.

`Sources` section: link to `.agent/skills/ambition-system/SKILL.md` and the spec doc.

- [ ] **Step 56.4: Commit**

```bash
git add wiki/systems/ambition-system.md wiki/INDEX.md
git commit -m "docs(ambition): wiki/systems page"
```

### Task 57: Update `wiki/systems/ai-goap.md` and `wiki/systems/quest-system.md`

**Files:**
- Modify: `wiki/systems/ai-goap.md`
- Modify: `wiki/systems/quest-system.md`

- [ ] **Step 57.1: Update `ai-goap.md` "ultimate goals" section**

Replace the StartAFamily / BestMartialArtist / FinancialAmbition examples with a single line redirecting to the Ambition system:

> **Note:** "Ultimate goals" — life-goal-driven NPC behavior — is now handled by the [[ambition-system]]. The examples below are illustrative of GOAP planning shape; ambitions plug into GOAP via transient goals registered by Pattern B Tasks (see `CharacterGoap.RegisterTransientGoal`).

Bump the `updated:` frontmatter date and add a Change log entry.

- [ ] **Step 57.2: Update `quest-system.md` to mention `AmbitionQuest`**

Add a paragraph in the IQuest implementations section:

> `AmbitionQuest` is a runtime IQuest produced from a QuestSO when a CharacterAmbition advances onto a step. It bridges the ambition system's behavior layer (Tasks) with the IQuest infrastructure for save/sync/HUD reuse. See [[ambition-system]].

Bump `updated:` and Change log.

- [ ] **Step 57.3: Commit**

```bash
git add wiki/systems/ai-goap.md wiki/systems/quest-system.md
git commit -m "docs(wiki): cross-link ambition-system from ai-goap and quest-system"
```

### Task 58: Evaluate / create `.claude/agents/ambition-system-specialist.md`

**Files:**
- Possibly create: `.claude/agents/ambition-system-specialist.md`

- [ ] **Step 58.1: Count interconnected scripts**

Run:

```bash
find Assets/Scripts/Character/Ambition Assets/Scripts/AI/BehaviourTree/Conditions/BTCond_CanPursueAmbition.cs Assets/Scripts/AI/BehaviourTree/Actions/BTAction_PursueAmbitionStep.cs -name "*.cs" | wc -l
```

Per project rule #29: 5+ interconnected scripts → create an agent.

- [ ] **Step 58.2: If threshold met, create the agent file**

Mirror an existing specialist agent (e.g. `.claude/agents/quest-system-specialist.md`) — 1-paragraph description, list of triggers, allowed tools (`Read, Edit, Write, Glob, Grep, Bash, Agent`), `model: opus` (per rule #29 + memory `feedback_always_opus.md`).

- [ ] **Step 58.3: Commit**

```bash
git add .claude/agents/ambition-system-specialist.md 2>/dev/null && git commit -m "docs(ambition): add specialist agent" || echo "Skipped — script count below threshold"
```

---

## Self-review checklist (run before claiming the plan complete)

1. **Spec coverage** — every Requirement (1–12) and Non-Goal in the spec has either a task implementing it or an explicit "Out-of-Scope" deferral.
2. **Placeholder scan** — search for `TODO|TBD|fill in|implement later|appropriate error` in the plan and replace with concrete content.
3. **Type consistency** — `CurrentStepQuest` is `IAmbitionStepQuest` everywhere; `CharacterAmbition.SetAmbition` signature matches across phases; `IsAmbitionStep` referenced consistently; `OnControllerSwitching` argument list (`Character, ControllerKind`) consistent across `TaskBase`, `IAmbitionStepQuest`, and `AmbitionQuest`.
4. **No orphan symbols** — every type referenced in a step is either imported via `using` or fully qualified, and is created in some earlier step.
5. **Network safety** — every state mutation is server-gated (`IsServer` check before `_current` mutation); no per-frame allocations in `BTCond_CanPursueAmbition` or `BTAction_PursueAmbitionStep`; logs gated behind `NPCDebug.VerboseAmbition`.

---

## Execution

Plan complete and saved to [docs/superpowers/plans/2026-05-02-ambition-system.md](docs/superpowers/plans/2026-05-02-ambition-system.md).

Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. The subagent gets the relevant task block as its full context — no risk of cross-task contamination. Best for a plan this size (58 tasks).

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints for review.

Which approach?





