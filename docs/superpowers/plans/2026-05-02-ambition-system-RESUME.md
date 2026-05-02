# Ambition System — Resume Note

> Pick this up in a fresh conversation. Point your new session at this file plus the plan and spec; you'll have everything you need to continue.

**Branch:** `multiplayyer` (continue here — do not branch off).
**Plan:** [docs/superpowers/plans/2026-05-02-ambition-system.md](2026-05-02-ambition-system.md) (58 tasks, 14 phases).
**Spec:** [docs/superpowers/specs/2026-05-02-ambition-system-design.md](../specs/2026-05-02-ambition-system-design.md).

## Current state — Phase 1 complete (Tasks 0–9 / 58)

**Last commit:** `d8661475` (Task 9 — full QuestSO body).

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

**Foundation state:** Compile-clean. NUnit `AmbitionContextTests` green (5/5).

## Architectural deviation locked in during Phase 1

**`AmbitionContext` lives in a new `MWI.Ambition.Pure` assembly definition.**

- Folder: `Assets/Scripts/Character/Ambition/Pure/` with its own `.asmdef`.
- `MWI.Ambition.Pure.asmdef` has `autoReferenced: true`, so Assembly-CSharp picks it up automatically — callers don't need to know.
- The plan's original `IsSerializableValueKind` used `typeof(MWI.WorldSystem.IWorldZone).IsAssignableFrom(t)` which would have compile-errored from a Pure assembly (no Assembly-CSharp reference allowed). The implementer (Task 3) replaced it with `t.GetInterfaces()` + string FullName check — same pattern already used for `Character`. Behavior identical.
- Test asmdef `Ambition.Tests.asmdef` references `MWI.Ambition.Pure`.

**Consequence for downstream tasks:** when adding new types, decide per-type whether they belong in Pure (no Unity dependency) or Assembly-CSharp (Unity / Character / IQuest / NetworkBehaviour). The plan doesn't anticipate this split — handle case-by-case. Most downstream types (CharacterAmbition, AmbitionQuest, all Tasks, BT nodes, save DTOs that reference SO assets) need to stay in Assembly-CSharp because they pull in Unity types. Pure should only grow if there's pure-logic worth isolating.

## How to resume

### 1. Bootstrap the new session

In your new conversation:

```
I'm resuming the Ambition System implementation. Read these in order:

1. docs/superpowers/plans/2026-05-02-ambition-system-RESUME.md  (this file)
2. docs/superpowers/plans/2026-05-02-ambition-system.md         (the 58-task plan)
3. docs/superpowers/specs/2026-05-02-ambition-system-design.md  (the design spec)

Verify the branch is `multiplayyer` and HEAD is at `d8661475` (or later if I've made other commits since).

Then invoke the `superpowers:subagent-driven-development` skill and continue from Task 10 (Phase 2 — parameter bindings).
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

### 4. Next task — Task 10 (start of Phase 2)

`TaskParameterBinding<T>` abstract + `StaticBinding<T>`. Files:
- `Assets/Scripts/Character/Ambition/Bindings/TaskParameterBinding.cs`
- `Assets/Scripts/Character/Ambition/Bindings/StaticBinding.cs`

Full task text in [the plan](2026-05-02-ambition-system.md#task-10-taskparameterbindingt-abstract--staticbindingt).

**Decision for the resuming session:** decide whether the bindings folder also lives in Pure, or in Assembly-CSharp. Bindings reference `AmbitionContext` (Pure) only — they have no Unity dependency themselves — so they could live in Pure. **Recommendation:** put them in Assembly-CSharp anyway. The downstream `RuntimeQueryBinding<T>` subclasses (Task 12, Task 49 `EligibleLoverQuery`) reference `Character` and `MapController`, which are Assembly-CSharp. Splitting the binding hierarchy across two assemblies (Static/Context in Pure, Runtime in Assembly-CSharp) creates an awkward boundary inside one polymorphic family. Keep them all together in Assembly-CSharp.

## Phases remaining (49 tasks)

| Phase | Tasks | Estimate (subagent dispatches) |
|---|---|---|
| Phase 2 — Parameter bindings | 10–12 | 3 |
| Phase 3 — Registries + settings | 13–15 | 3 |
| Phase 4 — Bridge + ordering tests | 16–17 | 2 (Task 16 needs `opus` — tricky IQuest bridge implementation) |
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

- **Test assembly references** — `Ambition.Tests.asmdef` references `MWI.Ambition.Pure`. If a future test needs to exercise types that live in Assembly-CSharp (e.g. `AmbitionQuest` from Task 16), either move that test to a PlayMode test, or add an Assembly-CSharp reference to the Tests asmdef (the existing pattern in `Hunger`/`Farming`/`Orders` test folders varies — check those before deciding).
- **`MWI.Quests.IQuest`** is the actual namespace (verified Task 6). Earlier Explore reports said `MWI.Quests` may differ — confirmed correct now.
- **`feedback_lazy_static_registry_pattern.md`** governs Task 13/14 (`AmbitionRegistry`, `QuestRegistry`). Joining clients skip `GameLauncher.LaunchSequence`, so the registries must lazy-init in `Get()`. Plan already reflects this — just don't let subagents shortcut to eager init.
- **`HibernatedNPCData`** location (Task 27) — grep first to find. Likely `Assets/Scripts/World/...`.

## Phase 1 milestone (verifiable)

Run in the new session to confirm starting state:

```bash
git log --oneline | head -10
# Expect: d8661475 feat(ambition): flesh out QuestSO with task list and ordering
#         c04691bf feat(ambition): add TaskOrderingMode and TaskBase polymorphic base
#         6921acf8 feat(ambition): add AmbitionSO, AmbitionInstance, CompletedAmbition, IAmbitionStepQuest
#         27803869 feat(ambition): add AmbitionContext with NUnit coverage
#         76afaa33 feat(ambition): add CompletionReason, ControllerKind, TaskStatus enums
#         10362092 feat(ambition): add ContextValueKind enum
#         7a614674 test(ambition): scaffold test folder
#         7c1300ca docs(ambition): implementation plan ...
#         f53d5c32 docs(ambition): apply spec review recommendations
#         d20784ec docs(ambition): design spec ...
```

If those 7 ambition commits are present (plus the 3 docs commits), Phase 1 is intact and you're ready to start Task 10.
