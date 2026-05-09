---
type: system
title: "Keys & Locks"
tags: [items, keys, locks, doors, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[items]]", "[[character-equipment]]", "[[building]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: item-inventory-specialist
secondary_agents: ["building-furniture-specialist"]
owner_code_path: "Assets/Scripts/Item/"
depends_on: ["[[items]]"]
depended_on_by: ["[[building]]"]
---

# Keys & Locks

## Summary
Keys are a specialized item family. `KeySO._lockId` is for static doors (dungeons, quest doors); leave empty for building keys — the LockId assigns at runtime. Tier gates match: `KeySO.Tier >= DoorLock.RequiredTier`. Lookup via `CharacterEquipment.FindKeyForLock(lockId, tier)` scans inventory + `HandsController.CarriedItem`. Locksmith skill tier caps max key tier that can be copied.

## Key classes / files
- `Assets/Data/Item/KeySO.cs`.
- [KeyInstance.cs](../../Assets/Scripts/Item/KeyInstance.cs).
- `Assets/Scripts/World/MapSystem/DoorHealth.cs` — `IDamageable` for breakable doors.

## Related
- `door-lock-system` SKILL covers the full lock/unlock API + breakable doors.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[items]] §6–§7.
- [.agent/skills/door-lock-system/SKILL.md](../../.agent/skills/door-lock-system/SKILL.md).
