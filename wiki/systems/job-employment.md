---
type: system
title: "Job Employment"
tags: [jobs, employment, schedule, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[jobs-and-logistics]]", "[[character-job]]", "[[character-schedule]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/Character/CharacterJob/"
depends_on: ["[[jobs-and-logistics]]"]
depended_on_by: ["[[jobs-and-logistics]]"]
---

# Job Employment

## Summary
The flow that connects a character to a workplace. `CommercialBuilding.AskForJob` — gated by HasOwner / HasCommunityLeader + vacancy + `DoesScheduleOverlap`. Community leaders can force-assign via `CommunityTracker.ImposeJobOnCitizen` (dissolves conflicts). On success: `CharacterJob.TakeJob` + `InjectWorkSchedule`.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[jobs-and-logistics]] + [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md).
