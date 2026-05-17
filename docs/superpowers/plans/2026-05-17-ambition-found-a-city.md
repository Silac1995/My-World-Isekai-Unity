# Ambition_FoundACity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the two AB-independent `TaskBase` subclasses (`Task_CreateCommunity`, `Task_PromoteCommunity`) and the `Ambition_FoundACity` `AmbitionSO` subclass that together drive an NPC (or surface to a player) the full city-founding ambition chain. Plan 4 will add the AB-targeted tasks (`Task_PlaceBuilding`, `Task_FinishConstruction`) and create the actual `Ambition_FoundACity.asset` with the full quest chain wired up; Plan 3 ships the type scaffolding that Plan 4 consumes.

**Architecture:**
- **`Task_CreateCommunity`** is the active "found a community" verb. Its `Tick` (re-evaluated every BT pass per the existing `AmbitionQuest.TickActiveTasks` cadence) first checks whether the actor already leads a community (returns `TaskStatus.Completed` if so), otherwise invokes `actor.CharacterCommunity.CheckAndCreateCommunity()` (Plan 1's gate-less founding gesture), and re-checks. Idempotent: re-invoking when the actor already leads is a guarded no-op. Optional `CommunityName` field overrides the default `"X's Settlement"` (a CharacterCommunity-defaulted name, set in Plan 1 Task 3).
- **`Task_PromoteCommunity`** is a passive completion-watcher. It does NOT drive the promotion — Plan 4's `Community.TryPromoteLevel()` does that. The task simply returns `TaskStatus.Completed` once `actor.CharacterCommunity.CurrentCommunity.level >= TargetLevel`. This lets the ambition chain include "wait until your community reaches Camp / Village / Town / City / Kingdom / Empire" steps that progress naturally as Plan 4's tier-up flow fires.
- **`Ambition_FoundACity`** is a concrete `AmbitionSO` subclass (the base is abstract — see `Assets/Scripts/Character/Ambition/AmbitionSO.cs:26`). Empty class body for v1; exists so Plan 4 can `CreateAssetMenu` an asset of this concrete type and authors can right-click → Create. No parameter slots (the ambition operates on the actor's own community). Default `ValidateParameters` is inherited unchanged.

**Why `Task_CreateCommunity.Tick` doesn't store the live `Community` in the `AmbitionContext`:** `AmbitionContext.Set<T>` (`Assets/Scripts/Character/Ambition/Pure/AmbitionContext.cs:36`) rejects values whose type isn't on its allow-list (`IsSerializableValueKind`). `Community` is a plain C# class in `Assembly-CSharp` and is NOT on the allow-list. We instead anchor downstream tasks to the actor: every subsequent task that needs "the founded community" reads `actor.CharacterCommunity.CurrentCommunity`, which is a server-side authoritative pointer set by `CharacterCommunity.SetCurrentCommunity` (Plan 1). This sidesteps the context-serialization constraint and matches the actor-as-anchor pattern the spec adopts for player-driven completion.

**Why both tasks live in the `Assembly-CSharp` assembly (not `MWI.Ambition.Pure`):** The Pure assembly cannot reference `Character` (lives in Assembly-CSharp; that's an architectural choice for ambition-save serialization isolation — see `Assets/Scripts/Character/Ambition/Pure/AmbitionContext.cs:55`). `TaskBase` itself lives in the Pure assembly, but the existing convention is to put Character-coupled subclasses in `Assets/Scripts/Character/Ambition/Tasks/` (outside the `Pure/` folder), which is Assembly-CSharp. Mirroring that placement.

**Tech Stack:** Unity 6.0 / NGO 2.x, C# 9 / .NET Framework 4.8, NUnit EditMode tests via `tests-run` MCP tool. No new asmdef, no new dependencies. Tests live in `Assets/Editor/Tests/Ambition/` (Assembly-CSharp-Editor, no asmdef — same pattern as Plan 1's `Assets/Editor/Tests/Community/`).

**Rules enforced throughout:** CLAUDE.md rules #1-#8 (think first), #9-#14 (SOLID — each Task is one verb), #15 (private fields `_underscorePrefix` — N/A here, fields are public `[SerializeField]` for inspector authoring), #18/#19/#19b (server-only behavior — full audit below), #22 (player↔NPC parity — the tasks work for both; players just don't have a BT ticking them, so their completion comes via the underlying state change), #28/#29/#29b (skill + agent + wiki updates), #31 (defensive null-checks on `actor.CharacterCommunity`).

**Network safety audit (rule #19b — performed BEFORE writing the plan):**
1. **Who writes via these tasks?** Server-side only. `Task_CreateCommunity.Tick` calls `CharacterCommunity.CheckAndCreateCommunity()` which is a server-side gesture (mutates `CommunityManager.activeCommunities` and `CharacterCommunity._currentCommunity` — both server-side state). The Tasks themselves are ticked by `AmbitionQuest.TickActiveTasks`, which is invoked from BT actions running server-side (the BT runs on the host per the project's NPC architecture).
2. **What replication channel?** **None** — these tasks operate on server-side state only. Clients see the resulting `Community.communityName` / `Community.leaders` changes via Plan 1's save-data path (`MapRegistry.CommunityData.LeaderIds`, replicated through the existing save snapshot when needed) and the chained `CharacterCommunity` save round-trip on portal-gate / bed save events.
3. **Late-joiner sees?** Same as before Plan 3 — community state isn't live-replicated. A late-joiner who arrives mid-ambition sees the ambition's *current* state via `CharacterAmbition` (already-replicated per the existing `NetworkAmbitionSnapshot` pipeline). Tasks themselves serialize via `AmbitionQuest._tasks` deep clone + Task's `SerializeState` / `DeserializeState` hooks; neither of our new tasks needs mid-pursuit state (both are stateless — Task_CreateCommunity re-checks "do I lead a community?" on every Tick; Task_PromoteCommunity does the same for level). No additional replication needed.
4. **Client-side pre-gate?** N/A — these tasks aren't gated by any client-side check. They run inside the server-side BT tick.
5. **`GetComponentInParent` spawn-race?** N/A — tasks read `actor.CharacterCommunity` which is set in `Character.Awake` via the standard character-subsystem wiring; spawn-race is handled by `Character`'s own `Awake` ordering.
6. **`InteractableObject.IsCharacterInInteractionZone` (rule #36)?** N/A — Plan 3 doesn't add any new player↔interactable surface.

---

## File Structure

**New files:**
- `Assets/Scripts/Character/Ambition/Tasks/Task_CreateCommunity.cs` — TaskBase subclass.
- `Assets/Scripts/Character/Ambition/Tasks/Task_PromoteCommunity.cs` — TaskBase subclass.
- `Assets/Scripts/Character/Ambition/AmbitionSOs/Ambition_FoundACity.cs` — AmbitionSO subclass.
- `Assets/Editor/Tests/Ambition/Task_CreateCommunityTests.cs` — EditMode unit tests (3-5 cases).
- `Assets/Editor/Tests/Ambition/Task_PromoteCommunityTests.cs` — EditMode unit tests (3-4 cases).

**Modified files:**
- None for Plan 3. Pure additive.

**Docs updated:**
- `.agent/skills/ambition-system/SKILL.md` (if exists) — add Task_CreateCommunity / Task_PromoteCommunity / Ambition_FoundACity to the public API tables. Otherwise add to `.agent/skills/community-system/SKILL.md` — pick whichever skill owns ambition documentation today.
- `wiki/systems/character-ambition.md` (or wherever ambition lives) — public API refresh + change log.
- `wiki/concepts/found-a-city-ambition.md` (NEW) — concept page documenting the quest chain shape (Create → BuildCapital [Plan 4] → Promote × 6) and the "actor-as-anchor" design choice instead of context-stored Community.

**Out of scope (Plan 4 owns these):**
- `Task_PlaceBuilding` + `Task_FinishConstruction` (need ABSO from Plan 4).
- `Quest_BuildCapital.asset`.
- `Ambition_FoundACity.asset` (the actual asset with the full quest chain wired up).
- `Quest_*.asset` for the Promote tier-up steps (created in Plan 4 alongside the full Ambition asset, so the chain ships atomically).
- `BTAction_PursueAmbitionStep` changes — the existing BTAction already calls `TickActiveTasks`; no Plan 3 changes needed.
- Player UI for ambition selection.
- `Community.TryPromoteLevel()` — Plan 4 owns the tier-up mutator (requires `AdministrativeBuilding` for treasury + `CommunityTierRequirementsRegistry`).

---

## Task 1: Add `Task_CreateCommunity` TaskBase subclass + EditMode tests

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Tasks/Task_CreateCommunity.cs`
- Create: `Assets/Editor/Tests/Ambition/Task_CreateCommunityTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Editor/Tests/Ambition/Task_CreateCommunityTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class Task_CreateCommunityTests
    {
        private Character MakeBareCharacter(string name)
        {
            // Headless: a bare Character GameObject with its CharacterCommunity subsystem.
            // Character.Awake auto-wires _character on every subsystem; we mirror that here
            // for the bare component setup (CharacterSystem hierarchy on the same GameObject).
            var go = new GameObject(name);
            var character = go.AddComponent<Character>();
            // CharacterCommunity expects to live on a child GameObject (Facade + child pattern),
            // but for a headless test the Character.cs Awake fallback uses GetComponentInChildren
            // and is null-safe. Adding it on the same GO is sufficient for the field-checking
            // logic Task_CreateCommunity exercises.
            go.AddComponent<CharacterCommunity>();
            return character;
        }

        [Test]
        public void Tick_returns_Completed_when_actor_already_leads_a_community()
        {
            var actor = MakeBareCharacter("Founder");
            var ctx = new AmbitionContext();
            var task = new Task_CreateCommunity();
            task.Bind(ctx);

            // Pre-seed: actor already leads a community.
            var pre = new global::Community("Pre-existing", actor);
            actor.CharacterCommunity.SetCurrentCommunity(pre);
            // SetCurrentCommunity doesn't add to leaders — but Community ctor does add the founder
            // to leaders. Verify the precondition.
            Assert.IsTrue(pre.IsLeader(actor));

            var status = task.Tick(actor, ctx);
            Assert.AreEqual(TaskStatus.Completed, status);
        }

        [Test]
        public void Tick_returns_Completed_after_founding_a_new_community()
        {
            var actor = MakeBareCharacter("Founder");
            var ctx = new AmbitionContext();
            var task = new Task_CreateCommunity();
            task.Bind(ctx);

            // Actor leads nothing initially.
            Assert.IsNull(actor.CharacterCommunity.CurrentCommunity);

            var status = task.Tick(actor, ctx);

            // After Tick, actor should lead a brand-new community.
            Assert.AreEqual(TaskStatus.Completed, status);
            Assert.IsNotNull(actor.CharacterCommunity.CurrentCommunity,
                "Tick must invoke CheckAndCreateCommunity so the actor now leads a community.");
            Assert.IsTrue(actor.CharacterCommunity.CurrentCommunity.IsLeader(actor));
        }

        [Test]
        public void Tick_is_idempotent_on_repeat_invocation()
        {
            var actor = MakeBareCharacter("Founder");
            var ctx = new AmbitionContext();
            var task = new Task_CreateCommunity();
            task.Bind(ctx);

            var first = task.Tick(actor, ctx);
            var foundedCommunity = actor.CharacterCommunity.CurrentCommunity;

            var second = task.Tick(actor, ctx);
            var third = task.Tick(actor, ctx);

            Assert.AreEqual(TaskStatus.Completed, first);
            Assert.AreEqual(TaskStatus.Completed, second);
            Assert.AreEqual(TaskStatus.Completed, third);
            Assert.AreSame(foundedCommunity, actor.CharacterCommunity.CurrentCommunity,
                "Repeat Ticks must not re-create the community.");
        }

        [Test]
        public void Tick_with_null_actor_returns_Running_defensively()
        {
            var ctx = new AmbitionContext();
            var task = new Task_CreateCommunity();
            task.Bind(ctx);
            Assert.AreEqual(TaskStatus.Running, task.Tick(null, ctx),
                "Null actor must not throw; return Running so the BT keeps trying.");
        }

        [Test]
        public void CommunityName_field_overrides_default_settlement_name()
        {
            var actor = MakeBareCharacter("Founder");
            var ctx = new AmbitionContext();
            var task = new Task_CreateCommunity { CommunityName = "Citadel of Light" };
            task.Bind(ctx);

            task.Tick(actor, ctx);

            Assert.AreEqual("Citadel of Light", actor.CharacterCommunity.CurrentCommunity.communityName);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Use `tests-run` MCP, `testMode: EditMode`, filter `MWI.Tests.Ambition.Task_CreateCommunityTests`. Expected: FAIL with "Task_CreateCommunity type does not exist".

- [ ] **Step 3: Add the helper hook on `CharacterCommunity`**

Plan 1's `CharacterCommunity.CheckAndCreateCommunity()` doesn't accept a name override — the name is hardcoded to `"{name}'s Settlement"` inside `CreateCommunity()`. For the test's `CommunityName` override to work, we need a small additive entry point.

Read `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs` and locate `private void CreateCommunity(string name)` (the parameter-less version is `public`; the `string name` overload is private around line 198). It currently exists and works as expected for direct calls; we just need to make it accessible to Task_CreateCommunity.

**No new method needed** — change `private void CreateCommunity(string name)` → `public void CreateCommunity(string name)`. That's the only modification (1 keyword swap). Add an XML doc comment update noting it's now public for Task_CreateCommunity:

```csharp
    /// <summary>
    /// Server-only. Founds a community with an explicit name (used by
    /// <c>Task_CreateCommunity</c> when the Ambition author overrides the default
    /// "{founder.Name}'s Settlement" via <c>Task_CreateCommunity.CommunityName</c>).
    /// </summary>
    public void CreateCommunity(string name)
```

If the file currently has TWO `CreateCommunity` methods (one no-arg `public`, one with name `private`), only the named one needs the `private` → `public` flip.

- [ ] **Step 4: Create `Task_CreateCommunity.cs`**

`Assets/Scripts/Character/Ambition/Tasks/Task_CreateCommunity.cs`:

```csharp
using System;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Ambition task: ensure the actor leads a community. Idempotent — re-Tick after the
    /// actor already leads is a guarded no-op (returns Completed immediately). Used as the
    /// first step of <c>Ambition_FoundACity</c>; Plan 4 chains <c>Task_PlaceBuilding</c>
    /// + <c>Task_FinishConstruction</c> next to build the AdministrativeBuilding.
    /// <para>
    /// Server-side only. Plan 1 stripped the trait + 4-friends gates from
    /// <c>CharacterCommunity.CheckAndCreateCommunity</c>; the sole remaining guard is "not
    /// already leading a community", which is also our Completed condition — so re-Tick
    /// after success short-circuits naturally.
    /// </para>
    /// </summary>
    [Serializable]
    public class Task_CreateCommunity : TaskBase
    {
        /// <summary>
        /// Optional override for the default community name. Default empty string defers
        /// to Plan 1's "{founder.Name}'s Settlement" pattern.
        /// </summary>
        public string CommunityName = string.Empty;

        public override void Bind(AmbitionContext ctx)
        {
            // No parameter bindings — the task operates on the actor alone.
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            // Defensive: null actor or missing subsystem keeps the BT trying.
            if (npc == null || npc.CharacterCommunity == null) return TaskStatus.Running;

            // Already leads a community → Completed (idempotent re-Tick).
            if (npc.CharacterCommunity.CurrentCommunity != null
                && npc.CharacterCommunity.CurrentCommunity.IsLeader(npc))
            {
                return TaskStatus.Completed;
            }

            // Drive the founding gesture. If CommunityName was set in the inspector,
            // use the named overload; otherwise the no-arg one applies the default
            // "{Name}'s Settlement" template.
            if (!string.IsNullOrEmpty(CommunityName))
            {
                npc.CharacterCommunity.CreateCommunity(CommunityName);
            }
            else
            {
                npc.CharacterCommunity.CheckAndCreateCommunity();
            }

            // Re-check post-action. CheckAndCreateCommunity is gated only by
            // "not already leading", so it should succeed unless the actor has
            // somehow been concurrently mutated (multi-leader race) — in which case
            // we report Running and the BT will retry next tick.
            return npc.CharacterCommunity.CurrentCommunity != null
                && npc.CharacterCommunity.CurrentCommunity.IsLeader(npc)
                ? TaskStatus.Completed
                : TaskStatus.Running;
        }

        public override void Cancel()
        {
            // No mid-pursuit state to clean up. Already-founded communities persist
            // (the founder may revisit the ambition later or pursue a parallel ambition).
        }
    }
}
```

- [ ] **Step 5: Re-run the tests**

Use `tests-run` MCP, `testMode: EditMode`, filter `MWI.Tests.Ambition.Task_CreateCommunityTests`. Expected: PASS (5 tests).

Also run a regression check via `tests-run` filter `MWI.Tests.*`. Expected: all pre-existing tests still pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Character/Ambition/Tasks/Task_CreateCommunity.cs Assets/Editor/Tests/Ambition/Task_CreateCommunityTests.cs Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs
git commit -m "$(cat <<'EOF'
feat(ambition): add Task_CreateCommunity TaskBase subclass

First step of Ambition_FoundACity. Idempotent Tick:
- Completed if actor already leads a community
- Otherwise invokes CharacterCommunity.CheckAndCreateCommunity() (Plan 1's
  gate-less founding gesture) or CreateCommunity(name) if the task's
  CommunityName field is set
- Re-checks and returns Completed/Running

Side change: CharacterCommunity.CreateCommunity(string name) flipped from
private to public so Task_CreateCommunity can use the named overload for
ambition-author-provided community names. The no-arg public CreateCommunity()
path is unchanged.

5 EditMode tests under MWI.Tests.Ambition.Task_CreateCommunityTests cover:
already-leads short-circuit, fresh-founding, idempotency, null-actor defense,
and CommunityName override.

Plan 3 of 5 for the City Founding spec
(docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md).
EOF
)"
```

---

## Task 2: Add `Task_PromoteCommunity` TaskBase subclass + EditMode tests

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Tasks/Task_PromoteCommunity.cs`
- Create: `Assets/Editor/Tests/Ambition/Task_PromoteCommunityTests.cs`

- [ ] **Step 1: Write the failing tests**

`Assets/Editor/Tests/Ambition/Task_PromoteCommunityTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class Task_PromoteCommunityTests
    {
        private Character MakeBareCharacter(string name)
        {
            var go = new GameObject(name);
            var character = go.AddComponent<Character>();
            go.AddComponent<CharacterCommunity>();
            return character;
        }

        [Test]
        public void Tick_returns_Running_when_actor_has_no_community()
        {
            var actor = MakeBareCharacter("Lonely");
            var task = new Task_PromoteCommunity { TargetLevel = CommunityLevel.Camp };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            Assert.AreEqual(TaskStatus.Running, task.Tick(actor, ctx));
        }

        [Test]
        public void Tick_returns_Running_when_community_below_target_level()
        {
            var actor = MakeBareCharacter("Founder");
            var community = new global::Community("Test", actor);
            // Community ctor sets level = CommunityLevel.SmallGroup (the first enum value).
            actor.CharacterCommunity.SetCurrentCommunity(community);

            var task = new Task_PromoteCommunity { TargetLevel = CommunityLevel.Camp };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            Assert.AreEqual(TaskStatus.Running, task.Tick(actor, ctx),
                "Community at SmallGroup with TargetLevel=Camp must be Running.");
        }

        [Test]
        public void Tick_returns_Completed_when_community_at_or_above_target_level()
        {
            var actor = MakeBareCharacter("Founder");
            var community = new global::Community("Test", actor);
            community.ChangeLevel(CommunityLevel.Camp);
            actor.CharacterCommunity.SetCurrentCommunity(community);

            var task = new Task_PromoteCommunity { TargetLevel = CommunityLevel.Camp };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            Assert.AreEqual(TaskStatus.Completed, task.Tick(actor, ctx));
        }

        [Test]
        public void Tick_passive_does_not_mutate_community_level()
        {
            var actor = MakeBareCharacter("Founder");
            var community = new global::Community("Test", actor);
            actor.CharacterCommunity.SetCurrentCommunity(community);

            var task = new Task_PromoteCommunity { TargetLevel = CommunityLevel.Camp };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            task.Tick(actor, ctx);
            Assert.AreEqual(CommunityLevel.SmallGroup, community.level,
                "Task_PromoteCommunity must be passive — it watches level but never sets it.");
        }

        [Test]
        public void Tick_with_null_actor_returns_Running_defensively()
        {
            var task = new Task_PromoteCommunity { TargetLevel = CommunityLevel.Camp };
            var ctx = new AmbitionContext();
            task.Bind(ctx);
            Assert.AreEqual(TaskStatus.Running, task.Tick(null, ctx));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Use `tests-run`, filter `MWI.Tests.Ambition.Task_PromoteCommunityTests`. Expected: FAIL.

- [ ] **Step 3: Create `Task_PromoteCommunity.cs`**

`Assets/Scripts/Character/Ambition/Tasks/Task_PromoteCommunity.cs`:

```csharp
using System;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Passive ambition task: completes once the actor's community has reached
    /// <see cref="TargetLevel"/>. Does NOT drive the promotion — Plan 4's
    /// <c>Community.TryPromoteLevel()</c> mutator owns that. Use one instance per
    /// tier step in <c>Ambition_FoundACity</c>'s quest chain:
    /// <c>Task_PromoteCommunity(Camp)</c> → <c>Task_PromoteCommunity(Village)</c> → …
    /// </summary>
    [Serializable]
    public class Task_PromoteCommunity : TaskBase
    {
        /// <summary>Tier the actor's community must reach (>=) for this task to Complete.</summary>
        public CommunityLevel TargetLevel = CommunityLevel.Camp;

        public override void Bind(AmbitionContext ctx)
        {
            // No parameter bindings — operates on the actor's CurrentCommunity.
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (npc == null || npc.CharacterCommunity == null) return TaskStatus.Running;

            var community = npc.CharacterCommunity.CurrentCommunity;
            if (community == null) return TaskStatus.Running;

            // CommunityLevel is an enum with ordered values (SmallGroup < Camp < Village < …).
            return community.level >= TargetLevel
                ? TaskStatus.Completed
                : TaskStatus.Running;
        }

        public override void Cancel()
        {
            // Purely passive — nothing to clean up.
        }
    }
}
```

- [ ] **Step 4: Re-run the tests**

Use `tests-run`, filter `MWI.Tests.Ambition.Task_PromoteCommunityTests`. Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/Ambition/Tasks/Task_PromoteCommunity.cs Assets/Editor/Tests/Ambition/Task_PromoteCommunityTests.cs
git commit -m "$(cat <<'EOF'
feat(ambition): add Task_PromoteCommunity TaskBase subclass

Passive completion-watcher: returns Completed once the actor's community has
reached TargetLevel. Does NOT drive the promotion — Plan 4's
Community.TryPromoteLevel() owns the mutator side.

One instance per tier step in Ambition_FoundACity's quest chain:
Camp → Village → Town → City → Kingdom → Empire.

5 EditMode tests cover: no-community Running, below-target Running, at-or-above
Completed, passivity verification (no level mutation), null-actor defense.

Plan 3 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 3: Add `Ambition_FoundACity` AmbitionSO subclass

**Files:**
- Create: `Assets/Scripts/Character/Ambition/AmbitionSOs/Ambition_FoundACity.cs`

- [ ] **Step 1: Create the subclass**

If `Assets/Scripts/Character/Ambition/AmbitionSOs/` doesn't exist, create the folder.

`Assets/Scripts/Character/Ambition/AmbitionSOs/Ambition_FoundACity.cs`:

```csharp
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Authored Ambition: "Found a City". Drives the full founding flow:
    /// CreateCommunity → BuildCapital (Plan 4) → PromoteCamp → PromoteVillage →
    /// PromoteTown → PromoteCity → PromoteKingdom → PromoteEmpire.
    /// <para>
    /// No parameter slots — the ambition operates on the actor's own community.
    /// Default <see cref="AmbitionSO.ValidateParameters"/> is inherited unchanged.
    /// </para>
    /// <para>
    /// Plan 3 ships only this typed shell. Plan 4 creates the <c>Ambition_FoundACity.asset</c>
    /// instance with the full quest chain wired up (adds <c>Quest_BuildCapital</c> +
    /// the Promote-tier quest assets).
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Ambition/Ambition_FoundACity", fileName = "Ambition_FoundACity")]
    public class Ambition_FoundACity : AmbitionSO
    {
        // Empty body: relies entirely on AmbitionSO's base authoring + ValidateParameters.
        // Plan 4 will populate the Quests list on the .asset instance, not in code.
    }
}
```

- [ ] **Step 2: Compile-check**

Use `assets-refresh` + `console-get-logs`. Expected: no errors.

- [ ] **Step 3: Run all tests for regression**

Use `tests-run` filter `MWI.Tests.*`. Expected: all pass (10 new from Tasks 1+2, plus existing baseline).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/Ambition/AmbitionSOs/Ambition_FoundACity.cs
git commit -m "$(cat <<'EOF'
feat(ambition): add Ambition_FoundACity AmbitionSO subclass

Concrete subclass of the abstract AmbitionSO base. Empty body; exists for the
CreateAssetMenu attribute so Plan 4 can right-click → Create the .asset.

The actual .asset (with the full quest chain wired up: CreateCommunity →
BuildCapital → PromoteCamp/Village/Town/City/Kingdom/Empire) ships in Plan 4
once the AB-targeting tasks (Task_PlaceBuilding, Task_FinishConstruction) and
the Quest_*.asset files exist.

Plan 3 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 4: Documentation (skill + wiki + concept page)

**Files:**
- Locate: existing ambition skill file (search `.agent/skills/` for "ambition" or "AmbitionSO")
- Modify or create: ambition system page in `wiki/systems/`
- Create: `wiki/concepts/found-a-city-ambition.md`

- [ ] **Step 1: Read `wiki/CLAUDE.md`** for schema rules.

- [ ] **Step 2: Discover the existing docs surface**

Run:
- `grep -rln "AmbitionSO\|Ambition_" .agent/skills/` — find which skill owns ambition docs.
- `grep -rln "ambition\|TaskBase" wiki/systems/` — find the wiki system page.

Common candidates: `.agent/skills/ambition-system/SKILL.md`, `wiki/systems/character-ambition.md`. If neither exists, the ambition system has no docs yet — surface it; we'll add the skill + wiki page as part of this task.

- [ ] **Step 3: Update the ambition skill / wiki page**

For whichever skill file owns ambition: add Task_CreateCommunity and Task_PromoteCommunity to the public-API table (one row each: name, fields, Tick semantics). Add Ambition_FoundACity to the AmbitionSO subclass list.

For the wiki system page (if exists): bump `updated:` to `2026-05-17`, add the two new tasks to `## Key classes / files`, append a change-log line:
`- 2026-05-17 — added Task_CreateCommunity + Task_PromoteCommunity + Ambition_FoundACity AmbitionSO subclass; CharacterCommunity.CreateCommunity(string) flipped to public for the named-override path. — claude`

If the wiki page doesn't exist, create `wiki/systems/character-ambition.md` (or similar) following the 10-section template — see `wiki/CLAUDE.md §4` for required sections.

- [ ] **Step 4: Create `wiki/concepts/found-a-city-ambition.md`**

```markdown
---
type: concept
title: "Found a City Ambition"
tags: [ambition, community, city-founding, npc-autonomy]
created: 2026-05-17
updated: 2026-05-17
sources:
  - "[Task_CreateCommunity.cs](../../Assets/Scripts/Character/Ambition/Tasks/Task_CreateCommunity.cs)"
  - "[Task_PromoteCommunity.cs](../../Assets/Scripts/Character/Ambition/Tasks/Task_PromoteCommunity.cs)"
  - "[Ambition_FoundACity.cs](../../Assets/Scripts/Character/Ambition/AmbitionSOs/Ambition_FoundACity.cs)"
related:
  - "[[character-ambition]]"
  - "[[character-community]]"
  - "[[world-community]]"
  - "[[building-grid]]"
  - "[[citizenship]]"
status: draft
confidence: medium
---

# Found a City Ambition

## Summary
**Ambition_FoundACity** is the top-level NPC (or player) ambition for the city-founding
flow. Its quest chain spans the full arc from "found a community" through
"reach the Empire tier", with one explicit AB-construction step in the middle.

```
Ambition_FoundACity
 ├─ Quest_CreateCommunity      (Plan 3 — Task_CreateCommunity)
 ├─ Quest_BuildCapital         (Plan 4 — Task_PlaceBuilding(ABSO) + Task_FinishConstruction)
 ├─ Quest_PromoteCamp          (Plan 3 — Task_PromoteCommunity(Camp))
 ├─ Quest_PromoteVillage       (Plan 3 — Task_PromoteCommunity(Village))
 ├─ Quest_PromoteTown          (Plan 3 — Task_PromoteCommunity(Town))
 ├─ Quest_PromoteCity          (Plan 3 — Task_PromoteCommunity(City))
 ├─ Quest_PromoteKingdom       (Plan 3 — Task_PromoteCommunity(Kingdom))
 └─ Quest_PromoteEmpire        (Plan 3 — Task_PromoteCommunity(Empire))
```

Plan 3 ships the AB-independent task scaffolding (`Task_CreateCommunity`,
`Task_PromoteCommunity`, the `Ambition_FoundACity` AmbitionSO subclass).
Plan 4 ships the AB-coupled tasks and the actual `.asset` files that wire
the chain end-to-end.

## Why the actor-as-anchor design (no `Community` in `AmbitionContext`)

`AmbitionContext.Set<T>` ([Pure/AmbitionContext.cs:36](../../Assets/Scripts/Character/Ambition/Pure/AmbitionContext.cs)) rejects values whose runtime type isn't on
its `IsSerializableValueKind` allow-list. The allow-list includes `Character`,
`ScriptableObject` subclasses, primitives, enums, and `IWorldZone` — but NOT
plain Assembly-CSharp classes like `Community`.

Storing the founded `Community` in context would require extending the allow-list,
which has knock-on serialization consequences (the save layer's `ContextEntryDTO`
switch would need a new arm for Community). Cheaper to skip context altogether
and let downstream tasks read `actor.CharacterCommunity.CurrentCommunity` —
that's the server-side authoritative pointer, set by Plan 1's
`SetCurrentCommunity`, and it's already what `Task_PromoteCommunity` uses.

This mirrors how `Task_FinishConstruction` (Plan 4) will resolve the AB by
scanning `BuildingManager.allBuildings.Where(b => b.BuildingSO == targetSO &&
b.PlacedByCharacterId == actor.CharacterId)` rather than stashing the live
Building in context.

## Why `Task_PromoteCommunity` is passive

Plan 4 owns `Community.TryPromoteLevel()` — the actual tier-up mutator that
checks treasury + required buildings + population + tier requirements. The
ambition task SHOULD NOT duplicate that logic. Instead it acts as a barrier:
the ambition pauses on Quest_PromoteCamp until SOMETHING (the player clicking
a Promote button via the admin console — Plan 5, OR an NPC leader autonomously
calling TryPromoteLevel — Plan 4 BTAction) actually raises the community level.
Once level >= TargetLevel, the task reports Completed and the ambition advances.

This decoupling means the same task definition supports both NPC and player
leadership styles without per-style branches.

## NPC vs. Player completion

- **NPC leader**: BT ticks `Task_CreateCommunity` which calls `CheckAndCreateCommunity()`
  server-side. Task completes the same tick. NPC then walks to a viable AB-placement
  spot (Plan 4's `Task_PlaceBuilding` GOAP wiring) and finishes the AB via the
  cooperative construction loop. Tier-up tasks complete passively as the NPC
  leader reaches required treasury / population.
- **Player leader**: The ambition appears in the quest log. Player clicks a
  (Plan 5) "Create Community" button → `Task_CreateCommunity.Tick` fires → completes.
  Player places the AB via the normal `BuildingPlacementManager` ghost flow →
  `Task_PlaceBuilding` (Plan 4) reports Completed once a matching building exists.
  Tier-up tasks complete when the player hits the Promote button in the admin
  console (Plan 5).

The same tasks; different driver patterns. Rule #22 (player↔NPC parity) holds.

## Open questions / TODO
- *Plan 4 — `Quest_BuildCapital` failure recovery*: if the AB is destroyed
  mid-construction, the task may resurrect via re-Tick; should it cancel and
  retry, or bail out of the ambition entirely?
- *Plan 4 — TryPromoteLevel autonomy*: should the NPC leader autonomously fire
  TryPromoteLevel as soon as criteria are met, or wait for explicit BTAction
  scheduling?
- *Plan Next — variant ambitions*: Ambition_FoundACity vs. Ambition_BuildVillage
  vs. Ambition_BuildEmpire — do we want sub-variants with shorter quest chains?

## Sources
- [Task_CreateCommunity.cs](../../Assets/Scripts/Character/Ambition/Tasks/Task_CreateCommunity.cs)
- [Task_PromoteCommunity.cs](../../Assets/Scripts/Character/Ambition/Tasks/Task_PromoteCommunity.cs)
- [Ambition_FoundACity.cs](../../Assets/Scripts/Character/Ambition/AmbitionSOs/Ambition_FoundACity.cs)
- [AmbitionContext.cs](../../Assets/Scripts/Character/Ambition/Pure/AmbitionContext.cs) — context serialization allow-list
- [docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md](../../docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md) §`AmbitionSO` chain — design source
- [docs/superpowers/plans/2026-05-17-ambition-found-a-city.md](../../docs/superpowers/plans/2026-05-17-ambition-found-a-city.md) — Plan 3 implementation
```

- [ ] **Step 5: Sanity grep**

`grep -rn "Task_CreateCommunity\|Task_PromoteCommunity\|Ambition_FoundACity" wiki/ .agent/` — expect at least 3+ hits in the new + updated docs.

- [ ] **Step 6: Commit**

```bash
git add wiki/concepts/found-a-city-ambition.md wiki/systems/ .agent/skills/
git commit -m "$(cat <<'EOF'
docs(ambition): wiki + skill updates for Ambition_FoundACity (Plan 3)

- wiki/concepts/found-a-city-ambition.md (NEW) — concept page covering the
  full quest chain (with Plan 3 vs. Plan 4 ownership), the actor-as-anchor
  design choice, and the NPC vs. player completion split
- wiki/systems/character-ambition.md (if exists, otherwise leaves a note) —
  Task_CreateCommunity / Task_PromoteCommunity / Ambition_FoundACity added
- .agent/skills/ambition-system/SKILL.md (if exists) — public-API table refresh

Per rules #28, #29b: every system touched in Plan 3 has its docs current.

Plan 3 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 5: Final verification + summary commit

**Files:** none (verification only).

- [ ] **Step 1: Full EditMode test sweep**

Use `tests-run` filter `MWI.Tests.*`. Expected: 147 baseline (from Plans 1+2) + 10 new = 157 tests, all green.

- [ ] **Step 2: Compile sanity**

`assets-refresh` + `console-get-logs`. Expected: clean.

- [ ] **Step 3: Grep sanity**

- `grep -rn "Task_CreateCommunity\b" Assets/Scripts` — expect 1 hit (the class definition).
- `grep -rn "Task_PromoteCommunity\b" Assets/Scripts` — expect 1 hit.
- `grep -rn "Ambition_FoundACity\b" Assets/Scripts` — expect 1 hit.

- [ ] **Step 4: Final summary commit**

```bash
git commit --allow-empty -m "$(cat <<'EOF'
chore(ambition): Plan 3 of 5 complete — Ambition_FoundACity scaffolding

Plan 3 of 5 for the City Founding spec
(docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md).

Network safety (rule #19b):
Plan 3 adds NO new client-visible state. Both new TaskBase subclasses operate on
server-side state only (CharacterCommunity.CurrentCommunity, Community.level).
No NetworkBehaviour was modified. Mid-pursuit state is stateless (each Tick
re-reads world state from scratch), so no SerializeState/DeserializeState
network audit needed.

Tasks ship:
- Task_CreateCommunity (active: drives CheckAndCreateCommunity until actor leads)
- Task_PromoteCommunity (passive: completes when community.level >= TargetLevel)
- Ambition_FoundACity (typed AmbitionSO subclass; empty body)

Out of scope (Plan 4 will land):
- Task_PlaceBuilding + Task_FinishConstruction (need ABSO)
- The actual Ambition_FoundACity.asset wiring (full quest chain)
- BTAction_PursueAmbitionStep changes (the existing BTAction already ticks tasks)

Tests: 10 new EditMode tests under MWI.Tests.Ambition.*
- 5 in Task_CreateCommunityTests
- 5 in Task_PromoteCommunityTests
Total EditMode suite: 157 tests, all green.

Side change: CharacterCommunity.CreateCommunity(string name) flipped from
private to public so Task_CreateCommunity can use the named-override path.

Commit sequence:
- Task 1: Task_CreateCommunity + 5 EditMode tests
- Task 2: Task_PromoteCommunity + 5 EditMode tests
- Task 3: Ambition_FoundACity AmbitionSO subclass
- Task 4: wiki + skill docs

Ready for Plan 4 (AdministrativeBuilding + JobBuilder + BuildOrder) to consume
Plan 3's task scaffolding and ship the full Ambition_FoundACity.asset with the
complete quest chain.
EOF
)"
```

---

## Self-Review Notes (post-write)

Re-checked against the user-stated Plan-3 scope ("Ambition_FoundACity"):

- ✅ **Task_CreateCommunity** — Task 1, with tests.
- ✅ **Task_PromoteCommunity** — Task 2, with tests.
- ✅ **Ambition_FoundACity AmbitionSO subclass** — Task 3.
- ✅ **Plan-4-deferred items explicitly listed** — Task_PlaceBuilding, Task_FinishConstruction, Quest_BuildCapital, the full .asset wiring, Community.TryPromoteLevel(), BTAction changes.
- ✅ **Documentation (rules #28, #29b)** — Task 4.
- ✅ **Network audit per Rule #19b** — recorded in plan header + summary commit.

Placeholder scan: no TODO / TBD / vague step descriptions. Every code step has the actual code.

Type consistency check:
- `Task_CreateCommunity` — used identically in plan, tests, and code; `CommunityName` field type `string`.
- `Task_PromoteCommunity` — `TargetLevel` field type `CommunityLevel`.
- `Ambition_FoundACity` — extends `AmbitionSO` (which exists).
- `CharacterCommunity.CreateCommunity(string)` — flipped private→public; the no-arg `CheckAndCreateCommunity()` stays as-is.

Plan length: 5 tasks. Each ends with a commit. Estimated 60-90 minutes for a focused engineer.
