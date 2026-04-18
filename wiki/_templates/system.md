---
type: system
title: "System name"
tags: []
created: YYYY-MM-DD
updated: YYYY-MM-DD
sources: []
related: []
status: wip
primary_agent: null
secondary_agents: []
owner_code_path: ""
depends_on: []
depended_on_by: []
---

# System name

## Summary
One paragraph. If a new engineer reads only this, they should understand what
the system is and what role it plays in the game.

## Purpose
Why does this system exist? What problem would not be solved without it?

## Responsibilities
- Responsibility 1 (what it owns)
- Responsibility 2
- Responsibility 3

**Non-responsibilities** (common misconceptions):
- Not responsible for X — that lives in [[other-system]].

## Key classes / files
| File | Role |
|------|------|
| [ClassA.cs](../../Assets/Scripts/.../ClassA.cs) | one-line description |
| [ClassB.cs](../../Assets/Scripts/.../ClassB.cs) | one-line description |

## Public API / entry points
- `ClassA.DoThing(args)` — one-line purpose.
- `EventBus.OnThingHappened` — when it fires, who listens.

## Data flow
Describe, in prose or a small diagram, how data moves through this system:
inputs → processing → outputs. Call out Server vs Client authority where
relevant.

```
[Input source] → [This system] → [Downstream consumer]
```

## Dependencies

### Upstream (this system needs)
- [[other-system]] — what it provides.

### Downstream (systems that need this)
- [[consumer-system]] — what they consume.

## State & persistence
- Runtime state: what lives in memory, where.
- Persisted state: what gets saved to disk / replicated over network.
- Save format: link to the serializer / schema.

## Known gotchas / edge cases
- [[gotcha-one]] — one-line note.
- [[gotcha-two]] — one-line note.

## Open questions / TODO
- [ ] Question or missing piece.

## Change log
- YYYY-MM-DD — <summary> — <agent/person>

## Sources
- [ClassA.cs](../../Assets/Scripts/.../ClassA.cs)
- [.agent/skills/<matching-skill>/SKILL.md](../../.agent/skills/<matching-skill>/SKILL.md) — procedural how-to
- [raw/design-docs/<relevant doc>.md](../../raw/design-docs/<relevant doc>.md)
