# Ask-to-Teach & Apply-for-Job Hold-E Interactions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface `InteractionMentorship` and `InteractionAskForJob` as entries in the hold-E character menu, gated by existing system rules, and routed through the existing `CharacterInvitation` accept/refuse pipeline for both NPC and player targets.

**Architecture:** Two existing `CharacterSystem` classes (`CharacterMentorship`, `CharacterJob`) each gain the `IInteractionProvider` interface plus a single `[Rpc(SendTo.Server)]` that mirrors `CharacterParty.RequestInviteToPartyServerRpc`. One private method moves from `InteractionMentorship` to `CharacterMentorship` so both the interaction class and the new provider can query the same "can this student still learn?" rule.

**Tech Stack:** Unity, C#, Unity NGO 2.x (`[Rpc(SendTo.Server)]`), existing invitation-template pattern.

**Spec:** [docs/superpowers/specs/2026-04-23-ask-mentorship-and-job-interactions-design.md](../specs/2026-04-23-ask-mentorship-and-job-interactions-design.md)

---

## File Structure

All touches are in existing files — no new files are created except optionally the wiki page if it doesn't already exist.

**Modified source files:**
- `Assets/Scripts/Character/CharacterSkills/CharacterMentorship.cs` — gains `IInteractionProvider`, `CanTeachStudent`, `GetInteractionOptions`, `RequestMentorshipServerRpc`
- `Assets/Scripts/Character/CharacterInteraction/InteractionMentorship.cs` — `CanStudentStillLearn` becomes a one-line passthrough to `CharacterMentorship.CanTeachStudent`
- `Assets/Scripts/Character/CharacterJob/CharacterJob.cs` — gains `IInteractionProvider`, `GetInteractionOptions`, `RequestJobApplicationServerRpc`, public `OwnedBuilding` accessor

**Documentation:**
- `.agent/skills/character-social-architect/SKILL.md` — mention new `CanTeachStudent` API + hold-E entry wiring
- `.agent/skills/npc-ai-specialist/SKILL.md` — mention new `RequestJobApplicationServerRpc` entry point
- `wiki/systems/character-mentorship.md` — update or create; change-log entry
- `wiki/systems/jobs-and-logistics.md` — change-log entry + public-API bump

No new prefabs, no new ScriptableObjects, no changes to scenes, `InteractionOption`, `IInteractionProvider`, `CharacterInteractable`, `InteractionInvitation`, `CharacterInvitation`, `UI_InvitationPrompt`, or any networking RPC in those existing files.

---

## Verification Workflow

Each code task follows this loop:

1. Apply the code edit with the `Edit` tool.
2. Refresh Unity asset database: call `mcp__ai-game-developer__assets-refresh`.
3. Check for compile errors: call `mcp__ai-game-developer__console-get-logs` with filter `logType: Error`.
4. If any compile errors, fix and repeat step 2.
5. For play-mode smoke tests: pause and ask the user to run Unity Play Mode and verify the named smoke test(s) from the spec's Testing section. Do not proceed to commit until the user confirms "pass".
6. Commit.

---

## Task 1: Move `CanStudentStillLearn` into `CharacterMentorship.CanTeachStudent`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterInteraction/InteractionMentorship.cs:42-74` (remove / thin out)
- Modify: `Assets/Scripts/Character/CharacterSkills/CharacterMentorship.cs` (add new public method near `GetTeachableSubjects` — around line 550)

**Why this is Task 1:** `CanTeachStudent` is the core gating rule for both the interaction class AND the new provider. Move it first so Tasks 2 and 3 can depend on it. Pure refactor — no behaviour change.

- [ ] **Step 1.1: Add `CanTeachStudent` to `CharacterMentorship`**

Open `Assets/Scripts/Character/CharacterSkills/CharacterMentorship.cs`. Insert this method immediately after `GetTeachableSubjects()` (after the closing `}` of `GetTeachableSubjects`, before `CalculateAcceptanceChance`):

