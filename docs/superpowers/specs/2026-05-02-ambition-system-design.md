# Ambition System Design

**Date:** 2026-05-02
**Branch:** `multiplayyer`
**Status:** Approved Design

## Problem Statement

Today, NPCs in the project are reactive — they fulfill needs (eat when hungry, sleep when tired, pick up clothes when naked), follow scheduled job shifts, and run GOAP plans against transient need-driven goals. They have no **long-term direction**: nothing makes a farmer NPC fundamentally different from a baker NPC over the arc of a save file. The user wants every Character (player or NPC) to be capable of carrying a **single life-goal "Ambition"** that, once their important needs are satisfied, drives their behavior toward a multi-step objective — *Sims 3 Ambition*-style. Examples:

- "Murder Lord Garrick" — one-step ambition (kill the named target).
- "Have a Family" — two-step chain (find a lover → make a child with that lover).
- "Build a Dynasty", "Become the Best Smith", "Avenge My Father" — extensible chains.

The system must be:

- **Player/NPC parity** (rule #22): a Character switched from NPC to Player via `Character.SwitchToPlayer` (`Character.cs:853`) must seamlessly continue ambition progress with no reset, with the player picking up exactly where the BT left off, and vice versa.
- **Authored**: designers create ambitions and their step quests as ScriptableObject assets.
- **Reusable across ambitions**: a "Find a Lover" step authored once should be droppable into any ambition that needs it.
- **Generic verb-level primitives below the quest layer**: the user explicitly asked for atomic, reusable, parameterized "tasks" — `Task_KillCharacter`, `Task_TalkToCharacter`, `Task_MoveToZone`, `Task_HarvestTarget` — that compose into Quests.
- **Patient / untimed**: an ambition is a life goal, not a deadline. No urgency curves, no auto-failure, no escalation — only `Set` / `Clear` / `Complete` as state transitions.
- **Persistent + multiplayer-correct**: ambition state survives save/load, hibernation, late-joiners, and the three multiplayer relationship pairings (rule #19).

This spec introduces the **Ambition** system as a new top-level Character subsystem, plus a new **Task** primitive layer below the existing `IQuest` system. The existing `IQuest` infrastructure (shipped 2026-04-23 — see `2026-04-23-quest-system-design.md`) is reused for the data, save, and player-HUD layers; the **behavior driver** is new (`BTAction_PursueAmbitionStep`) because the existing BT/GOAP pipeline never read claimed quests today.

### Requirements

1. **One ambition per Character at a time.** `Character.CharacterAmbition.Current` is either `null` (Inactive) or holds a single `AmbitionInstance`. Replacement (`SetAmbition` while Active) cancels the old ambition and pushes it into history before activating the new one.

2. **Three sources of assignment** (Q1 lock-in: paths B + C + D + spawn-with):
   - Editor-authored on the Character prefab/archetype (spawn-with).
   - Runtime scripted triggers — life events, dialogue completion, debug commands, quest completion hooks.
   - Player-driven assignment for owned/befriended NPCs via dev tools today, expandable to a permission-gated UI later.

3. **Important needs gate** (Q2 lock-in): ambition pursuit is blocked while any *important* need is `IsActive()`. The set of important needs is data-driven via `AmbitionSettings.GatingNeeds : List<NeedSO>` — for v1, populated with **Hunger** and **Sleep** only.

4. **Three-layer hierarchy**: `Ambition → Quest → Task`. An `AmbitionSO` declares an ordered list of `QuestSO`; a `QuestSO` declares an ordered/parallel/any-of list of `TaskBase` instances; a `TaskBase` is a polymorphic, parameterized atomic verb. Each layer is independently authored and reusable.

5. **`IQuest` reuse, not migration** (Q3 lock-in: option a + additive): each ambition step quest is an `AmbitionQuest : IQuest` runtime instance, lives in `CharacterQuestLog`, gets save/load + network sync + dev-inspector free. Existing `IQuest` subtypes (`BuildingTask`, `BuyOrder`, `TransportOrder`, `CraftingOrder`) are **not migrated** to the Task layer — that's a future refactor candidate, deferred to backlog.

6. **Per-ambition `OverridesSchedule` flag** (Q4 lock-in: option c): `AmbitionSO.OverridesSchedule : bool`. False (default) → ambition pursuit yields to the NPC's scheduled work shift. True → ambition pursuit can run during scheduled hours. Enforced inside `BTCond_CanPursueAmbition`, not by tree reordering.

7. **Parameter slots + runtime context bag** (Q5 lock-in: option a): `AmbitionSO.Parameters` declares typed input slots filled at `SetAmbition` time. `AmbitionContext` carries a runtime read/write dictionary chained Quest → Task. Step 1 of "Have a Family" writes `context["Lover"] = chosenChar`; Step 2 reads it via a `ContextBinding<Character>("Lover")` parameter binding.

8. **Manual-only lifecycle + history** (Q6 lock-in: option b): only three terminal transitions — auto-`Completed` on last step done, manual `ClearAmbition()` from script, manual `SetAmbition` while Active (which clears the old). No auto-fail, no timers, no urgency curves. **History list of `CompletedAmbition` records is kept on the Character** with a `Reason` enum (`Completed`, `ClearedByScript`, with `Failed` reserved for future).

9. **Server-authoritative mutations** (rule #18). `SetAmbition`, `ClearAmbition`, step advancement, history mutation all execute on the server. Clients route via ServerRpc; state replicates back via `NetworkVariable<NetworkAmbitionSnapshot>` + `ClientRpc` snapshot fan-out. `History` is server-authoritative and synced on demand only.

10. **Persisted via `ICharacterSaveData<AmbitionSaveData>`** (rule #20). `CharacterAmbition` saves the active instance + history. SO references stored as asset GUID, character references stored as `CharacterId` UUID, deferred-bind queue handles hibernated targets per the existing `CharacterRelation` pattern.

11. **Seamless controller-switch handoff**. The same Character carries both `PlayerController` and `NPCController` (only one enabled at a time, `Character.cs:649`). Mid-ambition `SwitchToPlayer` → BT stops driving, `CharacterQuestLog`'s active step quest auto-renders in the player's HUD, completion listeners stay subscribed, no progress reset. `SwitchToNPC` → BT resumes via idempotent `Task.Tick`. New `TaskBase.OnControllerSwitching(Character, ControllerKind)` virtual method handles in-flight driver cleanup (queued `CharacterAction`s, transient GOAP goals).

12. **Documentation shipped alongside code** (rules #28, #29, #29b): new `.agent/skills/ambition-system/SKILL.md`, new `wiki/systems/ambition-system.md`, updates to `wiki/systems/ai-goap.md` (the existing "ultimate goals" examples now redirect to the Ambition system), updates to `wiki/systems/quest-system.md` (cross-link to AmbitionQuest as a new IQuest subtype), and a new `.claude/agents/ambition-system-specialist.md` if the system grows past five interconnected scripts (per rule #29 evaluation).

### Non-Goals

- **Auto-fail predicates** (Q6 option c). If an ambition becomes impossible (target dead, lover relocated to a different map permanently), it stalls as a "zombie ambition" until a script calls `ClearAmbition()`. The `CompletionReason.Failed` enum value is reserved for a future iteration.
- **Time-bound or urgency-escalating ambitions.** No expiration, no priority promotion based on age.
- **Multiple concurrent ambitions per Character.** One active at a time. Stacking and prioritization are a future spec.
- **Branching quest chains within an ambition.** v1 supports linear (sequential), parallel (all-of), and any-of orderings inside a single Quest's task list — but the **Quest sequence within an ambition is strictly linear**. Branching ambition trees are a future feature.
- **Migration of existing `IQuest` subtypes onto the Task layer.** `BuildingTask`, `BuyOrder`, `TransportOrder`, `CraftingOrder` stay hand-coded for v1. A future refactor can give them `QuestSO` shells with Task lists, unifying the two pipelines, but it's intentionally out of v1 scope.
- **Ambition-driven dialogue.** "Bob just achieved his life goal of murdering Garrick" appearing in NPC dialogue is enabled by the History field (data exists), but no dialogue trigger system is shipped in this spec.
- **Reputation / faction effects from ambition completion.** History data is exposed; consumers of that data are out of scope here.
- **Auto-assigned ambitions at NPC spawn weighted by personality.** Rejected during Q1 — only spawn-with via prefab/archetype authoring is supported. A future "personality-driven random assignment" system can layer on top of `SetAmbition` without touching the core.
- **Macro-simulator advancement of ambitions during hibernation.** `MacroSimulator` does not tick ambitions in v1 — hibernated NPCs simply pause their pursuit. Compatible with the patient/untimed model from requirement 8.
- **Pruning / capping of `History` list.** Records accumulate forever per Character. If profiling shows this matters, add an LRU cap as a later optimization (logged in `wiki/projects/optimisation-backlog.md`).

---

## Architecture Overview

### Approach: New `CharacterAmbition` subsystem + new Task layer below `IQuest` + new BT branch

The ambition system is composed of four cleanly-bounded units. Each has one purpose, communicates via well-defined interfaces, and is independently testable (rule #9 SOLID, rule #1 design-for-isolation).

| Unit | Responsibility | Owns |
|---|---|---|
| `AmbitionSO` (+ `QuestSO`, `TaskBase`) | Authored design data: chain shape, parameter slots, task primitives | Static SO assets in `Assets/Resources/Data/Ambitions/` |
| `IAmbitionStepQuest extends IQuest` | Bridge contract: `IQuest` for save/sync/UI, plus behavior methods (`BindContext`, `Tick`, `Cancel`, `OnControllerSwitching`) | Runtime per-step state |
| `CharacterAmbition` | Per-character orchestrator: current `AmbitionInstance`, history, `Set`/`Clear`/advance API, events, save/load | Runtime character state |
| `BTAction_PursueAmbitionStep` (+ `BTCond_CanPursueAmbition`) | BT integration: gate + tick the active step task on the NPC | Behavior driver |

```
[AmbitionSO]                                         [QuestSO]                                  [TaskBase]
   • Steps: List<QuestSO>                               • Tasks: [SerializeReference] List<TaskBase>      polymorphic, inline-authored
   • Parameters: List<AmbitionParameterDef>             • Ordering: Sequential | Parallel | AnyOf            via [SerializeReference]
   • OverridesSchedule: bool                            • DisplayName, Description                          subclasses:
                                                                                                           Task_KillCharacter
                                                                                                           Task_TalkToCharacter
                                                                                                           Task_MoveToZone
                                                                                                           Task_HarvestTarget
                                                                                                           Task_DeliverItem
                                                                                                           Task_GatherItem
                                                                                                           Task_WaitDays

       │                                                    │                                                    │
       ▼                                                    ▼                                                    ▼

[CharacterAmbition]  ────► AmbitionInstance ────► IAmbitionStepQuest (= AmbitionQuest : IQuest)
   • Current                  • SO + Context           ──── lives in CharacterQuestLog ────► [Player HUD] (existing)
   • History                  • CurrentStepIndex                                              [Save / Sync]   (existing IQuest layer)
   • Set / Clear / Events     • CurrentStepQuest                                              [Dev Inspector] (existing)
   • OnAmbitionSet
   • OnStepAdvanced                                            │
   • OnAmbitionCompleted                                       ▼                  [server-only, ticked when NPC controller active]
   • OnAmbitionCleared                              [BTAction_PursueAmbitionStep]
                                                       gated by BTCond_CanPursueAmbition (priority 5.5)
                                                       calls IAmbitionStepQuest.Tick → TaskBase.Tick
                                                                                                                 │
                                                                                                                 ▼
                                                                                                  [CharacterAction queue]   (Pattern A)
                                                                                                  [GOAP transient goal]      (Pattern B)
```

### Data ownership

| Data | Owner | Persisted? | Synced? |
|---|---|---|---|
| `AmbitionSO`, `QuestSO`, `TaskBase` SO content | Asset registry (lazy-init per static-registry pattern) | Asset (committed) | No |
| `Current` (active `AmbitionInstance`) | `CharacterAmbition` | Yes (`AmbitionSaveData`) | Yes (compact NetworkVariable + ClientRpc snapshot) |
| `History` (list of `CompletedAmbition`) | `CharacterAmbition` | Yes | Server-authoritative; on-demand RPC |
| Mid-task `TaskState` | Each `TaskBase` instance | Yes (via `TaskBase.SerializeState/DeserializeState`) | Subset only (whatever the task chooses) |
| BT node state (per-tick) | `BTAction_PursueAmbitionStep` | No | Server-only |

### File / asset layout

```
Assets/
├── Scripts/Character/Ambition/
│   ├── CharacterAmbition.cs                ← root subsystem (CharacterSystem + ICharacterSaveData<AmbitionSaveData>)
│   ├── AmbitionSO.cs                       ← base ScriptableObject + AmbitionParameterDef
│   ├── AmbitionInstance.cs                 ← runtime state class
│   ├── AmbitionContext.cs                  ← typed bag wrapper
│   ├── CompletedAmbition.cs                ← history record
│   ├── AmbitionRegistry.cs                 ← lazy-init registry (late-joiner-safe)
│   ├── AmbitionSettings.cs                 ← global config SO (gating needs list)
│   ├── AmbitionSaveData.cs                 ← DTOs (AmbitionSaveData, ContextEntryDTO, CompletedAmbitionDTO, TaskStateDTO)
│   ├── ParameterBindings/
│   │   ├── TaskParameterBinding.cs
│   │   ├── StaticBinding.cs
│   │   ├── ContextBinding.cs
│   │   └── RuntimeQueryBinding.cs
│   ├── Quests/
│   │   ├── QuestSO.cs                      ← authored quest container
│   │   ├── AmbitionQuest.cs                ← IQuest runtime instance produced from a QuestSO
│   │   └── IAmbitionStepQuest.cs           ← bridge interface
│   └── Tasks/
│       ├── TaskBase.cs                     ← polymorphic base ([Serializable])
│       ├── Task_KillCharacter.cs
│       ├── Task_TalkToCharacter.cs
│       ├── Task_MoveToZone.cs
│       ├── Task_HarvestTarget.cs
│       ├── Task_DeliverItem.cs
│       ├── Task_GatherItem.cs
│       └── Task_WaitDays.cs
├── Scripts/AI/BehaviourTree/Conditions/
│   └── BTCond_CanPursueAmbition.cs
├── Scripts/AI/BehaviourTree/Actions/
│   └── BTAction_PursueAmbitionStep.cs
├── Scripts/UI/Ambition/
│   └── UI_AmbitionTracker.cs
├── UI/Ambition/
│   └── UI_AmbitionTracker.prefab
├── Resources/Data/Ambitions/
│   ├── Ambition_HaveAFamily.asset
│   ├── Ambition_Murder.asset
│   ├── Ambition_HaveTwoKids.asset
│   ├── AmbitionSettings.asset
│   └── Quests/
│       ├── Quest_FindLover.asset
│       ├── Quest_HaveChildWithLover.asset
│       └── Quest_AssassinateTarget.asset
└── Tests/
    ├── EditMode/Ambition/...
    └── PlayMode/Ambition/...
```

---

## Data Model

### `AmbitionSO`

```csharp
public abstract class AmbitionSO : ScriptableObject {
    public string DisplayName;
    [TextArea] public string Description;
    public Sprite Icon;
    public bool OverridesSchedule;
    public List<QuestSO> Quests;                    // ordered chain
    public List<AmbitionParameterDef> Parameters;   // typed input slots

    public abstract bool ValidateParameters(IReadOnlyDictionary<string,object> p);
}

[Serializable]
public class AmbitionParameterDef {
    public string Key;
    public ContextValueKind Kind;                   // Character | Primitive | Enum | ItemSO | Zone
    public bool Required;
}
```

Concrete subclasses (e.g. `Ambition_HaveAFamily`, `Ambition_Murder`) live as separate SO assets. Subclasses override `ValidateParameters` to type-check their declared slots.

### `QuestSO`

```csharp
public enum TaskOrderingMode { Sequential, Parallel, AnyOf }

public class QuestSO : ScriptableObject {
    public string DisplayName;
    [TextArea] public string Description;
    [SerializeReference] public List<TaskBase> Tasks;
    public TaskOrderingMode Ordering = TaskOrderingMode.Sequential;
}
```

Tasks are inline-authored via Unity's `[SerializeReference]` polymorphism — designers pick the task subclass from a dropdown and fill its parameter bindings in the inspector. No per-task asset bloat.

### `TaskBase`

```csharp
[Serializable]
public abstract class TaskBase {
    public abstract void Bind(AmbitionContext ctx);
    public abstract TaskStatus Tick(Character npc, AmbitionContext ctx);
    public abstract void Cancel();
    public virtual void OnControllerSwitching(Character npc, ControllerKind goingTo) { }
    public virtual string SerializeState() => "";
    public virtual void DeserializeState(string s) { }
    public virtual void RegisterCompletionListeners(Character npc, AmbitionContext ctx) { }
    public virtual void UnregisterCompletionListeners(Character npc) { }
}

public enum TaskStatus { Running, Completed, Failed }   // Failed reserved (v1: never returned)
public enum ControllerKind { Player, NPC }
```

#### Task primitive set (v1)

| Task | Parameters | Completes when | Driver pattern |
|---|---|---|---|
| `Task_KillCharacter` | `Target : Character` | target's `OnDeath` fires | A — `CharacterAction_AttackTarget` enqueue |
| `Task_TalkToCharacter` | `Target : Character` | dialogue completion via `CharacterInteraction` | A — `CharacterAction_StartInteraction` enqueue |
| `Task_MoveToZone` | `Zone : IWorldZone` | NPC transform within zone radius | B — transient GOAP goal `inZone(Zone)` |
| `Task_HarvestTarget` | `Target : Harvestable` | `Harvestable.IsDepleted == true`, depleted by this NPC | A — `CharacterAction_Harvest` enqueue |
| `Task_DeliverItem` | `Item : ItemSO`, `Recipient : Character` | recipient inventory contains item, sourced from this NPC | B — transient GOAP goal `hasItem(Recipient, Item)` (GOAP plans gather + travel + hand-off via existing `CharacterAction_GiveItem`) |
| `Task_GatherItem` | `Item : ItemSO`, `Count : int` | NPC inventory has count of item | B — transient GOAP goal `hasItem(Item, ≥Count)` |
| `Task_WaitDays` | `Days : int` | calendar days advanced ≥ N | none — completion listener on `TimeManager.OnDayChanged` |

### Parameter binding

Each parameter slot on a Task is wrapped in a binding so it can come from one of three sources:

```csharp
public abstract class TaskParameterBinding<T> {
    public abstract T Resolve(AmbitionContext ctx);
}

public class StaticBinding<T> : TaskParameterBinding<T> {
    public T Value;
}

public class ContextBinding<T> : TaskParameterBinding<T> {
    public string Key;
    public override T Resolve(AmbitionContext ctx) => ctx.Get<T>(Key);
}

public class RuntimeQueryBinding<T> : TaskParameterBinding<T> {
    public string WriteKey;     // optional — write resolved value back into context for downstream tasks
    public abstract T Resolve(AmbitionContext ctx);   // subclass implements query (e.g. eligibleLover)
}
```

Concrete `RuntimeQueryBinding` subclasses (e.g. `EligibleLoverQuery`) live next to the Task that uses them.

### `AmbitionContext`

```csharp
public sealed class AmbitionContext {
    private readonly Dictionary<string, object> _values = new();

    public T Get<T>(string key) {
        if (!_values.TryGetValue(key, out var v))
            throw new KeyNotFoundException($"Ambition context has no key '{key}'.");
        return (T)v;
    }

    public bool TryGet<T>(string key, out T value) { /* ... */ }

    public void Set<T>(string key, T value) {
        // Check the runtime type of the value, not the generic parameter T,
        // so that ctx.Set<object>("k", someCharacter) classifies as Character.
        var runtimeType = value?.GetType() ?? typeof(T);
        if (!IsSerializableValueKind(runtimeType))
            throw new InvalidOperationException(
                $"Ambition context value of type {runtimeType} is not serializable.");
        _values[key] = value;
    }

    public IReadOnlyDictionary<string,object> AsReadOnly() => _values;
}
```

Allowed value types (matched to `ContextValueKind`): `Character`, primitives (`int`, `float`, `bool`, `string`), enums, any `ScriptableObject` subclass with a stable asset GUID, and any `IWorldZone`. Any other type throws at `Set` time so authors hit the wall in the editor, not at production save time.

### `AmbitionInstance`

```csharp
public sealed class AmbitionInstance {
    public AmbitionSO SO;
    public int CurrentStepIndex;
    public IAmbitionStepQuest CurrentStepQuest;     // mirrors a CharacterQuestLog entry
    public AmbitionContext Context;
    public int AssignedDay;
}
```

### `CompletedAmbition`

```csharp
public enum CompletionReason {
    Completed,
    ClearedByScript,
    Failed                                          // reserved — never written by v1
}

public sealed class CompletedAmbition {
    public AmbitionSO SO;
    public AmbitionContext FinalContext;            // snapshot at completion
    public int CompletedDay;
    public CompletionReason Reason;
}
```

---

## Lifecycle & State Machine

### Top-level `CharacterAmbition` states

Two real states only: **Inactive** (`Current == null`) and **Active** (`Current != null`). Completion and clearing are *transitions*, not resident states — both push a `CompletedAmbition` into history and return to Inactive.

```
                  ┌──────────────────────────┐
                  │      Inactive (null)     │
                  └─────────────┬────────────┘
                                │ SetAmbition(so, params)
                                ▼
                  ┌──────────────────────────┐
                  │   Active(AmbitionInstance)│
                  └──┬─────────────────┬─────┘
                     │                 │
   last step quest   │                 │   ClearAmbition()  (or SetAmbition replacing)
   completes         │                 │
                     ▼                 ▼
        ┌────────────────────────────────────────────┐
        │  push CompletedAmbition into History       │
        │  (Reason = Completed | ClearedByScript)    │
        │  CurrentStepQuest cancelled + removed      │
        │  Active → Inactive                         │
        └────────────────────────────────────────────┘
```

### Transitions

| Transition | Trigger | Effect |
|---|---|---|
| `Inactive → Active` | `SetAmbition(so, params)` | Validate params, build `AmbitionInstance`, instantiate Step 0's `AmbitionQuest`, claim it in `CharacterQuestLog`, fire `OnAmbitionSet`. |
| `Active → Active` (same instance) | step quest's `OnStateChanged` reports `Completed` AND not last step | Advance `CurrentStepIndex`, instantiate next step's `AmbitionQuest` with the same context, claim it, fire `OnStepAdvanced`. |
| `Active → Inactive` (auto, success) | last step quest reports `Completed` | Snapshot context, push `CompletedAmbition { Reason = Completed }` to history, fire `OnAmbitionCompleted`, discard instance. |
| `Active → Inactive` (script, manual) | `ClearAmbition()` | Cancel `CurrentStepQuest` (removes from `CharacterQuestLog`), push `CompletedAmbition { Reason = ClearedByScript }`, fire `OnAmbitionCleared`, discard instance. |
| `Active → Inactive → Active` (replacement) | `SetAmbition(otherSO, params)` while Active | Internally: `ClearAmbition()` then the normal `Set` flow. The replaced ambition is recorded in history with `Reason = ClearedByScript`. |

#### Advancement source of truth

Step advancement is driven **only** by `CharacterAmbition`'s subscription to `IAmbitionStepQuest.OnStateChanged`. The BT shim (`BTAction_PursueAmbitionStep`) is a *report*, not a driver — when `Task.Tick` returns `Completed`, the shim returns `BTNodeStatus.Success` to the BT but does **not** call back into `CharacterAmbition` to advance the chain. The actual advancement happens via:

1. `Task.Tick` returns `Completed`.
2. The hosting `AmbitionQuest` rolls forward — when *all* its tasks are `Completed` per the `Ordering` policy, it marks itself `Completed` via `IQuest.SetState(Completed)`.
3. `IQuest.OnStateChanged` fires.
4. `CharacterAmbition`'s subscriber (registered in `SetAmbition`) advances `CurrentStepIndex` or transitions to history.

This guarantees the advance fires identically for NPC-driven completions (BT path) **and** player-driven completions (player kills the target manually — same `OnStateChanged` event, same advancement). One code path for both controllers.

### Quest / Task sub-states

While the top-level is `Active`:

```
Quest:  NotStarted → Running → Completed   (Cancelled if ClearAmbition fires mid-run)
Task:   Running → Completed                (Failed reserved — v1: never returned)
```

A `QuestSO` with `Ordering = Sequential` ticks one Task at a time. `Parallel` ticks all simultaneously and completes when all are `Completed`. `AnyOf` completes when any one Task does.

### Public API (`CharacterAmbition`)

```csharp
public sealed class CharacterAmbition : CharacterSystem, ICharacterSaveData<AmbitionSaveData> {
    // Mutations (server-authoritative; clients call via RPC).
    public void SetAmbition(AmbitionSO so, IReadOnlyDictionary<string,object> parameters = null);
    public void ClearAmbition();

    // Queries.
    public AmbitionInstance Current { get; }
    public bool HasActive => Current != null;
    public IReadOnlyList<CompletedAmbition> History { get; }
    public float CurrentProgress01 { get; }                // CompletedSteps / TotalSteps

    // Events.
    public event Action<AmbitionInstance> OnAmbitionSet;
    public event Action<AmbitionInstance, int> OnStepAdvanced;     // arg = newIndex
    public event Action<CompletedAmbition> OnAmbitionCompleted;
    public event Action<CompletedAmbition> OnAmbitionCleared;
}
```

---

## Behaviour Tree Integration

### Tree slot

Current order: `Orders → Combat → Assist → Aggression → Party Follow → Force Punch Out → Schedule (5) → GOAP (6) → Social → Wander`. Two new nodes plug in at priority 5.5:

```
…
├── Schedule (5)         ← BTCond_HasScheduledActivity → BTAction_Work
├── PursueAmbition (5.5) ← BTCond_CanPursueAmbition    → BTAction_PursueAmbitionStep   ← NEW
├── GOAP (6)
…
```

### `BTCond_CanPursueAmbition`

Returns true iff **all** of:

1. `Character.CharacterAmbition.HasActive == true`.
2. No need in `AmbitionSettings.GatingNeeds` is `IsActive()` (v1: Hunger, Sleep).
3. Either `Current.SO.OverridesSchedule == true`, OR `CharacterSchedule.CurrentActivity == None`.
4. The current step's `AmbitionQuest` is in `Running` state.

Pure boolean predicate, no allocations per tick (rule #34).

### `BTAction_PursueAmbitionStep`

Thin shim — delegates the actual decision to the active step's task tick:

```csharp
public override BTNodeStatus Execute(Character npc) {
    var step = npc.CharacterAmbition.Current?.CurrentStepQuest;
    if (step == null) return BTNodeStatus.Failure;

    var status = step.TickActiveTasks(npc);             // calls through IAmbitionStepQuest
    return status switch {
        TaskStatus.Running   => BTNodeStatus.Running,
        TaskStatus.Completed => BTNodeStatus.Success,   // CharacterAmbition's OnStateChanged listener
                                                        // advances the chain — see "Advancement source of truth"
        TaskStatus.Failed    => BTNodeStatus.Failure,   // v1: never returned
        _ => BTNodeStatus.Running,
    };
}
```

`TickActiveTasks` is on `IAmbitionStepQuest` (no concrete cast). The default implementation on `AmbitionQuest` follows the `QuestSO.Ordering` policy (sequential / parallel / any-of) and forwards to each `TaskBase.Tick`.

### Task → behavior translation patterns

Per rule #22, all gameplay flows through `CharacterAction`. Tasks have **two legitimate ways** to drive behavior:

Each task uses **exactly one** of the two patterns. The driver-pattern column in the primitive table (data model) is authoritative.

**Pattern A — direct `CharacterAction` enqueue** (preferred for one-shot tasks)

The Task owns the lifecycle of one or more `CharacterAction`s and reports up. Used by `Task_KillCharacter`, `Task_TalkToCharacter`, `Task_HarvestTarget` — anything that maps to a single existing action.

**Pattern B — transient GOAP goal injection** (for tasks needing planning across multiple sub-actions)

The Task pushes a transient `GoapGoal` into the planner (new method `CharacterGoap.RegisterTransientGoal(GoapGoal)`); the existing GOAP node at priority 6 picks it up next tick and plans the multi-action path. Used by `Task_GatherItem`, `Task_DeliverItem`, `Task_MoveToZone`. The new BT branch (5.5) sits *above* GOAP (6) but the Task it's running can drive GOAP to do the work — no conflict, the BT yields back, GOAP executes; when the goal is satisfied, the task completes on next tick.

### Controller-switch handoff

`CharacterAmbition` is identical for Player and NPC; only the consumer flips. State persistence on switch is automatic (subsystem lives on the root `Character`); the only new contract is in-flight driver cleanup:

```csharp
public abstract class TaskBase {
    public virtual void OnControllerSwitching(Character npc, ControllerKind goingTo) { }
}
```

- `Task_KillCharacter` (Pattern A) overrides to cancel the queued `CharacterAction_AttackTarget`.
- `Task_GatherItem` (Pattern B) overrides to call `CharacterGoap.UnregisterTransientGoal(_goal)` on switch to Player; the next NPC-side `Task.Tick` re-injects it idempotently.

`Character.SwitchToPlayer` and `SwitchToNPC` each gain one new line:

```csharp
CharacterAmbition.Current?.CurrentStepQuest.OnControllerSwitching(this, ControllerKind.Player);
```

placed before the existing `SwitchController<>` invocation.

### Idempotency requirement

`TaskBase.Tick(npc, ctx)` must be **idempotent w.r.t. the world state**: if the underlying condition is already met, return `Completed` immediately without re-acting; if a driver is already enqueued and still valid, return `Running` without re-enqueuing. This guarantees correct resumption after `SwitchToNPC` and after server reconciliation. Violations are bugs — caught by the `Task_Idempotency` unit test (one per subclass).

### Per-frame cost (rule #34 compliance)

- No allocations in any tick path. Cached buffers for list scans.
- No LINQ.
- Logs gated by `NPCDebug.VerboseAmbition` (new toggle, default off).
- Cache invalidation hooked into `OnAmbitionSet` / `OnStepAdvanced` — no per-tick polling of stale data.
- Server-only — `BTAction_PursueAmbitionStep` runs only when the BT runs (NPC controller enabled), which is server-side.

---

## Persistence

`CharacterAmbition` implements `ICharacterSaveData<AmbitionSaveData>` (rule #20). Loaded **after** `CharacterQuestLog`, `CharacterRelation`, `CharacterCommunity` (so context references resolve), **before** any HUD listener. `WorldZoneRegistry`, `AmbitionRegistry`, `QuestRegistry`, and any other SO registry consulted during reference resolution must be initialized **before** `CharacterAmbition.OnLoaded` runs — relies on the existing `GameLauncher.LaunchSequence` registry-init order plus the `feedback_lazy_static_registry_pattern.md` lazy `Get()` fallback for late-joining clients.

### Save DTO shape

```csharp
[Serializable]
public class AmbitionSaveData {
    public string ActiveAmbitionSOGuid;
    public List<ContextEntryDTO> Context;
    public int CurrentStepIndex;
    public List<TaskStateDTO> TaskStates;
    public int AssignedDay;
    public List<CompletedAmbitionDTO> History;
}

[Serializable]
public class ContextEntryDTO {
    public string Key;
    public ContextValueKind Kind;
    public string SerializedValue;
}

[Serializable]
public class CompletedAmbitionDTO {
    public string AmbitionSOGuid;
    public List<ContextEntryDTO> FinalContext;
    public int CompletedDay;
    public CompletionReason Reason;
}

[Serializable]
public class TaskStateDTO {
    public int TaskIndexInQuest;
    public string SerializedState;
}
```

### Reference resolution

| `ContextValueKind` | Stored as | Resolved via |
|---|---|---|
| `Character` | `CharacterId` (UUID, `Character.cs:321`) | `Character.FindByUUID(uuid)` (`Character.cs:358`) |
| `ItemSO` / `AmbitionSO` / `QuestSO` / `NeedSO` | Asset GUID | Per-type SO registry, lazy-init via `Get()` (late-joiner-safe pattern) |
| `IWorldZone` | Zone GUID | `WorldZoneRegistry.Get(guid)` |
| Primitives / enums | string-encoded | direct |

**Late-binding rule:** if a referenced `Character` is hibernated and `FindByUUID` returns null, the value is queued in a *deferred-bind queue* on `CharacterAmbition`. When `Character.OnCharacterSpawned` (`Character.cs:234`) fires, the queue retries resolution. Same pattern `CharacterRelation` uses for dormant relationships. If the referenced character is permanently deleted, the ambition is cleared on next tick with `Reason = ClearedByScript` and a logged warning.

### Mid-task state

Each `TaskBase` subclass that carries pursuit state (`Task_GatherItem._delivered`, `Task_TalkToCharacter._dialogChoice`) overrides `SerializeState` and `DeserializeState`. Stateless tasks pay zero cost.

### Network sync

- **Active state** — compact `NetworkVariable<NetworkAmbitionSnapshot>` (SO GUID + step index + progress01 + a small fixed-width context summary). Server-write, everyone-read.
- **Full DTO** — `ClientRpc` snapshot fan-out when a peer needs the rich state (own character on spawn, dev inspector). Mirrors the existing `CharacterQuestLog` snapshot pattern.
- **History** — server-authoritative, **not** synced by default. On-demand RPC for dev inspector / dialogue trigger consumers.

`AmbitionContext` values referencing other Characters use `NetworkObjectReference`-backed serialization, consistent with existing `IQuestTarget` wrappers.

### Hibernation

`HibernatedNPCData` gains one field:

```csharp
public AmbitionSaveData Ambition;
```

Populated by `CharacterDataCoordinator.Export` on hibernation despawn, restored on respawn. `MacroSimulator` does **not** advance ambitions during hibernation in v1 — patient/untimed model means hibernated NPCs simply pause.

### Load-time safety check

After all SO references resolve, `CharacterAmbition.OnLoaded` runs:

1. `_current.SO.ValidateParameters(_current.Context)`.
2. Verifies all step-task `ContextBinding` keys are present in the loaded context.

On failure: `Debug.LogError` with the offending GUID/key, clear the ambition (`Reason = ClearedByScript`), continue. Better to lose a zombie ambition than crash on load.

---

## Player UI Exposure

### Active step quest — zero new wiring

The current ambition step is a regular `IQuest` (`AmbitionQuest`) in the player's `CharacterQuestLog`. The existing quest tracker HUD already renders claimed quests — the ambition step shows up there automatically with the `QuestSO`'s authored title/description. No HUD changes for step-quest display.

`AmbitionQuest.IsAmbitionStep` returns true so the HUD can sort/tag/hide ambition steps from ordinary job quests. v1 ships with no special styling; UI polish is deferred to a future HUD pass.

### `UI_AmbitionTracker` widget

One small persistent HUD element (~150 lines) next to the existing quest tracker:

```
┌─ Have a Family ────────────────────┐
│  ● Find a lover            [done]  │
│  ○ Have a child with Alice         │
│  ────────────────                  │
│  Progress: 1 / 2 (50%)             │
└────────────────────────────────────┘
```

- Subscribes to `CharacterAmbition.OnAmbitionSet` / `OnStepAdvanced` / `OnAmbitionCompleted` / `OnAmbitionCleared`.
- Reads `CurrentProgress01` for the bar.
- Hidden when `HasActive == false`.
- Uses unscaled time per rule #26.
- Lives in `Assets/UI/Ambition/UI_AmbitionTracker.prefab`.

### Dev-Mode inspector — `AmbitionInspectorView`

New tab on the Character inspector (per `.claude/agents/debug-tools-architect.md`):

- Active ambition: SO name, step index, parameter values, context bag entries, `OverridesSchedule` flag.
- Active step quest: name, current task index, each task's `Status` + `SerializeState()`.
- History list: each `CompletedAmbition` with reason and day.
- Buttons: **Set Ambition** (dropdown of `AmbitionRegistry`), **Clear Ambition**, **Force Advance Step** (debug-only, skips current task).

Wired through the existing `CharacterInspectorView` + `CharacterSubTab` infrastructure. The "Set Ambition" dropdown is the testing entry point and serves as path **D** from Q1 (player-assignable via dev tools).

### Player input ownership (rule #33)

The Ambition system adds **zero new player input**. The player progresses ambition steps the same way they progress any quest — by performing the underlying actions through their normal input. The dev-mode "Set Ambition" button is UI-targeting input (button click), which is allowed; if a production hotkey is ever added, it lives in `PlayerController`.

---

## Testing Strategy

### EditMode unit tests

Located in `Assets/Tests/EditMode/Ambition/`. Run via `tests-run` MCP tool.

| Test | Coverage |
|---|---|
| `Ambition_StateMachine_HappyPath` | `Inactive → Active → step advance → Active → Completed`; history has one record with `Reason = Completed`. |
| `Ambition_StateMachine_ClearMidRun` | `SetAmbition → ClearAmbition`; `CurrentStepQuest` removed from `CharacterQuestLog`; history has `Reason = ClearedByScript`. |
| `Ambition_StateMachine_Replace` | `SetAmbition(A) → SetAmbition(B)`; A in history as Cleared; B is Current. |
| `Ambition_ParamValidation_Rejects` | `SetAmbition(Murder, params=null)` no-ops + logs error. |
| `Ambition_Context_StepWriteRead` | Step 1 writes context key, Step 2 reads it. |
| `Ambition_Context_SerializableValueOnly` | `Context.Set` of a non-serializable type throws. |
| `Ambition_Progress01` | Two-step ambition: 0.0 → 0.5 → 1.0 across step transitions. |
| `Task_Idempotency` (one per `TaskBase` subclass) | `Tick` twice with met-condition returns `Completed` twice; with unmet-condition does not double-enqueue. |
| `Task_SerializeRoundTrip` (one per stateful task) | `DeserializeState(SerializeState())` reproduces same `Tick` behavior. |
| `Quest_Ordering_Sequential_AdvancesOnEachComplete` | Three sequential tasks; quest advances task-by-task; `Completed` only on the third. |
| `Quest_Ordering_Parallel_CompletesWhenAllDone` | Three parallel tasks; all tick simultaneously; quest `Completed` only when all three are. |
| `Quest_Ordering_AnyOf_CompletesOnFirst` | Three any-of tasks; quest `Completed` on the first task to finish; remaining tasks `Cancel`-led. |
| `BTCond_CanPursueAmbition` | Truth table: 32 combinations of `(HasActive, HungerActive, SleepActive, ScheduleActive, OverridesSchedule)`. |

### PlayMode integration tests

Located in `Assets/Tests/PlayMode/Ambition/`.

| Test | Coverage |
|---|---|
| `BT_PursuesAmbition_WhenNeedsMet` | Spawn NPC, set ambition, force needs satisfied, schedule free; `BTAction_PursueAmbitionStep` runs and the bound `Task.Tick` is called. |
| `BT_YieldsToHungerNeed` | Set ambition, drop Hunger below `IsActive` threshold; BT runs needs branch. |
| `BT_RespectsOverridesScheduleFlag` | Two parallel runs, only the flag flipped; work shift wins in one, ambition in the other. |
| `Save_RoundTrip_ActiveAmbition` | Set + advance + save + load; active state, context, step index, history, mid-task `TaskStateDTO` all restored. |
| `Save_DeferredCharacterBind` | Save with context referencing hibernated character; load; respawn target; ambition resumes. |
| `Save_OrphanedSO_GracefullyClears` | Save with ambition referencing SO X; remove X from registry; load; ambition cleared with logged warning, no crash. |
| `ControllerSwitch_NPCToPlayerToNPC` | NPC mid-`Task_KillCharacter` (target at 50% HP); switch to Player; player kills target via input; switch back; next-step quest issued. |
| `ControllerSwitch_DriverCleanup` | NPC with `Task_GatherItem` in flight (transient GOAP goal injected); switch to Player; `OnControllerSwitching` fired and goal removed from planner. |

### Multiplayer matrix (rule #19)

Each scenario covers save-load + assignment + completion:

| Scenario | Test |
|---|---|
| **Host↔Client** | Player on Host has ambition; Client late-joins; Client receives the active `AmbitionInstance` snapshot, `UI_AmbitionTracker` populates. |
| **Client↔Client** | Two Clients each with ambitions; each renders only their own tracker; server-authoritative completion fires correct `OnAmbitionCompleted` to the right peer. |
| **Host/Client↔NPC** | NPC with editor-authored ambition (path C); both Host and Client see same progress in dev inspector; `BTAction_PursueAmbitionStep` ticks server-side only. |
| **History on-demand RPC** | Client opens dev inspector for a remote NPC; History RPC fires; full history list received. |

### Coverage / quality bars

- All `BTCond_CanPursueAmbition` paths: 100%.
- All state machine transitions: 100%.
- One idempotency test per `TaskBase` subclass.
- One save round-trip test per `TaskBase` overriding `SerializeState`.
- No test relies on `Time.deltaTime` — uses `TimeManager.Advance(dt)` per rule #26.
- All BT-touching tests run with `NPCDebug.VerboseAmbition = false` to verify no log-spam path is hot per rule #34.

### Manual smoke (post-implementation, before merge)

1. Fresh village, pick a farmer NPC, dev-inspector → `Set Ambition: Ambition_HaveAFamily`. Watch — eats first when hungry, courts after.
2. Same farmer, `Ambition_Murder` with another NPC as `Target` and `OverridesSchedule = true`. Watch — leaves shift to chase target.
3. Mid-pursuit, `Character.SwitchToPlayer` via dev tool. HUD tracker appears with current progress. Walk to target, finish kill manually. `Character.SwitchToNPC`. Ambition `Completed`, discarded, history has the entry.

---

## Out-of-Scope / Future Work

Logged for the optimisation/refactor backlog (`wiki/projects/optimisation-backlog.md` and future spec drafts):

1. **Auto-fail predicates** (`CompletionReason.Failed` populated). Each `TaskBase` declares `IsStillPossible()`; if false, ambition transitions Active → Failed. Migration cost: one enum value already reserved + one virtual method.
2. **Migration of existing IQuest subtypes onto the Task layer.** `BuildingTask` becomes a `QuestSO` with `[Task_HarvestTarget]`, `TransportOrder` becomes a `QuestSO` with `[Task_GatherItem, Task_DeliverItem]`, etc. Unifies the two pipelines.
3. **Personality-driven random ambition assignment at NPC spawn.** Layered on top of `SetAmbition` — no core changes needed.
4. **Branching ambition trees.** `AmbitionSO.Quests` becomes a graph instead of a list. Significant scope; needs its own spec.
5. **Multiple concurrent ambitions per Character.** Stacking + prioritization model. Future spec.
6. **Macro-simulator advancement during hibernation.** Compatible with the patient model only if specific tasks opt in (e.g. `Task_WaitDays`).
7. **Reputation / dialogue / faction effects driven by `History`.** Data exists, consumers don't.
8. **History pruning / LRU cap.** If profiling shows it matters.
9. **Dynamic mid-pursuit target reselection.** Today, if `Quest_FindLover` has selected Alice and Alice dies, the ambition zombies. A future iteration could re-run the `RuntimeQueryBinding` to pick a new lover.
10. **Cross-map ambitions.** v1 is single-map per ambition step; cross-map context bindings will need an `OriginWorldId` / `OriginMapId` field.

---

## References

- Q&A brainstorming session, 2026-05-02 (Q1–Q6 lock-ins documented in Requirements section).
- `2026-04-23-quest-system-design.md` — Quest System (the `IQuest` infrastructure this spec reuses).
- `2026-04-26-character-order-system-design.md` — `CharacterOrders` (precedent for server-authoritative + NetworkList sync + `ICharacterSaveData`).
- `2026-04-21-living-world-phase-1-design.md` — Hibernation + `MacroSimulator` (catch-up model).
- `Character.cs:649` (`IsPlayer()`), `Character.cs:853` (`SwitchToPlayer`), `Character.cs:869` (`SwitchToNPC`), `Character.cs:321` (`CharacterId`), `Character.cs:358` (`FindByUUID`), `Character.cs:234` (`OnCharacterSpawned`).
- `wiki/systems/ai-goap.md:69-72` — pre-existing "ultimate goal" examples (StartAFamily, BestMartialArtist, FinancialAmbition); will be updated post-implementation to redirect to the Ambition system.
- `.agent/skills/goap/SKILL.md:14-19` — same.
- Project rules #1, #9, #18, #19, #20, #22, #26, #28, #29, #29b, #33, #34.
- Memory: `feedback_lazy_static_registry_pattern.md`, `feedback_network_client_sync.md`, `feedback_charactersaveable_audit.md`, `feedback_player_npc_parity.md`, `feedback_input_handling_in_playercontroller.md`.
