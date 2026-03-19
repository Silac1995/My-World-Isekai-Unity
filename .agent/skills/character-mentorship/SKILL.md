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
