---
name: character-mentorship
description: Architecture, physical zoning, scheduling, and learning progression rules for the Character Mentorship system (teaching skills and combat styles).
---

# Character Mentorship System

The Mentorship System bridges the progression mechanics (Skills, Combat Styles) with the daily life simulation (AI Schedules, Social interaction, Physical positioning). It manages how a Character (Mentor) physically registers and conducts a class, and how Characters (Students) attend, receive XP ticks, and eventually graduate upon reaching maximum proficiency.

## When to use this skill
- When debugging issues where NPCs fail to teach or attend classes.
- When modifying how Skill or Combat XP is awarded during mentorship.
- When adjusting the logic for creating physical teaching zones (`MentorClassZone`).

## The Mentorship Architecture

The mentorship system relies on decoupling the logical progression (`SkillInstance`, `SkillSO`) from the daily lifecycle (`CharacterMentorship`, `ScheduleEntry`). The relationship between teacher and student is formalised via `MentorshipClass`.

### 1. CharacterMentorship (The Central Hub)
**Rule:** Both mentors and students utilize the `CharacterMentorship` component.
- For **Students**, it tracks the `_currentMentor`, `_learningSubject`, and procedural `_learningProgress` until a skill is properly unlocked.
- For **Mentors**, it manages hosted `MentorshipClass` instances, runs capacity/acceptance checks (`CalculateAcceptanceChance`), and dynamically manages the `ScheduleEntry` to initiate classes when the mentor is free (`Wander` or `Teach` activities).

### 2. MentorshipClass (The Formal Bonding)
A pure data class tracking the `Mentor`, the `TeachingSubject` (can be `SkillSO` or `CombatStyleSO`), and `EnrolledStudents`.
- It serves as an Event channel (`OnClassStarted`, `OnClassEnded`) to broadcast state changes explicitly to enrolled students, triggering their AI behaviours (`AttendClassBehaviour`).

### 3. MentorClassZone (Physical Constraints)
**Rule:** Classes must physically occur in the world using safe NavMesh generation.
- It dynamically resizes its `BoxCollider` and `NavMeshModifierVolume` based on student count.
- Provides `GetStudentSlotPosition(Character student)` to create orderly grid formations facing the mentor, preventing characters from overlapping.

### 4. Progression (SkillTier Multipliers)
**Rule:** Students cannot exceed their mentor's tier minus one (e.g. Advanced teaches up to Intermediate).
- Mentors apply XP multipliers based on their `SkillTier` (`GetMentorshipXPMultiplier`). Upon graduation, `Graduate(ScriptableObject subject)` is called to clear the mentor data.

## Important Considerations
- **Interrupts:** Mentorship is tied to `CharacterCombat`. If a mentor is attacked in combat, the `OnCombatModeChanged` event triggers `StopGivingLesson()`.
- **Dynamic Scheduling:** The mentor injects a high-priority `ScheduleActivity.Teach` entry into their `CharacterSchedule` dynamically on the hour mark if they have students and their default action is `Wander` or `Teach`.

## Player Entry Points: Hold-E Menu Provider (2026-04-24)

`CharacterMentorship` implements `IInteractionProvider`. When a player holds E on any character that has teachable subjects (`GetTeachableSubjects().Count > 0`, i.e. any skill/style/ability at level ≥ 35), the hold-E menu emits one `"Ask to teach {Subject}"` entry per teachable subject.

- **Gating (rendered client-side when the menu builds):**
  - Hidden entirely when the interactor is already this mentor's student for *this specific subject*.
  - Disabled with `(you already have a mentor)` when the interactor has any current mentor.
  - Disabled with `(you're already skilled enough)` when `CanTeachStudent(interactor, subject)` returns false (student tier ≥ mentorTier − 1).
- **Click routing:** `IsServer` → direct `InteractionMentorship(subject).Execute(interactor, mentor)`. Remote clients route via `RequestMentorshipServerRpc(mentorNetId, subjectAssetKey)`. The subject key format is `"{TypeName}:{AssetName}"` (e.g. `"SkillSO:Tailoring"`) and is resolved server-side by lookup inside the mentor's own `GetTeachableSubjects()` list — lookup doubles as a security check (a forged key cannot request a subject the mentor doesn't actually offer).
- **Player-to-player support:** `InteractionMentorship` inherits `InteractionInvitation`, so player targets automatically get the `UI_InvitationPrompt` accept/refuse flow via `CharacterInvitation.ReceiveInvitationClientRpc` → `ResolvePlayerInvitationServerRpc`. No new UI was added for this feature.

### New public APIs
- `CanTeachStudent(Character student, ScriptableObject subject) → bool` — tier gate, lifted from the former private `InteractionMentorship.CanStudentStillLearn`. Used by both the invitation class and the provider.
- `RequestMentorshipServerRpc(ulong mentorNetId, string subjectAssetKey)` — `[Rpc(SendTo.Server)]` entry point, invoked on the source's own `CharacterMentorship` from the owning client.
- `CurrentMentorNetId` — `NetworkVariable<ulong>` (server-write, everyone-read). Mirrors `_currentMentor.NetworkObject.NetworkObjectId` so the `CurrentMentor` property resolves correctly on remote clients when the plain `_currentMentor` field is null. Written by `SetMentor` / `ClearMentor` on the server only.

### Lifecycle fix shipped alongside
`CharacterMentorship.OnEnable` / `OnDisable` were `private void` (hiding the `CharacterSystem` base's `protected virtual` methods) — which silently skipped `_character.Register(this)`. This blocked `Character.GetAll<IInteractionProvider>()` from discovering the new provider at runtime. Fixed by converting both to `protected override void` with `base.OnEnable()` / `base.OnDisable()` chaining.