```csharp
    /// <summary>
    /// Returns true if <paramref name="student"/> can still learn <paramref name="subject"/>
    /// from this mentor. A student can learn only while their tier in the subject is strictly
    /// below <c>mentorTier - 1</c> (e.g. a Master mentor can teach up to Advanced-1, i.e. Intermediate).
    /// If the student does not know the subject at all, they can always learn it.
    /// </summary>
    public bool CanTeachStudent(Character student, ScriptableObject subject)
    {
        if (student == null || subject == null) return false;

        // Logic preserved line-for-line from InteractionMentorship.CanStudentStillLearn
        // (Task 1 is a pure refactor — no behaviour change).

        // 1. Mentor tier for this subject
        SkillTier mentorTier = SkillTier.Novice;
        if (subject is SkillSO skill)
        {
            mentorTier = SkillTierExtensions.GetTierForLevel(_character.CharacterSkills.GetSkillLevel(skill));
        }
        else if (subject is CombatStyleSO style)
        {
            var expertise = _character.CharacterCombat.KnownStyles.FirstOrDefault(s => s.Style == style);
            if (expertise != null) mentorTier = expertise.CurrentTier;
        }
        // AbilitySO and any other non-handled type: mentorTier stays Novice.
        // The tier gate at the bottom then evaluates `Novice < Novice - 1` = false,
        // which matches the pre-refactor behaviour for abilities.

        // 2. Student tier for this subject
        SkillTier studentTier = SkillTier.Novice;
        if (subject is SkillSO studentSkill)
        {
            if (!student.CharacterSkills.HasSkill(studentSkill))
                return true; // Ne connaît pas du tout la compétence, donc peut apprendre
            studentTier = SkillTierExtensions.GetTierForLevel(student.CharacterSkills.GetSkillLevel(studentSkill));
        }
        else if (subject is CombatStyleSO studentStyle)
        {
            var expertise = student.CharacterCombat.KnownStyles.FirstOrDefault(s => s.Style == studentStyle);
            if (expertise != null)
                studentTier = expertise.CurrentTier;
            else
                return true; // Ne connaît pas du tout le style
        }

        // 3. Gate: student must be strictly below mentorTier - 1
        return (int)studentTier < (int)mentorTier - 1;
    }
```

Save the file.

- [ ] **Step 1.2: Refresh + compile-check**

Run `mcp__ai-game-developer__assets-refresh`, then `mcp__ai-game-developer__console-get-logs` with `logType: Error`. Expected: zero new errors. If any errors reference the new method, fix before proceeding.

- [ ] **Step 1.3: Replace private `CanStudentStillLearn` in `InteractionMentorship` with a call to the new public method**

Open `Assets/Scripts/Character/CharacterInteraction/InteractionMentorship.cs`. Replace the private `CanStudentStillLearn(Character student, Character mentor, ScriptableObject subject)` method (currently lines 42-74) with a one-line passthrough:

```csharp
    private bool CanStudentStillLearn(Character student, Character mentor, ScriptableObject subject)
    {
        if (mentor == null || mentor.CharacterMentorship == null) return false;
        return mentor.CharacterMentorship.CanTeachStudent(student, subject);
    }
```

Save the file.

- [ ] **Step 1.4: Refresh + compile-check**

Run `mcp__ai-game-developer__assets-refresh`, then `mcp__ai-game-developer__console-get-logs` with `logType: Error`. Expected: zero errors.

- [ ] **Step 1.5: Smoke test — existing mentorship flow still works**

Ask the user to:
1. Launch Unity Play Mode with the Game scene.
2. Find an NPC that is already a mentor (or use Dev-Mode to force one to have teachable skills).
3. Trigger an existing NPC-initiated mentorship (via GOAP) or dialogue-initiated path if wired.
4. Confirm: mentor still evaluates correctly, still enrolls student, no regressions vs. baseline.

If the project has no easy way to trigger existing mentorship right now, this smoke is optional — the refactor is line-for-line equivalent — but preferred.

- [ ] **Step 1.6: Commit**

```bash
git add Assets/Scripts/Character/CharacterSkills/CharacterMentorship.cs \
        Assets/Scripts/Character/CharacterInteraction/InteractionMentorship.cs
git commit -m "$(cat <<'EOF'
refactor(mentorship): move CanStudentStillLearn into CharacterMentorship.CanTeachStudent

Exposes the tier-gate rule as a public method on CharacterMentorship so
the upcoming IInteractionProvider hook and the existing InteractionMentorship
can share a single implementation. Pure refactor, no behaviour change.
EOF
)"
```

---

## Task 2: `CharacterMentorship` implements `IInteractionProvider`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterSkills/CharacterMentorship.cs`

