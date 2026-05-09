---
type: system
title: "Dialogue Data"
tags: [dialogue, scriptable-object, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[dialogue]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: character-social-architect
owner_code_path: "Assets/Scripts/Dialogue/"
depends_on: ["[[dialogue]]"]
depended_on_by: ["[[dialogue]]"]
---

# Dialogue Data

## Summary
`DialogueSO` ScriptableObject holds ordered `DialogueLine`s. Each line has a `characterIndex` (1-based into participants) and `lineText` with placeholder tags like `[indexX].getName` resolved at runtime.

## Key classes / files
- [DialogueSO.cs](../../Assets/Scripts/Dialogue/DialogueSO.cs).

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[dialogue]] §1.
