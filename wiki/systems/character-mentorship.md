---
type: system
title: "Character Mentorship"
tags: [character, mentorship, social, teaching, tier-2]
created: 2026-04-19
updated: 2026-04-24
sources: []
related:
  - "[[social]]"
  - "[[combat]]"
  - "[[character-skills]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: character-social-architect
secondary_agents:
  - combat-gameplay-architect
  - character-system-specialist
owner_code_path: "Assets/Scripts/Character/"
depends_on:
  - "[[character]]"
  - "[[social]]"
depended_on_by:
  - "[[combat]]"
  - "[[social]]"
---

# Character Mentorship

## Summary
Teacher-student relationships where one character passes a skill or ability to another. Running ticks of `CharacterMentorship.ReceiveLessonTick` award partial XP (for skills) or gate-through learning progress (for abilities), with an `AbilitySO` branch that lets teachers pass any ability they know. Books form a complementary learning path via `IAbilitySource` (the book acts as a "teacher" in its place).

## Purpose
Let knowledge move through the world through social ties rather than flat unlocks. A mentor's expertise gates what the student can learn; books provide an alternate path that doesn't require a live teacher. Mentorship also drives social bonds — teaching someone updates relations through [[character-relation]].

## Responsibilities
- Identifying teacher/student pair during `InteractionMentor` (or similar interaction action).
- Running lesson ticks (`ReceiveLessonTick`) that award fractional skill XP or ability progress.
- Handling `AbilitySO` branch — which abilities can be taught, learning rate, prerequisites.
- Cooperating with book learning via `IAbilitySource`.
- Updating [[character-relation]] (teaching is a bonding activity).
- Gating `IsFree()` — the teacher is marked `Teaching` (a `CharacterBusyReason`) during a lesson.

**Non-responsibilities**:
- Does **not** define skills or abilities — that's [[character-skills]] / abilities subsystem.
- Does **not** own the interaction lifecycle — [[character-interaction]] wraps mentorship in a `InteractionMentor`-style action.

## Key classes / files

- `Assets/Scripts/Character/CharacterMentorship/CharacterMentorship.cs` — per-character component.
- `IAbilitySource` — implemented by `CharacterMentorship` and book items ([[items]] `CharacterBookKnowledge` boundary).
- Mentorship interaction action (`ICharacterInteractionAction` subclass).

## Public API

- `character.CharacterMentorship.ReceiveLessonTick(teacher, topic)` — called every tick during a lesson.
- `character.CharacterMentorship.BeginLesson(student, topic)` / `EndLesson()`.
- `character.CharacterMentorship.CanTeachStudent(student, subject)` — tier gate (student must be strictly below `mentorTier - 1`; unknown subjects always learnable). Used by both the `InteractionMentorship` invitation and the hold-E menu provider.
- `character.CharacterMentorship.GetInteractionOptions(interactor)` — `IInteractionProvider` hook. Emits one "Ask to teach {Subject}" entry per teachable subject, disabled-with-reason when the interactor already has a mentor or is too high-tier.
- `character.CharacterMentorship.RequestMentorshipServerRpc(mentorNetId, subjectAssetKey)` — client-routed path for hold-E clicks. Subject key format `"{TypeName}:{AssetName}"` resolved server-side against the mentor's own `GetTeachableSubjects()` list (lookup doubles as security check).
- `character.CharacterMentorship.CurrentMentorNetId` — server-authoritative `NetworkVariable<ulong>` tracking the mentor's `NetworkObjectId`. `CurrentMentor` property falls back to this when `_currentMentor` is null (the remote-client case).

## Dependencies

### Upstream
- [[character]] — subsystem component.
- [[social]] — wraps mentorship in an interaction lifecycle.
- [[character-skills]] — the skill definitions being taught.
- [[combat]] — for ability teaching: `AbilitySO` hierarchy.

### Downstream
- [[character-relation]] — teaching nudges relation values.
- [[combat]] — taught abilities plug into `CharacterAbilities`.

## State & persistence

- Teacher-student pair only lives during an active lesson (transient).
- Learning progress (for abilities) persists on the student via [[character-skills]] or `CharacterAbilities`.
- Relation deltas persist via [[character-relation]].

## Known gotchas

- **`IsFree` must return `Teaching`** when the teacher is mid-lesson. Without this, GOAP or player input can interrupt and desync the lesson.
- **`AbilitySO` teaching** — all known abilities are teachable; design-gate by ability `Tier` or prerequisite, not by filtering here.
- **Books vs teachers** — books implement `IAbilitySource` with the book item acting as the teacher. A student learning from a book doesn't block anyone else's `IsFree`.
- **Examples file exists** at [examples/mentorship_patterns.md](../../.agent/skills/character-mentorship/examples/mentorship_patterns.md) — consult for concrete patterns.

## Open questions

- [ ] Exact `ReceiveLessonTick` cadence — tied to `TimeManager`? Each BT tick? Per-tick skill XP fractions.
- [ ] Mentorship zone — is there a physical zone that gates lessons (per SKILL listing), or purely character-to-character?

## Change log
- 2026-04-24 — Added `IInteractionProvider` surface for hold-E menu ("Ask to teach {Subject}") + `CanTeachStudent` public API (lifted from `InteractionMentorship.CanStudentStillLearn`) + `RequestMentorshipServerRpc` + `CurrentMentorNetId` NetworkVariable so remote clients see their own mentor status. Also fixed pre-existing `OnEnable/OnDisable` lifecycle bug (was `private` hiding the base → `_character.Register(this)` never ran, blocking `GetAll<IInteractionProvider>` discovery). — claude
- 2026-04-19 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/character-mentorship/SKILL.md](../../.agent/skills/character-mentorship/SKILL.md)
- [.agent/skills/character-mentorship/examples/mentorship_patterns.md](../../.agent/skills/character-mentorship/examples/mentorship_patterns.md)
- [docs/superpowers/specs/2026-04-23-ask-mentorship-and-job-interactions-design.md](../../docs/superpowers/specs/2026-04-23-ask-mentorship-and-job-interactions-design.md) — hold-E menu design spec.
- [[social]] parent (Q7: Social hosts Mentorship sub-page).
- [[combat]] SKILL §9.H on book learning via `IAbilitySource`.