**What this task does:** Adds the "Ask to teach X" entries to the hold-E menu and the ServerRpc that routes remote-client clicks. After this task, a host player sees and can click mentorship entries; a remote client sees them and the click fires the RPC that executes on the server.

- [ ] **Step 2.1: Add interface, using directives, helper, provider method, and ServerRpc**

At the top of `Assets/Scripts/Character/CharacterSkills/CharacterMentorship.cs`, ensure these using directives are present (add any missing):

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using MWI.Time;
```

Change the class declaration from:

```csharp
public class CharacterMentorship : CharacterSystem
```

to:

```csharp
public class CharacterMentorship : CharacterSystem, IInteractionProvider
```

At the very end of the class (just before the final closing `}` of the class — before line 593 `}` that closes the class), insert the following four methods:

```csharp
    // ─────────────────────────────────────────────────────────────
    //  IInteractionProvider — hold-E menu entries ("Ask to teach X")
    // ─────────────────────────────────────────────────────────────

    public List<InteractionOption> GetInteractionOptions(Character interactor)
    {
        if (interactor == null || interactor == _character) return null;
        if (interactor.CharacterMentorship == null) return null; // source can't learn anyway

        var teachable = GetTeachableSubjects();
        if (teachable == null || teachable.Count == 0) return null;

        var options = new List<InteractionOption>(teachable.Count);
        foreach (var subject in teachable)
        {
            if (subject == null) continue;

            // Already the student of THIS mentor in THIS subject → hide entry entirely
            if (interactor.CharacterMentorship.CurrentMentor == _character &&
                interactor.CharacterMentorship.LearningSubject == subject)
                continue;

            bool disabled = false;
            string reason = null;

            if (interactor.CharacterMentorship.CurrentMentor != null)
            {
                disabled = true;
                reason = "you already have a mentor";
            }
            else if (!CanTeachStudent(interactor, subject))
            {
                disabled = true;
                reason = "you're already skilled enough";
            }

            string subjectName = GetSubjectDisplayName(subject);
            string label = disabled
                ? $"Ask to teach {subjectName} ({reason})"
                : $"Ask to teach {subjectName}";

            var capturedSubject = subject;
            var capturedInteractor = interactor;
            options.Add(new InteractionOption
            {
                Name = label,
                IsDisabled = disabled,
                Action = () => OnMentorshipEntryClicked(capturedInteractor, capturedSubject, disabled)
            });
        }

        return options.Count > 0 ? options : null;
    }

    private static string GetSubjectDisplayName(ScriptableObject subject)
    {
        if (subject is SkillSO skill) return skill.SkillName;
        if (subject is CombatStyleSO style) return style.StyleName;
        // AbilitySO and any future ScriptableObject fall back to the asset name
        return subject.name;
    }

    private void OnMentorshipEntryClicked(Character interactor, ScriptableObject subject, bool disabled)
    {
        if (disabled) return; // defensive — menu already prevents clicks on disabled entries
        if (interactor == null || subject == null) return;

        if (IsServer)
        {
            var invitation = new InteractionMentorship(subject);
            if (invitation.CanExecute(interactor, _character))
                invitation.Execute(interactor, _character);
            return;
        }

        // Remote client → route via ServerRpc on the interactor's own CharacterMentorship
        if (interactor.CharacterMentorship == null || _character.NetworkObject == null) return;
        string key = $"{subject.GetType().Name}:{subject.name}";
        interactor.CharacterMentorship.RequestMentorshipServerRpc(_character.NetworkObject.NetworkObjectId, key);
    }

    [Rpc(SendTo.Server)]
    public void RequestMentorshipServerRpc(ulong mentorNetId, string subjectAssetKey)
    {
        // Defensive lookups + re-validation. The client cannot be trusted.
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(mentorNetId, out var mentorObj))
            return;
        var mentor = mentorObj != null ? mentorObj.GetComponent<Character>() : null;
        if (mentor == null || mentor.CharacterMentorship == null) return;

        // Resolve the subject by looking it up inside the mentor's own teachable list.
        // This is lookup AND security in one step: a client cannot request a subject
        // this mentor is not actually offering.
        var parts = subjectAssetKey?.Split(':');
        if (parts == null || parts.Length != 2) return;
        var subject = mentor.CharacterMentorship.GetTeachableSubjects()
            .FirstOrDefault(s => s != null && s.GetType().Name == parts[0] && s.name == parts[1]);
        if (subject == null) return;

        var invitation = new InteractionMentorship(subject);
        if (!invitation.CanExecute(_character, mentor)) return;
        invitation.Execute(_character, mentor);
    }
