# Ambition System — Resume Note

> Pick this up in a fresh conversation. Point your new session at this file plus the plan and spec; you'll have everything you need to continue.

**Branch:** `multiplayyer` (continue here — do not branch off).
**Plan:** [docs/superpowers/plans/2026-05-02-ambition-system.md](2026-05-02-ambition-system.md) (58 tasks, 14 phases).
**Spec:** [docs/superpowers/specs/2026-05-02-ambition-system-design.md](../specs/2026-05-02-ambition-system-design.md).

## Current state — Phase 4 complete (Tasks 0–17 / 58)

**Last commit:** `913e1ed4` (Task 17 — Quest ordering NUnit tests).

**Phase 1 commits:**

| SHA | Task | Summary |
|---|---|---|
| `7a614674` | 0 | scaffold `Assets/Tests/EditMode/Ambition/` |
| `10362092` | 1 | `ContextValueKind` enum |
| `76afaa33` | 2 | `CompletionReason`, `ControllerKind`, `TaskStatus` enums |
| `27803869` | 3 | `AmbitionContext` + 5/5 NUnit tests (in `MWI.Ambition.Pure` asmdef — see deviation below) |
| `6921acf8` | 4–7 | `AmbitionSO` body + `AmbitionInstance` + `CompletedAmbition` + `IAmbitionStepQuest` + `QuestSO` stub (bundle commit) |
| `c04691bf` | 8 | `TaskOrderingMode` + `TaskBase` polymorphic base |
| `d8661475` | 9 | full `QuestSO` body |
| `d93b5199` | docs | resume note after Phase 1 |

**Phase 2 commits:**

