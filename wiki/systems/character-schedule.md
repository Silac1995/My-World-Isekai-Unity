---
type: system
title: "Character Schedule"
tags: [character, schedule, ai, time, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[ai]]", "[[jobs-and-logistics]]", "[[kevin]]"]
status: stable
confidence: medium
primary_agent: character-system-specialist
secondary_agents: ["npc-ai-specialist"]
owner_code_path: "Assets/Scripts/Character/CharacterSchedule/"
depends_on: ["[[character]]"]
depended_on_by: ["[[ai]]", "[[jobs-and-logistics]]"]
---

# Character Schedule

## Summary
Daily time-slot planner. Holds ordered activities for each in-game day (sleep, work, eat, socialize). Consumed by [[ai-behaviour-tree]] via `BTCond_HasScheduledActivity` + `BTAction_Work`, and by [[jobs-and-logistics]] via `InjectWorkSchedule`.

## Responsibilities
- Storing daily schedule slots (time range + activity type + target location).
- Firing "shift end" detection (`BTCond_NeedsToPunchOut`).
- Providing the current scheduled activity to the BT.
- Accepting schedule injection from jobs (`CharacterJob.InjectWorkSchedule`).

## Key classes / files
- `Assets/Scripts/Character/CharacterSchedule/CharacterSchedule.cs`.

## Open questions
- [ ] Schedule granularity — hourly? quarter-hour?
- [ ] Conflict resolution when a force-assigned job overlaps an existing slot. (SKILL says overlap is **intentionally** dissolved — confirm semantics.)

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/behaviour_tree/SKILL.md](../../.agent/skills/behaviour_tree/SKILL.md).
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md).
- [[ai]] and [[jobs-and-logistics]] parents.