```

Save the file.

- [ ] **Step 2.2: Refresh + compile-check**

Run `mcp__ai-game-developer__assets-refresh`, then `mcp__ai-game-developer__console-get-logs` with `logType: Error`. Expected: zero errors.

Common compile fixes if they appear:
- `InteractionOption` not found → check `using` directive (`InteractionOption` is in the default namespace today; if a future version namespaces it, add the using).
- `IInteractionProvider` ambiguous → same — default namespace; if problems, `using global::`.
- `Rpc` attribute missing → `using Unity.Netcode;` at the top.

- [ ] **Step 2.3: Smoke test — Solo (host only)**

Ask the user to:
1. Launch Unity Play Mode with the Game scene.
2. Use Dev-Mode god tool to spawn or select an NPC with at least one skill at level ≥ 35 (set via inspect tab or direct modification).
3. Walk the player character close to that NPC and **hold E**.
4. Confirm **spec smoke test #1:** see one "Ask to teach {SkillName}" entry per teachable subject.
5. Confirm **spec smoke test #6 (inverted):** pick an NPC with no skills ≥ 35 — no "Ask to teach" entries appear.
6. Confirm **spec smoke test #7 inverted for mentorship:** an NPC without `CharacterMentorship` component (if any exist) shows no entries and no null-reference errors in the console.
7. With a fresh player character (no current mentor), click an "Ask to teach X" entry → the NPC speaks the invitation line → after ~1 s thinks → accepts or refuses per `CalculateAcceptanceChance`.
8. After accepting, hold E on the same mentor again → that specific subject entry is hidden (you're already learning it from them); other teachable subjects show with the `(you already have a mentor)` disabled suffix. **Spec smoke test #3 confirmed.**

If any step fails, stop and debug before proceeding.

- [ ] **Step 2.4: Smoke test — Multiplayer (host + 1 client)**

Ask the user to:
1. Launch a Unity Play Mode host session plus a client session (or a build + editor host).
2. **Spec smoke test #10:** Client holds E on one of host's NPCs who is a mentor → entries appear with correct names on the client. Click one → NPC evaluates server-side → accept/refuse result syncs to client.
3. **Spec smoke test #13:** With two players both connected, Player A holds E on Player B (B has a teachable skill). A clicks "Ask to teach X". B's screen shows the `UI_InvitationPrompt` with the message. B clicks **Refuse** → A sees the refuse message, A's relation with B drops by 1 (verify via character debug UI).
4. **Spec smoke test #13 accept variant:** Repeat; B clicks **Accept** → A becomes B's student (verify `CurrentMentor` on A's debug panel, `HostedClasses` on B's).
5. **Parity check (rule #19):** confirm in step 3 that host's view of Player A's character and client's view of Player A's character both show identical state before and after.

Stop if any test fails.

- [ ] **Step 2.5: Commit**

```bash
git add Assets/Scripts/Character/CharacterSkills/CharacterMentorship.cs
git commit -m "$(cat <<'EOF'
feat(mentorship): hold-E menu entries for Ask-to-teach + ServerRpc

CharacterMentorship now implements IInteractionProvider. For each teachable
subject the mentor offers, emits an "Ask to teach X" entry, gated and
disabled-with-reason per spec. Click handler uses direct Execute on host
and routes via RequestMentorshipServerRpc on remote clients. Server-side
resolver looks the subject up inside the mentor's own teachable list,
so a forged RPC cannot request a subject the mentor does not actually
offer.
EOF
)"
```

---

## Task 3: `CharacterJob` implements `IInteractionProvider`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterJob/CharacterJob.cs`

**What this task does:** Adds the "Apply for X" entries when the target owns a commercial building with vacant jobs, plus the ServerRpc. Also exposes the existing private `_ownedBuilding` as a public `OwnedBuilding` accessor (needed by the provider — currently only `IsOwner` is public).

- [ ] **Step 3.1: Expose `OwnedBuilding` as a public accessor**

Open `Assets/Scripts/Character/CharacterJob/CharacterJob.cs`. Find the existing line:

```csharp
    public bool IsOwner => _ownedBuilding != null && _ownedBuilding.Owner == _character;
```