| SHA | Task | Summary |
|---|---|---|
| `6fa0b0b5` | chore | catch up 11 orphaned `.meta` files from Phase 1 commits |
| `674bef2f` | 10 | `TaskParameterBinding<T>` + `StaticBinding<T>` (initially in Assembly-CSharp `Bindings/`) |
| `16b9c324` | 11 | `ContextBinding<T>` + 3 NUnit tests; **bindings migrated to `Pure/`** (see deviation #2 below) |
| `148a5c73` | chore | remove orphaned `Bindings/` folder after migration |
| `3cc72b13` | 11+ | extra `ContextBinding` tests covering null-context and empty-key branches (5/5 pass) |
| `74dca048` | 12 | `RuntimeQueryBinding<T>` re-creating `Bindings/` under Assembly-CSharp (per architectural rule below) |
| `5d9ca78a` | docs | resume note update after Phase 2 |

**Phase 3 commits:**

| SHA | Task | Summary |
|---|---|---|
| `56d571e8` | 13 | `AmbitionRegistry` lazy-init (Assembly-CSharp) |
| `f16854f5` | 14 | `QuestRegistry` lazy-init (Assembly-CSharp) |
| `712502ba` | 15 | `AmbitionSettings` — **plan deviation:** `List<string>` of need type-names instead of `List<NeedSO>` (see deviation #3 below) |
| `98644070` | docs | resume note update after Phase 3 |

**Phase 4 commits:**

| SHA | Task | Summary |
|---|---|---|
| `a5709ae8` | 16 | `AmbitionQuest` bridging `IAmbitionStepQuest` → `IQuest`. **Plan deviation:** plan's `IQuest` skeleton (Title/Description/Issuer/Receiver/State/IsAmbitionStep + OnStateChanged) was OUT OF DATE; the real interface has 19 members (no Receiver). Plan's `QuestState.NotStarted/Running/Cancelled` enum values don't exist — remapped to `Open / Completed / Abandoned`. See deviation #4. |
| `913e1ed4` | 17 | Quest ordering NUnit tests in **new asmdef `AmbitionQuest.Tests`** at `Assets/Tests/EditMode/AmbitionQuest/`. Uses the project's IL-emission + reflection pattern (see deviation #5) — the only working pattern for testing Assembly-CSharp types from EditMode. 3/3 pass. |

**Foundation state:** Compile-clean. Two test asmdefs now exist:
- `Ambition.Tests` (Pure-only): 10/10 green (5 `AmbitionContextTests` + 5 `ContextBindingTests`).
- `AmbitionQuest.Tests` (Assembly-CSharp via reflection): 3/3 green (`QuestOrderingTests`).

Total: 13/13 green across all Ambition-related tests.

## Architectural deviations locked in (Phase 1 + Phase 2)

### Deviation #1 — `AmbitionContext` lives in a new `MWI.Ambition.Pure` assembly definition

- Folder: `Assets/Scripts/Character/Ambition/Pure/` with its own `.asmdef`.
- `MWI.Ambition.Pure.asmdef` has `autoReferenced: true`, so Assembly-CSharp picks it up automatically — callers don't need to know.
- The plan's original `IsSerializableValueKind` used `typeof(MWI.WorldSystem.IWorldZone).IsAssignableFrom(t)` which would have compile-errored from a Pure assembly (no Assembly-CSharp reference allowed). The implementer (Task 3) replaced it with `t.GetInterfaces()` + string FullName check — same pattern already used for `Character`. Behavior identical.
- Test asmdef `Ambition.Tests.asmdef` has `overrideReferences: true` + `autoReferenced: false` and references `MWI.Ambition.Pure` only. **Critical consequence:** Unity asmdefs cannot reference `Assembly-CSharp` by name (it is the no-asmdef catch-all), and the override + auto-ref-off configuration above means the Tests asmdef cannot see ANY Assembly-CSharp type. To unit-test a type, that type must live in `MWI.Ambition.Pure`.

### Deviation #2 — Binding hierarchy split across asmdefs (Phase 2 lock-in)

The plan's original Tasks 10–12 placed all binding types in `Assets/Scripts/Character/Ambition/Bindings/` (Assembly-CSharp). That breaks unit testing for `ContextBinding<T>` (Task 11) per Deviation #1's test-asmdef constraint. **The locked-in split:**

| Type | Asmdef | Folder |
|---|---|---|
| `TaskParameterBinding<T>` (abstract base) | `MWI.Ambition.Pure` | `Assets/Scripts/Character/Ambition/Pure/` |
| `StaticBinding<T>` | `MWI.Ambition.Pure` | `Assets/Scripts/Character/Ambition/Pure/` |
| `ContextBinding<T>` | `MWI.Ambition.Pure` | `Assets/Scripts/Character/Ambition/Pure/` |
| `RuntimeQueryBinding<T>` (abstract, references `Character`) | Assembly-CSharp | `Assets/Scripts/Character/Ambition/Bindings/` |
| `EligibleLoverQuery` (Task 49) and other concrete query subclasses | Assembly-CSharp | (next to the Task that uses them) |

**Why it works:** `MWI.Ambition.Pure.asmdef` has `autoReferenced: true`, so Assembly-CSharp picks up its types. `RuntimeQueryBinding<T>` (Assembly-CSharp) inheriting from `TaskParameterBinding<T>` (Pure) is a valid one-way reference — no circular dependency.

**Note on `Character` namespace:** `Character.cs` has no `namespace` declaration, so `Character` resolves from any Assembly-CSharp file with no `using`. Verified during Task 12.

### The general rule going forward

**When adding any new type, decide per-type whether it belongs in Pure or Assembly-CSharp:**

- **Pure** — pure-logic types with no Unity, no `Character`, no SO references, no `MonoBehaviour`/`NetworkBehaviour`. They can be unit-tested directly from `Ambition.Tests`.
- **Assembly-CSharp** — anything with Unity dependencies (Character, MonoBehaviour, ScriptableObject types, NetworkBehaviour, NavMesh, etc.). Cannot be unit-tested by `Ambition.Tests`; integration-test in PlayMode instead.

**Most downstream types (CharacterAmbition, AmbitionQuest, all Tasks, BT nodes, save DTOs that reference SO assets, NetworkAmbitionSnapshot) belong in Assembly-CSharp.** Pure should only grow if there's pure-logic worth unit-isolating.

### Important #2 from Task 12 code review — addressed and rejected

The Task 12 code reviewer claimed `cached != null` and `picked != null` are "compile-time false for any `T : struct`", suggesting the cache would be re-queried every tick for value-type bindings. **Verified false via Roslyn execute on 2026-05-02:** for unconstrained generic `T`, `value != null` evaluates to `true` for both `int(7)` and `int(0)` (the runtime boxes the value-type and the box is non-null). The `!= null` check is a redundant no-op for value types but causes no bug. Cache lookup works correctly. No action needed.

### Deviation #3 — `AmbitionSettings.GatingNeedTypeNames` uses `List<string>`, not `List<NeedSO>` (Phase 3 lock-in)

The plan's Task 15 referenced a `NeedSO : ScriptableObject` class. **Verified during Phase 3: this class does not exist in the project.** `CharacterNeed` is a plain abstract C# class in the global namespace, instantiated per-character via constructor. The existing project convention (visible in `CharacterNeeds.Deserialize` at `Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs:310`) uses `GetType().Name` (string type names) for need lookup.

**Adapted shape (committed in `712502ba`):**

```csharp
[SerializeField] private List<string> _gatingNeedTypeNames = new List<string> { "NeedHunger", "NeedSleep" };
public IReadOnlyList<string> GatingNeedTypeNames => _gatingNeedTypeNames;
```

**Downstream impact — Task 39 (`BTCond_CanPursueAmbition`):** the plan's predicate uses `AmbitionSettings.GatingNeeds` and iterates `NeedSO.IsActive()`. Adapt to:

```csharp
foreach (var typeName in AmbitionSettings.Instance.GatingNeedTypeNames)
{
    foreach (var need in npc.CharacterNeeds.AllNeeds)
    {
        if (need.GetType().Name == typeName && need.IsActive())
            return false; // gating need is active → cannot pursue ambition
    }
}
return true;
```

`CharacterNeeds.AllNeeds : List<CharacterNeed>` is the public list (verified at `CharacterNeeds.cs:11`). Per rule #34 (no per-frame allocations), the BT condition is called every tick — keep this loop allocation-free (no LINQ, no `string.Equals` overload that allocates, no boxing).

**Future hardening (logged for the optimisation backlog, not blocking):** consider caching the resolved `CharacterNeed` instance per-typename per-character to avoid the inner-loop type-name comparison on every BT tick. Currently it's O(N×M) where N = gating needs (~2) and M = character needs (~5), so ~10 string comparisons per tick — acceptable for v1 but a candidate for cache invalidation if needs grow.

### Important #1 from Task 12 code review — deferred

The Task 12 code reviewer flagged that `RuntimeQueryBinding.ResolveWithCharacter` silently no-ops the cache write when `WriteKey` is empty. This is plan-prescribed behavior — `WriteKey` is documented as optional. A `Debug.LogWarning` would either spam (fired every successful query when `WriteKey` is intentionally empty) or require per-instance state tracking. **Better path:** address in a future hardening pass with editor-time validation rather than runtime logging. Not blocking.

### Deviation #4 — `AmbitionQuest` had to remap to the real `IQuest` shape (Phase 4 lock-in)

The plan's Task 16 code block (around line 1305 of `2026-05-02-ambition-system.md`) was based on `2026-04-23-quest-system-design.md`, but the **shipped** `MWI.Quests.IQuest` (at `Assets/Scripts/Quest/IQuest.cs`) is significantly larger. Lock-in adaptations:

| Plan member | Actual interface | Resolution |
|---|---|---|
| `Receiver` | absent | dropped entirely |
| `Title`, `Description`, `Issuer`, `State`, `OnStateChanged` | all present | kept as-is |
| (none) | `QuestId`, `OriginWorldId`, `OriginMapId`, `Type`, `InstructionLine`, `IsExpired`, `RemainingDays`, `TotalProgress`, `Required`, `MaxContributors`, `Contributors`, `Contribution`, `TryJoin`, `TryLeave`, `RecordProgress`, `Target`, `OnProgressRecorded` | added with sensible ambition-quest defaults (see below) |
| `QuestState.NotStarted / Running / Cancelled` | enum is `Open / Full / Completed / Abandoned / Expired` | `NotStarted/Running` → `Open` (collapses the `BindContext` `if (NotStarted) SetState(Running)` line into a no-op); `Cancelled` → `Abandoned` |
| `IsAmbitionStep` flag on `IQuest` | not on the interface | kept as an `AmbitionQuest`-only public property (a marker for HUD/inspector code that already has a typed `AmbitionQuest` reference) |

**Defaults committed for the missing members:**
- `Type = QuestType.Custom`
- `IsExpired = false`, `RemainingDays = -1` (sentinel — ambitions are patient/untimed per the spec)
- `MaxContributors = 1`
- `Contributors` = single-element list with `_self`, allocated once in ctor
- `Contribution` = empty `Dictionary<string,int>`, allocated once
- `TryJoin / TryLeave` return `false`; `RecordProgress` is a no-op (progress is task-driven, not contribution-driven)
- `Target` returns `null` for v1 (Phase 11 Task 43 may revisit)
- `OnProgressRecorded` declared but never fired (CS0067 expected; implementer noted, both reviewers approved)
- `QuestId` generated once via `Guid.NewGuid().ToString("N")`
- `OriginWorldId / OriginMapId` exposed with public setters (mirroring `BuyOrder.cs:122-123`) so the persistence layer in Phase 6 can populate

**Reference implementation:** the implementer mirrored `BuyOrder.cs:119-198` for the boilerplate IQuest plumbing. Future ambition-related IQuest implementers should follow the same pattern.

**Implementer's optimization choices ratified by both reviewers:**
- `_completedCount` int caches "tasks already completed" so `TotalProgress` is allocation-free.
- `TickSequential` indexes via `_completedCount` (only ticks the next-active task per call), not the plan's full-loop walk. Behavior is equivalent for canonical idempotent tasks; the IL test in Task 17 walks through call-by-call to verify.
- `TickAnyOf` reports `Required` progress on win (sets `_completedCount = _tasks.Count`) for HUD parity.
- Cloning uses a `[Serializable]` `TaskListWrapper` with `[SerializeReference] List<TaskBase>` field for the `JsonUtility` round-trip — preserves polymorphism without per-subclass overrides. Allocated once in the ctor.

**Downstream impact:** Tasks 22-24 (persistence) must read `_state`, `_tasks`, `_completedCount`, `_ctx`, and `_so` (via SO GUID lookup) when exporting/importing. The current shape doesn't lock multiplayer — Phase 7 can either keep `AmbitionQuest` server-only with quest-log NetworkVariable diff or wrap it in a `NetworkBehaviour`.

### Deviation #5 — Tests of Assembly-CSharp Ambition types require IL-emission scaffolding (Phase 4 lock-in)

**Empirically verified during Task 17:** Unity silently drops `"Assembly-CSharp"` from EditMode asmdef `references[]`, even when `overrideReferences: true` is set and the reference is explicitly listed. A probe file using the simple `using MWI.Ambition;` + `private class CountTask : TaskBase {}` approach failed compilation with `error CS0234: The type or namespace name 'Ambition' does not exist in the namespace 'MWI'`.

**Therefore, every future Ambition test that touches an Assembly-CSharp type (`AmbitionQuest`, `QuestSO`, `TaskBase`, `Character`, `CharacterAmbition`, …) must use the `Assets/Tests/EditMode/ToolStorage` precedent:**

1. Create a new test asmdef (or reuse `AmbitionQuest.Tests` at `Assets/Tests/EditMode/AmbitionQuest/`) with `references: ["UnityEngine.TestRunner", "UnityEditor.TestRunner", "Assembly-CSharp"]`, `overrideReferences: true`, `autoReferenced: false`. The `"Assembly-CSharp"` entry is silently dropped but signals intent and is harmless.
2. In `[OneTimeSetUp]`, resolve all required types via `AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp").GetType("MWI.Ambition.AmbitionQuest")` etc. Cache `MethodInfo`, `FieldInfo`, enum values via `Enum.Parse`. Assert each lookup `Is.Not.Null` with descriptive failure messages.
3. For tests that need to subclass an Assembly-CSharp abstract type (e.g., a `CountTask : TaskBase` stub), build the subclass dynamically via `System.Reflection.Emit.AssemblyBuilder` — see the canonical example at `Assets/Tests/EditMode/AmbitionQuest/QuestOrderingTests.cs:140-240` (`BuildCountTaskType`). The dynamic type has full subclass behavior but is invisible to `JsonUtility` — so any code path that JSON-clones the task must be bypassed (Task 17's `MakeQuest` writes `_tasks` directly via reflection).
4. Per-test instance allocation goes through a `MakeQuest`-style helper that builds fresh `QuestSO` / `AmbitionContext` / `AmbitionQuest` and injects test stubs.

**Tests that touch ONLY `MWI.Ambition.Pure` types (`AmbitionContext`, `ContextBinding`, `StaticBinding`, `TaskParameterBinding`, `TaskOrderingMode`, `TaskStatus`, `ControllerKind`, `CompletionReason`) can stay in the simpler `Ambition.Tests` asmdef** — that one references only `MWI.Ambition.Pure` and works without IL.

**Decision rule for future tasks:** if the test target has any field or method signature that mentions `Character`, `CharacterAmbition`, `AmbitionQuest`, `QuestSO`, `TaskBase`, `IQuest`, etc. — write the test in `AmbitionQuest.Tests` (or a sibling Assembly-CSharp test asmdef) using the IL pattern. The OneTimeSetUp scaffolding (~120 lines) is heavy but amortizes across all tests in the file.

**Why not refactor Pure to absorb these types?** Each of `AmbitionQuest`, `QuestSO`, `TaskBase`, `CharacterAmbition` references `Character` (Assembly-CSharp, global namespace) somewhere in its public surface. Moving them to Pure would require either (a) genericizing `Tick(Character, AmbitionContext)` into `Tick(object, AmbitionContext)` and downcasting at every call site, or (b) introducing an `ICharacter` abstraction in Pure that `Character` implements — both are larger refactors than the IL pattern's overhead justifies.

## How to resume

### 1. Bootstrap the new session

In your new conversation:

```
I'm resuming the Ambition System implementation. Read these in order:

1. docs/superpowers/plans/2026-05-02-ambition-system-RESUME.md  (this file)
2. docs/superpowers/plans/2026-05-02-ambition-system.md         (the 58-task plan)
3. docs/superpowers/specs/2026-05-02-ambition-system-design.md  (the design spec)

Verify the branch is `multiplayyer` and HEAD is at `913e1ed4` (or later if I've made other commits since).

Then invoke the `superpowers:subagent-driven-development` skill and continue from Task 18 (Phase 5 — `CharacterAmbition` skeleton with subsystem registration).
```

### 2. Cadence the prior session settled on

- **One subagent per task** (per the skill). Use the `Agent` tool with `subagent_type: general-purpose`.
- **Model selection:**
  - `haiku` for enum-only or trivial-write tasks.
  - `sonnet` for tasks with logic + NUnit tests + scene-aware reasoning.
  - `opus` for tricky integration (CharacterAmbition state machine, Persistence Import/Export, BT integration, NetworkVariable wiring).
- **Reviews:**
  - **Skip the formal two-stage review for trivial tasks** (enum-only, folder scaffolding, type forward-references). Both reviewers would just say "approved." Note the skip explicitly with reasoning.
  - **Run the full two-stage review** (spec compliance → code quality, in order) for tasks with logic, tests, or cross-system wiring. Templates: `.claude/plugins/cache/claude-plugins-official/superpowers/5.0.6/skills/subagent-driven-development/{spec,code-quality}-reviewer-prompt.md`.

### 3. Operational rules learned during Phase 1

- **Working tree has unrelated mods** (farming, harvestable, character-visuals, etc.). Every subagent dispatch must be told to use **explicit `git add <path>`** — never `git add .` or `git add -A`. Otherwise unrelated changes pollute the commit.
- **`mcp__ai-game-developer__console-get-logs` returns stale + new errors mixed together.** Subagents should filter for errors that reference files they just created/modified. Pre-existing errors (UI shader, Vivox, project-path-with-spaces, Room collider) are noise — don't let subagents try to fix them.
- **Unity `.meta` files must be explicitly added to commits.** `git status` shows them as untracked after `assets-refresh`. Pattern: `git add <path>.cs <path>.cs.meta`.
- **Verify member names against the actual codebase before relying on them.** The plan often says "verify `X.Method` exists, substitute if different" — subagents should grep before assuming. Examples flagged in the plan: `CharacterCombat.RequestAttack`, `CharacterInteraction.OnInteractionEnded`, `CharacterRelation.HasPartner`, `ScheduleActivity.None`, `IWorldZone.Center`/`Radius`.
- **`assets-create-folder` MCP tool** must be used before the first `script-update-or-create` writes into a new directory. Don't fall back to bash `mkdir` unless the MCP tool is genuinely unavailable.
- **`tests-run` MCP tool** — use `mode: "EditMode"` and `class: "MWI.Tests.Ambition.<name>"` for targeted runs. The Ambition test assembly is `Ambition.Tests` (asmdef in `Assets/Tests/EditMode/Ambition/`).

### 4. Next task — Task 18 (start of Phase 5)

`CharacterAmbition` skeleton with subsystem registration. File:
- `Assets/Scripts/Character/CharacterAmbition/CharacterAmbition.cs` (new folder; verify exact path against the plan at line 1593).

Full task text in [the plan](2026-05-02-ambition-system.md#task-18-characterambition-skeleton-with-subsystem-registration).

**Recommended model:** `opus` — `CharacterAmbition` is the orchestration centerpiece. It owns the active `AmbitionInstance`, the `History` list, the public `Set / Clear / Replace` API, and the event surface (`OnAmbitionSet`, `OnStepAdvanced`, `OnAmbitionCompleted`, `OnAmbitionCleared`). It does NOT yet drive behavior (that's Phase 9 BT branch).

**Pre-flight verification before dispatching the implementer:**

1. **Read Task 18 in the plan in full** (around line 1593). Skim the entire ~210-line code block so you can spot silent simplifications.
2. **Verify the existing CharacterSystem base-class shape.** Read `Assets/Scripts/Character/Character.cs` for the auto-assignment pattern (`GetComponentInChildren<>()` fallback in `Awake`). Read 1-2 existing subsystems (e.g. `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs`, `Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs`) for the exact ctor / field / event idiom.
3. **Verify the `Character.cs` auto-wire pattern.** Per CLAUDE.md "Character GameObject Hierarchy": every subsystem holds a reference to root `Character` (not other subsystems directly). Cross-system communication routes through `Character`.
4. **Verify which assembly `CharacterAmbition` lives in.** It's a `MonoBehaviour` (or `NetworkBehaviour` after Phase 7), so it's Assembly-CSharp — not Pure.
5. **Skim `AmbitionRegistry.cs`** (Task 13) to confirm the `Get(string id) → AmbitionSO` resolver shape — Task 18's `SetAmbition(string ambitionId, ...)` overload needs this.

**Phase 5 also has:**
- **Task 19** — state-machine NUnit tests. **Note:** these tests will need the `AmbitionQuest.Tests` IL-emission pattern (deviation #5) since `CharacterAmbition` is Assembly-CSharp. Either reuse `AmbitionQuest.Tests` asmdef (rename if you want a less Quest-specific name later) or create a sibling. The setup cost is already paid — adding more `[Test]` methods is cheap.
- **Task 20** — wire `CharacterAmbition` into `Character.cs` (a `[SerializeField]` field + `GetComponentInChildren<>()` fallback in `Awake`). Read `Character.cs:649` and surrounding subsystem-wire blocks for the exact pattern.
- **Task 21** — manual prefab edits (add a `CharacterAmbition` child GameObject to all Character prefabs). Cannot be subagent-dispatched cleanly; do interactively in the editor or via `gameobject-create` + `assets-prefab-open` MCP tools.

**Reuse-vs-rename decision deferred to Phase 5:** the `AmbitionQuest.Tests` asmdef name is narrowly scoped. If many Phase 5+ tests pile up, consider renaming it to `Ambition.AssemblyCSharp.Tests` (rename the `.asmdef` and update its `name` field). For now (3 tests), the narrow name is fine.

## Phases remaining (41 tasks)

| Phase | Tasks | Estimate (subagent dispatches) |
|---|---|---|
| ~~Phase 2 — Parameter bindings~~ | ~~10–12~~ | DONE |
| ~~Phase 3 — Registries + settings~~ | ~~13–15~~ | DONE |
| ~~Phase 4 — Bridge + ordering tests~~ | ~~16–17~~ | DONE |
| Phase 5 — `CharacterAmbition` core | 18–21 | 4 (Task 18 `opus`, Task 21 manual prefab edits) |
| Phase 6 — Persistence | 22–27 | 6 (Tasks 23, 24 `opus` — save round-trip + deferred-bind) |
| Phase 7 — Networking | 28–30 | 3 (Task 29 `opus` — NetworkBehaviour conversion) |
| Phase 8 — Task primitives | 31–38 | 8 (`sonnet` for most; `opus` if member-name mismatches surface) |
| Phase 9 — BT integration | 39–41 | 3 |
| Phase 10 — Controller-switch handoff | 42 | 1 |
| Phase 11 — Player UI | 43–46 | 4 (Task 45 manual prefab build) |
| Phase 12 — Dev-Mode inspector | 47–48 | 2 |
| Phase 13 — Sample SO content | 49–51 | 3 (manual asset authoring via `script-execute`) |
| Phase 14 — Manual smoke + docs | 52–58 | 7 (Tasks 52–54 are Play-mode smokes, can't be subagent-dispatched cleanly — run interactively) |

**Recommended cadence per resume session:** 1–2 phases per session. Keeps each session focused, prevents context bloat, gives natural review checkpoints.

## Open notes / things to watch

- **Test assembly references** — `Ambition.Tests.asmdef` has `overrideReferences: true` + `autoReferenced: false`, so it can ONLY see types in `MWI.Ambition.Pure`. Unity asmdefs cannot reference `Assembly-CSharp` by name. If a future test (e.g. for `AmbitionQuest` in Task 16) needs an Assembly-CSharp type, the only options are: (a) move the test to PlayMode (which has reflection access via the play-mode runner), or (b) move the type-under-test into `MWI.Ambition.Pure` if it has no Unity dependency. Don't try to "add Assembly-CSharp to references[]" — Unity silently drops it.
- **`Character` is in the global namespace** — `Character.cs` has no `namespace` declaration. Resolves with no `using` from any Assembly-CSharp file. Verified Task 12.
- **`MWI.Quests.IQuest`** is the actual namespace (verified Task 6). Earlier Explore reports said `MWI.Quests` may differ — confirmed correct now.
- **`feedback_lazy_static_registry_pattern.md`** governs Task 13/14 (`AmbitionRegistry`, `QuestRegistry`). Joining clients skip `GameLauncher.LaunchSequence`, so the registries must lazy-init in `Get()`. Plan already reflects this — just don't let subagents shortcut to eager init.
- **`HibernatedNPCData`** location (Task 27) — grep first to find. Likely `Assets/Scripts/World/...`.
- **Orphaned `.meta` files** — Phase 1 implementer subagents repeatedly forgot to add `.meta` files to commits, requiring a chore cleanup commit at start of Phase 2. **Mitigation already baked into Phase 2 prompts:** every dispatch now includes an explicit `git add` line listing both `.cs` and `.cs.meta` files. Continue this pattern in Phase 3+ prompts.
- **Reviewer rigor** — Task 12's code quality reviewer made a confidently-stated but factually-wrong claim about C# generic null-checks behaving differently for value types. Always verify strong technical claims before accepting reviewer feedback. The Roslyn-execute approach via `mcp__ai-game-developer__script-execute` is a fast way to settle "what does C# actually do here" questions in 30 seconds.

## Phase 1 + 2 + 3 + 4 milestone (verifiable)

Run in the new session to confirm starting state:

```bash
git log --oneline | head -22
# Expect (HEAD = 913e1ed4 or one commit further if the resume-note doc was committed after):
#   913e1ed4 test(ambition): cover Sequential/Parallel/AnyOf quest ordering
#   a5709ae8 feat(ambition): add AmbitionQuest bridging IAmbitionStepQuest -> IQuest
#   98644070 docs(ambition): update resume note after Phase 3 (Tasks 13-15)
#   712502ba feat(ambition): add AmbitionSettings with gating-need type-name list
#   f16854f5 feat(ambition): add QuestRegistry with lazy-init
#   56d571e8 feat(ambition): add AmbitionRegistry with lazy-init
#   5d9ca78a docs(ambition): update resume note after Phase 2 (Tasks 10-12)
#   74dca048 feat(ambition): add RuntimeQueryBinding base in Assembly-CSharp
#   3cc72b13 test(ambition): cover null-context and empty-key branches in ContextBinding
#   148a5c73 chore(ambition): remove orphaned Bindings/ folder after migration to Pure asmdef
#   16b9c324 feat(ambition): add ContextBinding<T> with tests; migrate bindings to Pure asmdef
#   674bef2f feat(ambition): add TaskParameterBinding and StaticBinding
#   6fa0b0b5 chore(ambition): add missing .meta files orphaned from Phase 1 commits
#   d93b5199 docs(ambition): resume note after Phase 1 (Tasks 0-9)
#   d8661475 feat(ambition): flesh out QuestSO with task list and ordering
#   c04691bf feat(ambition): add TaskOrderingMode and TaskBase polymorphic base
#   6921acf8 feat(ambition): add AmbitionSO, AmbitionInstance, CompletedAmbition, IAmbitionStepQuest
#   27803869 feat(ambition): add AmbitionContext with NUnit coverage
#   76afaa33 feat(ambition): add CompletionReason, ControllerKind, TaskStatus enums
#   10362092 feat(ambition): add ContextValueKind enum
#   7a614674 test(ambition): scaffold test folder
```

Run all Ambition tests:

```
mcp__ai-game-developer__tests-run mode=EditMode testNamespace=MWI.Tests.Ambition         # Expect: 10 passed (Pure)
mcp__ai-game-developer__tests-run mode=EditMode testNamespace=MWI.Tests.AmbitionQuest    # Expect: 3 passed (Assembly-CSharp via reflection)
# Total: 13/13 passing across both Ambition test asmdefs.
```

If those commits are present and the tests pass, Phases 1–4 are intact and you're ready to start Task 18.
