---
type: system
title: "Character Community (adapter)"
tags: [character, community, world, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[world]]", "[[character-relation]]", "[[character-traits]]", "[[kevin]]"]
status: stable
confidence: medium
primary_agent: character-system-specialist
secondary_agents: ["world-system-specialist"]
owner_code_path: "Assets/Scripts/Character/CharacterCommunity/"
depends_on: ["[[character]]", "[[world]]"]
depended_on_by: ["[[world]]"]
---

# Character Community (adapter)

## Summary
**Character-side adapter** to [[world]]'s community system. Runs the founding gate (`CheckAndCreateCommunity` — requires `canCreateCommunity` trait + ≥4 friends + not already a leader) and holds a reference to the community the character currently belongs to. The actual community entities (`Community`, `CommunityLevel`, `CommunityManager`) live under `Assets/Scripts/World/Community/` (see [[world]] → `world-community`).

## Responsibilities
- Founding gate: `CheckAndCreateCommunity`.
- Current community membership reference.
- Leadership flags (is this character the leader of their community?).
- Forwarding data changes to the world-side registry.

## Key classes / files
- [CharacterCommunity.cs](../../Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs).
- Counterpart: `Assets/Scripts/World/Community/` — `Community.cs`, `CommunityLevel.cs`, `CommunityManager.cs`.

## Open questions
- [ ] **Q4 resolution** — this split (character-side adapter vs world-side entity) was inferred from file contents on 2026-04-18 and has **not yet been explicitly confirmed by Kevin**. Correctness is therefore medium.
- [ ] No SKILL.md for `character-community` — tracked in [[TODO-skills]].

## Change log
- 2026-04-19 — Stub. Q4 inference noted. — Claude / [[kevin]]

## Sources
- [.agent/skills/community-system/SKILL.md](../../.agent/skills/community-system/SKILL.md)
- File inspection 2026-04-18 — [CharacterCommunity.cs](../../Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs) (1 file) vs `Assets/Scripts/World/Community/` (3 files).