Immediately below it, add:

```csharp
    public CommercialBuilding OwnedBuilding => _ownedBuilding;
```

- [ ] **Step 3.2: Add interface, using directives, provider method, and ServerRpc**

At the top of the same file, ensure these using directives are present (add any missing):

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
```

Change the class declaration from:

```csharp
public class CharacterJob : CharacterSystem, ICharacterSaveData<JobSaveData>
```

to:

```csharp
public class CharacterJob : CharacterSystem, ICharacterSaveData<JobSaveData>, IInteractionProvider
```

At the end of the class (before its closing `}`), insert:

```csharp
    // ─────────────────────────────────────────────────────────────
    //  IInteractionProvider — hold-E menu entries ("Apply for X")
    // ─────────────────────────────────────────────────────────────

    public List<InteractionOption> GetInteractionOptions(Character interactor)
    {
        if (interactor == null || interactor == _character) return null;
        if (interactor.CharacterJob == null) return null; // source can't hold jobs

        if (!IsOwner) return null;
        var building = OwnedBuilding;
        if (building == null) return null;

        // Iterate the full Jobs list once with a natural stable index; skip assigned.
        // Avoids computing index via GetAvailableJobs + IndexOf (O(N²) + IReadOnlyList.IndexOf unavailable).
        var options = new List<InteractionOption>();
        for (int idx = 0; idx < building.Jobs.Count; idx++)
        {
            var job = building.Jobs[idx];
            if (job == null || job.IsAssigned) continue;

            bool disabled = false;
            string reason = null;
            if (interactor.CharacterJob.HasJob)
            {
                disabled = true;
                reason = "you already have a job";
            }

            string title = string.IsNullOrEmpty(job.JobTitle) ? "Worker" : job.JobTitle;
            string label = disabled
                ? $"Apply for {title} ({reason})"
                : $"Apply for {title}";

            var capturedInteractor = interactor;
            var capturedBuilding = building;
            var capturedJob = job;
            var capturedIdx = idx;
            options.Add(new InteractionOption
            {
                Name = label,
                IsDisabled = disabled,
                Action = () => OnJobEntryClicked(capturedInteractor, capturedBuilding, capturedJob, capturedIdx, disabled)
            });
        }

        return options.Count > 0 ? options : null;
    }

    private void OnJobEntryClicked(Character interactor, CommercialBuilding building, Job job, int stableIdx, bool disabled)
    {
        if (disabled) return;
        if (interactor == null || building == null || job == null) return;

        if (IsServer)
        {
            var invitation = new InteractionAskForJob(building, job);
            if (invitation.CanExecute(interactor, _character))
                invitation.Execute(interactor, _character);
            return;
        }

        // Remote client → route via ServerRpc on the interactor's own CharacterJob
        if (interactor.CharacterJob == null || _character.NetworkObject == null) return;
        interactor.CharacterJob.RequestJobApplicationServerRpc(_character.NetworkObject.NetworkObjectId, stableIdx);
    }

    [Rpc(SendTo.Server)]
    public void RequestJobApplicationServerRpc(ulong ownerNetId, int jobStableIndex)
    {
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(ownerNetId, out var ownerObj))
            return;
        var owner = ownerObj != null ? ownerObj.GetComponent<Character>() : null;
        if (owner == null || owner.CharacterJob == null || !owner.CharacterJob.IsOwner) return;

        var building = owner.CharacterJob.OwnedBuilding;
        if (building == null) return;
        if (jobStableIndex < 0 || jobStableIndex >= building.Jobs.Count) return;

        var job = building.Jobs[jobStableIndex];
        if (job == null || job.IsAssigned) return; // race: filled since client built menu

        var invitation = new InteractionAskForJob(building, job);
        if (!invitation.CanExecute(_character, owner)) return;
        invitation.Execute(_character, owner);
    }
```

Save the file.

- [ ] **Step 3.3: Refresh + compile-check**

Run `mcp__ai-game-developer__assets-refresh`, then `mcp__ai-game-developer__console-get-logs` with `logType: Error`. Expected: zero errors.

- [ ] **Step 3.4: Smoke test — Solo (host only)**

Ask the user to:
1. Launch Unity Play Mode.
2. Use Dev-Mode to find or spawn an NPC who owns a `CommercialBuilding` (e.g. a Shop or Forge) with at least one vacant job (verify via `UI_CommercialBuildingDebug`).
3. Walk the player near the owner, hold E.
4. **Spec smoke test #2:** see one "Apply for {JobTitle}" entry per vacant job.
5. Start a shop with two vacant jobs (clerk + stocker or similar) → two entries appear.
6. **Spec smoke test #5:** hold E on an NPC employee (not the owner) → zero "Apply for" entries.
7. **Spec smoke test #9:** click an entry → owner says the invitation line → evaluates via `AskForJob` → accepts or refuses. On accept, verify `CharacterJob.HasJob == true` on the player via the debug panel.
8. **Spec smoke test #4:** with a player who already has a job, hold E on another owner → entries show disabled with "(you already have a job)" suffix.
9. Confirm no null-reference errors in the console throughout.

Stop on any failure.

- [ ] **Step 3.5: Smoke test — Multiplayer (host + 1 client)**

Ask the user to:
1. Launch host + client.
2. **Spec smoke test #11:** client holds E on host's NPC owner → entries appear → click → ServerRpc fires → owner evaluates → on accept, `CharacterJob.HasJob` on client syncs to true.
3. **Spec smoke test #12:** client player becomes owner of a shop (use Dev-Mode to force ownership if needed), with ≥ 1 vacant job. Host holds E on the client player → entries appear. Host clicks "Apply for X" → client player sees `UI_InvitationPrompt` → client clicks Accept → host becomes employed (verify host's `CharacterJob.HasJob`).
4. **Spec smoke test #14 (race):** two clients simultaneously click "Apply for" on the same single-vacancy building → only one succeeds; the second is silently rejected by the server `CanExecute` check (no UI for the second client — acceptable per spec).
5. **Spec smoke test #15 (parity):** same NPC boss visible from host and client → identical "Apply for X" entries on both.

Stop on any failure.

- [ ] **Step 3.6: Commit**

```bash
git add Assets/Scripts/Character/CharacterJob/CharacterJob.cs
git commit -m "$(cat <<'EOF'
feat(job): hold-E menu entries for Apply-for-Job + ServerRpc

CharacterJob now implements IInteractionProvider. When the target is a
boss/owner with vacant jobs, emits one "Apply for {JobTitle}" entry per
vacancy, disabled-with-reason when the interactor already has a job.
Click routes via direct Execute on host, ServerRpc on remote clients.
Server re-validates owner status, job index range, and vacancy state
before executing the invitation. Also exposes OwnedBuilding public
accessor (was private _ownedBuilding with no accessor).
EOF
)"
```

---

## Task 4: Documentation updates

**Files:**
- Modify: `.agent/skills/character-social-architect/SKILL.md`
- Modify: `.agent/skills/npc-ai-specialist/SKILL.md`
- Modify (or create): `wiki/systems/character-mentorship.md`
- Modify: `wiki/systems/jobs-and-logistics.md`

**What this task does:** Per project rules #28, #29, and #29b, SKILL.md and wiki pages must be updated whenever a system changes. Agent files only need updating if the change extends their domain — the `character-social-architect` already covers `CharacterInteraction` / `CharacterInvitation`, and `npc-ai-specialist` already covers `CharacterJob` / `CommercialBuilding`. Both just need new-API notes, not structural rewrites.

- [ ] **Step 4.1: Update `.agent/skills/character-social-architect/SKILL.md`**

Read the current file first to locate the mentorship-related section. Add (under the relevant section, e.g. "Mentorship System" or similar):

```markdown
### New (2026-04-23): hold-E menu entries for mentorship

`CharacterMentorship` implements `IInteractionProvider` and advertises one
"Ask to teach {Subject}" entry per teachable subject. Entry is disabled
(grayed with a reason suffix) when the interactor already has a mentor or
is too high-tier to learn from this mentor; entry for a currently-active
mentor/subject pair is hidden entirely.

**New public API:**
- `CharacterMentorship.CanTeachStudent(Character student, ScriptableObject subject)` — tier gate, used by both the provider and `InteractionMentorship.CanStudentStillLearn` (now a one-line passthrough).

**New ServerRpc:**
- `CharacterMentorship.RequestMentorshipServerRpc(ulong mentorNetId, string subjectAssetKey)` — routes client-side menu clicks through the server. The subject key format is `"{TypeName}:{AssetName}"` and is resolved server-side by lookup within the mentor's own `GetTeachableSubjects()` list (lookup IS the security check).

Player-to-player targets automatically use the existing `CharacterInvitation.ReceiveInvitationClientRpc` → `UI_InvitationPrompt` → `ResolvePlayerInvitationServerRpc` accept/refuse pipeline (no new UI).
```

- [ ] **Step 4.2: Update `.agent/skills/npc-ai-specialist/SKILL.md`**

Read the current file to find the job-related section. Add:

```markdown
### New (2026-04-23): hold-E menu entries for Apply-for-Job

`CharacterJob` implements `IInteractionProvider` and, when the character is
an owner with vacant jobs (`IsOwner && OwnedBuilding.GetAvailableJobs().Any()`),
advertises one "Apply for {JobTitle}" entry per vacancy. Entry is
disabled-with-reason when the interactor already has a job.

**New public API:**
- `CharacterJob.OwnedBuilding` — was a private `_ownedBuilding` field with only the `IsOwner` bool accessor; now exposed directly for callers needing the building reference.

**New ServerRpc:**
- `CharacterJob.RequestJobApplicationServerRpc(ulong ownerNetId, int jobStableIndex)` — routes client-side menu clicks. `jobStableIndex` is the index in `CommercialBuilding.Jobs` (stable), NOT in `GetAvailableJobs()` (volatile). Server re-validates owner status, index range, and `!job.IsAssigned` before constructing the invitation.
```

- [ ] **Step 4.3: Update (or create) `wiki/systems/character-mentorship.md`**

Read `wiki/CLAUDE.md` first (per the project rules under #29b) to confirm frontmatter and naming conventions.

If the page exists: bump `updated:` in the frontmatter to `2026-04-23`; add this to the **Change log** section at the bottom:

```markdown
- 2026-04-23 — CharacterMentorship implements IInteractionProvider; new CanTeachStudent public API; new RequestMentorshipServerRpc for client-routed hold-E entries — claude
```

Add to the **Public API** section a line for `CanTeachStudent` and one for `RequestMentorshipServerRpc`. Link the SKILL.md from the **Sources** section if not already linked.

If the page does not exist: create it from `wiki/_templates/` following `wiki/CLAUDE.md` rules. Minimum sections: Purpose, Responsibilities, Key classes / files, Public API, Data flow, Dependencies, State & persistence, Gotchas, Open questions, Change log. Keep it brief — this spec's documentation scope is "update/add a system page" not "write a full system doc from scratch". If creating, reference both the spec and the plan in **Sources**.

- [ ] **Step 4.4: Update `wiki/systems/jobs-and-logistics.md`**

Bump `updated:` to `2026-04-23`. Append to **Change log**:

```markdown
- 2026-04-23 — CharacterJob implements IInteractionProvider for hold-E "Apply for {JobTitle}" entries; OwnedBuilding now public; added RequestJobApplicationServerRpc — claude
```

Add a `RequestJobApplicationServerRpc` bullet under **Public API**.

- [ ] **Step 4.5: Commit**

```bash
git add .agent/skills/character-social-architect/SKILL.md \
        .agent/skills/npc-ai-specialist/SKILL.md \
        wiki/systems/character-mentorship.md \
        wiki/systems/jobs-and-logistics.md
git commit -m "$(cat <<'EOF'
docs: update SKILL.md + wiki for hold-E mentorship / job entries

Documents the new IInteractionProvider implementations on
CharacterMentorship and CharacterJob, the CanTeachStudent public API,
the two new ServerRpcs, and the OwnedBuilding accessor. Matches project
rules #28 (SKILL.md), #29 (agent evaluation — no new agent needed,
existing domains extended), and #29b (wiki).
EOF
)"
```

---

## Done check

After Task 4 commits, run:

```bash
git log --oneline -5
```

Expected: four new commits above the spec-commit (`6af55c6`) in this order (most recent first):
1. `docs: update SKILL.md + wiki ...`
2. `feat(job): hold-E menu entries ...`
3. `feat(mentorship): hold-E menu entries ...`
4. `refactor(mentorship): move CanStudentStillLearn ...`

Confirm spec's full testing list is exercised across the three smoke-test sessions (Task 1.5 optional, Task 2.3+2.4, Task 3.4+3.5).

No PR is created by this plan — the user decides when to open one.
